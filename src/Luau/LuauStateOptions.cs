namespace Luau;

/// <summary>
/// Configures resource limits and loading policy for a root Luau state.
/// Options are validated when initialized so native callbacks can consume
/// primitive, prevalidated values without performing validation or allocating.
/// </summary>
public sealed class LuauStateOptions
{
    long? memoryLimitBytes;
    int? maxSourceBytes;
    int? maxBytecodeBytes;
    LuauExecutionOptions defaultExecutionOptions = LuauExecutionOptions.Default;
    LuauBytecodePolicy bytecodePolicy = LuauBytecodePolicy.Reject;

    /// <summary>
    /// Gets the finite default policy for ordinary, untrusted script states.
    /// Hosts that intentionally run trusted content may explicitly construct
    /// a less restrictive options instance.
    /// </summary>
    public static LuauStateOptions Default { get; } = new()
    {
        MemoryLimitBytes = 64L * 1024 * 1024,
        MaxSourceBytes = 1024 * 1024,
        MaxBytecodeBytes = 4 * 1024 * 1024,
        DefaultExecutionOptions = new LuauExecutionOptions
        {
            WallClockLimit = TimeSpan.FromMilliseconds(250),
            InterruptCountLimit = 100_000,
            MaxResultCount = 64,
        },
        BytecodePolicy = LuauBytecodePolicy.Reject,
    };

    /// <summary>
    /// Gets the optional native VM allocation limit in bytes. Managed wrapper
    /// allocations are not included in this limit.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The assigned limit is zero or negative.
    /// </exception>
    public long? MemoryLimitBytes
    {
        get => memoryLimitBytes;
        init
        {
            if (value.HasValue && (value.Value <= 0 || value.Value == long.MaxValue))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MemoryLimitBytes),
                    value,
                    $"A memory limit must be between 1 and {long.MaxValue - 1} bytes.");
            }

            memoryLimitBytes = value;
        }
    }

    /// <summary>
    /// Gets the maximum UTF-8 source payload size in bytes, or
    /// <see langword="null"/> when source size is unbounded.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The assigned limit is zero or negative.
    /// </exception>
    public int? MaxSourceBytes
    {
        get => maxSourceBytes;
        init
        {
            if (value.HasValue && value.Value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxSourceBytes), value, "A source-size limit must be greater than zero.");
            }

            maxSourceBytes = value;
        }
    }

    /// <summary>
    /// Gets the maximum precompiled bytecode payload size in bytes, or
    /// <see langword="null"/> when bytecode size is unbounded.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The assigned limit is zero or negative.
    /// </exception>
    public int? MaxBytecodeBytes
    {
        get => maxBytecodeBytes;
        init
        {
            if (value.HasValue && value.Value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxBytecodeBytes), value, "A bytecode-size limit must be greater than zero.");
            }

            maxBytecodeBytes = value;
        }
    }

    /// <summary>
    /// Gets the budgets used when an execution call does not supply its own
    /// <see cref="LuauExecutionOptions"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">The assigned value is null.</exception>
    public LuauExecutionOptions DefaultExecutionOptions
    {
        get => defaultExecutionOptions;
        init => defaultExecutionOptions = value ?? throw new ArgumentNullException(nameof(DefaultExecutionOptions));
    }

    /// <summary>
    /// Gets the policy for persistent bytecode artifacts. The default is
    /// <see cref="LuauBytecodePolicy.Reject"/>. A trusted host must configure
    /// <see cref="LuauBytecodePolicy.RequireValidator"/> with a provenance validator.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The assigned enum value is not defined.
    /// </exception>
    public LuauBytecodePolicy BytecodePolicy
    {
        get => bytecodePolicy;
        init
        {
            if (value < LuauBytecodePolicy.Reject || value > LuauBytecodePolicy.RequireValidator)
            {
                throw new ArgumentOutOfRangeException(nameof(BytecodePolicy), value, "Unknown bytecode policy.");
            }

            bytecodePolicy = value;
        }
    }

    /// <summary>
    /// Gets the host validator used when <see cref="BytecodePolicy"/> is
    /// <see cref="LuauBytecodePolicy.RequireValidator"/>.
    /// </summary>
    public ILuauBytecodeValidator? BytecodeValidator { get; init; }

    /// <summary>
    /// Validates relationships between options after object initialization.
    /// State creation should call this before allocating native resources.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Bytecode validation is required but no validator is configured.
    /// </exception>
    public void Validate()
    {
        if (bytecodePolicy == LuauBytecodePolicy.RequireValidator && BytecodeValidator is null)
        {
            throw new InvalidOperationException(
                $"{nameof(BytecodeValidator)} must be provided when {nameof(BytecodePolicy)} is {LuauBytecodePolicy.RequireValidator}.");
        }
    }
}
