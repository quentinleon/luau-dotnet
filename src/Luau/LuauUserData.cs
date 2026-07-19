using static Luau.Internal.Interop.NativeMethods;

namespace Luau;

/// <summary>
/// Represents opaque Luau userdata through a generation-checked VM reference.
/// Dispose owned instances deterministically; callback-borrowed instances
/// expire when their callback frame closes unless retained.
/// </summary>
public unsafe sealed class LuauUserData : ILuauReference, ILuauCallbackBorrowedReference, IDisposable
{
    LuauState? state;
    int reference;
    int disposeState;
    readonly object lifetimeGate = new();
    readonly LuauCallFrame? borrowedFrame;

    /// <summary>Gets whether this reference is valid only for its callback frame.</summary>
    public bool IsBorrowed => borrowedFrame != null;

    /// <summary>Gets whether this wrapper, its callback frame, or its owning state is no longer usable.</summary>
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

    /// <summary>Gets the native byte size of the userdata payload.</summary>
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

    internal LuauUserData(LuauState state, int reference, LuauCallFrame? borrowedFrame = null)
    {
        this.state = state;
        this.reference = reference;
        this.borrowedFrame = borrowedFrame;
        borrowedFrame?.RegisterBorrowed(this);
    }

    /// <summary>Creates an independently disposable owner for this same userdata.</summary>
    public LuauUserData Retain()
    {
        using var access = AcquireReference();
        return new LuauUserData(
            access.State,
            LuauReferenceHelper.RetainReference(
                access.State,
                access.Reference,
                "retain Luau userdata"));
    }

    /// <summary>Returns the Luau textual representation of this userdata.</summary>
    public override string ToString()
    {
        using var access = AcquireReference();
        return LuauReferenceHelper.RefToString(access.State, access.Reference);
    }

    /// <summary>Releases this wrapper's owned VM reference. The operation is idempotent.</summary>
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

    /// <summary>Releases the VM reference if the owner was not disposed deterministically.</summary>
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
        borrowedFrame?.EnsureBorrowedActive();
        var currentState = Volatile.Read(ref state);
        if (currentState == null || currentState.IsDisposed)
        {
            ThrowHelper.ThrowObjectDisposedException(nameof(LuauUserData));
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
                ThrowHelper.ThrowObjectDisposedException(nameof(LuauUserData));
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


    void ILuauCallbackBorrowedReference.InvalidateBorrowed() => DisposeCore();
}
