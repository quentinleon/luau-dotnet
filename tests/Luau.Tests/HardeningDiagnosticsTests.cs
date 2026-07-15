namespace Luau.Tests;

public sealed class HardeningDiagnosticsTests
{
    [Fact]
    public void LuauExceptionRetainsChunkAndInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var exception = new LuauException("failed", "@mods/test.luau", inner);

        Assert.Equal("@mods/test.luau", exception.ChunkName);
        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public void LoadLimitIncludesStructuredSizeAndChunkContext()
    {
        var exception = new LuauLoadLimitException(
            "@mods/large.luau",
            LuauLoadInputKind.Source,
            actualBytes: 2048,
            limitBytes: 1024);

        Assert.Equal("@mods/large.luau", exception.ChunkName);
        Assert.Equal(LuauLoadInputKind.Source, exception.InputKind);
        Assert.Equal(2048, exception.ActualBytes);
        Assert.Equal(1024, exception.LimitBytes);
        Assert.Contains("@mods/large.luau", exception.Message);
        Assert.Contains("2048", exception.Message);
        Assert.Contains("1024", exception.Message);
    }

    [Fact]
    public void LoadLimitWithoutChunkDoesNotInventChunkContext()
    {
        var exception = new LuauLoadLimitException(
            null,
            LuauLoadInputKind.Bytecode,
            actualBytes: 12,
            limitBytes: 10);

        Assert.Null(exception.ChunkName);
        Assert.StartsWith("Bytecode size", exception.Message);
    }

    [Fact]
    public void MemorySnapshotRetainsUsageAndLimit()
    {
        var snapshot = new LuauMemoryUsageSnapshot(currentBytes: 400, peakBytes: 600, limitBytes: 1024);

        Assert.Equal(400, snapshot.CurrentBytes);
        Assert.Equal(600, snapshot.PeakBytes);
        Assert.Equal(1024, snapshot.LimitBytes);
        Assert.True(snapshot.IsLimited);
    }

    [Fact]
    public void MemorySnapshotRejectsInvalidValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LuauMemoryUsageSnapshot(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LuauMemoryUsageSnapshot(2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LuauMemoryUsageSnapshot(0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LuauMemoryUsageSnapshot(2, 3, 2));
    }

    [Fact]
    public void MemoryLimitIncludesSnapshotRequestAndChunk()
    {
        var usage = new LuauMemoryUsageSnapshot(currentBytes: 900, peakBytes: 950, limitBytes: 1024);
        var exception = new LuauMemoryLimitException("@mods/bomb.luau", usage, attemptedBytes: 1100);

        Assert.Equal("@mods/bomb.luau", exception.ChunkName);
        Assert.Equal(1024, exception.LimitBytes);
        Assert.Equal(900, exception.Usage.CurrentBytes);
        Assert.Equal(1100, exception.AttemptedBytes);
        Assert.Contains("@mods/bomb.luau", exception.Message);
        Assert.Contains("1024", exception.Message);
        Assert.Contains("1100", exception.Message);
    }

    [Fact]
    public void WallClockBudgetRetainsObservedValuesAndChunk()
    {
        var limit = TimeSpan.FromMilliseconds(25);
        var elapsed = TimeSpan.FromMilliseconds(30);
        var exception = new LuauExecutionBudgetException("@mods/loop.luau", limit, elapsed);

        Assert.Equal("@mods/loop.luau", exception.ChunkName);
        Assert.Equal(LuauExecutionBudgetKind.WallClock, exception.BudgetKind);
        Assert.Equal(limit, exception.WallClockLimit);
        Assert.Equal(elapsed, exception.Elapsed);
        Assert.Null(exception.InterruptCountLimit);
        Assert.Contains("@mods/loop.luau", exception.Message);
        Assert.Contains("25", exception.Message);
    }

    [Fact]
    public void InterruptBudgetRetainsObservedValuesAndChunk()
    {
        var exception = new LuauExecutionBudgetException("@mods/loop.luau", limit: 100, observedInterruptCount: 101);

        Assert.Equal(LuauExecutionBudgetKind.InterruptCount, exception.BudgetKind);
        Assert.Equal(100, exception.InterruptCountLimit);
        Assert.Equal(101, exception.ObservedInterruptCount);
        Assert.Null(exception.WallClockLimit);
        Assert.Contains("101", exception.Message);
    }

    [Fact]
    public void CancellationRetainsNormalDotNetSemanticsAndChunk()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        var exception = new LuauExecutionCanceledException("@mods/canceled.luau", source.Token);

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal("@mods/canceled.luau", exception.ChunkName);
        Assert.Equal(source.Token, exception.CancellationToken);
        Assert.Contains("@mods/canceled.luau", exception.Message);
    }

    [Fact]
    public void ManagedCallbackFailureRetainsInnerCallbackAndChunk()
    {
        var inner = new InvalidOperationException("host exploded");
        var exception = new LuauManagedCallbackException("@mods/callback.luau", "spawn", inner);

        Assert.Equal("@mods/callback.luau", exception.ChunkName);
        Assert.Equal("spawn", exception.CallbackName);
        Assert.Same(inner, exception.InnerException);
        Assert.Contains("@mods/callback.luau", exception.Message);
        Assert.Contains("spawn", exception.Message);
        Assert.Contains("host exploded", exception.Message);
    }

    [Fact]
    public void ManagedCallbackFailureSurvivesThrowingExceptionMessage()
    {
        var inner = new ThrowingMessageException();

        var exception = new LuauManagedCallbackException("@mods/callback.luau", "spawn", inner);

        Assert.Same(inner, exception.InnerException);
        Assert.Contains(nameof(ThrowingMessageException), exception.Message);
        Assert.Contains("message unavailable", exception.Message);
    }

    [Fact]
    public void DiagnosticExceptionsRejectContradictoryObservedValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LuauLoadLimitException(
            null,
            LuauLoadInputKind.Source,
            actualBytes: 10,
            limitBytes: 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LuauExecutionBudgetException(
            null,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LuauExecutionBudgetException(
            null,
            limit: 2,
            observedInterruptCount: 1));

        var usage = new LuauMemoryUsageSnapshot(currentBytes: 1, peakBytes: 1, limitBytes: 10);
        Assert.Throws<ArgumentOutOfRangeException>(() => new LuauMemoryLimitException(null, usage, attemptedBytes: 10));
    }

    sealed class ThrowingMessageException : Exception
    {
        public override string Message => throw new InvalidOperationException("Message getter failed");
    }
}
