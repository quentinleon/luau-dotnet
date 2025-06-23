using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using static Luau.Native.NativeMethods;

namespace Luau;

public partial class LuauState
{
    public unsafe LuauFunction Load(ReadOnlySpan<byte> bytecode, ReadOnlySpan<char> chunkName)
    {
        ThrowIfDisposed();
        LoadInternal(bytecode, chunkName);
        var func = ToFunction(-1);
        return func;
    }

    public unsafe LuauFunction Load(ReadOnlySpan<byte> bytecode, ReadOnlySpan<byte> utf8ChunkName = default)
    {
        ThrowIfDisposed();
        LoadInternal(bytecode, utf8ChunkName);
        var func = ToFunction(-1);
        return func;
    }

    unsafe void LoadInternal(ReadOnlySpan<byte> bytecode, ReadOnlySpan<char> chunkName)
    {
        if (chunkName.IsEmpty)
        {
            LoadInternal(bytecode, ReadOnlySpan<byte>.Empty);
            return;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(chunkName.Length * 3 + 1);
        try
        {
            var utf8Count = Encoding.UTF8.GetBytes(chunkName, buffer);
            buffer[utf8Count] = 0;
            LoadInternal(bytecode, buffer.AsSpan(0, utf8Count));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    unsafe void LoadInternal(ReadOnlySpan<byte> bytecode, ReadOnlySpan<byte> chunkName)
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
            fixed (byte* ptr = bytecode, namePtr = chunkName)
            {
                status = luau_load(l, namePtr, ptr, bytecodeSize, 0);
            }
        }

        if (status != 0)
        {
            throw new LuauException(Pop().ToString());
        }
    }
}