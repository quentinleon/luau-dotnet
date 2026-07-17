using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Luau.Internal.Interop;
using static Luau.Internal.Interop.NativeMethods;

namespace Luau;

internal enum LuauAllocatorFailure
{
    None = 0,
    QuotaExceeded = 1,
    SystemOutOfMemory = 2,
}

internal sealed unsafe class LuauVmContext
{
    static readonly ConcurrentDictionary<IntPtr, WeakReference<LuauState>> globalStates = new();
    static readonly ConcurrentDictionary<IntPtr, WeakReference<LuauVmContext>> globalContexts = new();
    static readonly ConcurrentDictionary<int, WeakReference<LuauVmContext>> managedCallbackOwners = new();
    static readonly AsyncLocal<ScriptOperation?> ambientOperation = new();
    static readonly LuauHostInterruptPoll interruptCallback = Interrupt;
    static readonly IntPtr interruptPointer = Marshal.GetFunctionPointerForDelegate(interruptCallback);
    static int nextGlobalManagedCallbackId;

    readonly object lifecycleGate = new();
    readonly object nativeGate = new();
    readonly Dictionary<IntPtr, WeakReference<LuauState>> states = [];
    readonly Dictionary<int, LuauManagedCallbackRegistration> managedCallbacks = [];
    readonly Dictionary<int, int> managedCallbackNativeReferences = [];
    readonly HashSet<int> managedCallbackWrapperOwners = [];
    readonly Dictionary<string, LuauValue> moduleCache = new(StringComparer.Ordinal);
    readonly HashSet<string> loadingModules = new(StringComparer.Ordinal);
    readonly HashSet<IntPtr> sandboxedThreads = [];
    readonly List<int> deferredReferences = [];
    readonly CancellationTokenSource disposalCancellationSource = new();

    IntPtr mainPointer;
    WeakReference<LuauState>? rootEntry;
    ScriptOperation? activeOperation;
    int lifecycleState;
    int rootSandboxed;
    int releasedReferenceCount;
    int closeCount;

    LuauHostMemoryInfo finalMemoryInfo;
    bool hasFinalMemoryInfo;

    internal LuauVmContext(LuauHostState* main, LuauStateOptions options)
    {
        mainPointer = (IntPtr)main;
        Options = options;
    }

    internal LuauStateOptions Options { get; }

    internal bool IsDisposed => Volatile.Read(ref lifecycleState) != 0;
    internal bool IsDisposalRequested => Volatile.Read(ref lifecycleState) != 0;
    internal CancellationToken DisposalToken => disposalCancellationSource.Token;
    internal bool IsRootSandboxed => Volatile.Read(ref rootSandboxed) != 0;

    internal int CachedStateCount
    {
        get
        {
            lock (lifecycleGate)
            {
                return states.Count;
            }
        }
    }

    internal int ReleasedReferenceCount => Volatile.Read(ref releasedReferenceCount);
    internal int CloseCount => Volatile.Read(ref closeCount);

    internal int ManagedCallbackCount
    {
        get
        {
            lock (lifecycleGate)
            {
                return managedCallbacks.Count;
            }
        }
    }

    internal LuauMemoryUsageSnapshot MemoryUsage
    {
        get
        {
            lock (nativeGate)
            {
                var info = ReadMemoryInfo();
                if (info.tracked == 0)
                {
                    return LuauMemoryUsageSnapshot.Untracked;
                }

                long? limit = info.limit_bytes == 0
                    ? null
                    : ToDiagnosticByteCount(info.limit_bytes);
                return new LuauMemoryUsageSnapshot(
                    ToDiagnosticByteCount(info.current_bytes),
                    ToDiagnosticByteCount(info.peak_bytes),
                    limit);
            }
        }
    }

    internal LuauAllocatorFailure AllocatorFailure
    {
        get
        {
            lock (nativeGate)
            {
                return ReadMemoryInfo().failure switch
                {
                    LuauHostAllocatorFailure.None => LuauAllocatorFailure.None,
                    LuauHostAllocatorFailure.Quota => LuauAllocatorFailure.QuotaExceeded,
                    LuauHostAllocatorFailure.System => LuauAllocatorFailure.SystemOutOfMemory,
                    var failure => throw new LuauException(
                        $"The Luau host returned unknown allocator failure {failure}.")
                };
            }
        }
    }

