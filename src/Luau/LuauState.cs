using System.Runtime.CompilerServices;
using Luau.Native;
using static Luau.Native.NativeMethods;

namespace Luau;

public unsafe partial class LuauState : IDisposable, ILuauReference
{
    lua_State* l;
    readonly LuauVmContext context;
    LuauState? root;
    readonly bool isMainThread;
    int reference;
    int disposeState;
    WeakReference<LuauState>? cacheEntry;
    readonly DisposableBag disposables = new();
    readonly CancellationTokenSource lifetimeCancellationSource = new();
    int managedResourcesDisposeState;

    public bool IsDisposed => Volatile.Read(ref disposeState) != 0 || context.IsDisposed;
    public bool IsMainThread => isMainThread;
    public LuauStateOptions Options => context.Options;
    public LuauMemoryUsageSnapshot MemoryUsage => context.MemoryUsage;

    LuauReferenceAccess ILuauReference.AcquireReference()
    {
        var access = EnterNativeAccess();
        return new LuauReferenceAccess(GetRoot(), reference, lifetimeGate: null, access);
    }

    internal LuauState? From => isMainThread ? null : root;
    internal LuauVmContext Context => context;
    internal lua_State* PointerUnsafe => l;
    internal int RegisteredDisposableCount => disposables.Count;
    internal CancellationToken LifetimeToken => lifetimeCancellationSource.Token;

    internal void RegisterDisposable(IDisposable disposable)
    {
        disposables.Add(disposable);
    }

    internal void UnregisterDisposable(IDisposable disposable)
    {
        disposables.Remove(disposable);
    }

    public static LuauState Create()
    {
        return Create(LuauStateOptions.Default);
    }

    public static LuauState Create(LuauStateOptions options)
    {
        return Create(options, LuauNativeProtection.AbiVerifier);
    }

