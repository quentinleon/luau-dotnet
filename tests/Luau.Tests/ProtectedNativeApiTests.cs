namespace Luau.Tests;

public sealed class ProtectedNativeApiTests
{
    [Fact]
    public void GlobalAssignmentContainsQuotaFailureAndLeavesStateUsable()
    {
        using var state = LuauState.Create(new LuauStateOptions
        {
            MemoryLimitBytes = 1_048_576,
        });
        state.Context.ArmQuotaFailureOnNextGrowth();

        var exception = Assert.Throws<LuauMemoryLimitException>(
            () => state["payload"] = new string('x', 1_025));

        Assert.Equal(1_048_576, exception.LimitBytes);
        Assert.True(exception.Usage.IsTracked);
        Assert.True(exception.AttemptedBytes > exception.LimitBytes);
        state["payload"] = "still usable";
        Assert.Equal("still usable", state["payload"].Read<string>());
    }

}
