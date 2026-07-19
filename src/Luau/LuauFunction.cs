namespace Luau;

public abstract class LuauFunction : IDisposable
{
    LuauState? state;
    int disposeState;
    readonly object lifetimeGate = new();

    internal LuauFunction(LuauState state)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
    }

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

    private protected LuauState OwningState => state!;

    /// <summary>
    /// Invokes a script closure synchronously on its owning VM root.
    /// Managed callback functions are host capabilities and cannot be invoked
    /// directly by managed callers.
    /// </summary>
    public LuauValue[] Invoke(
        ReadOnlySpan<LuauValue> arguments = default,
        LuauExecutionOptions? executionOptions = null)
    {
        if (this is not LuauScriptFunction scriptFunction)
        {
            throw CreateHostInvocationCapabilityException();
        }

        return scriptFunction.InvokeWithArguments(arguments, executionOptions);
    }

    /// <summary>
    /// Invokes a script closure asynchronously on its owning VM root.
    /// Managed callback functions are host capabilities and cannot be invoked
    /// directly by managed callers.
    /// </summary>
    public ValueTask<LuauValue[]> InvokeAsync(
        ReadOnlyMemory<LuauValue> arguments = default,
        CancellationToken cancellationToken = default,
        LuauExecutionOptions? executionOptions = null)
    {
        if (this is not LuauScriptFunction scriptFunction)
        {
            throw CreateHostInvocationCapabilityException();
        }

        return scriptFunction.InvokeWithArgumentsAsync(
            arguments,
            cancellationToken,
            executionOptions);
    }

    /// <summary>
    /// Resolves the stack that callers should use when invoking this function.
    /// Managed callbacks retain their creating state; registry-backed script
    /// functions override this to use the VM root stack so a closure produced
    /// by a yielded coroutine can be invoked without resuming that coroutine.
    /// </summary>
    private protected virtual LuauState ResolvePublicState(LuauState owningState) => owningState;

    private protected virtual void DisposeCore() { }

    public void Dispose()
    {
        LuauState? owningState;
        lock (lifetimeGate)
        {
            if (Interlocked.Exchange(ref disposeState, 1) != 0)
            {
                return;
            }

            owningState = state;
        }

        try
        {
            DisposeCore();
        }
        finally
        {
            owningState?.UnregisterDisposable(this);
            lock (lifetimeGate)
            {
                state = null;
            }
            GC.SuppressFinalize(this);
        }
    }

    private protected void DisposeFromFinalizer()
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

    private protected void ThrowIfDisposed()
    {
        if (IsDisposed) ThrowHelper.ThrowObjectDisposedException(nameof(LuauFunction));
    }

    private protected LuauReferenceAccess AcquireReference(int reference)
    {
        var currentState = Volatile.Read(ref state);
        if (currentState == null || currentState.IsDisposed)
        {
            ThrowHelper.ThrowObjectDisposedException(nameof(LuauFunction));
        }

        var referenceState = currentState!.GetMainThread();
        var nativeAccess = currentState.EnterNativeAccess();
        Monitor.Enter(lifetimeGate);
        try
        {
            if (disposeState != 0 ||
                !ReferenceEquals(state, currentState) ||
                reference < 0 ||
                currentState.IsDisposed)
            {
                ThrowHelper.ThrowObjectDisposedException(nameof(LuauFunction));
            }

            return new LuauReferenceAccess(referenceState, reference, lifetimeGate, nativeAccess);
        }
        catch
        {
            Monitor.Exit(lifetimeGate);
            nativeAccess.Dispose();
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

    static InvalidOperationException CreateHostInvocationCapabilityException()
    {
        return new InvalidOperationException(
            "Managed callback functions are host capabilities that can only be invoked by Luau code.");
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
