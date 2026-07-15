namespace Luau;

/// <summary>
/// Configures optional budgets for one Luau execution or resume operation.
/// A <see langword="null"/> budget is unlimited.
/// </summary>
public sealed class LuauExecutionOptions
{
    TimeSpan? wallClockLimit;
    long? interruptCountLimit;
    int? maxResultCount;

    /// <summary>
    /// Gets an options instance with no execution budgets.
    /// </summary>
    public static LuauExecutionOptions Default { get; } = new();

    /// <summary>
    /// Gets the maximum wall-clock duration for an operation, or
    /// <see langword="null"/> when no wall-clock budget is configured.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The assigned duration is zero or negative.
    /// </exception>
    public TimeSpan? WallClockLimit
    {
        get => wallClockLimit;
        init
        {
            if (value.HasValue && value.Value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(WallClockLimit), value, "A wall-clock limit must be greater than zero.");
            }

            wallClockLimit = value;
        }
    }

    /// <summary>
    /// Gets the maximum number of interrupt callbacks permitted for an
    /// operation, or <see langword="null"/> when no interrupt-count budget is
    /// configured.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The assigned count is zero or negative.
    /// </exception>
    public long? InterruptCountLimit
    {
        get => interruptCountLimit;
        init
        {
            if (value.HasValue && value.Value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(InterruptCountLimit), value, "An interrupt-count limit must be greater than zero.");
            }

            interruptCountLimit = value;
        }
    }

    /// <summary>
    /// Gets the host scheduler used for asynchronous managed-callback dispatch
    /// and subsequent VM continuations, or <see langword="null"/> when no
    /// thread affinity is required.
    /// </summary>
    /// <remarks>
    /// Unity hosts should normally configure a
    /// <see cref="LuauSynchronizationContextScheduler"/> captured on the Unity
    /// main thread. This controls Luau's own continuation work; an async host
    /// callback that accesses thread-affine APIs after one of its own awaits
    /// must also preserve or explicitly restore the appropriate context.
    /// </remarks>
    public ILuauContinuationScheduler? ContinuationScheduler { get; init; }

    /// <summary>
    /// Gets the maximum number of values an operation may return, or
    /// <see langword="null"/> when result count is unlimited. This bounds both
    /// the Luau stack output and managed arrays created by convenience APIs.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The assigned count is negative.
    /// </exception>
    public int? MaxResultCount
    {
        get => maxResultCount;
        init
        {
            if (value.HasValue && value.Value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxResultCount), value, "A result-count limit cannot be negative.");
            }

            maxResultCount = value;
        }
    }

    /// <summary>
    /// Gets whether at least one execution budget is configured.
    /// </summary>
    public bool HasBudget => wallClockLimit.HasValue || interruptCountLimit.HasValue || maxResultCount.HasValue;
}
