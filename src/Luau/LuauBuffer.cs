using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Luau.Native;
using static Luau.Native.NativeMethods;

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
            var originalTop = lua_gettop(pointer);
            try
            {
                nuint length;
                LuauReferenceHelper.PushReference(state, access.Reference, "read a Luau buffer");
                lua_tobuffer(pointer, -1, &length);
                return checked((int)length);
            }
            finally
            {
                lua_settop(pointer, originalTop);
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

    [Obsolete(LuauCompatibilityDiagnostics.NativePointer)]
    public void* AsPointer()
    {
        using var access = AcquireReference();
        return LuauReferenceHelper.GetRefPointer(access.State, access.Reference);
    }

    public Span<byte> AsSpan()
    {
        using var access = AcquireReference();
        var state = access.State;

        var pointer = state.PointerUnsafe;
        var originalTop = lua_gettop(pointer);
        try
        {
            nuint length;
            LuauReferenceHelper.PushReference(state, access.Reference, "read a Luau buffer");
            var buffer = lua_tobuffer(pointer, -1, &length);
            return new Span<byte>(buffer, checked((int)length));
        }
        finally
        {
            lua_settop(pointer, originalTop);
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
