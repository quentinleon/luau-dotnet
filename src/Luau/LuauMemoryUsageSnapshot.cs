namespace Luau;

/// <summary>
/// An immutable snapshot of native memory tracked for one root Luau VM.
/// </summary>
public readonly struct LuauMemoryUsageSnapshot
{
    internal static LuauMemoryUsageSnapshot Untracked { get; } = new(0, 0, null, isTracked: false);

    /// <summary>
    /// Initializes a memory usage snapshot.
    /// </summary>
    /// <param name="currentBytes">Currently allocated native VM bytes.</param>
    /// <param name="peakBytes">Highest observed native VM allocation.</param>
    /// <param name="limitBytes">Configured limit, or null for an unlimited VM.</param>
    public LuauMemoryUsageSnapshot(long currentBytes, long peakBytes, long? limitBytes = null)
        : this(currentBytes, peakBytes, limitBytes, isTracked: true)
    {
    }

    internal LuauMemoryUsageSnapshot(long currentBytes, long peakBytes, long? limitBytes, bool isTracked)
    {
        if (currentBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentBytes), currentBytes, "Current memory usage cannot be negative.");
        }

        if (peakBytes < currentBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(peakBytes), peakBytes, "Peak memory usage cannot be less than current usage.");
        }

        if (limitBytes.HasValue && limitBytes.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limitBytes), limitBytes, "A memory limit must be greater than zero.");
        }

        if (limitBytes.HasValue && peakBytes > limitBytes.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(peakBytes), peakBytes, "Peak memory usage cannot exceed the configured limit.");
        }

        CurrentBytes = currentBytes;
        PeakBytes = peakBytes;
        LimitBytes = limitBytes;
        IsTracked = isTracked;
    }

    /// <summary>
    /// Gets currently allocated native VM bytes.
    /// </summary>
    public long CurrentBytes { get; }

    /// <summary>
    /// Gets the highest observed native VM allocation in bytes.
    /// </summary>
    public long PeakBytes { get; }

    /// <summary>
    /// Gets the configured native VM limit in bytes, or null for an unlimited VM.
    /// </summary>
    public long? LimitBytes { get; }

    /// <summary>
    /// Gets whether the VM uses the tracked allocator. Compatibility states
    /// created without a memory limit intentionally report untracked usage.
    /// </summary>
    public bool IsTracked { get; }

    /// <summary>
    /// Gets whether this VM has a configured memory limit.
    /// </summary>
    public bool IsLimited => LimitBytes.HasValue;
}
