namespace Luau;

/// <summary>Bounds artifact construction, encoding, and parsing.</summary>
public sealed record LuauArtifactLimits
{
    int? maxEnvelopeBytes = 8 * 1024 * 1024;
    int? maxBytecodeBytes = 4 * 1024 * 1024;
    int? maxProvenanceBytes = 64 * 1024;
    int? maxProvenanceIdBytes = 256;
    int? maxSourceIdentityBytes = 1024;

    /// <summary>Gets the finite limits used by default.</summary>
    public static LuauArtifactLimits Default { get; } = new();

    /// <summary>Gets an explicitly unsafe, unbounded artifact profile.</summary>
    public static LuauArtifactLimits UnsafeUnbounded { get; } = new()
    {
        MaxEnvelopeBytes = null,
        MaxBytecodeBytes = null,
        MaxProvenanceBytes = null,
        MaxProvenanceIdBytes = null,
        MaxSourceIdentityBytes = null,
    };

    /// <summary>Gets the maximum complete encoded envelope size.</summary>
    public int? MaxEnvelopeBytes
    {
        get => maxEnvelopeBytes;
        init => maxEnvelopeBytes = Validate(value, nameof(MaxEnvelopeBytes));
    }

    /// <summary>Gets the maximum bytecode payload size.</summary>
    public int? MaxBytecodeBytes
    {
        get => maxBytecodeBytes;
        init => maxBytecodeBytes = Validate(value, nameof(MaxBytecodeBytes));
    }

    /// <summary>Gets the maximum opaque provenance payload size.</summary>
    public int? MaxProvenanceBytes
    {
        get => maxProvenanceBytes;
        init => maxProvenanceBytes = Validate(value, nameof(MaxProvenanceBytes));
    }

    /// <summary>Gets the maximum UTF-8 provenance identifier size.</summary>
    public int? MaxProvenanceIdBytes
    {
        get => maxProvenanceIdBytes;
        init => maxProvenanceIdBytes = Validate(value, nameof(MaxProvenanceIdBytes));
    }

    /// <summary>Gets the maximum UTF-8 source identity size.</summary>
    public int? MaxSourceIdentityBytes
    {
        get => maxSourceIdentityBytes;
        init => maxSourceIdentityBytes = Validate(value, nameof(MaxSourceIdentityBytes));
    }

    static int? Validate(int? value, string name)
    {
        if (value.HasValue && value.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "An artifact limit must be positive.");
        }

        return value;
    }
}

/// <summary>Identifies why an artifact envelope was rejected.</summary>
public enum LuauArtifactFailureKind
{
    /// <summary>The envelope is truncated, malformed, or has trailing bytes.</summary>
    Malformed = 0,

    /// <summary>The format or artifact schema is unsupported.</summary>
    UnsupportedVersion = 1,

    /// <summary>A declared or observed field exceeds a configured limit.</summary>
    LimitExceeded = 2,

    /// <summary>The envelope or bytecode integrity hash does not match.</summary>
    IntegrityMismatch = 3,

    /// <summary>The compiler or host ABI identity does not match this build.</summary>
    RuntimeIdentityMismatch = 4,
}

/// <summary>Reports a typed artifact construction or codec failure.</summary>
public sealed class LuauArtifactException : LuauException
{
    internal LuauArtifactException(
        LuauArtifactFailureKind failureKind,
        string message,
        string? fieldName = null,
        long? actual = null,
        long? limit = null,
        Exception? innerException = null)
        : base(message, chunkName: null, innerException)
    {
        FailureKind = failureKind;
        FieldName = fieldName;
        Actual = actual;
        Limit = limit;
    }

    /// <summary>Gets the rejection category.</summary>
    public LuauArtifactFailureKind FailureKind { get; }

    /// <summary>Gets the rejected field name, when applicable.</summary>
    public string? FieldName { get; }

    /// <summary>Gets the observed byte count, when applicable.</summary>
    public long? Actual { get; }

    /// <summary>Gets the configured byte limit, when applicable.</summary>
    public long? Limit { get; }
}
