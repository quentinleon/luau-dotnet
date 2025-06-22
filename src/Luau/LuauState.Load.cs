using System.Buffers;
using System.Text;
using static Luau.Native.NativeMethods;

namespace Luau;

public partial class LuauState
{
    public unsafe LuauFunction Load(ReadOnlySpan<byte> bytecode, ReadOnlySpan<char> chunkName = default)
    {
        ThrowIfDisposed();
        LoadInternal(bytecode, chunkName);
        var func = ToFunction(-1);
        return func;
    }

    unsafe void LoadInternal(ReadOnlySpan<byte> bytecode, ReadOnlySpan<char> chunkName = default)
    {
        var bytecodeSize = (nuint)(bytecode.Length * sizeof(byte));

        int status;
        if (chunkName.IsEmpty)
        {
            var defaultChunkName = "main"u8;
            fixed (byte* ptr = bytecode, namePtr = defaultChunkName)
            {
                status = luau_load(l, namePtr, ptr, bytecodeSize, 0);
            }
        }
        else
        {
            var buffer = ArrayPool<byte>.Shared.Rent(chunkName.Length * 3 + 1);
            try
            {
                var utf8Count = Encoding.UTF8.GetBytes(chunkName, buffer);
                buffer[utf8Count] = 0;

                fixed (byte* ptr = bytecode, namePtr = buffer)
                {
                    status = luau_load(l, namePtr, ptr, bytecodeSize, 0);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        if (status != 0)
        {
            throw new LuauException(Pop().ToString());
        }
    }
}