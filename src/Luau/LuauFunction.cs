using Luau.Native;

namespace Luau;

public abstract class LuauFunction(LuauState state) : IDisposable
{
    LuauState? state = state;
    int disposeState;
    readonly object lifetimeGate = new();

    public LuauState State
    {
        get
        {
            lock (lifetimeGate)
            {
                var currentState = state;
                if (disposeState != 0 || currentState == null || currentState.IsDisposed)
                {
                    ThrowHelper.ThrowObjectDisposedException(nameof(LuauFunction));
                }

                return ResolvePublicState(currentState!);
            }
        }
    }

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

    protected LuauState OwningState => state!;

    /// <summary>
    /// Resolves the stack that callers should use when invoking this function.
    /// Managed callbacks retain their creating state; registry-backed script
    /// functions override this to use the VM root stack so a closure produced
    /// by a yielded coroutine can be invoked without resuming that coroutine.
    /// </summary>
    protected virtual LuauState ResolvePublicState(LuauState owningState) => owningState;

    public abstract ValueTask<int> InvokeAsync(int argumentCount, CancellationToken cancellationToken = default);
    public unsafe abstract void* AsPointer();
    public abstract lua_CFunction AsCFunction();

    protected virtual void DisposeCore() { }

    public void Dispose()
    {
        lock (lifetimeGate)
        {
            if (Interlocked.Exchange(ref disposeState, 1) != 0)
            {
                return;
            }

            var owningState = state;
            try
            {
                DisposeCore();
            }
            finally
            {
                owningState?.UnregisterDisposable(this);
                state = null;
                GC.SuppressFinalize(this);
            }
        }
    }

    protected void DisposeFromFinalizer()
    {
        try
        {
            Dispose();
        }
        catch
        {
            // A finalizer cannot safely report cleanup failures.
        }
    }

    protected void ThrowIfDiposed()
    {
        if (IsDisposed) ThrowHelper.ThrowObjectDisposedException(nameof(LuauFunction));
    }

    private protected LuauReferenceAccess AcquireReference(int reference)
    {
        Monitor.Enter(lifetimeGate);
        try
        {
            var currentState = state;
            if (disposeState != 0 || currentState == null || reference < 0 || currentState.IsDisposed)
            {
                ThrowHelper.ThrowObjectDisposedException(nameof(LuauFunction));
            }

            var referenceState = currentState!.GetMainThread();
            var nativeAccess = currentState.EnterNativeAccess();
            return new LuauReferenceAccess(referenceState, reference, lifetimeGate, nativeAccess);
        }
        catch
        {
            Monitor.Exit(lifetimeGate);
            throw;
        }
    }

    private protected LuauFunctionAccess AcquireFunctionAccess()
    {
        Monitor.Enter(lifetimeGate);
        try
        {
            var currentState = state;
            if (disposeState != 0 || currentState == null || currentState.IsDisposed)
            {
                ThrowHelper.ThrowObjectDisposedException(nameof(LuauFunction));
            }

            return new LuauFunctionAccess(currentState!, lifetimeGate);
        }
        catch
        {
            Monitor.Exit(lifetimeGate);
            throw;
        }
    }

    internal LuauFunctionAccess AcquireForPush()
    {
        return AcquireFunctionAccess();
    }
}

internal readonly ref struct LuauFunctionAccess
{
    readonly object lifetimeGate;

    internal LuauState State { get; }

    internal LuauFunctionAccess(LuauState state, object lifetimeGate)
    {
        State = state;
        this.lifetimeGate = lifetimeGate;
    }

    public void Dispose()
    {
        Monitor.Exit(lifetimeGate);
    }
}
