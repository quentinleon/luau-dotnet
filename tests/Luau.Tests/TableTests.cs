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
}