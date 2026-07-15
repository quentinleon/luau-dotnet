using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Luau.Native;
using static Luau.Native.NativeMethods;

namespace Luau;

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate int LuauInterrupt(lua_State* state, int gcState);

internal sealed unsafe class LuauVmContext
{
    static readonly ConcurrentDictionary<IntPtr, WeakReference<LuauState>> globalStates = new();
    static readonly ConcurrentDictionary<IntPtr, WeakReference<LuauVmContext>> globalContexts = new();
    static readonly ConcurrentDictionary<int, WeakReference<LuauVmContext>> managedCallbackOwners = new();
    static readonly AsyncLocal<ScriptOperation?> ambientOperation = new();
    static readonly LuauInterrupt interruptCallback = Interrupt;
    static readonly IntPtr interruptPointer = Marshal.GetFunctionPointerForDelegate(interruptCallback);
    static int nextGlobalManagedCallbackId;

    readonly object lifecycleGate = new();
    readonly object nativeGate = new();
    readonly Dictionary<IntPtr, WeakReference<LuauState>> states = [];
    readonly Dictionary<int, LuauManagedCallbackRegistration> managedCallbacks = [];
    readonly Dictionary<int, int> managedCallbackNativeReferences = [];
    readonly HashSet<int> managedCallbackWrapperOwners = [];
    readonly Dictionary<string, LuauValue> moduleCache = new(StringComparer.Ordinal);
    readonly HashSet<IntPtr> sandboxedThreads = [];
    readonly List<int> deferredReferences = [];
    readonly LuauTrackedAllocator? allocator;
    readonly CancellationTokenSource disposalCancellationSource = new();

    IntPtr mainPointer;
    WeakReference<LuauState>? rootEntry;
    ScriptOperation? activeOperation;
    int lifecycleState;
    int rootSandboxed;
    int releasedReferenceCount;
    int closeCount;

    internal LuauVmContext(lua_State* main, LuauStateOptions options, LuauTrackedAllocator? allocator)
    {
        mainPointer = (IntPtr)main;
        Options = options;
        this.allocator = allocator;
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
                var tracked = allocator;
                return tracked == null
                    ? LuauMemoryUsageSnapshot.Untracked
                    : new LuauMemoryUsageSnapshot(
                        checked((long)tracked.CurrentBytes),
                        checked((long)tracked.PeakBytes),
                        tracked.LimitBytes is { } limit ? checked((long)limit) : null);
            }
        }
    }

    internal LuauAllocatorFailure AllocatorFailure => allocator?.LastFailure ?? LuauAllocatorFailure.None;
    internal long LastAttemptedAllocationBytes => allocator == null
        ? 0
        : LuauTrackedAllocator.ToDiagnosticByteCount(allocator.LastAttemptedBytes);

    internal void ResetAllocatorFailure()
    {
        allocator?.ResetLastFailure();
    }

    internal ScriptOperation BeginOperation(
        LuauState state,
        string? chunkName,
        LuauExecutionOptions? options,
        CancellationToken cancellationToken,
        bool isAsync)
    {
        var effectiveOptions = options ?? Options.DefaultExecutionOptions;
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
                    ambientOperation.Value);

                if (luau_ffi_protected_install_interrupt(
                        (lua_State*)mainPointer,
                        interruptPointer.ToPointer()) == 0)
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
                    luau_ffi_protected_uninstall_interrupt((lua_State*)mainPointer);
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
                        lua_unref((lua_State*)mainPointer, reference);
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

    internal int RegisterManagedCallback(string? name, Func<LuauState, int> callback)
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

    internal void ReleaseManagedCallbackWrapper(int id, bool disable)
    {
        var removeGlobalOwner = false;
        lock (lifecycleGate)
        {
            managedCallbackWrapperOwners.Remove(id);
            if (disable)
            {
                managedCallbacks.Remove(id);
            }

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

    internal LuauState GetOrCreateThread(lua_State* source, lua_State* thread, int stackIndex)
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
                var referenceStatus = luau_ffi_protected_ref(source, stackIndex, &reference);
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

    internal static bool TryGetState(lua_State* pointer, out LuauState state)
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

    internal static bool TryGetContext(lua_State* pointer, out LuauVmContext context)
    {
        if (pointer != null)
        {
            var mainPointer = (IntPtr)lua_mainthread(pointer);
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

    internal void ReleaseChild(LuauState child, lua_State* pointer, int reference, WeakReference<LuauState>? entry)
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
                        lua_unref((lua_State*)mainPointer, reference);
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
                    lua_unref((lua_State*)mainPointer, reference);
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
            lua_close((lua_State*)pointer);
            Interlocked.Increment(ref closeCount);
        }
        finally
        {
            globalContexts.TryRemove(pointer, out _);
            allocator?.Dispose();
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

    [AOT.MonoPInvokeCallback(typeof(LuauInterrupt))]
    static int Interrupt(lua_State* state, int gcState)
    {
        try
        {
            if (gcState >= 0 || !TryGetContext(state, out var context))
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
