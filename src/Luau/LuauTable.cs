using System.Collections;
using System.Diagnostics.CodeAnalysis;
using static Luau.Internal.Interop.NativeMethods;

namespace Luau;

public unsafe sealed class LuauTable : ILuauReference, IDisposable, IEnumerable<KeyValuePair<LuauValue, LuauValue>>
{
    public struct Enumerator(LuauTable table) : IEnumerator<KeyValuePair<LuauValue, LuauValue>>
    {
        KeyValuePair<LuauValue, LuauValue> current;
        bool completed;
        public KeyValuePair<LuauValue, LuauValue> Current => current;

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (completed)
            {
                return false;
            }

            if (table.TryMoveNext(current.Key, out var next))
            {
                current = next;
                return true;
            }

            current = default;
            completed = true;
            return false;
        }

        public void Dispose()
        {
        }

        public void Reset()
        {
            current = default;
            completed = false;
        }
    }

    LuauState? state;
    int reference;
    int disposeState;
    readonly object lifetimeGate = new();

    public bool IsDisposed
    {
        get
        {
            if (Volatile.Read(ref disposeState) != 0)
            {
                return true;
            }

            var currentState = Volatile.Read(ref state);
            return currentState == null || currentState.IsDisposed;
        }
    }

    LuauReferenceAccess ILuauReference.AcquireReference() => AcquireReference();

    public LuauValue this[LuauValue key]
    {
        get
        {
            using var access = AcquireReference();
            var state = access.State;
            using var hostOperation = new LuauDirectHostOperationScope(state);
            var pointer = state.PointerUnsafe;
            state.Push(this);
            state.Push(key);

            var ignoredType = 0;
            LuauNativeProtection.Prepare(state.Context);
            var status = luau_host_table_get(pointer, -2, &ignoredType);
            LuauNativeProtection.ThrowIfFailed(state, pointer, status, "read a Luau table value");
            var result = state.ToValue(-1);
            hostOperation.CompleteAndRestore(
                "A direct host table read cannot yield or suspend the Luau thread.");
            return result;
        }
        set
        {
            using var access = AcquireReference();
            var state = access.State;
            using var hostOperation = new LuauDirectHostOperationScope(state);
            var pointer = state.PointerUnsafe;
            state.Push(this);
            state.Push(key);
            state.Push(value);

            LuauNativeProtection.Prepare(state.Context);
            var status = luau_host_table_set(pointer, -3);
            LuauNativeProtection.ThrowIfFailed(state, pointer, status, "write a Luau table value");
            hostOperation.CompleteAndRestore(
                "A direct host table write cannot yield or suspend the Luau thread.");
        }
    }

    /// <summary>
    /// Gets Luau's raw sequence length for this table. This is the value used
    /// by raw <c>#</c>/<c>lua_objlen</c> semantics, not a count of key/value
    /// entries.
    /// </summary>
    public int Length
    {
        get
        {
            using var access = AcquireReference();
            var state = access.State;
            var pointer = state.PointerUnsafe;
            var originalTop = luau_host_stack_get_top(pointer);
            try
            {
                state.Push(this);
                return luau_host_object_length(pointer, -1);
            }
            finally
            {
                state.SetTop(originalTop);
            }
        }
    }

    internal LuauTable(LuauState state, int reference)
    {
        this.state = state;
        this.reference = reference;
    }

    public LuauTable Clone()
    {
        using var access = AcquireReference();
        var state = access.State;
        var pointer = state.PointerUnsafe;
        var originalTop = luau_host_stack_get_top(pointer);
        try
        {
            state.Push(this);
            LuauNativeProtection.Prepare(state.Context);
            var status = luau_host_table_clone(pointer, -1);
            LuauNativeProtection.ThrowIfFailed(state, pointer, status, "clone a Luau table");
            return state.ToTable(-1);
        }
        finally
        {
            state.SetTop(originalTop);
        }
    }

    public bool TryMoveNext(LuauValue key, out KeyValuePair<LuauValue, LuauValue> result)
    {
        using var access = AcquireReference();
        var state = access.State;

        var pointer = state.PointerUnsafe;
        var originalTop = luau_host_stack_get_top(pointer);
        try
        {
            state.Push(this);
            state.Push(key);

            var hasNext = 0;
            LuauNativeProtection.Prepare(state.Context);
            var status = luau_host_table_next(pointer, -2, &hasNext);
            LuauNativeProtection.ThrowIfFailed(
                state,
                pointer,
                status,
                "enumerate a Luau table");
            if (hasNext == 0)
            {
                result = default;
                return false;
            }

            var value = state.ToValue(-1);
            var nextKey = state.ToValue(-2);
            result = new(nextKey, value);
            return true;
        }
        finally
        {
            state.SetTop(originalTop);
        }
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
        using var access = AcquireReference();
        var state = access.State;
        var pointer = state.PointerUnsafe;
        var originalTop = luau_host_stack_get_top(pointer);
        try
        {
            LuauReferenceHelper.PushReference(state, access.Reference, "clear a Luau table");
            LuauNativeProtection.Prepare(state.Context);
            var status = luau_host_table_clear(pointer, -1);
            LuauNativeProtection.ThrowIfFailed(state, pointer, status, "clear a Luau table");
        }
        finally
        {
            state.SetTop(originalTop);
        }
    }

    public bool ContainsKey(LuauValue key)
    {
        return !this[key].IsNil;
    }

    public LuauValue RawGet(LuauValue key)
    {
        using var access = AcquireReference();
        var state = access.State;
        var pointer = state.PointerUnsafe;
        var originalTop = luau_host_stack_get_top(pointer);
        try
        {
            state.Push(this);
            state.Push(key);

            var ignoredType = 0;
            LuauNativeProtection.Prepare(state.Context);
            var status = luau_host_table_raw_get(pointer, -2, &ignoredType);
            LuauNativeProtection.ThrowIfFailed(state, pointer, status, "read a raw Luau table value");
            return state.ToValue(-1);
        }
        finally
        {
            state.SetTop(originalTop);
        }
    }

    public void RawSet(LuauValue key, LuauValue value)
    {
        using var access = AcquireReference();
        var state = access.State;
        var pointer = state.PointerUnsafe;
        var originalTop = luau_host_stack_get_top(pointer);
        try
        {
            state.Push(this);
            state.Push(key);
            state.Push(value);

            LuauNativeProtection.Prepare(state.Context);
            var status = luau_host_table_raw_set(pointer, -3);
            LuauNativeProtection.ThrowIfFailed(state, pointer, status, "write a raw Luau table value");
        }
        finally
        {
            state.SetTop(originalTop);
        }
    }

    internal void SetReadOnly()
    {
        using var access = AcquireReference();
        var state = access.State;
        var pointer = state.PointerUnsafe;
        var originalTop = luau_host_stack_get_top(pointer);
        try
        {
            state.Push(this);
            LuauNativeProtection.Prepare(state.Context);
            var status = luau_host_table_set_readonly(pointer, -1, 1);
            LuauNativeProtection.ThrowIfFailed(
                state,
                pointer,
                status,
                "make a Luau table read-only");
        }
        finally
        {
            state.SetTop(originalTop);
        }
    }

    public bool TryGetValue(LuauValue key, [MaybeNullWhen(false)] out LuauValue value)
    {
        value = this[key];
        return !value.IsNil;
    }

    public override string ToString()
    {
        using var access = AcquireReference();
        return LuauReferenceHelper.RefToString(access.State, access.Reference);
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
        DisposeCore();
        GC.SuppressFinalize(this);
    }

    void DisposeCore()
    {
        LuauState? owningState;
        int currentReference;
        lock (lifetimeGate)
        {
            if (Interlocked.Exchange(ref disposeState, 1) != 0)
            {
                return;
            }

            owningState = Interlocked.Exchange(ref state, null);
            currentReference = Interlocked.Exchange(ref reference, -1);
        }

        if (owningState != null && currentReference >= 0)
        {
            owningState.TryReleaseReference(currentReference);
        }
    }

    ~LuauTable()
    {
        try
        {
            DisposeCore();
        }
        catch
        {
            // Finalizers must not surface cleanup failures.
        }
    }

    LuauReferenceAccess AcquireReference()
    {
        var currentState = Volatile.Read(ref state);
        if (currentState == null || currentState.IsDisposed)
        {
            ThrowHelper.ThrowObjectDisposedException(nameof(LuauTable));
        }

        var referenceState = currentState!.GetMainThread();
        var nativeAccess = currentState.EnterNativeAccess();
        Monitor.Enter(lifetimeGate);
        try
        {
            var currentReference = reference;
            if (disposeState != 0 ||
                !ReferenceEquals(state, currentState) ||
                currentReference < 0 ||
                currentState.IsDisposed)
            {
                ThrowHelper.ThrowObjectDisposedException(nameof(LuauTable));
            }

            return new LuauReferenceAccess(referenceState, currentReference, lifetimeGate, nativeAccess);
        }
        catch
        {
            Monitor.Exit(lifetimeGate);
            nativeAccess.Dispose();
            throw;
        }
    }
}
