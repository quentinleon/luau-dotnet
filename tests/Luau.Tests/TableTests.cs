namespace Luau;

public sealed class TableTests
{
    [Fact]
    public void CreateAndDispose()
    {
        using var state = LuauState.Create();
        var table = state.CreateTable();
        table.Dispose();
    }

    [Fact]
    public void IndexerGetSet()
    {
        using var state = LuauState.Create();
        var table = state.CreateTable();
        table["test"] = 10;
        Assert.Equal(10, table["test"]);
    }

    [Fact]
    public void RawGetSet()
    {
        using var state = LuauState.Create();
        var table = state.CreateTable();
        table.RawSet("test", 10);
        Assert.Equal(10, table.RawGet("test"));
    }

    [Fact]
    public void ContainsKey()
    {
        using var state = LuauState.Create();
        var table = state.CreateTable();
        Assert.False(table.ContainsKey("test"));
        table["test"] = 10;
        Assert.True(table.ContainsKey("test"));
    }

    [Fact]
    public void Foreach()
    {
        using var state = LuauState.Create();
        var table = state.CreateTable();
        table[1] = 10;
        table["key"] = "value";
        Assert.Equal(
            [new KeyValuePair<LuauValue, LuauValue>(1, 10), new KeyValuePair<LuauValue, LuauValue>("key", "value")],
            table
        );
    }

    [Fact]
    public void Clone()
    {
        using var state = LuauState.Create();
        var table = state.CreateTable();
        table["test"] = 10;
        var clone = table.Clone();

        Assert.Equal(10, clone["test"]);
        clone["test"] = 20;
        Assert.Equal(20, clone["test"]);
        Assert.Equal(10, table["test"]);
    }

    [Fact]
    public void ThrowingMetamethodIsContainedAndRestoresStack()
    {
        using var state = LuauState.Create();
        state.OpenBaseLibrary();
        using var table = state.DoString(
            "return setmetatable({}, { __index = function() error('protected table failure') end })")[0]
            .Read<LuauTable>();
        var originalTop = state.GetTop();

        var exception = Assert.Throws<LuauException>(() => _ = table["missing"]);

        Assert.Contains("protected table failure", exception.Message);
        Assert.Equal(originalTop, state.GetTop());
        Assert.Equal(42, Assert.Single(state.DoString("return 40 + 2")).Read<int>());
    }

    [Fact]
    public void DirectMetamethodGetAndSetUseDefaultExecutionBudget()
    {
        using var state = LuauState.Create(new LuauStateOptions
        {
            DefaultExecutionOptions = new LuauExecutionOptions
            {
                InterruptCountLimit = 10,
            },
        });
        state.OpenBaseLibrary();
        using var table = state.DoString(
            "return setmetatable({}, { " +
            "__index = function() while true do end end, " +
            "__newindex = function() while true do end end })")[0]
            .Read<LuauTable>();

        Assert.Throws<LuauExecutionBudgetException>(() => _ = table["missing"]);
        Assert.Throws<LuauExecutionBudgetException>(() => table["missing"] = 1);
        Assert.Equal(0, state.GetTop());
        Assert.Equal(12, state.DoString("return 6 + 6").Single().Read<int>());
    }

    [Fact]
    public void DirectMetamethodSurfacesManagedCallbackFailure()
    {
        using var state = LuauState.Create();
        state.OpenBaseLibrary();
        var cause = new InvalidOperationException("host metamethod callback failed");
        using var callback = state.CreateFunction("explode", _ => throw cause);
        state["explode"] = callback;
        using var table = state.DoString(
            "return setmetatable({}, { __index = function() return explode() end })")[0]
            .Read<LuauTable>();

        var exception = Assert.Throws<LuauManagedCallbackException>(
            () => _ = table["missing"]);

        Assert.Equal("explode", exception.CallbackName);
        Assert.Same(cause, exception.InnerException);
        Assert.Equal(0, state.GetTop());
    }

    [Fact]
    public void InvalidIteratorKeyIsContainedAndStateRecovers()
    {
        using var state = LuauState.Create();
        using var table = state.CreateTable();
        table[1] = 10;
        var originalTop = state.GetTop();

        Assert.Throws<LuauException>(
            () => table.TryMoveNext("not a table key", out _));

        Assert.Equal(originalTop, state.GetTop());
        Assert.Equal(10, table[1].Read<int>());
    }

    [Fact]
    public void TableYieldedByChildUsesRootStackAndChildRemainsResumable()
    {
        using var root = LuauState.Create();
        root.OpenCoroutineLibrary();
        using var child = root.CreateThread();
        using var table = Assert.Single(child.DoString(
            "local value = { answer = 41 }; coroutine.yield(value); return value.answer + 1",
            "@references/yielded-table.luau")).Read<LuauTable>();

        Assert.Equal(LuauThreadStatus.Suspended, child.GetStatus());
        Assert.Equal(41, table["answer"].Read<int>());
        table["answer"] = 42d;
        Assert.Equal(LuauThreadStatus.Suspended, child.GetStatus());

        Assert.Equal(43, Assert.Single(child.Resume()).Read<int>());
        Assert.Equal(9, Assert.Single(root.DoString("return 9")).Read<int>());
    }

    [Fact]
    public void SuspendedChildGlobalReadDoesNotResetCoroutine()
    {
        using var root = LuauState.Create();
        root.OpenCoroutineLibrary();
        using var child = root.CreateThread();

        Assert.Equal(5, Assert.Single(child.DoString(
            "host_visible = 41; coroutine.yield(5); return host_visible + 1",
            "@references/suspended-globals.luau")).Read<int>());

        Assert.Equal(LuauThreadStatus.Suspended, child.GetStatus());
        Assert.Equal(41, child["host_visible"].Read<int>());
        Assert.Equal(LuauThreadStatus.Suspended, child.GetStatus());
        Assert.Equal(42, Assert.Single(child.Resume()).Read<int>());
    }
}
