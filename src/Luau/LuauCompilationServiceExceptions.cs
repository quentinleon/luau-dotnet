using System.Globalization;

namespace Luau;

/// <summary>Identifies a compilation-service resource dimension.</summary>
public enum LuauCompilationLimitKind
{
    /// <summary>UTF-8 source bytes in one request.</summary>
    SourceBytesPerRequest = 0,

    /// <summary>Admitted requests that have not completed.</summary>
    QueuedRequestCount = 1,

    /// <summary>Aggregate source bytes for admitted, incomplete requests.</summary>
    QueuedSourceBytes = 2,

    /// <summary>Bytecode bytes in one compiler result.</summary>
    BytecodeBytesPerResult = 3,
}

/// <summary>
/// Reports a request rejected by a finite compilation-service resource bound.
/// </summary>
public sealed class LuauCompilationLimitException : LuauException
{
    /// <summary>Initializes a compilation-service limit failure.</summary>
    public LuauCompilationLimitException(
        LuauCompilationLimitKind limitKind,
        long actual,
        long limit)
        : base(CreateMessage(limitKind, actual, limit))
    {
        if (limitKind < LuauCompilationLimitKind.SourceBytesPerRequest ||
            limitKind > LuauCompilationLimitKind.BytecodeBytesPerResult)
        {
            throw new ArgumentOutOfRangeException(nameof(limitKind), limitKind, "Unknown compilation limit.");
        }
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "A compilation limit must be positive.");
        }
        if (actual <= limit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(actual),
                actual,
                "The observed value must exceed the configured limit.");
        }

        LimitKind = limitKind;
        Actual = actual;
        Limit = limit;
    }

    /// <summary>Gets the resource dimension that rejected the request.</summary>
    public LuauCompilationLimitKind LimitKind { get; }

    /// <summary>Gets the observed count or byte size.</summary>
    public long Actual { get; }

    /// <summary>Gets the configured maximum count or byte size.</summary>
    public long Limit { get; }

    static string CreateMessage(LuauCompilationLimitKind limitKind, long actual, long limit)
    {
        var label = limitKind switch
        {
            LuauCompilationLimitKind.SourceBytesPerRequest => "Source size",
            LuauCompilationLimitKind.QueuedRequestCount => "Queued request count",
            LuauCompilationLimitKind.QueuedSourceBytes => "Queued source size",
            LuauCompilationLimitKind.BytecodeBytesPerResult => "Bytecode result size",
            _ => "Compilation resource",
        };
        return $"{label} of {actual.ToString(CultureInfo.InvariantCulture)} exceeds the configured " +
            $"limit of {limit.ToString(CultureInfo.InvariantCulture)}.";
    }
}

/// <summary>
/// Reports that compiler workers, admitted-request publication, or active
/// native calls did not drain within the finite disposal period. Workers
/// remain un-aborted and continue draining. A later disposal attempt waits for
/// the same shutdown again and can complete after the workers exit.
/// </summary>
public sealed class LuauCompilationShutdownException : TimeoutException
{
    /// <summary>Initializes an incomplete-shutdown report.</summary>
    public LuauCompilationShutdownException(TimeSpan timeout, int activeRequestCount)
        : base(CreateMessage(timeout, activeRequestCount))
    {
        if (timeout <= TimeSpan.Zero || timeout == System.Threading.Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "The timeout must be finite and positive.");
        }
        if (activeRequestCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(activeRequestCount),
                activeRequestCount,
                "The active request count cannot be negative.");
        }

        Timeout = timeout;
        ActiveRequestCount = activeRequestCount;
    }

    /// <summary>Gets the configured drain timeout.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>
    /// Gets the active native-call count observed at timeout. A zero value can
    /// mean worker exit or cancellation-registration cleanup remained pending.
    /// </summary>
    public int ActiveRequestCount { get; }

    static string CreateMessage(TimeSpan timeout, int activeRequestCount)
    {
        return $"Luau compiler shutdown did not finish within " +
            $"{timeout.TotalMilliseconds.ToString(CultureInfo.InvariantCulture)} ms; " +
            $"{activeRequestCount.ToString(CultureInfo.InvariantCulture)} native compilation request(s) were active " +
            "when observed; worker exit or request cleanup may also remain pending. " +
            "The worker threads were not aborted and will continue draining.";
    }
}
