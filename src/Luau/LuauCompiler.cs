using System.Buffers;
using System.ComponentModel;
using System.Text;
using Luau.Internal;
using Luau.Internal.Interop;
using static Luau.Internal.Interop.NativeMethods;

namespace Luau;

/// <summary>
/// Provides the expert, synchronous entry point to the official native Luau
/// compiler. This type does not grant persistent-artifact trust; callers that
/// accept untrusted input should use a bounded <see cref="ILuauCompilationService"/>.
/// </summary>
public unsafe static class LuauCompiler
{
    const ulong MaximumManagedBytecodeLength = 0X7FFFFFC7; // Array.MaxLength
    const int MaximumDiagnosticBytes = 16 * 1024;

    internal static void Compile(
        IBufferWriter<byte> writer,
        ReadOnlySpan<byte> source,
        LuauCompileOptions? options,
        LuauNativeAbiVerifier abiVerifier)
    {
        Compile(writer, source, options, abiVerifier, null, null);
    }

    internal static void Compile(
        IBufferWriter<byte> writer,
        ReadOnlySpan<byte> source,
        LuauCompileOptions? options,
        LuauNativeAbiVerifier abiVerifier,
        int? maximumOutputBytes,
        string? chunkName)
    {
        if (writer == null)
        {
            throw new ArgumentNullException(nameof(writer));
        }
        if (abiVerifier == null)
        {
            throw new ArgumentNullException(nameof(abiVerifier));
        }

        var output = default(LuauHostBuffer);
        try
        {
            var result = CompileNative(
                source,
                options ?? LuauCompileOptions.Default,
                abiVerifier,
                &output);
            if (!result.IsDiagnostic &&
                maximumOutputBytes is { } limit &&
                result.Length > limit)
            {
                throw new LuauLoadLimitException(
                    chunkName,
                    LuauLoadInputKind.Bytecode,
                    result.Length,
                    limit);
            }

            var copyLength = GetBoundedOutputLength(output.data, result.Length, result.IsDiagnostic);
            var destination = writer.GetSpan(copyLength);
            if (destination.Length < copyLength)
            {
                throw new InvalidOperationException("The buffer writer returned less space than requested.");
            }

            new ReadOnlySpan<byte>(output.data, copyLength).CopyTo(destination);
            writer.Advance(copyLength);
        }
        finally
        {
            luau_host_buffer_free(&output);
        }
    }

    /// <summary>
    /// Expert synchronous tooling path that compiles an owned snapshot of
    /// trusted UTF-8 source into opaque, loadable output.
    /// </summary>
    /// <remarks>
    /// This method does not impose a finite source or output admission limit.
    /// Ordinary Unity content and streamed or otherwise untrusted mods should
    /// use <see cref="ILuauCompilationService"/> and its shared bounded lane.
    /// </remarks>
    /// <exception cref="LuauCompilationException">
    /// The compiler reports a source diagnostic or no loadable output.
    /// </exception>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public static LuauCompilerOutput Compile(ReadOnlySpan<byte> source, LuauCompileOptions? options = null)
    {
        return Compile(source, options, LuauNativeProtection.AbiVerifier);
    }

    internal static LuauCompilerOutput Compile(
        ReadOnlySpan<byte> source,
        LuauCompileOptions? options,
        LuauNativeAbiVerifier abiVerifier)
    {
        var compileOptions = (options ?? LuauCompileOptions.Default) with { };
        var sourceSnapshot = source.ToArray();
        return CompileOwnedSource(sourceSnapshot, compileOptions, abiVerifier, null, default);
    }

    internal static LuauCompilerOutput CompileOwnedSource(
        byte[] sourceSnapshot,
        LuauCompileOptions options,
        int maximumOutputBytes)
    {
        return CompileOwnedSource(sourceSnapshot, options, maximumOutputBytes, default);
    }

    internal static LuauCompilerOutput CompileOwnedSource(
        byte[] sourceSnapshot,
        LuauCompileOptions options,
        int maximumOutputBytes,
        CancellationToken cancellationToken)
    {
        return CompileOwnedSource(
            sourceSnapshot,
            options,
            LuauNativeProtection.AbiVerifier,
            maximumOutputBytes,
            cancellationToken);
    }

