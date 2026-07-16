using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using static Luau.Internal.Interop.NativeMethods;

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
            var originalTop = luau_host_stack_get_top(pointer);
            try
            {
                LuauReferenceHelper.PushReference(state, access.Reference, "read Luau userdata");
                return luau_host_object_length(pointer, -1);
            }
            finally
            {
                state.SetTop(originalTop);
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
        var originalTop = luau_host_stack_get_top(pointer);

        try
        {
            LuauReferenceHelper.PushReference(state, access.Reference, "read Luau userdata");
            var size = luau_host_object_length(pointer, -1);

            if (size != sizeof(T))
            {
                result = default;
                return false;
            }

            var ptr = (T*)luau_host_to_userdata(pointer, -1);
            result = *ptr;
            return true;
        }
        finally
        {
            state.SetTop(originalTop);
        }
#pragma warning restore CS8500

    }

    public T Read<T>()
    {
        if (TryRead<T>(out var result)) return result;
        throw new InvalidOperationException($"Cannot convert {typeof(T)} to {typeof(T).Name}");
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
