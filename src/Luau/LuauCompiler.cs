using System.Buffers;
using System.Text;
using Luau.Internal;
using Luau.Internal.Interop;
using static Luau.Internal.Interop.NativeMethods;

namespace Luau;

public unsafe static class LuauCompiler
{
    const ulong MaximumManagedBytecodeLength = 0X7FFFFFC7; // Array.MaxLength

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

        abiVerifier.EnsureAvailable();
        var output = default(LuauHostBuffer);
        try
        {
            fixed (byte* ptr = source)
            {
                var nativeOptions = CreateHostCompileOptions(options ?? LuauCompileOptions.Default);
                var status = luau_host_compile(
                    ptr,
                    checked((ulong)source.Length),
                    &nativeOptions,
                    &output);
                ThrowIfCompileFailed(status);
            }

            var length = GetManagedBytecodeLength(output.data, output.size);
            if (maximumOutputBytes is { } limit && length > limit)
            {
                throw new LuauLoadLimitException(
                    chunkName,
                    LuauLoadInputKind.Bytecode,
                    length,
                    limit);
            }

            var destination = writer.GetSpan(length);
            if (destination.Length < length)
            {
                throw new InvalidOperationException("The buffer writer returned less space than requested.");
            }

            new ReadOnlySpan<byte>(output.data, length).CopyTo(destination);
            writer.Advance(length);
        }
        finally
        {
            luau_host_buffer_free(&output);
        }
    }

    /// <summary>
    /// Compiles an owned snapshot of UTF-8 source into opaque, loadable output.
    /// </summary>
    /// <exception cref="LuauCompilationException">
    /// The compiler reports a source diagnostic or no loadable output.
    /// </exception>
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
        var bytecode = CompileBytecode(sourceSnapshot, compileOptions, abiVerifier);
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

        return new LuauCompilerOutput(
            bytecode,
            compileOptions,
            LuauBytecodeHash.Sha256(sourceSnapshot),
            LuauNativeProtection.ExpectedUpstreamRevisionHash,
            LuauNativeProtection.ExpectedHostBuildFingerprint);
    }

    static byte[] CompileBytecode(
        ReadOnlySpan<byte> source,
        LuauCompileOptions options,
        LuauNativeAbiVerifier abiVerifier)
    {
        if (abiVerifier == null)
        {
            throw new ArgumentNullException(nameof(abiVerifier));
        }

        abiVerifier.EnsureAvailable();
        var output = default(LuauHostBuffer);
        try
        {
            fixed (byte* ptr = source)
            {
                var nativeOptions = CreateHostCompileOptions(options);
                var status = luau_host_compile(
                    ptr,
                    checked((ulong)source.Length),
                    &nativeOptions,
                    &output);
                ThrowIfCompileFailed(status);
            }

            var length = GetManagedBytecodeLength(output.data, output.size);
            var result = new byte[length];
            new ReadOnlySpan<byte>(output.data, length).CopyTo(result);

            return result;
        }
        finally
        {
            luau_host_buffer_free(&output);
        }
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
}