    internal static LuauState Create(LuauStateOptions options, LuauNativeAbiVerifier abiVerifier)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }
        if (abiVerifier == null)
        {
            throw new ArgumentNullException(nameof(abiVerifier));
        }
        options.Validate();
        abiVerifier.EnsureAvailable();

        LuauTrackedAllocator? allocator = null;
        lua_State* statePointer;

        if (options.MemoryLimitBytes is { } memoryLimitBytes)
        {
            if ((ulong)memoryLimitBytes >= (ulong)(~(nuint)0))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    memoryLimitBytes,
                    "The memory limit cannot be represented by the native allocator on this platform.");
            }

            allocator = new LuauTrackedAllocator(checked((nuint)memoryLimitBytes));
            statePointer = lua_newstate(LuauTrackedAllocator.Callback, allocator.UserData);
        }
        else
        {
            statePointer = luaL_newstate();
        }

        if (statePointer == null)
        {
            try
            {
                if (allocator?.LastFailure == LuauAllocatorFailure.QuotaExceeded)
                {
                    var limit = options.MemoryLimitBytes!.Value;
                    var usage = new LuauMemoryUsageSnapshot(
                        checked((long)allocator.CurrentBytes),
                        checked((long)allocator.PeakBytes),
                        limit);
                    var attempted = Math.Max(limit + 1, LuauTrackedAllocator.ToDiagnosticByteCount(allocator.LastAttemptedBytes));
                    throw new LuauMemoryLimitException(null, usage, attempted);
                }

                throw new OutOfMemoryException("Unable to create a Luau state.");
            }
            finally
            {
                allocator?.ReleaseAfterFailedStateCreation();
            }
        }

        return CreateStateInternal(statePointer, options, allocator);
    }

    internal static LuauState GetCachedState(lua_State* l)
    {
        if (l == null)
        {
            ThrowHelper.ThrowObjectDisposedException(nameof(LuauState));
        }

        if (LuauVmContext.TryGetState(l, out var state))
        {
            return state;
        }

        var main = lua_mainthread(l);
        if (!LuauVmContext.TryGetState(main, out var root))
        {
            ThrowHelper.ThrowInvalidOperationException("The Luau VM is not owned by a managed LuauState");
        }

        using var access = root.context.EnterNativeAccess(root);
        var originalTop = lua_gettop(l);
        try
        {
            var ignoredIsMainThread = 0;
            LuauNativeProtection.Prepare(root.context);
            var status = luau_ffi_protected_pushthread(l, &ignoredIsMainThread);
            LuauNativeProtection.ThrowIfFailed(root, l, status, "retain the current Luau thread");
            return root.context.GetOrCreateThread(l, l, -1);
        }
        finally
        {
            lua_settop(l, originalTop);
        }
    }

    internal static LuauState CreateStateInternal(lua_State* l)
    {
        return CreateStateInternal(l, LuauStateOptions.Default, allocator: null);
    }

    static LuauState CreateStateInternal(lua_State* l, LuauStateOptions options, LuauTrackedAllocator? allocator)
    {
        if (l == null)
        {
            throw new OutOfMemoryException("Unable to create a Luau state");
        }

        if (LuauVmContext.TryGetState(l, out _))
        {
            ThrowHelper.ThrowInvalidOperationException("The Luau state is already registered");
        }

        var context = new LuauVmContext(l, options, allocator);
        try
        {
            var state = new LuauState(l, context, root: null, reference: -1, isMainThread: true);
            context.RegisterRoot(state);
            return state;
        }
        catch
        {
            lua_close(l);
            allocator?.Dispose();
            throw;
        }
    }

    internal LuauState(lua_State* l, LuauVmContext context, LuauState? root, int reference, bool isMainThread)
    {
        this.l = l;
        this.context = context;
        this.root = root ?? this;
        this.reference = reference;
        this.isMainThread = isMainThread;
    }

    [Obsolete(LuauCompatibilityDiagnostics.NativePointer)]
    public lua_State* AsPointer()
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        return l;
    }

    public LuauThreadStatus GetStatus()
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        return (LuauThreadStatus)lua_status(l);
    }

    public LuauState GetMainThread()
    {
        ThrowIfDisposed();
        return GetRoot();
    }

    public override unsafe string ToString()
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        return LuauReferenceHelper.RefToString(this, reference);
    }

    public void Dispose()
    {
        DisposeCore();
        GC.SuppressFinalize(this);
    }

    void DisposeCore()
    {
        if (Interlocked.Exchange(ref disposeState, 1) != 0)
        {
            return;
        }

        RequestLifetimeCancellation();
        try
        {
            if (isMainThread)
            {
                context.DisposeRoot(this);
            }
            else
            {
                context.ReleaseChild(this, l, Interlocked.Exchange(ref reference, -1), cacheEntry);
            }
        }
        catch
        {
            InvalidateNativeState();
            DisposeManagedResources();
        }
    }

    ~LuauState()
    {
        try
        {
            DisposeCore();
        }
        catch
        {
            // Finalizers must never surface managed or native cleanup failures.
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ThrowIfDisposed()
    {
        if (IsDisposed) ThrowHelper.ThrowObjectDisposedException(nameof(LuauState));
        context.ThrowIfNativeAccessDenied();
    }

    internal LuauNativeAccess EnterNativeAccess()
    {
        return context.EnterNativeAccess(this);
    }

    internal void TryReleaseReference(int reference)
    {
        context.TryReleaseReference(reference);
    }

    internal void SetCacheEntry(WeakReference<LuauState> entry)
    {
        cacheEntry = entry;
    }

    internal void InvalidateFromRoot()
    {
        Interlocked.Exchange(ref disposeState, 1);
        Interlocked.Exchange(ref reference, -1);
        InvalidateNativeState();
        GC.SuppressFinalize(this);
    }

    internal void InvalidateNativeState()
    {
        l = null;
        cacheEntry = null;
        if (!isMainThread)
        {
            root = null;
        }
    }

    internal void DisposeManagedResources()
    {
        if (Interlocked.Exchange(ref managedResourcesDisposeState, 1) != 0)
        {
            return;
        }

        disposables.Dispose();
        lifetimeCancellationSource.Dispose();
    }

    internal void RequestLifetimeCancellation()
    {
        try
        {
            lifetimeCancellationSource.Cancel();
        }
        catch
        {
            // Host cancellation registrations cannot prevent native teardown.
        }
    }

    LuauState GetRoot()
    {
        ThrowIfDisposed();
        return root!;
    }
}
