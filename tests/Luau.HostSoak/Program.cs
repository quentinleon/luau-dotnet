#pragma warning disable CS0618 // Host soak coverage deliberately forces native GC through the transitional pointer API.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Luau;
using Luau.Native;

namespace Luau.HostSoak;

internal static class Program
{
    const int SchemaVersion = 1;
    const long MemoryLimit = 8 * 1024 * 1024;
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    const string Runtime = "LuauHost";

    static async Task<int> Main(string[] args)
    {
        try
        {
            return args.FirstOrDefault() switch
            {
                "run" => await RunCommandAsync(args[1..]).ConfigureAwait(false),
                _ => PrintUsage(),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"{exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    static async Task<int> RunCommandAsync(string[] args)
    {
        var output = ReadRequiredOption(args, "--output");
        var soakIterations = ReadIntOption(args, "--soak-iterations", 25, minimum: 1);
        RejectUnknownOptions(args, "--output", "--soak-iterations");

        var scenarios = await RunScenariosAsync().ConfigureAwait(false);
        var soak = await RunSoakAsync(soakIterations).ConfigureAwait(false);
        var report = new HarnessReport(
            SchemaVersion,
            Runtime,
            soakIterations,
            scenarios,
            soak);

        var fullOutput = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutput)!);
        await File.WriteAllTextAsync(
            fullOutput,
            JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine).ConfigureAwait(false);

        var failedSoak = soak.Where(outcome => outcome.Passed != outcome.Attempts).ToArray();
        if (failedSoak.Length != 0)
        {
            foreach (var failure in failedSoak)
            {
                Console.Error.WriteLine(
                    $"{Runtime}: soak group '{failure.Name}' failed after {failure.Passed} of " +
                    $"{failure.Attempts} iterations ({failure.FailureType}).");
            }
            Console.Error.WriteLine($"Partial structured report: {fullOutput}");
            return 1;
        }

        Console.WriteLine(
            $"{Runtime}: {scenarios.Count} host scenarios and " +
            $"{soak.Count} soak groups x {soakIterations} iterations passed.");
        Console.WriteLine($"Structured report: {fullOutput}");
        return 0;
    }

    static async Task<List<ScenarioOutcome>> RunScenariosAsync()
    {
        return
        [
            RunValueRoundTrip(),
            RunRuntimeErrorRecovery(),
            RunAllocatorFaultRecovery(),
            RunManagedCallbackLifetime(),
            await RunCancellationRecoveryAsync().ConfigureAwait(false),
            await RunRootDisposalDuringExecutionAsync().ConfigureAwait(false),
            RunSandboxedModuleCache(),
            RunInterruptBudgetRecovery(),
            RunReferenceRelease(),
        ];
    }

    static ScenarioOutcome RunValueRoundTrip()
    {
        using var state = CreateLimitedState();
        var beforeTop = state.GetTop();
        var beforeMemory = Snapshot(state);
        var values = state.DoString(
            "return true, 3.5, 'stage-three'",
            "@host-soak/value-round-trip.luau");
        var afterTop = state.GetTop();
        var afterMemory = Snapshot(state);

        Require(afterTop == beforeTop, "value-round-trip-stack");
        Require(values.Length == 3, "value-round-trip-count");
        Require(values[0].Read<bool>(), "value-round-trip-bool");
        Require(values[1].Read<double>() == 3.5, "value-round-trip-number");
        Require(values[2].Read<string>() == "stage-three", "value-round-trip-string");
        return Outcome(
            "value-round-trip",
            values,
            failure: null,
            beforeTop,
            afterTop,
            beforeMemory,
            afterMemory,
            state,
            usable: true);
    }

    static ScenarioOutcome RunRuntimeErrorRecovery()
    {
        using var state = CreateLimitedState();
        state.OpenBaseLibrary();
        var beforeTop = state.GetTop();
        var beforeMemory = Snapshot(state);
        LuauException failure;
        try
        {
            state.DoString("error('deterministic boom')", "@host-soak/runtime-error.luau");
            throw new HarnessAssertionException("runtime-error-not-raised");
        }
        catch (LuauException exception)
        {
            failure = exception;
        }

        var afterFailureTop = state.GetTop();
        var values = state.DoString("return 6 * 7", "@host-soak/runtime-recovery.luau");
        var afterMemory = Snapshot(state);
        Require(afterFailureTop == beforeTop, "runtime-error-stack");
        Require(ReadSingleInteger(values) == 42, "runtime-error-recovery");

        return Outcome(
            "runtime-error-recovery",
            values,
            Classify(failure, "script-runtime"),
            beforeTop,
            state.GetTop(),
            beforeMemory,
            afterMemory,
            state,
            usable: true);
    }

    static ScenarioOutcome RunAllocatorFaultRecovery()
    {
        const long limit = 1024 * 1024;
        using var state = CreateLimitedState(limit);
        state["pushHugeString"] = state.CreateFunction(
            "pushHugeString",
            callbackState =>
            {
                callbackState.PushString(new string('x', 2 * 1024 * 1024));
                return 1;
            });
        var beforeTop = state.GetTop();
        var beforeMemory = Snapshot(state);
        LuauManagedCallbackException failure;
        try
        {
            state.DoString("return pushHugeString()", "@host-soak/allocator-fault.luau");
            throw new HarnessAssertionException("allocator-fault-not-raised");
        }
        catch (LuauManagedCallbackException exception)
        {
            failure = exception;
        }

        var afterFailureTop = state.GetTop();
        var values = state.DoString("return 40 + 2", "@host-soak/allocator-recovery.luau");
        var afterMemory = Snapshot(state);
        var memoryFailure = failure.InnerException as LuauMemoryLimitException;
        Require(afterFailureTop == beforeTop, "allocator-fault-stack");
        Require(ReadSingleInteger(values) == 42, "allocator-fault-recovery");
        Require(memoryFailure is not null, "allocator-fault-inner-kind");
        Require(memoryFailure.AttemptedBytes > memoryFailure.LimitBytes, "allocator-attempted-usage");

        return Outcome(
            "allocator-fault-recovery",
            values,
            Classify(failure, "quota"),
            beforeTop,
            state.GetTop(),
            beforeMemory,
            afterMemory,
            state,
            usable: true);
    }

    static ScenarioOutcome RunManagedCallbackLifetime()
    {
        using var state = CreateLimitedState();
        var beforeTop = state.GetTop();
        var beforeMemory = Snapshot(state);
        var callback = state.CreateFunction(
            "explode",
            _ => throw new InvalidOperationException("managed callback failure"));
        state["explode"] = callback;
        callback.Dispose();

        LuauManagedCallbackException failure;
        try
        {
            state.DoString("explode()", "@host-soak/callback-failure.luau");
            throw new HarnessAssertionException("callback-failure-not-raised");
        }
        catch (LuauManagedCallbackException exception)
        {
            failure = exception;
        }

        Require(failure.CallbackName == "explode", "callback-name");
        Require(failure.InnerException is InvalidOperationException, "callback-inner-kind");
        Require(state.GetTop() == beforeTop, "callback-failure-stack");
        state["explode"] = LuauValue.Nil;
        ForceNativeAndManagedCollection(state);
        Require(state.Context.ManagedCallbackCount == 0, "callback-owner-release");
        var values = state.DoString("return 21 * 2", "@host-soak/callback-recovery.luau");
        Require(ReadSingleInteger(values) == 42, "callback-recovery");

        return Outcome(
            "managed-callback-lifetime",
            values,
            Classify(failure, "managed-callback"),
            beforeTop,
            state.GetTop(),
            beforeMemory,
            Snapshot(state),
            state,
            usable: true);
    }

    static async Task<ScenarioOutcome> RunCancellationRecoveryAsync()
    {
        using var state = CreateLimitedState();
        using var cancellation = new CancellationTokenSource();
        var entered = NewSignal();
        var release = NewSignal();
        state["pending"] = state.CreateFunction(
            "pending",
            async (_, _) =>
            {
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false);
                return 0;
            });

        var beforeTop = state.GetTop();
        var beforeMemory = Snapshot(state);
        var execution = state.DoStringAsync(
            "pending(); return 1",
            "@host-soak/cancellation.luau",
            cancellationToken: cancellation.Token).AsTask();
        await entered.Task.WaitAsync(Timeout).ConfigureAwait(false);
        cancellation.Cancel();
        var remainedPendingUntilCallbackCompleted = !execution.IsCompleted;
        release.TrySetResult();

        LuauExecutionCanceledException failure;
        try
        {
            await execution.WaitAsync(Timeout).ConfigureAwait(false);
            throw new HarnessAssertionException("cancellation-not-raised");
        }
        catch (LuauExecutionCanceledException exception)
        {
            failure = exception;
        }

        var values = state.DoString("return 42", "@host-soak/cancellation-recovery.luau");
        Require(remainedPendingUntilCallbackCompleted, "cancellation-completed-early");
        Require(state.GetTop() == beforeTop, "cancellation-stack");
        Require(ReadSingleInteger(values) == 42, "cancellation-recovery");

        return Outcome(
            "cancellation-recovery",
            values,
            Classify(failure, "canceled"),
            beforeTop,
            state.GetTop(),
            beforeMemory,
            Snapshot(state),
            state,
            usable: true,
            facts: new() { ["pendingUntilCallbackCompletion"] = remainedPendingUntilCallbackCompleted.ToString() });
    }

