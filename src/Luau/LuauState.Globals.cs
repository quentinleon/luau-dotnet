using System.Buffers;
using System.Text;
using static Luau.Native.NativeMethods;

namespace Luau;

public unsafe partial class LuauState
{
    public LuauValue this[ReadOnlySpan<byte> key]
    {
        get
        {
            ThrowIfDisposed();

            fixed (byte* s = key)
            {
                lua_getglobal(l, s);
            }

            return Pop();
        }
        set
        {
            ThrowIfDisposed();

            Push(value);

            fixed (byte* s = key)
            {
                lua_setglobal(l, s);
            }
        }
    }

    public LuauValue this[string key]
    {
        get
        {
            var buffer = ArrayPool<byte>.Shared.Rent(key.Length * 3 + 1);
            try
            {
                var count = Encoding.UTF8.GetBytes(key, buffer);
                buffer[count] = 0;
                return this[buffer.AsSpan(0, count)];
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        set
        {
            var buffer = ArrayPool<byte>.Shared.Rent(key.Length * 3 + 1);
            try
            {
                var count = Encoding.UTF8.GetBytes(key, buffer);
                buffer[count] = 0;
                this[buffer.AsSpan(0, count)] = value;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}