    internal long LastAttemptedAllocationBytes
    {
        get
        {
            lock (nativeGate)
            {
                return ToDiagnosticByteCount(ReadMemoryInfo().last_attempted_bytes);
            }
        }
    }

    internal void ResetAllocatorFailure()
    {
        lock (nativeGate)
        {
            if (mainPointer == IntPtr.Zero)
            {
                return;
            }

            var status = luau_host_memory_reset_failure((LuauHostState*)mainPointer);
            if (status != LuauHostStatus.Ok)
            {
                throw new LuauException(
                    $"The Luau host could not reset allocator diagnostics (status {(int)status}).");
            }
        }
    }

    internal void ArmQuotaFailureOnNextGrowth()
    {
        lock (nativeGate)
        {
            if (mainPointer == IntPtr.Zero)
            {
                ThrowHelper.ThrowObjectDisposedException(nameof(LuauState));
            }

            var status = luau_host_memory_arm_quota_failure((LuauHostState*)mainPointer);
            if (status == LuauHostStatus.Unsupported)
            {
                throw new InvalidOperationException(
                    "A finite native memory limit is required before quota fault injection can be armed.");
            }
            if (status != LuauHostStatus.Ok)
            {
                throw new LuauException(
                    $"The Luau host could not arm quota fault injection (status {(int)status}).");
            }
        }
    }

    LuauHostMemoryInfo ReadMemoryInfo()
    {
        if (mainPointer == IntPtr.Zero)
        {
            return hasFinalMemoryInfo ? finalMemoryInfo : default;
        }

        var info = new LuauHostMemoryInfo
        {
            struct_size = checked((uint)sizeof(LuauHostMemoryInfo)),
        };
        var status = luau_host_memory_get((LuauHostState*)mainPointer, &info);
        if (status != LuauHostStatus.Ok)
        {
            throw new LuauException(
                $"The Luau host could not report allocator diagnostics (status {(int)status}).");
        }

        return info;
    }

    internal static long ToDiagnosticByteCount(ulong value)
    {
        return value > long.MaxValue ? long.MaxValue : (long)value;
    }

    internal ScriptOperation BeginOperation(
        LuauState state,
        string? chunkName,
        LuauExecutionOptions? options,
        CancellationToken cancellationToken,
        bool isAsync,
        ScriptOperationMode mode)
    {
        var effectiveOptions = LuauExecutionOptions.ResolveForOperation(
            Options.DefaultExecutionOptions,
            options);
        if (effectiveOptions.ContinuationScheduler is { } scheduler && !scheduler.CheckAccess())
        {
            throw new InvalidOperationException(
                "Luau execution must begin on its configured continuation scheduler. " +
                "Dispatch the call to the VM owner thread before compiling, pushing arguments, or resuming it.");
        }

        lock (nativeGate)
        {
            lock (lifecycleGate)
            {
                if (lifecycleState != 0 || mainPointer == IntPtr.Zero)
                {
                    ThrowHelper.ThrowObjectDisposedException(nameof(LuauState));
                }

                if (activeOperation != null)
                {
                    ThrowHelper.ThrowInvalidOperationException("The Luau VM is already executing.");
                }

                var operation = new ScriptOperation(
                    this,
                    state,
                    chunkName,
                    effectiveOptions,
                    cancellationToken,
                    isAsync,
                    mode,
                    ambientOperation.Value);

                var callbacks = new LuauHostCallbackTable
                {
                    struct_size = (uint)sizeof(LuauHostCallbackTable),
                    version = 1,
                    interrupt_poll = interruptPointer,
                };
                if (luau_host_interrupt_install(
                        (LuauHostState*)mainPointer,
                        &callbacks) != LuauHostStatus.Ok)
                {
                    operation.Dispose();
                    throw new PlatformNotSupportedException(
                        "The native Luau plugin could not install the managed execution interrupt trampoline.");
                }

                ResetAllocatorFailure();
                activeOperation = operation;
                ambientOperation.Value = operation;
                return operation;
            }
        }
    }

