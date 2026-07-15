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
            using var hostOperation = state.BeginHostOperationIfNeeded();
            var pointer = state.PointerUnsafe;
            var originalTop = lua_gettop(pointer);
            var restoreStack = true;
            var resetAttempted = false;
            try
            {
                state.Push(this);
                state.Push(key);

                var ignoredType = 0;
                LuauNativeProtection.Prepare(state.Context);
                var status = luau_ffi_protected_gettable(pointer, -2, &ignoredType);
                LuauNativeProtection.ThrowIfFailed(state, pointer, status, "read a Luau table value");
                if (hostOperation.IsOwnedOperationSuspended)
                {
                    restoreStack = false;
                    resetAttempted = true;
                    hostOperation.AbortSuspendedOperation();
                    throw new LuauException("A direct host table read cannot yield or suspend the Luau thread.");
                }

                return state.ToValue(-1);
            }
            catch
            {
                if (!resetAttempted && hostOperation.IsOwnedOperationSuspended)
                {
                    restoreStack = false;
                    resetAttempted = true;
                    hostOperation.AbortSuspendedOperation();
                }

                throw;
            }
            finally
            {
                if (restoreStack)
                {
                    lua_settop(pointer, originalTop);
                }
            }
        }
        set
        {
            using var access = AcquireReference();
            var state = access.State;
            using var hostOperation = state.BeginHostOperationIfNeeded();
            var pointer = state.PointerUnsafe;
            var originalTop = lua_gettop(pointer);
            var restoreStack = true;
            var resetAttempted = false;
            try
            {
                state.Push(this);
                state.Push(key);
                state.Push(value);

                LuauNativeProtection.Prepare(state.Context);
                var status = luau_ffi_protected_settable(pointer, -3);
                LuauNativeProtection.ThrowIfFailed(state, pointer, status, "write a Luau table value");
                if (hostOperation.IsOwnedOperationSuspended)
                {
                    restoreStack = false;
                    resetAttempted = true;
                    hostOperation.AbortSuspendedOperation();
                    throw new LuauException("A direct host table write cannot yield or suspend the Luau thread.");
                }
            }
            catch
            {
                if (!resetAttempted && hostOperation.IsOwnedOperationSuspended)
                {
                    restoreStack = false;
                    resetAttempted = true;
                    hostOperation.AbortSuspendedOperation();
                }

                throw;
            }
            finally
            {
                if (restoreStack)
                {
                    lua_settop(pointer, originalTop);
                }
            }
        }
    }

    public int Count
    {
        get
        {
            using var access = AcquireReference();
            var state = access.State;
            var pointer = state.PointerUnsafe;
            var originalTop = lua_gettop(pointer);
            try
            {
                state.Push(this);
                return lua_objlen(pointer, -1);
            }
            finally
            {
                lua_settop(pointer, originalTop);
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
        var originalTop = lua_gettop(pointer);
        try
        {
            state.Push(this);
            LuauNativeProtection.Prepare(state.Context);
            var status = luau_ffi_protected_clonetable(pointer, -1);
            LuauNativeProtection.ThrowIfFailed(state, pointer, status, "clone a Luau table");
            return state.ToTable(-1);
        }
        finally
        {
            lua_settop(pointer, originalTop);
        }
    }

    public bool TryMoveNext(LuauValue key, out KeyValuePair<LuauValue, LuauValue> result)
    {
        using var access = AcquireReference();
        var state = access.State;

        var pointer = state.PointerUnsafe;
        var originalTop = lua_gettop(pointer);
        try
        {
            state.Push(this);
            state.Push(key);

            var hasNext = 0;
            LuauNativeProtection.Prepare(state.Context);
            var status = luau_ffi_protected_next(pointer, -2, &hasNext);
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
            lua_settop(pointer, originalTop);
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
        var originalTop = lua_gettop(pointer);
        try
        {
            LuauReferenceHelper.PushReference(state, access.Reference, "clear a Luau table");
            LuauNativeProtection.Prepare(state.Context);
            var status = luau_ffi_protected_cleartable(pointer, -1);
            LuauNativeProtection.ThrowIfFailed(state, pointer, status, "clear a Luau table");
        }
        finally
        {
            lua_settop(pointer, originalTop);
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
        var originalTop = lua_gettop(pointer);
        try
        {
            state.Push(this);
            state.Push(key);

            var ignoredType = 0;
            LuauNativeProtection.Prepare(state.Context);
            var status = luau_ffi_protected_rawget(pointer, -2, &ignoredType);
            LuauNativeProtection.ThrowIfFailed(state, pointer, status, "read a raw Luau table value");
            return state.ToValue(-1);
        }
        finally
        {
            lua_settop(pointer, originalTop);
        }
    }

    public void RawSet(LuauValue key, LuauValue value)
    {
        using var access = AcquireReference();
        var state = access.State;
        var pointer = state.PointerUnsafe;
        var originalTop = lua_gettop(pointer);
        try
        {
            state.Push(this);
            state.Push(key);
            state.Push(value);

            LuauNativeProtection.Prepare(state.Context);
            var status = luau_ffi_protected_rawset(pointer, -3);
            LuauNativeProtection.ThrowIfFailed(state, pointer, status, "write a raw Luau table value");
        }
        finally
        {
            lua_settop(pointer, originalTop);
        }
    }

    public bool TryGetValue(LuauValue key, [MaybeNullWhen(false)] out LuauValue value)
    {
        value = this[key];
        return !value.IsNil;
    }

    public void* AsPointer()
    {
        using var access = AcquireReference();
        return LuauReferenceHelper.GetRefPointer(access.State, access.Reference);
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
        lock (lifetimeGate)
        {
            if (Interlocked.Exchange(ref disposeState, 1) != 0)
            {
                return;
            }

            var owningState = Interlocked.Exchange(ref state, null);
            var currentReference = Interlocked.Exchange(ref reference, -1);
            if (owningState != null && currentReference >= 0)
            {
                owningState.TryReleaseReference(currentReference);
            }
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
        Monitor.Enter(lifetimeGate);
        try
        {
            var currentState = state;
            var currentReference = reference;
            if (disposeState != 0 || currentState == null || currentReference < 0 || currentState.IsDisposed)
            {
                ThrowHelper.ThrowObjectDisposedException(nameof(LuauTable));
            }

            var referenceState = currentState!.GetMainThread();
            var nativeAccess = currentState.EnterNativeAccess();
            return new LuauReferenceAccess(referenceState, currentReference, lifetimeGate, nativeAccess);
        }
        catch
        {
            Monitor.Exit(lifetimeGate);
            throw;
        }
    }
}
