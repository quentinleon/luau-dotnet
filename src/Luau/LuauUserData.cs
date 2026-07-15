using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using static Luau.Native.NativeMethods;

namespace Luau;

public unsafe sealed class LuauUserData : ILuauReference, IDisposable
{
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

    public int Size
    {
        get
        {
            using var access = AcquireReference();
            var state = access.State;
            var pointer = state.PointerUnsafe;
            var originalTop = lua_gettop(pointer);
            try
            {
                LuauReferenceHelper.PushReference(state, access.Reference, "read Luau userdata");
                return lua_objlen(pointer, -1);
            }
            finally
            {
                lua_settop(pointer, originalTop);
            }
        }
    }

    internal LuauUserData(LuauState state, int reference)
    {
        this.state = state;
        this.reference = reference;
    }

    public bool TryRead<T>([NotNullWhen(true)] out T? result)
    {
        using var access = AcquireReference();
        var state = access.State;

        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            result = default;
            return false;
        }

#pragma warning disable CS8500
        var pointer = state.PointerUnsafe;
        var originalTop = lua_gettop(pointer);

        try
        {
            LuauReferenceHelper.PushReference(state, access.Reference, "read Luau userdata");
            var size = lua_objlen(pointer, -1);

            if (size != sizeof(T))
            {
                result = default;
                return false;
            }

            var ptr = (T*)lua_touserdata(pointer, -1);
            result = *ptr;
            return true;
        }
        finally
        {
            lua_settop(pointer, originalTop);
        }
#pragma warning restore CS8500

    }

    public T Read<T>()
    {
        if (TryRead<T>(out var result)) return result;
        throw new InvalidOperationException($"Cannot convert {typeof(T)} to {typeof(T).Name}");
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

    ~LuauUserData()
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
                ThrowHelper.ThrowObjectDisposedException(nameof(LuauUserData));
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
