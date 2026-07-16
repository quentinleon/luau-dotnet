#pragma warning disable CS0618 // Deliberate regression coverage for transitional unsupported APIs.

using System.Text;

namespace Luau.Tests;

public sealed class HighLevelProtectedRegressionTests
{
    [Fact]
    public void InvalidStringIndexDoesNotConsumeAnUnrelatedStackValue()
    {
        using var state = LuauState.Create();
        state.PushInteger(42);

        Assert.Throws<InvalidOperationException>(() => state.ToString(2));

        Assert.Equal(1, state.GetTop());
        Assert.Equal(42, state.Pop().Read<int>());
    }

    [Fact]
    public void NonYieldableTostringMetamethodBudgetIsTypedAndVmRecovers()
    {
        const string chunkName = "@budgets/non-yieldable-tostring.luau";
        using var state = LuauState.Create();
        state.OpenBaseLibrary();

        var exception = Assert.Throws<LuauExecutionBudgetException>(() => state.DoString(
            "return tostring(setmetatable({}, { __tostring = function() while true do end end }))",
            chunkName,
            executionOptions: new LuauExecutionOptions { InterruptCountLimit = 1 }));

        Assert.Equal(LuauExecutionBudgetKind.InterruptCount, exception.BudgetKind);
        Assert.Equal(chunkName, exception.ChunkName);
        Assert.True(exception.ObservedInterruptCount > exception.InterruptCountLimit);
        Assert.Equal(42, Assert.Single(state.DoString("return 40 + 2")).Read<int>());
    }

    [Fact]
    public void CallbackPushStringQuotaFailureIsControlledAndVmRecovers()
    {
        const long memoryLimit = 1_048_576;
        const string chunkName = "@callbacks/push-string-quota.luau";
        using var state = LuauState.Create(new LuauStateOptions
        {
            MemoryLimitBytes = memoryLimit,
            BytecodePolicy = LuauBytecodePolicy.Reject,
        });
        state["pushHugeString"] = state.CreateFunction(
            "pushHugeString",
            callbackState =>
            {
                callbackState.PushString(new string('x', 2_097_152));
                return 1;
            });
        var originalTop = state.GetTop();

        var exception = Assert.Throws<LuauManagedCallbackException>(
            () => state.DoString("return pushHugeString()", chunkName));

        Assert.Equal(chunkName, exception.ChunkName);
        Assert.Equal("pushHugeString", exception.CallbackName);
        var memoryException = Assert.IsType<LuauMemoryLimitException>(exception.InnerException);
        Assert.Equal(memoryLimit, memoryException.LimitBytes);
        Assert.True(memoryException.AttemptedBytes > memoryLimit);
        Assert.Equal(originalTop, state.GetTop());

        var results = state.DoString("return 40 + 2", "@callbacks/recovered.luau");
        Assert.Single(results);
        Assert.Equal(42, results[0].Read<int>());
        Assert.Equal(originalTop, state.GetTop());
    }

    [Fact]
    public void ToDisplayStringReturnsExactUtf8MetamethodOutputAndPreservesInput()
    {
        const string expected = "héllø 🐺 東京";
        using var state = LuauState.Create();
        state.OpenLibraries();
        using var value = Assert.Single(state.DoString(
            $$"""
            return setmetatable({}, {
                __tostring = function()
                    return "{{expected}}"
                end,
            })
            """,
            "@display/utf8.luau")).Read<LuauTable>();
        var originalTop = state.GetTop();

        state.PushTable(value);
        try
        {
            Assert.Equal(expected, state.ToDisplayString(-1));
            Assert.Equal(originalTop + 1, state.GetTop());
        }
        finally
        {
            state.SetTop(originalTop);
        }
    }