    internal static LuauCompilerOutput CompileOwnedSource(
        byte[] sourceSnapshot,
        LuauCompileOptions options,
        LuauNativeAbiVerifier abiVerifier,
        int? maximumOutputBytes,
        CancellationToken cancellationToken)
    {
        if (sourceSnapshot == null)
        {
            throw new ArgumentNullException(nameof(sourceSnapshot));
        }
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }
        if (maximumOutputBytes is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumOutputBytes),
                maximumOutputBytes,
                "The maximum compiler output size must be greater than zero.");
        }

        var compileOptions = options with { };
        var bytecode = CompileBytecode(
            sourceSnapshot,
            compileOptions,
            abiVerifier,
            maximumOutputBytes,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (bytecode.Length == 0)
        {
            throw new LuauCompilationException("The Luau compiler returned an empty result.");
        }
        if (bytecode[0] == 0)
        {
            var message = bytecode.Length == 1
                ? "The Luau compiler failed without a diagnostic."
                : Encoding.UTF8.GetString(bytecode, 1, bytecode.Length - 1);
            throw new LuauCompilationException(message);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var sourceSha256 = LuauBytecodeHash.Sha256(sourceSnapshot);
        cancellationToken.ThrowIfCancellationRequested();
        return new LuauCompilerOutput(
            bytecode,
            compileOptions,
            sourceSha256,
            LuauNativeProtection.ExpectedUpstreamRevisionHash,
            LuauNativeProtection.ExpectedHostBuildFingerprint);
    }

    static byte[] CompileBytecode(
        ReadOnlySpan<byte> source,
        LuauCompileOptions options,
        LuauNativeAbiVerifier abiVerifier,
        int? maximumOutputBytes,
        CancellationToken cancellationToken)
    {
        if (abiVerifier == null)
        {
            throw new ArgumentNullException(nameof(abiVerifier));
        }

        var output = default(LuauHostBuffer);
        try
        {
            var nativeResult = CompileNative(
                source,
                options,
                abiVerifier,
                &output);
            // Running cancellation never interrupts the native call. Once it
            // returns, skip the managed copy/hash/capability path and let the
            // caller publish cancellation while finally frees the host buffer.
            cancellationToken.ThrowIfCancellationRequested();
            if (!nativeResult.IsDiagnostic &&
                maximumOutputBytes is { } limit &&
                nativeResult.Length > limit)
            {
                throw new LuauCompilationLimitException(
                    LuauCompilationLimitKind.BytecodeBytesPerResult,
                    nativeResult.Length,
                    limit);
            }

            var copyLength = GetBoundedOutputLength(
                output.data,
                nativeResult.Length,
                nativeResult.IsDiagnostic);
            var result = new byte[copyLength];
            new ReadOnlySpan<byte>(output.data, copyLength).CopyTo(result);

            return result;
        }
        finally
        {
            luau_host_buffer_free(&output);
        }
    }

    static (int Length, bool IsDiagnostic) CompileNative(
        ReadOnlySpan<byte> source,
        LuauCompileOptions options,
        LuauNativeAbiVerifier abiVerifier,
        LuauHostBuffer* output)
    {
        abiVerifier.EnsureAvailable();
        fixed (byte* ptr = source)
        {
            var nativeOptions = CreateHostCompileOptions(options);
            var status = luau_host_compile(
                ptr,
                checked((ulong)source.Length),
                &nativeOptions,
                output);
            ThrowIfCompileFailed(status);
        }

        var length = GetManagedBytecodeLength(output->data, output->size);
        // A zero-prefixed native payload is a source diagnostic, not
        // bytecode. Its size is already bounded by the admitted source;
        // bytecode-result policies must not reclassify it as an
        // infrastructure quota failure.
        return (length, length > 0 && output->data[0] == 0);
    }

    static LuauHostCompileOptions CreateHostCompileOptions(LuauCompileOptions options)
    {
        return new LuauHostCompileOptions
        {
            struct_size = checked((uint)sizeof(LuauHostCompileOptions)),
            version = 1,
            optimization_level = options.OptimizationLevel,
            debug_level = options.DebugLevel,
            type_info_level = options.TypeInfoLevel,
            coverage_level = options.CoverageLevel,
        };
    }

    static int GetBoundedOutputLength(byte* output, int length, bool isDiagnostic)
    {
        if (!isDiagnostic || length <= MaximumDiagnosticBytes + 1)
        {
            return length;
        }

        return 1 + BoundedUtf8Decoder.GetValidPrefixLength(
            output + 1,
            checked((ulong)(length - 1)),
            MaximumDiagnosticBytes);
    }

    static int GetManagedBytecodeLength(byte* code, ulong size)
    {
        if (code == null)
        {
            throw new OutOfMemoryException("The native Luau compiler could not allocate its output buffer.");
        }

        if (size > MaximumManagedBytecodeLength)
        {
            throw new LuauException("Bytecode size is too large.");
        }

        return checked((int)size);
    }

    static void ThrowIfCompileFailed(LuauHostStatus status)
    {
        switch (status)
        {
            case LuauHostStatus.Ok:
                return;
            case LuauHostStatus.SystemOutOfMemory:
                throw new OutOfMemoryException("The native Luau compiler could not allocate its output buffer.");
            case LuauHostStatus.CompilerError:
                throw new LuauException("The native Luau compiler failed with a contained C++ exception.");
            case LuauHostStatus.Unsupported:
                throw new PlatformNotSupportedException("The Luau host does not support the requested compiler operation.");
            case LuauHostStatus.InvalidArgument:
                throw new LuauException("The Luau host rejected the managed compiler request.");
            default:
                throw new LuauException($"The native Luau compiler returned unknown host status {(int)status}.");
        }
    }

    internal static void EnsureAvailable()
    {
        LuauNativeProtection.AbiVerifier.EnsureAvailable();
    }
}
