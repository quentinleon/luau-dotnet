using System.Buffers;
using System.Runtime.InteropServices;
using static Luau.Native.NativeMethods;

namespace Luau;

public unsafe static class LuauCompiler
{
    public static void Compile(IBufferWriter<byte> writer, ReadOnlySpan<byte> source, LuauCompileOptions? options = null)
    {
        byte* code;
        nuint size;

        fixed (byte* ptr = source)
        {
            var nativeOptions = (options ?? LuauCompileOptions.Default).options;
            code = luau_compile(ptr, (nuint)(source.Length * sizeof(byte)), &nativeOptions, &size);
        }

        try
        {
            var destination = writer.GetSpan((int)size);
            new ReadOnlySpan<byte>(code, (int)size).CopyTo(destination);
            writer.Advance((int)size);
        }
        finally
        {
#if NET8_0_OR_GREATER
            NativeMemory.Free(code);
#else
            Marshal.FreeHGlobal((IntPtr)code);
#endif
        }
    }

    public static byte[] Compile(ReadOnlySpan<byte> source, LuauCompileOptions? options = null)
    {
        byte* code;
        nuint size;

        fixed (byte* ptr = source)
        {
            var nativeOptions = (options ?? LuauCompileOptions.Default).options;
            code = luau_compile(ptr, (nuint)(source.Length * sizeof(byte)), &nativeOptions, &size);
        }

        try
        {
            if (size > 0X7FFFFFC7) // Array.MaxLength
            {
                throw new LuauException("Bytecode size is too large");
            }

            var result = new byte[(int)size];
            new ReadOnlySpan<byte>(code, (int)size).CopyTo(result);

            return result;
        }
        finally
        {
#if NET8_0_OR_GREATER
            NativeMemory.Free(code);
#else
            Marshal.FreeHGlobal((IntPtr)code);
#endif
        }
    }
}