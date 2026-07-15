using System.Text;

namespace Luau.Tests;

public sealed class HardeningRuntimeTests
{
    [Fact]
    public void AllocationBombStopsAtConfiguredLimitAndRetainsChunkName()
    {
        const long memoryLimit = 1_048_576;
        const string chunkName = "@mods/allocation-bomb.luau";
        using var state = LuauState.Create(new LuauStateOptions
        {
            MemoryLimitBytes = memoryLimit,
            BytecodePolicy = LuauBytecodePolicy.Reject,
        });
        state.OpenBaseLibrary();
        state.OpenStringLibrary();

        var exception = Assert.Throws<LuauMemoryLimitException>(() => state.DoString(
            """
            local values = {}
            for index = 1, 100000 do
                values[index] = string.rep("x", 256) .. tostring(index)
            end
            return #values
            """,
            chunkName));

        Assert.Equal(chunkName, exception.ChunkName);
        Assert.Equal(memoryLimit, exception.LimitBytes);
        Assert.Equal(memoryLimit, exception.Usage.LimitBytes);
        Assert.InRange(exception.Usage.CurrentBytes, 1, memoryLimit);
        Assert.InRange(exception.Usage.PeakBytes, exception.Usage.CurrentBytes, memoryLimit);
        Assert.True(exception.AttemptedBytes > memoryLimit);
        Assert.Contains(chunkName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MemoryUsageTracksCurrentPeakAndConfiguredLimit()
    {
        const long memoryLimit = 4_194_304;
        using var state = LuauState.Create(new LuauStateOptions
        {
            MemoryLimitBytes = memoryLimit,
            BytecodePolicy = LuauBytecodePolicy.Reject,
        });
        state.OpenStringLibrary();

        var before = state.MemoryUsage;
        var results = state.DoString(
            "local payload = string.rep('x', 65536); return #payload",
            "@mods/memory-snapshot.luau");
        var after = state.MemoryUsage;

        Assert.Single(results);
        Assert.Equal(65_536, results[0].Read<int>());
        Assert.True(before.IsLimited);
        Assert.Equal(memoryLimit, before.LimitBytes);
        Assert.InRange(before.CurrentBytes, 1, memoryLimit);
        Assert.InRange(before.PeakBytes, before.CurrentBytes, memoryLimit);
        Assert.Equal(memoryLimit, after.LimitBytes);
        Assert.InRange(after.CurrentBytes, 1, memoryLimit);
        Assert.InRange(after.PeakBytes, after.CurrentBytes, memoryLimit);
        Assert.True(after.PeakBytes > before.PeakBytes);
    }

    [Fact]
    public void LimitedStatesCanBeCreatedRunAndDisposedRepeatedly()
    {
        for (var iteration = 0; iteration < 32; iteration++)
        {
            using var state = LuauState.Create(new LuauStateOptions
            {
                MemoryLimitBytes = 1_048_576,
                BytecodePolicy = LuauBytecodePolicy.Reject,
            });

            var results = state.DoString(
                $"return {iteration} + 1",
                $"@mods/stress-{iteration}.luau");

            Assert.Single(results);
            Assert.Equal(iteration + 1, results[0].Read<int>());
            Assert.Equal(1_048_576, state.MemoryUsage.LimitBytes);
            Assert.InRange(state.MemoryUsage.CurrentBytes, 1, 1_048_576);
            Assert.InRange(
                state.MemoryUsage.PeakBytes,
                state.MemoryUsage.CurrentBytes,
                1_048_576);
        }
    }

    [Fact]
    public void OversizedSourceIsRejectedBeforeCompilationAndRetainsChunkName()
    {
        const string chunkName = "@mods/source-too-large.luau";
        using var state = LuauState.Create(new LuauStateOptions
        {
            MaxSourceBytes = 8,
            BytecodePolicy = LuauBytecodePolicy.Reject,
        });

        // This input is intentionally invalid Luau. The size error must win over
        // any compiler diagnostic because the source bound is a preflight guard.
        var exception = Assert.Throws<LuauLoadLimitException>(() => state.DoString(
            "this is invalid Luau source",
            chunkName));

        Assert.Equal(chunkName, exception.ChunkName);
        Assert.Equal(LuauLoadInputKind.Source, exception.InputKind);
        Assert.Equal(27, exception.ActualBytes);
        Assert.Equal(8, exception.LimitBytes);
        Assert.Contains(chunkName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OversizedBytecodeIsRejectedBeforeValidatorOrNativeLoader()
    {
        const string chunkName = "@mods/bytecode-too-large.luau";
        var validator = new RecordingValidator(accept: true);
        using var state = LuauState.Create(new LuauStateOptions
        {
            MaxBytecodeBytes = 4,
            BytecodePolicy = LuauBytecodePolicy.RequireValidator,
            BytecodeValidator = validator,
        });
        var stackTop = state.GetTop();

        var exception = Assert.Throws<LuauLoadLimitException>(() => state.Load(
            [0xff, 0x00, 0x80, 0x01, 0x02],
            chunkName));

        Assert.Equal(chunkName, exception.ChunkName);
        Assert.Equal(LuauLoadInputKind.Bytecode, exception.InputKind);
        Assert.Equal(5, exception.ActualBytes);
        Assert.Equal(4, exception.LimitBytes);
        Assert.Equal(0, validator.CallCount);
        Assert.Equal(stackTop, state.GetTop());
    }

    [Fact]
    public void DefaultPolicyBlocksHostBytecodeButAllowsInternallyCompiledSource()
    {
        const string chunkName = "@mods/rejected-bytecode.luau";
        var bytecode = LuauCompiler.Compile("return 41"u8);
        using var state = LuauState.Create();
        var stackTop = state.GetTop();

        var exception = Assert.Throws<LuauException>(() => state.Execute(bytecode, chunkName));
        var sourceResults = state.DoString("return 42", "@mods/trusted-source.luau");

        Assert.Equal(chunkName, exception.ChunkName);
        Assert.Contains("disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(stackTop, state.GetTop());
        Assert.Single(sourceResults);
        Assert.Equal(42, sourceResults[0].Read<int>());
    }

    [Fact]
    public void TrustedBytecodeStillObservesByteSizeLimit()
    {
        const string chunkName = "@host/oversized-bundle.luau";
        var bytecode = LuauCompiler.Compile("return 1"u8);
        using var state = LuauState.Create(new LuauStateOptions
        {
            MaxBytecodeBytes = bytecode.Length - 1,
        });

        var exception = Assert.Throws<LuauLoadLimitException>(() =>
            state.ExecuteTrustedBytecode(bytecode, chunkName));

        Assert.Equal(LuauLoadInputKind.Bytecode, exception.InputKind);
        Assert.Equal(bytecode.Length, exception.ActualBytes);
        Assert.Equal(bytecode.Length - 1, exception.LimitBytes);
        Assert.Equal(chunkName, exception.ChunkName);
    }

    [Fact]
    public void TrustedBytecodeStillObservesExecutionLimit()
    {
        const string chunkName = "@host/bounded-bundle.luau";
        var bytecode = LuauCompiler.Compile("while true do end"u8);
        using var state = LuauState.Create(new LuauStateOptions
        {
            DefaultExecutionOptions = new LuauExecutionOptions
            {
                InterruptCountLimit = 1,
            },
        });

        var exception = Assert.Throws<LuauExecutionBudgetException>(() =>
            state.ExecuteTrustedBytecode(bytecode, chunkName));

        Assert.Equal(LuauExecutionBudgetKind.InterruptCount, exception.BudgetKind);
        Assert.Equal(chunkName, exception.ChunkName);
    }

    [Fact]
    public void ValidatorCanRejectBytecodeBeforeNativeLoading()
    {
        const string chunkName = "@mods/validator-rejected.luau";
        var validator = new RecordingValidator(accept: false);
        var bytecode = LuauCompiler.Compile("return 1"u8);
        using var state = LuauState.Create(new LuauStateOptions
        {
            BytecodePolicy = LuauBytecodePolicy.RequireValidator,
            BytecodeValidator = validator,
        });
        var stackTop = state.GetTop();

        var exception = Assert.Throws<LuauException>(() => state.Load(bytecode, chunkName));

        Assert.Equal(chunkName, exception.ChunkName);
        Assert.Contains("rejected", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, validator.CallCount);
        Assert.Equal(chunkName, validator.LastChunkName);
        Assert.Equal(bytecode, validator.LastBytecode);
        Assert.Equal(stackTop, state.GetTop());
    }

    [Fact]
    public void ValidatorCanAcceptValidBytecode()
    {
        const string chunkName = "@mods/validator-accepted.luau";
        var validator = new RecordingValidator(accept: true);
        var bytecode = LuauCompiler.Compile("return 123"u8);
        using var state = LuauState.Create(new LuauStateOptions
        {
            BytecodePolicy = LuauBytecodePolicy.RequireValidator,
            BytecodeValidator = validator,
        });

        var results = state.Execute(bytecode, chunkName);

        Assert.Single(results);
        Assert.Equal(123, results[0].Read<int>());
        Assert.Equal(1, validator.CallCount);
        Assert.Equal(chunkName, validator.LastChunkName);
        Assert.Equal(bytecode, validator.LastBytecode);
    }

    [Fact]
    public void SyntaxAndRuntimeErrorsRetainTheirChunkNames()
    {
        using var state = LuauState.Create(new LuauStateOptions
        {
            BytecodePolicy = LuauBytecodePolicy.Reject,
        });
        state.OpenBaseLibrary();

        var syntax = Assert.Throws<LuauException>(() => state.DoString(
            "local value =",
            "@mods/syntax-error.luau"));
        var runtime = Assert.Throws<LuauException>(() => state.DoString(
            "error('runtime boom')",
            "@mods/runtime-error.luau"));

        Assert.Equal("@mods/syntax-error.luau", syntax.ChunkName);
        Assert.Contains("@mods/syntax-error.luau", syntax.Message, StringComparison.Ordinal);
        Assert.Equal("@mods/runtime-error.luau", runtime.ChunkName);
        Assert.Contains("@mods/runtime-error.luau", runtime.Message, StringComparison.Ordinal);
        Assert.Contains("runtime boom", runtime.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HardenedRejectPolicyHandlesMalformedInputWithoutEnteringNativeLoader()
    {
        const string chunkName = "@mods/malformed-bytecode.luau";
        using var state = LuauState.Create(new LuauStateOptions
        {
            BytecodePolicy = LuauBytecodePolicy.Reject,
        });
        var stackTop = state.GetTop();

        var exception = Assert.Throws<LuauException>(() => state.Load(
            [0xff, 0xff, 0xff, 0xff, 0x7f, 0x00, 0x01],
            chunkName));

        Assert.Equal(chunkName, exception.ChunkName);
        Assert.Contains("precompiled bytecode is disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(stackTop, state.GetTop());
    }

    sealed class RecordingValidator(bool accept) : ILuauBytecodeValidator
    {
        public int CallCount { get; private set; }
        public byte[]? LastBytecode { get; private set; }
        public string? LastChunkName { get; private set; }

        public bool IsValid(ReadOnlySpan<byte> bytecode, ReadOnlySpan<byte> utf8ChunkName)
        {
            CallCount++;
            LastBytecode = bytecode.ToArray();
            LastChunkName = Encoding.UTF8.GetString(utf8ChunkName);
            return accept;
        }
    }
}
