using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Luau.Internal.Interop;
using static Luau.Internal.Interop.NativeMethods;

namespace Luau;

public unsafe sealed class LuauBuffer : IDisposable, ILuauReference
{
    LuauState? state;
    int reference;
    int disposeState;
    readonly object lifetimeGate = new();

    LuauReferenceAccess ILuauReference.AcquireReference() => AcquireReference();
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

    internal LuauBuffer(LuauState state, int reference)
    {
        this.state = state;
        this.reference = reference;
    }

    public override string ToString()
    {
        using var access = AcquireReference();
        return LuauReferenceHelper.RefToString(access.State, access.Reference);
    }

    public Span<byte> AsSpan()
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
            return new Span<byte>(buffer, checked((int)length));
        }
        finally
        {
            state.SetTop(originalTop);
        }
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
        Monitor.Enter(lifetimeGate);
        try
        {
            var currentState = state;
            var currentReference = reference;
            if (disposeState != 0 || currentState == null || currentReference < 0 || currentState.IsDisposed)
            {
                ThrowHelper.ThrowObjectDisposedException(nameof(LuauBuffer));
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
