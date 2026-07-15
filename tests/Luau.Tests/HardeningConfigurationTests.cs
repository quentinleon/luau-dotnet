namespace Luau.Tests;

public sealed class HardeningConfigurationTests
{
    [Fact]
    public void StateDefaultsRejectHostBytecode()
    {
        var options = LuauStateOptions.Default;

        Assert.Null(options.MemoryLimitBytes);
        Assert.Null(options.MaxSourceBytes);
        Assert.Null(options.MaxBytecodeBytes);
        Assert.Same(LuauExecutionOptions.Default, options.DefaultExecutionOptions);
        Assert.Equal(LuauBytecodePolicy.Reject, options.BytecodePolicy);
        Assert.Null(options.BytecodeValidator);
        Assert.Null(options.DefaultExecutionOptions.ContinuationScheduler);
        options.Validate();

        using var state = LuauState.Create(options);
        Assert.False(state.MemoryUsage.IsTracked);
    }

    [Fact]
    public void ExecutionOptionsRetainContinuationScheduler()
    {
        var scheduler = new InlineScheduler();
        var options = new LuauExecutionOptions
        {
            ContinuationScheduler = scheduler,
        };

        Assert.Same(scheduler, options.ContinuationScheduler);
        Assert.False(options.HasBudget);
    }

    [Fact]
    public void StateOptionsRetainConfiguredLimits()
    {
        var execution = new LuauExecutionOptions
        {
            WallClockLimit = TimeSpan.FromMilliseconds(250),
            InterruptCountLimit = 50,
            MaxResultCount = 25,
        };
        var validator = new AcceptingValidator();
        var options = new LuauStateOptions
        {
            MemoryLimitBytes = 1_000_000,
            MaxSourceBytes = 10_000,
            MaxBytecodeBytes = 20_000,
            DefaultExecutionOptions = execution,
            BytecodePolicy = LuauBytecodePolicy.RequireValidator,
            BytecodeValidator = validator,
        };

        options.Validate();

        Assert.Equal(1_000_000, options.MemoryLimitBytes);
        Assert.Equal(10_000, options.MaxSourceBytes);
        Assert.Equal(20_000, options.MaxBytecodeBytes);
        Assert.Same(execution, options.DefaultExecutionOptions);
        Assert.Equal(25, execution.MaxResultCount);
        Assert.Same(validator, options.BytecodeValidator);
        Assert.True(execution.HasBudget);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MemoryLimitMustBePositive(long value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LuauStateOptions { MemoryLimitBytes = value });
    }

    [Fact]
    public void MemoryLimitMustLeaveRoomForRepresentableFailureDiagnostics()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LuauStateOptions { MemoryLimitBytes = long.MaxValue });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SourceLimitMustBePositive(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LuauStateOptions { MaxSourceBytes = value });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BytecodeLimitMustBePositive(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LuauStateOptions { MaxBytecodeBytes = value });
    }

    [Fact]
    public void WallClockLimitMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LuauExecutionOptions { WallClockLimit = TimeSpan.Zero });
        Assert.Throws<ArgumentOutOfRangeException>(() => new LuauExecutionOptions { WallClockLimit = TimeSpan.FromTicks(-1) });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InterruptCountLimitMustBePositive(long value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LuauExecutionOptions { InterruptCountLimit = value });
    }

    [Fact]
    public void ResultCountLimitCannotBeNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LuauExecutionOptions { MaxResultCount = -1 });
        Assert.Equal(0, new LuauExecutionOptions { MaxResultCount = 0 }.MaxResultCount);
    }

    [Fact]
    public void RequireValidatorMustHaveValidator()
    {
        var options = new LuauStateOptions
        {
            BytecodePolicy = LuauBytecodePolicy.RequireValidator,
        };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(LuauStateOptions.BytecodeValidator), exception.Message);
    }

    [Fact]
    public void RejectPolicyDoesNotRequireValidator()
    {
        var options = new LuauStateOptions
        {
            BytecodePolicy = LuauBytecodePolicy.Reject,
        };

        options.Validate();
    }

    [Fact]
    public void UndefinedBytecodePolicyIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LuauStateOptions
        {
            BytecodePolicy = (LuauBytecodePolicy)int.MaxValue,
        });
    }

    [Fact]
    public void DefaultExecutionOptionsCannotBeNull()
    {
        Assert.Throws<ArgumentNullException>(() => new LuauStateOptions
        {
            DefaultExecutionOptions = null!,
        });
    }

    sealed class AcceptingValidator : ILuauBytecodeValidator
    {
        public bool IsValid(ReadOnlySpan<byte> bytecode, ReadOnlySpan<byte> utf8ChunkName) => true;
    }

    sealed class InlineScheduler : ILuauContinuationScheduler
    {
        public bool CheckAccess() => true;

        public void Post(Action continuation) => continuation();
    }
}
