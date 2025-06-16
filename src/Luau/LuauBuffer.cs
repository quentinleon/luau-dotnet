using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Luau.Native;
using static Luau.Native.NativeMethods;

namespace Luau;

public unsafe sealed class LuauBuffer : IDisposable, ILuauReference
{
    LuauState state;
    int reference;

    LuauState ILuauReference.State => state;
    int ILuauReference.Reference => reference;
    public bool IsDisposed => state == null || state.IsDisposed;

    public int Length
    {
        get
        {
            ThrowIfDisposed();

            var l = state.AsPointer();
            nuint len;
            lua_getref(l, reference);
            lua_tobuffer(l, -1, &len);
            lua_pop(l, 1);

            return (int)len;
        }
    }

    internal LuauBuffer(LuauState state, int reference)
    {
        this.state = state;
        this.reference = reference;
    }

    public override string ToString()
    {
        ThrowIfDisposed();
        return LuauReferenceHelper.RefToString(state, reference);
    }

    public void* AsPointer()
    {
        ThrowIfDisposed();
        return LuauReferenceHelper.GetRefPointer(state, reference);
    }

    public Span<byte> AsSpan()
    {
        ThrowIfDisposed();

        var l = state.AsPointer();
        nuint len;
        lua_getref(l, reference);
        var ptr = lua_tobuffer(l, -1, &len);
        lua_pop(l, 1);

        return new Span<byte>(ptr, (int)len);
    }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            lua_unref(state.AsPointer(), reference);
            state = null!;
        }
    }

    void ThrowIfDisposed()
    {
        if (IsDisposed) ThrowHelper.ThrowObjectDisposedException(nameof(LuauTable));
    }
}