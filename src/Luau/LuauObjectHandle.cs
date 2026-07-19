namespace Luau;

/// <summary>
/// An opaque, per-VM Luau userdata capability for one explicitly described
/// managed object. The target and registry token are never exposed publicly.
/// </summary>
public sealed class LuauObjectHandle : ILuauReference, IDisposable
{
    readonly object lifetimeGate = new();
    readonly LuauObjectToken token;
    LuauState? state;
    int disposeState;

    internal LuauObjectHandle(LuauState state, LuauObjectToken token)
    {
        this.state = state;
        this.token = token;
    }

    /// <summary>Gets whether this managed wrapper or its owning VM is disposed.</summary>
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

    /// <summary>Gets the root state that owns this per-VM capability.</summary>
    public LuauState State
    {
        get
        {
            var currentState = Volatile.Read(ref state);
            if (Volatile.Read(ref disposeState) != 0 || currentState == null || currentState.IsDisposed)
            {
                ThrowHelper.ThrowObjectDisposedException(nameof(LuauObjectHandle));
            }

            return currentState;
        }
    }

    LuauReferenceAccess ILuauReference.AcquireReference() => AcquireReference();

    /// <summary>Returns the generated capability type name without exposing its target.</summary>
    public override string ToString()
    {
        var currentState = State;
        return $"{currentState.Context.ObjectRegistry.ResolveDescriptor(token).TypeName} capability";
    }

    /// <summary>
    /// Releases this managed wrapper. Luau values that still reference the
    /// userdata remain valid until the VM collects them.
    /// </summary>
    public void Dispose()
    {
        LuauState? owningState;
        lock (lifetimeGate)
        {
            if (Interlocked.Exchange(ref disposeState, 1) != 0)
            {
                return;
            }

            owningState = Interlocked.Exchange(ref state, null);
        }

        if (owningState != null)
        {
            owningState.Context.ObjectRegistry.ReleaseWrapper(token, owningState);
        }

        GC.SuppressFinalize(this);
    }

    ~LuauObjectHandle()
    {
        try
        {
            Dispose();
        }
        catch
        {
            // Finalizers must never surface capability cleanup failures.
        }
    }

    LuauReferenceAccess AcquireReference()
    {
        var currentState = Volatile.Read(ref state);
        if (currentState == null || currentState.IsDisposed)
        {
            ThrowHelper.ThrowObjectDisposedException(nameof(LuauObjectHandle));
        }

        var nativeAccess = currentState.EnterNativeAccess();
        Monitor.Enter(lifetimeGate);
        try
        {
            if (disposeState != 0 || !ReferenceEquals(state, currentState) || currentState.IsDisposed)
            {
                ThrowHelper.ThrowObjectDisposedException(nameof(LuauObjectHandle));
            }

            var reference = currentState.Context.ObjectRegistry.GetReference(token);
            return new LuauReferenceAccess(currentState.GetMainThread(), reference, lifetimeGate, nativeAccess);
        }
        catch
        {
            Monitor.Exit(lifetimeGate);
            nativeAccess.Dispose();
            throw;
        }
    }
}

/// <summary>
/// Thrown when a state reaches its configured finite managed capability count.
/// Releasing Luau references and running garbage collection makes slots reusable.
/// </summary>
public sealed class LuauManagedHandleLimitException : LuauException
{
    /// <summary>Creates a managed-handle quota failure.</summary>
    public LuauManagedHandleLimitException(int limit)
        : base($"The Luau state reached its managed capability limit of {Validate(limit)} handles.")
    {
        Limit = limit;
    }

    /// <summary>Gets the configured per-state handle limit.</summary>
    public int Limit { get; }

    static int Validate(int limit)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "A managed capability limit must be positive.");
        }

        return limit;
    }
}