    static async Task<ScenarioOutcome> RunRootDisposalDuringExecutionAsync()
    {
        var state = CreateLimitedState();
        var context = state.Context;
        var entered = NewSignal();
        var release = NewSignal();
        var lateAccess = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        state["pending"] = state.CreateFunction(
            "pending",
            async (callbackState, _) =>
            {
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false);
                lateAccess.TrySetResult(CaptureException(() => callbackState.PushInteger(1)));
                return 0;
            });

        var beforeTop = state.GetTop();
        var beforeMemory = Snapshot(state);
        var execution = state.DoStringAsync(
            "pending(); return 1",
            "@host-soak/dispose-active.luau").AsTask();
        await entered.Task.WaitAsync(Timeout).ConfigureAwait(false);
        state.Dispose();
        var deferredClose = !execution.IsCompleted;
        release.TrySetResult();

        ObjectDisposedException failure;
        try
        {
            await execution.WaitAsync(Timeout).ConfigureAwait(false);
            throw new HarnessAssertionException("active-disposal-not-raised");
        }
        catch (ObjectDisposedException exception)
        {
            failure = exception;
        }

        var lateFailure = await lateAccess.Task.WaitAsync(Timeout).ConfigureAwait(false);
        Require(lateFailure is ObjectDisposedException, "late-callback-access-kind");
        Require(deferredClose, "active-disposal-close-not-deferred");
        Require(context.IsDisposed, "active-disposal-context-open");
        Require(context.CloseCount == 1, "active-disposal-close-count");

