namespace Luau;

/// <summary>
/// Dispatches asynchronous Luau execution continuations to a host-owned
/// execution context.
/// </summary>
/// <remarks>
/// Unity hosts should normally use <see cref="LuauSynchronizationContextScheduler"/>
/// captured on the Unity main thread. Implementations must deliver each
/// successfully posted continuation exactly once.
/// </remarks>
public interface ILuauContinuationScheduler
{
    /// <summary>
    /// Gets whether the caller is already running on this scheduler's target
    /// execution context.
    /// </summary>
    bool CheckAccess();

    /// <summary>
    /// Posts a continuation to the target execution context.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="continuation"/> is <see langword="null"/>.
    /// </exception>
    void Post(Action continuation);
}
