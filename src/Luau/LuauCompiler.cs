using System.Buffers;
using System.Runtime.InteropServices;
using Luau.Native;
using static Luau.Native.NativeMethods;

namespace Luau;

public unsafe static class LuauCompiler
{
    const ulong MaximumManagedBytecodeLength = 0X7FFFFFC7; // Array.MaxLength

    public static void Compile(IBufferWriter<byte> writer, ReadOnlySpan<byte> source, LuauCompileOptions? options = null)
    {
        Compile(writer, source, options, LuauNativeProtection.AbiVerifier);
    }

    internal static void Compile(
        IBufferWriter<byte> writer,
        ReadOnlySpan<byte> source,
        LuauCompileOptions? options,
        LuauNativeAbiVerifier abiVerifier)
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
                var nativeOptions = CreateHostCompileOptions((options ?? LuauCompileOptions.Default).options);
                var status = luau_host_compile(
                    ptr,
                    checked((ulong)source.Length),
                    &nativeOptions,
                    &output);
                ThrowIfCompileFailed(status);
            }

            var length = GetManagedBytecodeLength(output.data, output.size);
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

    public static byte[] Compile(ReadOnlySpan<byte> source, LuauCompileOptions? options = null)
    {
        return Compile(source, options, LuauNativeProtection.AbiVerifier);
    }

    internal static byte[] Compile(
        ReadOnlySpan<byte> source,
        LuauCompileOptions? options,
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
                var nativeOptions = CreateHostCompileOptions((options ?? LuauCompileOptions.Default).options);
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

    static LuauHostCompileOptions CreateHostCompileOptions(lua_CompileOptions options)
    {
        if (options.vectorLib != null ||
            options.vectorCtor != null ||
            options.vectorType != null ||
            options.mutableGlobals != null ||
            options.userdataTypes != null ||
            options.librariesWithKnownMembers != null ||
            options.libraryMemberTypeCb != null ||
            options.libraryMemberConstantCb != null ||
            options.disabledBuiltins != null)
        {
            throw new PlatformNotSupportedException(
                "The Luau host compile ABI does not support vector-library names, mutable globals, " +
                "userdata types, known-library members, library-member callbacks, or disabled builtins.");
        }

        return new LuauHostCompileOptions
        {
            struct_size = checked((uint)sizeof(LuauHostCompileOptions)),
            version = 1,
            optimization_level = options.optimizationLevel,
            debug_level = options.debugLevel,
            type_info_level = options.typeInfoLevel,
            coverage_level = options.coverageLevel,
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
