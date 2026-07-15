using System.Buffers;
using System.Runtime.InteropServices;
using static Luau.Native.NativeMethods;

namespace Luau;

public unsafe static class LuauCompiler
{
    const nuint MaximumManagedBytecodeLength = 0X7FFFFFC7; // Array.MaxLength

    public static void Compile(IBufferWriter<byte> writer, ReadOnlySpan<byte> source, LuauCompileOptions? options = null)
    {
        if (writer == null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        byte* code;
        nuint size;

        fixed (byte* ptr = source)
        {
            var nativeOptions = (options ?? LuauCompileOptions.Default).options;
            LuauNativeProtection.EnsureAvailable();
            var status = luau_ffi_protected_compile(
                ptr,
                (nuint)(source.Length * sizeof(byte)),
                &nativeOptions,
                &code,
                &size);
            ThrowIfCompileFailed(status);
        }

        try
        {
            var length = GetManagedBytecodeLength(code, size);
            var destination = writer.GetSpan(length);
            if (destination.Length < length)
            {
                throw new InvalidOperationException("The buffer writer returned less space than requested.");
            }

            new ReadOnlySpan<byte>(code, length).CopyTo(destination);
            writer.Advance(length);
        }
        finally
        {
            free(code);
        }
    }

    public static byte[] Compile(ReadOnlySpan<byte> source, LuauCompileOptions? options = null)
    {
        byte* code;
        nuint size;

        fixed (byte* ptr = source)
        {
            var nativeOptions = (options ?? LuauCompileOptions.Default).options;
            LuauNativeProtection.EnsureAvailable();
            var status = luau_ffi_protected_compile(
                ptr,
                (nuint)(source.Length * sizeof(byte)),
                &nativeOptions,
                &code,
                &size);
            ThrowIfCompileFailed(status);
        }

        try
        {
            var length = GetManagedBytecodeLength(code, size);
            var result = new byte[length];
            new ReadOnlySpan<byte>(code, length).CopyTo(result);

            return result;
        }
        finally
        {
            free(code);
        }
    }

    static int GetManagedBytecodeLength(byte* code, nuint size)
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

    static void ThrowIfCompileFailed(int status)
    {
        switch ((uint)status)
        {
            case LUAU_PROTECTED_COMPILE_OK:
                return;
            case LUAU_PROTECTED_COMPILE_OUT_OF_MEMORY:
                throw new OutOfMemoryException("The native Luau compiler could not allocate its output buffer.");
            case LUAU_PROTECTED_COMPILE_ERROR:
                throw new LuauException("The native Luau compiler failed with a contained C++ exception.");
            default:
                throw new LuauException($"The native Luau compiler returned unknown protected status {status}.");
        }
    }
}
