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
    public void LoadsModulesWithFileSystemRequire()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Luau.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var modulePath = Path.Combine(directory, "module.luau");
            File.WriteAllText(modulePath, "return { answer = 42 }");

            using var state = LuauState.Create();
            state.OpenRequireLibrary(new FileSystemLuauRequirer
            {
                WorkingDirectory = directory,
            });

            var luauPath = modulePath.Replace('\\', '/').Replace("'", "\\'");
            var results = state.DoString($"local module = require('{luauPath}'); return module.answer");

            Assert.Single(results);
            Assert.Equal(42, results[0].Read<int>());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void InvokesCSharpCallbacksFromLuau()
    {
        using var state = LuauState.Create();
        state["add"] = state.CreateFunction(l =>
        {
            var rhs = l.ToNumber(-1);
            var lhs = l.ToNumber(-2);
            l.PushNumber(lhs + rhs);
            return 1;
        });

        var results = state.DoString("return add(2, 3)");

        Assert.Single(results);
        Assert.Equal(5, results[0].Read<int>());
    }

    [Fact]
    public async Task InvokesAsyncCSharpCallbacksFromLuau()
    {
        using var state = LuauState.Create();
        state["wait"] = state.CreateFunction(async (l, ct) =>
        {
            await Task.Delay(1, ct);
            return 0;
        });

        var results = await state.DoStringAsync("wait(); return 7");

        Assert.Single(results);
        Assert.Equal(7, results[0].Read<int>());
    }

    [Fact]
    public async Task GeneratedAsyncCallbackReceivesArgumentsAfterNativeYield()
    {
        using var state = LuauState.Create();
        state["addLater"] = state.CreateFunction(
            async (CancellationToken cancellationToken, double lhs, double rhs) =>
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                return lhs + rhs;
            });

        var results = await state.DoStringAsync("return addLater(19, 23)");

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

        Assert.Throws<ObjectDisposedException>(() => state.GetTop());
    }
}