        return Outcome(
            "root-disposal-during-execution",
            [],
            Classify(failure, "disposed"),
            beforeTop,
            afterTop: null,
            beforeMemory,
            Snapshot(state),
            state,
            usable: false,
            facts: new()
            {
                ["closeDeferred"] = deferredClose.ToString(),
                ["lateAccessFailure"] = lateFailure!.GetType().Name,
            });
    }

    static ScenarioOutcome RunSandboxedModuleCache()
    {
        using var root = CreateLimitedState();
        root.OpenBaseLibrary();
        var requirer = new CountingRequirer();
        root.OpenRequireLibrary(requirer);
        root.SandboxRoot();
        using var first = root.CreateSandboxedThread();
        using var second = root.CreateSandboxedThread();
        var beforeTop = root.GetTop();
        var beforeMemory = Snapshot(root);

        var firstValues = first.DoString(
            "privateValue = 11; return require('shared'), privateValue",
            "@host-soak/sandbox-first.luau");
        var secondValues = second.DoString(
            "return require('shared'), privateValue == nil",
            "@host-soak/sandbox-second.luau");
        Require(firstValues[0].Read<int>() == 73, "sandbox-first-module");
        Require(firstValues[1].Read<int>() == 11, "sandbox-first-private");
        Require(secondValues[0].Read<int>() == 73, "sandbox-second-module");
        Require(secondValues[1].Read<bool>(), "sandbox-sibling-isolation");
        Require(requirer.LoadCount == 1, "sandbox-cache-count");
        var rootValues = root.DoString("return 42", "@host-soak/sandbox-root-recovery.luau");
        Require(ReadSingleInteger(rootValues) == 42, "sandbox-root-recovery");

        return Outcome(
            "sandboxed-module-cache",
            [.. firstValues, .. secondValues, .. rootValues],
            failure: null,
            beforeTop,
            root.GetTop(),
            beforeMemory,
            Snapshot(root),
            root,
            usable: true,
            facts: new() { ["moduleLoadCount"] = requirer.LoadCount.ToString(CultureInfo.InvariantCulture) });
    }

    static ScenarioOutcome RunInterruptBudgetRecovery()
    {
        using var state = CreateLimitedState();
        var beforeTop = state.GetTop();
        var beforeMemory = Snapshot(state);
        LuauExecutionBudgetException failure;
        try
        {
            state.DoString(
                "while true do end",
                "@host-soak/interrupt-budget.luau",
                executionOptions: new LuauExecutionOptions { InterruptCountLimit = 1 });
            throw new HarnessAssertionException("interrupt-budget-not-raised");
        }
        catch (LuauExecutionBudgetException exception)
        {
            failure = exception;
        }

        Require(failure.BudgetKind == LuauExecutionBudgetKind.InterruptCount, "interrupt-budget-kind");
        Require(failure.ObservedInterruptCount > failure.InterruptCountLimit, "interrupt-budget-count");
        var values = state.DoString("return 42", "@host-soak/interrupt-recovery.luau");
        Require(state.GetTop() == beforeTop, "interrupt-budget-stack");
        Require(ReadSingleInteger(values) == 42, "interrupt-budget-recovery");

        return Outcome(
            "interrupt-budget-recovery",
            values,
            Classify(failure, "interrupt-budget"),
            beforeTop,
            state.GetTop(),
            beforeMemory,
            Snapshot(state),
            state,
            usable: true);
    }

    static ScenarioOutcome RunReferenceRelease()
    {
        using var root = CreateLimitedState();
        var context = root.Context;
        var beforeTop = root.GetTop();
        var beforeMemory = Snapshot(root);
        var releasedBefore = context.ReleasedReferenceCount;
        var child = root.CreateThread();
        var table = root.CreateTable();
        child.Dispose();
        table.Dispose();
        var released = context.ReleasedReferenceCount - releasedBefore;
        var values = root.DoString("return 42", "@host-soak/reference-recovery.luau");
        Require(released == 2, "reference-release-count");
        Require(ReadSingleInteger(values) == 42, "reference-recovery");

        return Outcome(
            "reference-release",
            values,
            failure: null,
            beforeTop,
            root.GetTop(),
            beforeMemory,
            Snapshot(root),
            root,
            usable: true,
            facts: new() { ["releasedByScenario"] = released.ToString(CultureInfo.InvariantCulture) });
    }

    static async Task<List<SoakOutcome>> RunSoakAsync(int iterations)
    {
        var groups = new (string Name, Func<int, Task> Run)[]
        {
            ("root-create-dispose", iteration => { SoakRootCreateDispose(iteration); return Task.CompletedTask; }),
            ("child-gc-fallback", iteration => { SoakChildGcFallback(iteration); return Task.CompletedTask; }),
            ("callback-closure-collection", iteration => { SoakCallbackCollection(iteration); return Task.CompletedTask; }),
            ("cancellation-race", SoakCancellationRaceAsync),
            ("root-disposal-active", SoakRootDisposalActiveAsync),
            ("allocator-pressure", iteration => { SoakAllocatorPressure(iteration); return Task.CompletedTask; }),
            ("async-callback-completion-failure", SoakAsyncCallbackCompletionFailureAsync),
            ("sandbox-module-cache-reuse", iteration => { SoakSandboxModuleCache(iteration); return Task.CompletedTask; }),
        };

        var outcomes = new List<SoakOutcome>(groups.Length);
        foreach (var group in groups)
        {
            var passed = 0;
            string? failureType = null;
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                try
                {
                    await group.Run(iteration).ConfigureAwait(false);
                    passed++;
                }
                catch (Exception exception)
                {
                    failureType = exception.GetType().Name;
                    break;
                }
            }

            outcomes.Add(new(group.Name, iterations, passed, failureType));
        }

        return outcomes;
    }

    static void SoakRootCreateDispose(int iteration)
    {
        var state = CreateLimitedState();
        var context = state.Context;
        Require(ReadSingleInteger(state.DoString($"return {iteration} + 1")) == iteration + 1, "soak-root-value");
        state.Dispose();
        Require(state.IsDisposed && context.IsDisposed, "soak-root-disposed");
        Require(context.CloseCount == 1, "soak-root-close-count");
        Require(state.MemoryUsage.CurrentBytes == 0, "soak-root-final-memory");
    }

    static void SoakChildGcFallback(int iteration)
    {
        using var root = CreateLimitedState();
        var releasedBefore = root.Context.ReleasedReferenceCount;
        var child = CreateAbandonedChild(root);
        ForceFinalizersUntil(() => !child.TryGetTarget(out _));
        Require(root.Context.ReleasedReferenceCount == releasedBefore + 1, "soak-child-release");
        Require(ReadSingleInteger(root.DoString($"return {iteration}")) == iteration, "soak-child-root-recovery");
    }

    static void SoakCallbackCollection(int iteration)
    {
        using var state = CreateLimitedState();
        PushTransientCallbacks(state, iteration);
        state["transient"] = LuauValue.Nil;
        ForceNativeAndManagedCollection(state);
        Require(state.Context.ManagedCallbackCount == 0, "soak-callback-count");
    }

    static async Task SoakCancellationRaceAsync(int iteration)
    {
        using var state = CreateLimitedState();
        using var cancellation = new CancellationTokenSource();
        var entered = NewSignal();
        var release = NewSignal();
        state["pending"] = state.CreateFunction(
            $"pending-{iteration}",
            async (_, _) =>
            {
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false);
                return 0;
            });
        var execution = state.DoStringAsync("pending()", cancellationToken: cancellation.Token).AsTask();
        await entered.Task.WaitAsync(Timeout).ConfigureAwait(false);
        cancellation.Cancel();
        Require(!execution.IsCompleted, "soak-cancellation-early");
        release.TrySetResult();
        await RequireThrowsAsync<LuauExecutionCanceledException>(execution).ConfigureAwait(false);
        Require(ReadSingleInteger(state.DoString("return 42")) == 42, "soak-cancellation-recovery");
    }

    static async Task SoakRootDisposalActiveAsync(int iteration)
    {
        var state = CreateLimitedState();
        var context = state.Context;
        var entered = NewSignal();
        var release = NewSignal();
        state["pending"] = state.CreateFunction(
            $"pending-{iteration}",
            async (_, _) =>
            {
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false);
                return 0;
            });
        var execution = state.DoStringAsync("pending()").AsTask();
        await entered.Task.WaitAsync(Timeout).ConfigureAwait(false);
        state.Dispose();
        Require(!execution.IsCompleted, "soak-disposal-early");
        release.TrySetResult();
        await RequireThrowsAsync<ObjectDisposedException>(execution).ConfigureAwait(false);
        Require(context.IsDisposed && context.CloseCount == 1, "soak-disposal-close");
    }

    static void SoakAllocatorPressure(int iteration)
    {
        using var state = CreateLimitedState(1024 * 1024);
        state["pushHugeString"] = state.CreateFunction(
            $"pushHugeString-{iteration}",
            callbackState =>
            {
                callbackState.PushString(new string('x', 2 * 1024 * 1024));
                return 1;
            });
        var top = state.GetTop();
        var failure = RequireThrows<LuauManagedCallbackException>(() => state.DoString("return pushHugeString()"));
        Require(failure.InnerException is LuauMemoryLimitException, "soak-allocator-inner-kind");
        Require(state.GetTop() == top, "soak-allocator-stack");
        Require(ReadSingleInteger(state.DoString($"return {iteration} + 1")) == iteration + 1, "soak-allocator-recovery");
    }

    static async Task SoakAsyncCallbackCompletionFailureAsync(int iteration)
    {
        using var state = CreateLimitedState();
        state["succeed"] = state.CreateFunction(
            $"succeed-{iteration}",
            async (callbackState, _) =>
            {
                await Task.Yield();
                callbackState.PushInteger(42);
                return 1;
            });
        state["fail"] = state.CreateFunction(
            $"fail-{iteration}",
            async (_, _) =>
            {
                await Task.Yield();
                throw new InvalidOperationException("expected soak failure");
            });

        Require(ReadSingleInteger(await state.DoStringAsync("return succeed()").ConfigureAwait(false)) == 42, "soak-async-success");
        await RequireThrowsAsync<LuauManagedCallbackException>(state.DoStringAsync("fail()").AsTask()).ConfigureAwait(false);
        Require(ReadSingleInteger(state.DoString("return 42")) == 42, "soak-async-failure-recovery");
    }

    static void SoakSandboxModuleCache(int iteration)
    {
        using var root = CreateLimitedState();
        root.OpenBaseLibrary();
        var requirer = new CountingRequirer();
        root.OpenRequireLibrary(requirer);
        root.SandboxRoot();
        using var first = root.CreateSandboxedThread();
        using var second = root.CreateSandboxedThread();
        Require(ReadSingleInteger(first.DoString("return require('shared')")) == 73, "soak-module-first");
        Require(ReadSingleInteger(second.DoString("return require('shared')")) == 73, "soak-module-second");
        Require(requirer.LoadCount == 1, "soak-module-load-count");
        Require(ReadSingleInteger(root.DoString($"return {iteration}")) == iteration, "soak-module-root-recovery");
    }

    static ScenarioOutcome Outcome(
        string name,
        IReadOnlyList<LuauValue> values,
        FailureObservation? failure,
        int beforeTop,
        int? afterTop,
        MemorySnapshot beforeMemory,
        MemorySnapshot afterMemory,
        LuauState state,
        bool usable,
        SortedDictionary<string, string>? facts = null)
    {
        Require(afterTop is null || afterTop == beforeTop, $"{name}-stack");
        Require(usable == !state.IsDisposed, $"{name}-usable-state");
        return new(
            name,
            values.Select(ValueObservation.From).ToArray(),
            failure,
            new(beforeTop, afterTop),
            new(beforeMemory, afterMemory),
            new(usable, state.IsDisposed),
            new(
                state.Context.ReleasedReferenceCount,
                state.Context.ManagedCallbackCount,
                state.Context.CloseCount),
            facts ?? []);
    }

    static FailureObservation Classify(Exception exception, string kind)
    {
        string? chunkName = exception switch
        {
            LuauException luau => luau.ChunkName,
            LuauExecutionCanceledException canceled => canceled.ChunkName,
            _ => null,
        };
        var details = new SortedDictionary<string, string>(StringComparer.Ordinal);
        switch (exception)
        {
            case LuauMemoryLimitException memory:
                details["attemptedExceedsLimit"] = (memory.AttemptedBytes > memory.LimitBytes).ToString();
                details["limitBytes"] = memory.LimitBytes.ToString(CultureInfo.InvariantCulture);
                break;
            case LuauManagedCallbackException callback:
                details["callbackName"] = callback.CallbackName ?? "";
                details["innerException"] = callback.InnerException?.GetType().Name ?? "";
                if (callback.InnerException is LuauMemoryLimitException innerMemory)
                {
                    details["attemptedExceedsLimit"] =
                        (innerMemory.AttemptedBytes > innerMemory.LimitBytes).ToString();
                    details["limitBytes"] = innerMemory.LimitBytes.ToString(CultureInfo.InvariantCulture);
                }
                break;
            case LuauExecutionBudgetException budget:
                details["budgetKind"] = budget.BudgetKind.ToString();
                details["observedExceedsLimit"] =
                    (budget.ObservedInterruptCount > budget.InterruptCountLimit).ToString();
                break;
        }

        return new(kind, exception.GetType().Name, chunkName, details);
    }

    static LuauState CreateLimitedState(long memoryLimit = MemoryLimit)
    {
        return LuauState.Create(new LuauStateOptions
        {
            MemoryLimitBytes = memoryLimit,
            BytecodePolicy = LuauBytecodePolicy.Reject,
        });
    }

    static MemorySnapshot Snapshot(LuauState state) => MemorySnapshot.From(state.MemoryUsage);

    static int ReadSingleInteger(IReadOnlyList<LuauValue> values)
    {
        Require(values.Count == 1, "expected-single-value");
        return values[0].Read<int>();
    }

    static void ForceNativeAndManagedCollection(LuauState state)
    {
        unsafe
        {
            NativeMethods.lua_gc(state.AsPointer(), 2, 0); // LUA_GCCOLLECT
        }
        ForceFinalizersUntil(() => state.Context.ManagedCallbackCount == 0);
    }

    static void ForceFinalizersUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 10 && !condition(); attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
        Require(condition(), "finalizer-retry-budget");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference<LuauState> CreateAbandonedChild(LuauState root) => new(root.CreateThread());

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void PushTransientCallbacks(LuauState state, int iteration)
    {
        for (var index = 0; index < 16; index++)
        {
            state["transient"] = state.CreateFunction($"transient-{iteration}-{index}", _ => 0);
        }
    }

    static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    static Exception? CaptureException(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    static T RequireThrows<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            return exception;
        }
        throw new HarnessAssertionException($"expected-{typeof(T).Name}");
    }

    static async Task<T> RequireThrowsAsync<T>(Task task) where T : Exception
    {
        try
        {
            await task.WaitAsync(Timeout).ConfigureAwait(false);
        }
        catch (T exception)
        {
            return exception;
        }
        throw new HarnessAssertionException($"expected-{typeof(T).Name}");
    }

    static void Require([DoesNotReturnIf(false)] bool condition, string code)
    {
        if (!condition)
        {
            throw new HarnessAssertionException(code);
        }
    }

    static string ReadRequiredOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0 || index == args.Length - 1 || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{name} requires a value.");
        }
        return args[index + 1];
    }

    static int ReadIntOption(string[] args, string name, int fallback, int minimum)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0)
        {
            return fallback;
        }
        if (index == args.Length - 1 ||
            !int.TryParse(args[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
            value < minimum)
        {
            throw new ArgumentException($"{name} requires an integer of at least {minimum}.");
        }
        return value;
    }

    static void RejectUnknownOptions(string[] args, params string[] known)
    {
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !known.Contains(args[index], StringComparer.Ordinal))
            {
                throw new ArgumentException($"Unknown or incomplete option: {args[index]}");
            }
        }
    }

    static int PrintUsage()
    {
        Console.Error.WriteLine(
            "Usage:\n" +
            "  Luau.HostSoak run --output <report.json> [--soak-iterations <count>]");
        return 2;
    }

    static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    sealed class CountingRequirer : LuauRequirer
    {
        int loadCount;
        internal int LoadCount => Volatile.Read(ref loadCount);

        protected override bool TryLoadModule(LuauState state, string fullPath, string requireArgument)
        {
            Interlocked.Increment(ref loadCount);
            state.PushInteger(73);
            return true;
        }

        protected override bool TryGetAliasPath(string alias, [NotNullWhen(true)] out string? path)
        {
            path = null;
            return false;
        }
    }

    sealed class HarnessAssertionException : Exception
    {
        internal HarnessAssertionException(string message) : base(message) { }
    }
}

