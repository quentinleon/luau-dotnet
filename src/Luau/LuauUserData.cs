using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using static Luau.Native.NativeMethods;

namespace Luau;

public unsafe sealed class LuauUserData : ILuauReference, IDisposable
{
    LuauState state;
    int reference;

    public bool IsDisposed => state == null || state.IsDisposed;

    LuauState ILuauReference.State => state;
    int ILuauReference.Reference => reference;

    public int Size
    {
        get
        {
            ThrowIfDisposed();
            var l = state.AsPointer();
            lua_getref(l, reference);
            var size = lua_objlen(l, -1);
            lua_pop(l, 1);
            return size;
        }
    }

    internal LuauUserData(LuauState state, int reference)
    {
        this.state = state;
        this.reference = reference;
    }

    public bool TryRead<T>([NotNullWhen(true)] out T? result)
    {
        ThrowIfDisposed();

        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            result = default;
            return false;
        }

#pragma warning disable CS8500
        var l = state.AsPointer();

        lua_getref(l, reference);
        var size = lua_objlen(l, -1);

        if (size != sizeof(T))
        {
            result = default;
            return false;
        }

        var ptr = (T*)lua_touserdata(l, -1);
        result = *ptr;
        lua_pop(l, 1);

        return true;
#pragma warning restore CS8500

    }

    public T Read<T>()
    {
        if (TryRead<T>(out var result)) return result;
        throw new InvalidOperationException($"Cannot convert {typeof(T)} to {typeof(T).Name}");
    }

    public void* AsPointer()
    {
        ThrowIfDisposed();
        return LuauReferenceHelper.GetRefPointer(state, reference);
    }

    public override string ToString()
    {
        ThrowIfDisposed();
        return LuauReferenceHelper.RefToString(state, reference);
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