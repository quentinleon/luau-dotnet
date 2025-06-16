using System.Collections;
using System.Diagnostics.CodeAnalysis;
using static Luau.Native.NativeMethods;

namespace Luau;

public unsafe sealed class LuauTable : ILuauReference, IDisposable, IEnumerable<KeyValuePair<LuauValue, LuauValue>>
{
    public struct Enumerator(LuauTable table) : IEnumerator<KeyValuePair<LuauValue, LuauValue>>
    {
        KeyValuePair<LuauValue, LuauValue> current;
        public KeyValuePair<LuauValue, LuauValue> Current => current;

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            return table.TryMoveNext(current.Key, out current);
        }

        public void Dispose()
        {
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }
    }

    LuauState state;
    int reference;

    public bool IsDisposed => state == null || state.IsDisposed;

    LuauState ILuauReference.State => state;
    int ILuauReference.Reference => reference;

    public LuauValue this[LuauValue key]
    {
        get
        {
            ThrowIfDisposed();

            state.Push(this);
            state.Push(key);
            lua_gettable(state.AsPointer(), -2);
            var result = state.Pop();
            return result;
        }
        set
        {
            ThrowIfDisposed();

            var ptr = state!.AsPointer();
            state.Push(this);
            state.Push(key);
            state.Push(value);
            lua_settable(ptr, -3);
            lua_pop(ptr, 1);
        }
    }

    public int Count
    {
        get
        {
            ThrowIfDisposed();

            state.Push(this);
            lua_objlen(state.AsPointer(), -1);
            var len = state.Pop().Read<int>();
            return len;
        }
    }

    internal LuauTable(LuauState state, int reference)
    {
        this.state = state;
        this.reference = reference;
    }

    public bool TryMoveNext(LuauValue key, out KeyValuePair<LuauValue, LuauValue> result)
    {
        ThrowIfDisposed();

        var ptr = state.AsPointer();

        state.Push(this);
        state.Push(key);

        var status = lua_next(ptr, -2);
        if (status == 0)
        {
            lua_pop(ptr, 1);
            result = default;
            return false;
        }

        var value = state.Pop();
        var nextKey = state.Pop();
        lua_pop(ptr, 1);

        result = new(nextKey, value);
        return true;
    }

    public void Add(LuauValue key, LuauValue value)
    {
        this[key] = value;
    }

    public void Add(KeyValuePair<LuauValue, LuauValue> item)
    {
        this[item.Key] = item.Value;
    }

    public void Clear()
    {
        ThrowIfDisposed();

        var ptr = state.AsPointer();
        lua_getref(ptr, reference);
        lua_cleartable(ptr, -1);
    }

    public bool ContainsKey(LuauValue key)
    {
        return !this[key].IsNil;
    }

    public LuauValue RawGet(LuauValue key)
    {
        ThrowIfDisposed();

        state.Push(this);
        state.Push(key);

        var ptr = state.AsPointer();
        lua_rawget(ptr, -2);
        return state.Pop();
    }

    public void RawSet(LuauValue key, LuauValue value)
    {
        ThrowIfDisposed();

        state.Push(this);
        state.Push(key);
        state.Push(value);

        var ptr = state.AsPointer();
        lua_rawset(ptr, -3);
        lua_pop(ptr, 1);
    }

    public bool TryGetValue(LuauValue key, [MaybeNullWhen(false)] out LuauValue value)
    {
        value = this[key];
        return !value.IsNil;
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

    public Enumerator GetEnumerator()
    {
        return new(this);
    }

    IEnumerator<KeyValuePair<LuauValue, LuauValue>> IEnumerable<KeyValuePair<LuauValue, LuauValue>>.GetEnumerator()
    {
        return GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
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