    [Fact]
    public void BoundedDisplayStringCapsManagedUtf8AndPreservesStack()
    {
        const string expected = "A🐺東京";
        using var state = LuauState.Create();
        state.OpenLibraries();
        using var value = Assert.Single(state.DoString(
            $$"""
            return setmetatable({}, {
                __tostring = function()
                    return "{{expected}}" .. string.rep("🐺", 4096)
                end,
            })
            """,
            "@display/bounded-utf8.luau")).Read<LuauTable>();
        var originalTop = state.GetTop();

        state.PushTable(value);
        try
        {
            var bounded = state.ToDisplayString(-1, 5, out var wasTruncated);

            Assert.Equal("A🐺", bounded);
            Assert.True(wasTruncated);
            Assert.Equal(5, Encoding.UTF8.GetByteCount(bounded));
            Assert.Equal(originalTop + 1, state.GetTop());

            Assert.Equal(string.Empty, state.ToDisplayString(-1, 0, out wasTruncated));
            Assert.True(wasTruncated);
            Assert.Equal(originalTop + 1, state.GetTop());
        }
        finally
        {
            state.SetTop(originalTop);
        }
    }

    [Fact]
    public void ThrowingDisplayMetamethodRestoresStackAndLeavesVmReusable()
    {
        using var state = LuauState.Create();
        state.OpenLibraries();
        using var value = Assert.Single(state.DoString(
            """
            return setmetatable({}, {
                __tostring = function()
                    error("display boom")
                end,
            })
            """,
            "@display/throwing.luau")).Read<LuauTable>();
        var originalTop = state.GetTop();

        state.PushTable(value);
        try
        {
            var exception = Assert.Throws<LuauException>(() => state.ToDisplayString(-1));
            Assert.Contains("display boom", exception.Message, StringComparison.Ordinal);
            Assert.Equal(originalTop + 1, state.GetTop());
        }
        finally
        {
            state.SetTop(originalTop);
        }

        var results = state.DoString("return 21 * 2", "@display/recovered.luau");
        Assert.Single(results);
        Assert.Equal(42, results[0].Read<int>());
        Assert.Equal(originalTop, state.GetTop());
    }

    [Fact]
    public void ReferenceToStringDoesNotInvokeUntrustedMetamethods()
    {
        using var state = LuauState.Create();
        state.OpenBaseLibrary();
        using var value = Assert.Single(state.DoString(
            "return setmetatable({}, { __tostring = function() error('must not run') end })",
            "@display/reference-safe.luau")).Read<LuauTable>();

        var text = value.ToString();

        Assert.StartsWith("table: 0x", text, StringComparison.Ordinal);
        Assert.Equal(7, Assert.Single(state.DoString("return 7")).Read<int>());
    }

    [Fact]
    public void PublicPushNilGrowsTheNativeStackSafely()
    {
        using var state = LuauState.Create();

        for (var index = 0; index < 512; index++)
        {
            state.PushNil();
        }

        Assert.Equal(512, state.GetTop());
        state.SetTop(0);
        state.PushNumber(42);
        Assert.Equal(42, state.Pop().Read<int>());
        Assert.Equal(0, state.GetTop());
    }

    [Fact]
    public void PublicSetTopQuotaFailurePreservesStackAndTypedDiagnostics()
    {
        using var state = LuauState.Create(new LuauStateOptions
        {
            MemoryLimitBytes = 8_388_608,
            BytecodePolicy = LuauBytecodePolicy.Reject,
        });
        var originalTop = state.GetTop();
        state.Context.ArmQuotaFailureOnNextGrowth();

        Assert.Throws<LuauMemoryLimitException>(() => state.SetTop(4_096));

        Assert.Equal(originalTop, state.GetTop());
        state.PushInteger(42);
        Assert.Equal(42, state.Pop().Read<int>());
        Assert.Equal(originalTop, state.GetTop());
    }

    [Fact]
    public void PublicXMoveQuotaFailurePreservesBothStacksAndTypedDiagnostics()
    {
        using var root = LuauState.Create(new LuauStateOptions
        {
            MemoryLimitBytes = 8_388_608,
            BytecodePolicy = LuauBytecodePolicy.Reject,
        });
        using var child = root.CreateThread();
        root.SetTop(0);
        child.SetTop(0);
        for (var index = 0; index < 1_024; index++)
        {
            root.PushInteger(index);
        }

        var sourceTop = root.GetTop();
        var destinationTop = child.GetTop();
        root.Context.ArmQuotaFailureOnNextGrowth();

        Assert.Throws<LuauMemoryLimitException>(() => root.XMove(child, sourceTop));

        Assert.Equal(sourceTop, root.GetTop());
        Assert.Equal(destinationTop, child.GetTop());
        Assert.Equal(1_023, root.ToInteger(-1));
    }
}
