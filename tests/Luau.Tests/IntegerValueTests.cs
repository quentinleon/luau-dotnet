namespace Luau.Tests;

public sealed class IntegerValueTests
{
    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(long.MaxValue)]
    public void SignedIntegerBoundariesRoundTripLosslessly(long expected)
    {
        using var state = LuauState.Create();
        state["value"] = expected;

        var actual = state["value"];
        Assert.Equal(LuauType.Integer, actual.Type);
        Assert.Equal(expected, actual.Read<long>());
    }

    [Fact]
    public void IntegerDoubleConversionIsExactUnlessLossIsExplicit()
    {
        const long exactValue = 9_007_199_254_740_992;
        const long inexactValue = 9_007_199_254_740_993;

        var exact = LuauValue.FromInteger(exactValue);
        var inexact = LuauValue.FromInteger(inexactValue);
        var maximum = LuauValue.FromInteger(long.MaxValue);

        Assert.True(exact.TryRead<double>(out var exactDouble));
        Assert.Equal((double)exactValue, exactDouble);
        Assert.False(inexact.TryRead<double>(out _));
        Assert.False(maximum.TryRead<double>(out _));
        Assert.Throws<InvalidOperationException>(() => inexact.Read<double>());
        Assert.Equal((double)inexactValue, inexact.ReadDoubleLossy());
    }

    [Fact]
    public void IntegralReadsAreRangeChecked()
    {
        Assert.Equal(byte.MaxValue, LuauValue.FromInteger(byte.MaxValue).Read<byte>());
        Assert.False(LuauValue.FromInteger((long)byte.MaxValue + 1).TryRead<byte>(out _));
        Assert.False(LuauValue.FromInteger(-1).TryRead<uint>(out _));
        Assert.False(LuauValue.FromInteger((long)int.MaxValue + 1).TryRead<int>(out _));
        Assert.Equal((ulong)long.MaxValue, LuauValue.FromInteger(long.MaxValue).Read<ulong>());

        Assert.False(LuauValue.FromNumber((double)long.MaxValue).TryRead<long>(out _));
        Assert.False(LuauValue.FromNumber(double.PositiveInfinity).TryRead<long>(out _));
        Assert.Throws<InvalidOperationException>(() => LuauValue.FromInteger(256).Read<byte>());
    }

    [Fact]
    public void ManagedIntegralCreationUsesTheNativeIntegerKind()
    {
        using var state = LuauState.Create();

        Assert.Equal(LuauType.Integer, LuauValue.CreateFrom(42).Type);
        Assert.Equal(LuauType.Integer, LuauValue.CreateFrom(uint.MaxValue).Type);
        Assert.Equal(LuauType.Integer, LuauValue.CreateFrom(long.MinValue).Type);
        Assert.Equal(LuauType.Number, LuauValue.CreateFrom(42d).Type);
        Assert.Throws<OverflowException>(() => LuauValue.CreateFrom(ulong.MaxValue));
    }

    [Fact]
    public void IntegerAndNumberHaveDistinctManagedAndNativeTableKeySemantics()
    {
        var integer = LuauValue.FromInteger(1);
        var number = LuauValue.FromNumber(1);
        Assert.NotEqual(integer, number);

        var dictionary = new Dictionary<LuauValue, string>
        {
            [integer] = "integer",
            [number] = "number",
        };
        Assert.Equal(2, dictionary.Count);

        using var state = LuauState.Create();
        using var table = state.CreateTable();
        table.RawSet(integer, "integer");
        table.RawSet(number, "number");

        Assert.Equal("integer", table.RawGet(integer).Read<string>());
        Assert.Equal("number", table.RawGet(number).Read<string>());
    }

    [Fact]
    public void IntegerLibraryMakesScriptConversionsAndArithmeticExplicit()
    {
        using var state = LuauState.Create();
        state.OpenIntegerLibrary();
        state["lhs"] = 40;
        state["rhs"] = 2;

        var results = state.DoString(
            "return integer.add(lhs, rhs), integer.tonumber(lhs), class == nil",
            "@integer/explicit-operations.luau");

        Assert.Equal(LuauType.Integer, results[0].Type);
        Assert.Equal(42, results[0].Read<long>());
        Assert.Equal(LuauType.Number, results[1].Type);
        Assert.Equal(40, results[1].Read<double>());
        Assert.True(results[2].Read<bool>());
    }

    [Fact]
    public void SpanTableCreationRetainsLuauArraySemantics()
    {
        using var state = LuauState.Create();
        using var table = state.CreateTable(["first", "second"]);
        state["values"] = table;

        var results = state.DoString("return #values, values[1], values[2]");

        Assert.Equal(2, results[0].Read<int>());
        Assert.Equal("first", results[1].Read<string>());
        Assert.Equal("second", results[2].Read<string>());
    }

    [Fact]
    public void OrdinaryScriptNumericLiteralsKeepNumberBehavior()
    {
        using var state = LuauState.Create();

        var result = Assert.Single(state.DoString("return 42", "@integer/script-number.luau"));

        Assert.Equal(LuauType.Number, result.Type);
        Assert.Equal(42, result.Read<int>());
    }

    [Theory]
    [InlineData(12, "class")]
    [InlineData(13, "object")]
    public void UnsupportedUpstreamObjectKindsFailDeliberately(
        int nativeType,
        string expectedKind)
    {
        using var state = LuauState.Create();
        state.PushString("stack-sentinel");
        var originalTop = state.GetTop();

        var exception = Assert.Throws<LuauUnsupportedValueException>(
            () => state.ToValueForNativeTypeFixture(-1, nativeType));

        Assert.Equal(expectedKind, exception.ValueKind);
        Assert.Contains(expectedKind, exception.Message, StringComparison.Ordinal);
        Assert.Equal(originalTop, state.GetTop());
        Assert.Equal("stack-sentinel", state.ToString(-1));
    }
}