    internal void EndOperation(ScriptOperation operation)
    {
        IntPtr pointerToClose = IntPtr.Zero;

        lock (nativeGate)
        {
            lock (lifecycleGate)
            {
                if (!ReferenceEquals(activeOperation, operation))
                {
                    return;
                }

                if (mainPointer != IntPtr.Zero)
                {
                    luau_host_interrupt_uninstall((LuauHostState*)mainPointer);
                }

                activeOperation = null;
                if (ReferenceEquals(ambientOperation.Value, operation))
                {
                    ambientOperation.Value = operation.PreviousAmbient;
                }

                if (lifecycleState == 0 && mainPointer != IntPtr.Zero)
                {
                    foreach (var reference in deferredReferences)
                    {
                        luau_host_reference_release((LuauHostState*)mainPointer, reference);
                        Interlocked.Increment(ref releasedReferenceCount);
                    }
                }

                deferredReferences.Clear();

                if (lifecycleState == 1 && mainPointer != IntPtr.Zero)
                {
                    pointerToClose = mainPointer;
                    mainPointer = IntPtr.Zero;
                }
            }

            if (pointerToClose != IntPtr.Zero)
            {
                CloseNative(pointerToClose);
            }
        }
    }

    internal LuauNativeAccess EnterNativeAccess(LuauState state)
    {
        Monitor.Enter(nativeGate);
        try
        {
            if (Volatile.Read(ref lifecycleState) != 0 ||
                state.IsDisposed ||
                state.PointerUnsafe == null)
            {
                ThrowHelper.ThrowObjectDisposedException(nameof(LuauState));
            }

            ThrowIfNativeAccessDenied();
            return new LuauNativeAccess(nativeGate);
        }
        catch
        {
            Monitor.Exit(nativeGate);
            throw;
        }
    }

    internal LuauNativeAccess EnterOperationNativeAccess(ScriptOperation operation)
    {
        Monitor.Enter(nativeGate);
        try
        {
            lock (lifecycleGate)
            {
                if (!ReferenceEquals(activeOperation, operation) || mainPointer == IntPtr.Zero)
                {
                    ThrowHelper.ThrowObjectDisposedException(nameof(LuauState));
                }
            }

            return new LuauNativeAccess(nativeGate);
        }
        catch
        {
            Monitor.Exit(nativeGate);
            throw;
        }
    }

    internal void ThrowIfNativeAccessDenied()
    {
        var active = Volatile.Read(ref activeOperation);
        var scheduler = active?.Options.ContinuationScheduler
            ?? Options.DefaultExecutionOptions.ContinuationScheduler;
        if (scheduler != null && !scheduler.CheckAccess())
        {
            throw new InvalidOperationException(
                "The Luau VM can only be accessed from its configured continuation scheduler.");
        }

        if (active != null && !ReferenceEquals(ambientOperation.Value, active))
        {
            ThrowHelper.ThrowInvalidOperationException("The Luau VM is executing on another operation.");
        }

        active?.ThrowIfAsyncCallbackAccessUnsafe();
    }

    internal ScriptOperation? GetActiveOperation()
    {
        return Volatile.Read(ref activeOperation);
    }

    internal int RegisterManagedCallback(
        string? name,
        Func<LuauState, CancellationToken, int> callback)
    {
        lock (lifecycleGate)
        {
            if (lifecycleState != 0)
            {
                ThrowHelper.ThrowObjectDisposedException(nameof(LuauState));
            }

            var id = NextManagedCallbackId();
            managedCallbacks.Add(id, new LuauManagedCallbackRegistration(id, name, callback));
            managedCallbackWrapperOwners.Add(id);
            managedCallbackOwners[id] = new WeakReference<LuauVmContext>(this);
            return id;
        }
    }

    internal int RegisterManagedCallback(
        string? name,
        Func<LuauState, CancellationToken, ValueTask<int>> callback)
    {
        lock (lifecycleGate)
        {
            if (lifecycleState != 0)
            {
                ThrowHelper.ThrowObjectDisposedException(nameof(LuauState));
            }

            var id = NextManagedCallbackId();
            managedCallbacks.Add(id, new LuauManagedCallbackRegistration(id, name, callback));
            managedCallbackWrapperOwners.Add(id);
            managedCallbackOwners[id] = new WeakReference<LuauVmContext>(this);
            return id;
        }
    }

    internal bool TryGetManagedCallback(int id, out LuauManagedCallbackRegistration registration)
    {
        lock (lifecycleGate)
        {
            return managedCallbacks.TryGetValue(id, out registration!);
        }
    }

