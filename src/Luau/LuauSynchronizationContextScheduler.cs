namespace Luau;

/// <summary>
/// Dispatches Luau continuations through a captured
/// <see cref="SynchronizationContext"/>.
/// </summary>
/// <remarks>
/// Construct or capture this scheduler on the thread owned by the supplied
/// synchronization context. In Unity, that is normally the main thread.
/// </remarks>
public sealed class LuauSynchronizationContextScheduler : ILuauContinuationScheduler
{
    readonly SynchronizationContext synchronizationContext;
    readonly int ownerManagedThreadId;

    /// <summary>
    /// Creates a scheduler for <paramref name="synchronizationContext"/> and
    /// records the current thread as its owning thread.
    /// </summary>
    public LuauSynchronizationContextScheduler(SynchronizationContext synchronizationContext)
    {
        this.synchronizationContext = synchronizationContext
            ?? throw new ArgumentNullException(nameof(synchronizationContext));
        ownerManagedThreadId = Environment.CurrentManagedThreadId;
    }

    /// <summary>
    /// Gets the synchronization context used for dispatch.
    /// </summary>
    public SynchronizationContext SynchronizationContext => synchronizationContext;

    /// <summary>
    /// Gets the managed thread ID captured when this scheduler was created.
    /// </summary>
    public int OwnerManagedThreadId => ownerManagedThreadId;

    /// <summary>
    /// Captures <see cref="SynchronizationContext.Current"/> on its owning
    /// thread.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The current thread has no synchronization context.
    /// </exception>
    public static LuauSynchronizationContextScheduler CaptureCurrent()
    {
        var current = SynchronizationContext.Current;
        if (current == null)
        {
            throw new InvalidOperationException(
                "The current thread has no SynchronizationContext to capture.");
        }

        return new LuauSynchronizationContextScheduler(current);
    }

    /// <inheritdoc/>
    public bool CheckAccess()
    {
        return Environment.CurrentManagedThreadId == ownerManagedThreadId;
    }

    /// <inheritdoc/>
    public void Post(Action continuation)
    {
        if (continuation == null)
        {
            throw new ArgumentNullException(nameof(continuation));
        }

        synchronizationContext.Post(
            static state => ((Action)state!).Invoke(),
            continuation);
    }
}
