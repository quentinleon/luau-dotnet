using Luau;

namespace Luau.Tests;

public sealed class EngineTests
{
    [Fact]
    public void CompilesAndExecutesSource()
    {
        using var state = LuauState.Create();

        var results = state.DoString("return 1 + 2");

        Assert.Single(results);
        Assert.Equal(3, results[0].Read<int>());
    }

    [Fact]
    public async Task LoadsAndExecutesTrustedBytecode()
    {
        using var state = LuauState.Create();
        var bytecode = LuauCompiler.Compile("return 123"u8);
        using var function = state.LoadTrustedBytecode(
            bytecode,
            "@engine/trusted-bytecode.luau");

        var results = await function.InvokeAsync([]);

        Assert.Single(results);
        Assert.Equal(123, results[0].Read<int>());
    }

    [Fact]
    public void OpensSelectedStandardLibraries()
    {
        using var state = LuauState.Create();
        state.OpenMathLibrary();

        var results = state.DoString("return math.cos(0)");

        Assert.Single(results);
        Assert.Equal(1, results[0].Read<int>());
    }

    [Fact]
    public void InvokesCSharpCallbacksFromLuau()
    {
        using var state = LuauState.Create();
        state["add"] = state.CreateFunction(context =>
        {
            var lhs = context.Read<double>(0);
            var rhs = context.Read<double>(1);
            context.Return(lhs + rhs);
        });

        var results = state.DoString("return add(2, 3)");

        Assert.Single(results);
        Assert.Equal(5, results[0].Read<int>());
    }

    [Fact]
    public async Task InvokesAsyncCSharpCallbacksFromLuau()
    {
        using var state = LuauState.Create();
        state["wait"] = state.CreateAsyncFunction(async context =>
        {
            await Task.Delay(1, context.CancellationToken);
        });

        var results = await state.DoStringAsync("wait(); return 7");

        Assert.Single(results);
        Assert.Equal(7, results[0].Read<int>());
    }

    [Fact]
    public async Task GeneratedAsyncCallbackReceivesArgumentsAfterNativeYield()
    {
        using var state = LuauState.Create();
        state.OpenLibrary(new EngineTestLibrary());

        var results = await state.DoStringAsync("return engineTest.addLater(19, 23)");

        Assert.Equal(42, Assert.Single(results).Read<int>());
    }

    [Fact]
    public void PropagatesLuauErrors()
    {
        using var state = LuauState.Create();
        state.OpenBaseLibrary();

        var ex = Assert.Throws<LuauException>(() => state.DoString("error('boom')"));

        Assert.Contains("boom", ex.Message);
    }

    [Fact]
    public void ThrowsAfterStateDisposal()
    {
        var state = LuauState.Create();

        state.Dispose();

        Assert.Throws<ObjectDisposedException>(() => state.CreateTable());
    }
}

[LuauLibrary("engineTest")]
public sealed partial class EngineTestLibrary
{
    [LuauMember("addLater")]
    public static async ValueTask<double> AddLater(
        CancellationToken cancellationToken,
        double lhs,
        double rhs)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        return lhs + rhs;
    }
}