    internal void AddManagedCallbackNativeReference(int id)
    {
        lock (lifecycleGate)
        {
            if (!managedCallbacks.ContainsKey(id))
            {
                ThrowHelper.ThrowObjectDisposedException(nameof(LuauFunction));
            }

            managedCallbackNativeReferences.TryGetValue(id, out var count);
            managedCallbackNativeReferences[id] = checked(count + 1);
        }
    }

    internal void ReleaseManagedCallbackWrapper(int id)
    {
        var removeGlobalOwner = false;
        lock (lifecycleGate)
        {
            managedCallbackWrapperOwners.Remove(id);
            removeGlobalOwner = RemoveManagedCallbackIfUnownedLocked(id);
        }

        if (removeGlobalOwner)
        {
            managedCallbackOwners.TryRemove(id, out _);
        }
    }

    internal static void ReleaseManagedCallbackFromNative(int id)
    {
        if (id == 0 ||
            !managedCallbackOwners.TryGetValue(id, out var owner) ||
            !owner.TryGetTarget(out var context))
        {
            managedCallbackOwners.TryRemove(id, out _);
            return;
        }

        var removeGlobalOwner = false;
        lock (context.lifecycleGate)
        {
            if (context.managedCallbackNativeReferences.TryGetValue(id, out var count))
            {
                if (count <= 1)
                {
                    context.managedCallbackNativeReferences.Remove(id);
                }
                else
                {
                    context.managedCallbackNativeReferences[id] = count - 1;
                }
            }

            removeGlobalOwner = context.RemoveManagedCallbackIfUnownedLocked(id);
        }

        if (removeGlobalOwner)
        {
            managedCallbackOwners.TryRemove(id, out _);
        }
    }

    internal void MarkRootSandboxed()
    {
        if (Interlocked.Exchange(ref rootSandboxed, 1) != 0)
        {
            ThrowHelper.ThrowInvalidOperationException("The Luau root state is already sandboxed.");
        }
    }

    internal bool IsThreadSandboxed(IntPtr pointer)
    {
        lock (lifecycleGate)
        {
            return sandboxedThreads.Contains(pointer);
        }
    }

    internal void MarkThreadSandboxed(IntPtr pointer)
    {
        lock (lifecycleGate)
        {
            if (!sandboxedThreads.Add(pointer))
            {
                ThrowHelper.ThrowInvalidOperationException("The Luau child thread is already sandboxed.");
            }
        }
    }

    internal bool TryGetCachedModule(string key, out LuauValue value)
    {
        lock (lifecycleGate)
        {
            return moduleCache.TryGetValue(key, out value);
        }
    }

    internal void CacheModule(string key, LuauValue value)
    {
        lock (lifecycleGate)
        {
            if (lifecycleState != 0)
            {
                ThrowHelper.ThrowObjectDisposedException(nameof(LuauState));
            }

            moduleCache[key] = value;
        }
    }

    internal void BeginModuleLoad(string key, string requireArgument)
    {
        lock (lifecycleGate)
        {
            if (!loadingModules.Add(key))
            {
                throw new LuauException(
                    $"Circular module dependency detected while requiring '{requireArgument}'.");
            }
        }
    }

    internal void EndModuleLoad(string key)
    {
        lock (lifecycleGate)
        {
            loadingModules.Remove(key);
        }
    }

    internal void RegisterRoot(LuauState root)
    {
        lock (lifecycleGate)
        {
            if (lifecycleState != 0)
            {
                ThrowHelper.ThrowObjectDisposedException(nameof(LuauState));
            }

            var pointer = (IntPtr)root.PointerUnsafe;
            if (pointer == IntPtr.Zero || pointer != mainPointer || states.ContainsKey(pointer))
            {
                ThrowHelper.ThrowInvalidOperationException("The Luau state is already registered");
            }

            var entry = new WeakReference<LuauState>(root);
            states.Add(pointer, entry);
            rootEntry = entry;
            root.SetCacheEntry(entry);
            globalStates[pointer] = entry;
            globalContexts[pointer] = new WeakReference<LuauVmContext>(this);
        }
    }

