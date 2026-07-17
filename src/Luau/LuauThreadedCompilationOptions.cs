namespace Luau;

/// <summary>
/// Immutable resource policy for <see cref="LuauThreadedCompilationService"/>.
/// </summary>
public sealed class LuauThreadedCompilationOptions
{
    int workerCount = 1;
    int maxQueuedRequestCount = 16;
    long maxQueuedSourceBytes = 4L * 1024 * 1024;
    int maxSourceBytesPerRequest = 1024 * 1024;
    int maxBytecodeBytesPerResult = 4 * 1024 * 1024;
    TimeSpan shutdownTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Gets the conservative, finite standalone service defaults.</summary>
    public static LuauThreadedCompilationOptions Default { get; } = new();

    /// <summary>
    /// Gets the number of dedicated compiler workers. The first backend
    /// supports one worker by default and an explicitly selected second worker.
    /// </summary>
    public int WorkerCount
    {
        get => workerCount;
        init
        {
            if (value is < 1 or > 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(WorkerCount),
                    value,
                    "The threaded compiler supports one or two workers.");
            }

            workerCount = value;
        }
    }

    /// <summary>
    /// Gets the maximum admitted requests that have not completed, including
    /// requests currently owned by workers.
    /// </summary>
    public int MaxQueuedRequestCount
    {
        get => maxQueuedRequestCount;
        init
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxQueuedRequestCount),
                    value,
                    "The queued-request limit must be greater than zero.");
            }

            maxQueuedRequestCount = value;
        }
    }

    /// <summary>
    /// Gets the aggregate source-byte limit for admitted requests that have
    /// not completed, including requests currently compiling.
    /// </summary>
    public long MaxQueuedSourceBytes
    {
        get => maxQueuedSourceBytes;
        init
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxQueuedSourceBytes),
                    value,
                    "The queued-source limit must be greater than zero.");
            }

            maxQueuedSourceBytes = value;
        }
    }

    /// <summary>Gets the source-byte limit for one request.</summary>
    public int MaxSourceBytesPerRequest
    {
        get => maxSourceBytesPerRequest;
        init
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxSourceBytesPerRequest),
                    value,
                    "The per-request source limit must be greater than zero.");
            }

            maxSourceBytesPerRequest = value;
        }
    }

    /// <summary>
    /// Gets the maximum bytecode payload accepted from one native compilation.
    /// Native intermediate allocations are not included.
    /// </summary>
    public int MaxBytecodeBytesPerResult
    {
        get => maxBytecodeBytesPerResult;
        init
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxBytecodeBytesPerResult),
                    value,
                    "The per-result bytecode limit must be greater than zero.");
            }

            maxBytecodeBytesPerResult = value;
        }
    }

    /// <summary>
    /// Gets the finite period disposal waits for active native calls. Timing
    /// out reports incomplete shutdown but never aborts a compiler thread.
    /// </summary>
    public TimeSpan ShutdownTimeout
    {
        get => shutdownTimeout;
        init
        {
            if (value <= TimeSpan.Zero ||
                value == Timeout.InfiniteTimeSpan ||
                value.TotalMilliseconds > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ShutdownTimeout),
                    value,
                    $"The shutdown timeout must be between one tick and {int.MaxValue} milliseconds.");
            }

            shutdownTimeout = value;
        }
    }

    internal LuauThreadedCompilationOptions Snapshot()
    {
        return new LuauThreadedCompilationOptions
        {
            WorkerCount = WorkerCount,
            MaxQueuedRequestCount = MaxQueuedRequestCount,
            MaxQueuedSourceBytes = MaxQueuedSourceBytes,
            MaxSourceBytesPerRequest = MaxSourceBytesPerRequest,
            MaxBytecodeBytesPerResult = MaxBytecodeBytesPerResult,
            ShutdownTimeout = ShutdownTimeout,
        };
    }
}
