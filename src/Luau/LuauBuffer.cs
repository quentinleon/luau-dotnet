using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Luau.Internal.Interop;
using static Luau.Internal.Interop.NativeMethods;

namespace Luau;

/// <summary>
/// Represents a mutable Luau byte buffer through a generation-checked VM
/// reference. Dispose owned instances deterministically; callback-borrowed
/// instances expire when their callback frame closes unless retained.
/// </summary>
public unsafe sealed class LuauBuffer : IDisposable, ILuauReference, ILuauCallbackBorrowedReference
{
    LuauState? state;
    int reference;
    int disposeState;
    readonly object lifetimeGate = new();
    readonly LuauCallFrame? borrowedFrame;

    /// <summary>Gets whether this reference is valid only for its callback frame.</summary>
    public bool IsBorrowed => borrowedFrame != null;

    LuauReferenceAccess ILuauReference.AcquireReference() => AcquireReference();
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

    /// <summary>Gets the current byte length of the VM buffer.</summary>
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
                ulong length;
                LuauReferenceHelper.PushReference(state, access.Reference, "read a Luau buffer");
                luau_host_to_buffer(pointer, -1, &length);
                return checked((int)length);
            }
            finally
            {
                state.SetTop(originalTop);
            }
        }
    }

    internal LuauBuffer(LuauState state, int reference, LuauCallFrame? borrowedFrame = null)
    {
        this.state = state;
        this.reference = reference;
        this.borrowedFrame = borrowedFrame;
        borrowedFrame?.RegisterBorrowed(this);
    }

    /// <summary>Creates an independently disposable owner for this same buffer.</summary>
    public LuauBuffer Retain()
    {
        using var access = AcquireReference();
        return new LuauBuffer(
            access.State,
            LuauReferenceHelper.RetainReference(
                access.State,
                access.Reference,
                "retain a Luau buffer"));
    }

    /// <summary>Returns the Luau textual representation of this buffer.</summary>
    public override string ToString()
    {
        using var access = AcquireReference();
        return LuauReferenceHelper.RefToString(access.State, access.Reference);
    }

    /// <summary>
    /// Copies the entire buffer into a managed array while the native buffer is
    /// protected by its lifetime and VM serialization gates.
    /// </summary>
    public byte[] ToArray()
    {
        using var access = AcquireReference();
        var state = access.State;

        var pointer = state.PointerUnsafe;
        var originalTop = luau_host_stack_get_top(pointer);
        try
        {
            ulong length;
            LuauReferenceHelper.PushReference(state, access.Reference, "read a Luau buffer");
            var buffer = luau_host_to_buffer(pointer, -1, &length);
            return new ReadOnlySpan<byte>(buffer, checked((int)length)).ToArray();
        }
        finally
        {
            state.SetTop(originalTop);
        }
    }

    /// <summary>
    /// Copies bytes from this buffer into <paramref name="destination"/>.
    /// </summary>
    public void Read(int sourceOffset, Span<byte> destination)
    {
        if (sourceOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceOffset));
        }

        using var access = AcquireReference();
        var state = access.State;
        var pointer = state.PointerUnsafe;
        var originalTop = luau_host_stack_get_top(pointer);
        try
        {
            ulong nativeLength;
            LuauReferenceHelper.PushReference(state, access.Reference, "read a Luau buffer");
            var buffer = luau_host_to_buffer(pointer, -1, &nativeLength);
            var length = checked((int)nativeLength);
            if (sourceOffset > length || destination.Length > length - sourceOffset)
            {
                throw new ArgumentException("The requested range exceeds the Luau buffer.", nameof(destination));
            }

            new ReadOnlySpan<byte>((byte*)buffer + sourceOffset, destination.Length).CopyTo(destination);
        }
        finally
        {
            state.SetTop(originalTop);
        }
    }

    /// <summary>
    /// Copies <paramref name="source"/> into this buffer.
    /// </summary>
    public void Write(int destinationOffset, ReadOnlySpan<byte> source)
    {
        if (destinationOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(destinationOffset));
        }

        using var access = AcquireReference();
        var state = access.State;
        var pointer = state.PointerUnsafe;
        var originalTop = luau_host_stack_get_top(pointer);
        try
        {
            ulong nativeLength;
            LuauReferenceHelper.PushReference(state, access.Reference, "write a Luau buffer");
            var buffer = luau_host_to_buffer(pointer, -1, &nativeLength);
            var length = checked((int)nativeLength);
            if (destinationOffset > length || source.Length > length - destinationOffset)
            {
                throw new ArgumentException("The requested range exceeds the Luau buffer.", nameof(source));
            }

            source.CopyTo(new Span<byte>((byte*)buffer + destinationOffset, source.Length));
        }
        finally
        {
            state.SetTop(originalTop);
        }
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
    ~LuauBuffer()
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
            ThrowHelper.ThrowObjectDisposedException(nameof(LuauBuffer));
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
                ThrowHelper.ThrowObjectDisposedException(nameof(LuauBuffer));
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