    internal LuauState GetOrCreateThread(LuauHostState* source, LuauHostState* thread, int stackIndex)
    {
        var pointer = (IntPtr)thread;

        lock (nativeGate)
        {
            lock (lifecycleGate)
            {
                if (lifecycleState != 0)
                {
                    ThrowHelper.ThrowObjectDisposedException(nameof(LuauState));
                }

                if (TryGetStateLocked(pointer, out var cached))
                {
                    return cached;
                }

                LuauState? root = null;
                rootEntry?.TryGetTarget(out root);
                if (root == null || root.IsDisposed)
                {
                    throw new ObjectDisposedException(nameof(LuauState));
                }

                var reference = -1;
                LuauNativeProtection.Prepare(this);
                var referenceStatus = luau_host_reference_create(source, stackIndex, &reference);
                LuauNativeProtection.ThrowIfFailed(
                    root,
                    source,
                    referenceStatus,
                    "retain a Luau thread");
                var state = new LuauState(thread, this, root, reference, isMainThread: false);
                var entry = new WeakReference<LuauState>(state);

                states[pointer] = entry;
                state.SetCacheEntry(entry);
                globalStates[pointer] = entry;
                return state;
            }
        }
    }

    internal static bool TryGetState(LuauHostState* pointer, out LuauState state)
    {
        var key = (IntPtr)pointer;
        if (globalStates.TryGetValue(key, out var entry) &&
            entry.TryGetTarget(out state!) &&
            !state.IsDisposed)
        {
            return true;
        }

        if (entry != null)
        {
            TryRemoveGlobal(key, entry);
        }

        state = null!;
        return false;
    }

    internal static bool TryGetContext(LuauHostState* pointer, out LuauVmContext context)
    {
        if (pointer != null)
        {
            var mainPointer = (IntPtr)luau_host_main_thread(pointer);
            if (globalContexts.TryGetValue(mainPointer, out var entry) &&
                entry.TryGetTarget(out context!))
            {
                return true;
            }

            if (entry != null)
            {
                globalContexts.TryRemove(mainPointer, out _);
            }
        }

        context = null!;
        return false;
    }

    internal void ReleaseChild(LuauState child, LuauHostState* pointer, int reference, WeakReference<LuauState>? entry)
    {
        lock (nativeGate)
        {
            lock (lifecycleGate)
            {
                RemoveStateLocked((IntPtr)pointer, entry);
                sandboxedThreads.Remove((IntPtr)pointer);

                if (lifecycleState == 0 && mainPointer != IntPtr.Zero && reference >= 0)
                {
                    if (activeOperation == null)
                    {
                        luau_host_reference_release((LuauHostState*)mainPointer, reference);
                        Interlocked.Increment(ref releasedReferenceCount);
                    }
                    else
                    {
                        deferredReferences.Add(reference);
                    }
                }
            }
        }

        child.InvalidateNativeState();
        child.DisposeManagedResources();
    }

    internal bool TryReleaseReference(int reference)
    {
        if (reference < 0)
        {
            return false;
        }

        lock (nativeGate)
        {
            lock (lifecycleGate)
            {
                if (lifecycleState != 0 || mainPointer == IntPtr.Zero)
                {
                    return false;
                }

                if (activeOperation == null)
                {
                    luau_host_reference_release((LuauHostState*)mainPointer, reference);
                    Interlocked.Increment(ref releasedReferenceCount);
                }
                else
                {
                    deferredReferences.Add(reference);
                }

                return true;
            }
        }
    }

    internal void DisposeRoot(LuauState root)
    {
        List<LuauState> liveStates = [];
        IntPtr pointerToClose = IntPtr.Zero;

        lock (lifecycleGate)
        {
            if (lifecycleState != 0)
            {
                root.InvalidateNativeState();
                root.DisposeManagedResources();
                return;
            }

            Volatile.Write(ref lifecycleState, 1);
        }

        try
        {
            disposalCancellationSource.Cancel();
        }
        catch
        {
            // Cancellation callbacks are host code. VM teardown must continue.
        }

        lock (nativeGate)
        {
            lock (lifecycleGate)
            {
                foreach (var pair in states)
                {
                    TryRemoveGlobal(pair.Key, pair.Value);

                    if (pair.Value.TryGetTarget(out var state))
                    {
                        state.InvalidateFromRoot();
                        liveStates.Add(state);
                    }
                }

                states.Clear();
                rootEntry = null;
                foreach (var callbackId in managedCallbacks.Keys)
                {
                    managedCallbackOwners.TryRemove(callbackId, out _);
                }

                managedCallbacks.Clear();
                managedCallbackNativeReferences.Clear();
                managedCallbackWrapperOwners.Clear();
                moduleCache.Clear();
                loadingModules.Clear();
                sandboxedThreads.Clear();

                if (!liveStates.Contains(root))
                {
                    root.InvalidateFromRoot();
                    liveStates.Add(root);
                }

                deferredReferences.Clear();

                if (activeOperation == null)
                {
                    pointerToClose = mainPointer;
                    mainPointer = IntPtr.Zero;
                }
            }

            if (pointerToClose != IntPtr.Zero)
            {
                CloseNative(pointerToClose);
            }
        }

        foreach (var state in liveStates)
        {
            state.RequestLifetimeCancellation();
            state.DisposeManagedResources();
        }
    }