internal sealed record HarnessReport(
    int SchemaVersion,
    string Runtime,
    int SoakIterations,
    List<ScenarioOutcome> Scenarios,
    List<SoakOutcome> Soak);

internal sealed record ScenarioOutcome(
    string Name,
    ValueObservation[] ReturnedValues,
    FailureObservation? Failure,
    StackObservation Stack,
    MemoryObservation Memory,
    StateObservation State,
    LifetimeObservation Lifetime,
    SortedDictionary<string, string> Facts);

internal sealed record ValueObservation(string Kind, string Value)
{
    internal static ValueObservation From(LuauValue value)
    {
        var text = value.Type switch
        {
            LuauType.Nil => "nil",
            LuauType.Boolean => value.Read<bool>().ToString(),
            LuauType.Number => value.Read<double>().ToString("R", CultureInfo.InvariantCulture),
            LuauType.Integer => value.Read<long>().ToString(CultureInfo.InvariantCulture),
            LuauType.String => value.Read<string>(),
            LuauType.Vector => value.Read<System.Numerics.Vector3>().ToString(),
            _ => value.Type.ToString(),
        };
        return new(value.Type.ToString(), text);
    }
}

internal sealed record FailureObservation(
    string Kind,
    string ExceptionType,
    string? ChunkName,
    SortedDictionary<string, string> Details);

internal sealed record StackObservation(int Before, int? After);
internal sealed record MemoryObservation(MemorySnapshot Before, MemorySnapshot After);
internal sealed record StateObservation(bool Usable, bool Disposed);
internal sealed record LifetimeObservation(int ReleasedReferences, int ManagedCallbacks, int ClosedRoots);
internal sealed record SoakOutcome(string Name, int Attempts, int Passed, string? FailureType);

internal sealed record MemorySnapshot(
    long CurrentBytes,
    long PeakBytes,
    long? LimitBytes,
    bool IsTracked,
    bool IsLimited)
{
    internal static MemorySnapshot From(LuauMemoryUsageSnapshot value) =>
        new(value.CurrentBytes, value.PeakBytes, value.LimitBytes, value.IsTracked, value.IsLimited);
}