    void CloseNative(IntPtr pointer)
    {
        try
        {
            var closingMemoryInfo = new LuauHostMemoryInfo
            {
                struct_size = checked((uint)sizeof(LuauHostMemoryInfo)),
            };
            var preserveMemoryInfo =
                luau_host_memory_get((LuauHostState*)pointer, &closingMemoryInfo) == LuauHostStatus.Ok;
            luau_host_state_close((LuauHostState*)pointer);
            if (preserveMemoryInfo)
            {
                // lua_close releases every VM allocation. Preserve the peak,
                // configured limit, and final diagnostics after the native
                // allocator context itself has been destroyed.
                closingMemoryInfo.current_bytes = 0;
                finalMemoryInfo = closingMemoryInfo;
                hasFinalMemoryInfo = true;
            }
            Interlocked.Increment(ref closeCount);
        }
        finally
        {
            globalContexts.TryRemove(pointer, out _);
            disposalCancellationSource.Dispose();
            Volatile.Write(ref lifecycleState, 2);
        }
    }

    bool TryGetStateLocked(IntPtr pointer, out LuauState state)
    {
        if (states.TryGetValue(pointer, out var entry))
        {
            if (entry.TryGetTarget(out state!) && !state.IsDisposed)
            {
                return true;
            }

            RemoveStateLocked(pointer, entry);
        }

        state = null!;
        return false;
    }

    void RemoveStateLocked(IntPtr pointer, WeakReference<LuauState>? expectedEntry)
    {
        if (states.TryGetValue(pointer, out var currentEntry) &&
            (expectedEntry == null || ReferenceEquals(currentEntry, expectedEntry)))
        {
            states.Remove(pointer);
            TryRemoveGlobal(pointer, currentEntry);
        }
    }

    static void TryRemoveGlobal(IntPtr pointer, WeakReference<LuauState> expectedEntry)
    {
        if (globalStates.TryGetValue(pointer, out var currentEntry) && ReferenceEquals(currentEntry, expectedEntry))
        {
            globalStates.TryRemove(pointer, out _);
        }
    }

    static int NextManagedCallbackId()
    {
        var id = Interlocked.Increment(ref nextGlobalManagedCallbackId);
        if (id == 0)
        {
            id = Interlocked.Increment(ref nextGlobalManagedCallbackId);
        }

        if (id < 0)
        {
            throw new InvalidOperationException("The process exhausted managed Luau callback identifiers.");
        }

        return id;
    }

    bool RemoveManagedCallbackIfUnownedLocked(int id)
    {
        if (managedCallbackWrapperOwners.Contains(id) ||
            managedCallbackNativeReferences.ContainsKey(id))
        {
            return false;
        }

        managedCallbacks.Remove(id);
        return true;
    }

    [AOT.MonoPInvokeCallback(typeof(LuauHostInterruptPoll))]
    static int Interrupt(LuauHostState* state, LuauHostInterruptKind kind)
    {
        try
        {
            if (kind != LuauHostInterruptKind.Execution || !TryGetContext(state, out var context))
            {
                return 0;
            }

            var operation = context.GetActiveOperation();
            if (operation == null)
            {
                return 0;
            }

            operation.PollInterrupt();
            return operation.YieldReason != ScriptYieldReason.None ? 1 : 0;
        }
        catch
        {
            // Interrupt callbacks must never throw across the native boundary.
            // A later safepoint will retry cancellation or budget checks.
            return 0;
        }
    }
}

internal readonly ref struct LuauNativeAccess
{
    readonly object gate;

    internal LuauNativeAccess(object gate)
    {
        this.gate = gate;
    }

    public void Dispose()
    {
        Monitor.Exit(gate);
    }
}
