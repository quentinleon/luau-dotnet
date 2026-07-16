using System.Runtime.InteropServices;
using Luau.Native;
using static Luau.Native.NativeMethods;

namespace Luau;

internal enum LuauAllocatorFailure
{
    None,
    QuotaExceeded,
    SystemOutOfMemory,
}

/// <summary>
/// Tracks allocations made through a Luau <see cref="lua_Alloc"/> callback.
/// The owner must dispose this object only after <c>lua_close</c> returns, or
/// immediately after a failed <c>lua_newstate</c> call.
/// </summary>
internal sealed unsafe class LuauTrackedAllocator : IDisposable
{
    static readonly lua_Alloc callback = Allocate;

    readonly nuint? limitBytes;

    IntPtr userData;
    nuint currentBytes;
    nuint peakBytes;
    nuint lastAttemptedBytes;
    int lastFailure;
    int failNextGrowthWithQuota;

    internal LuauTrackedAllocator(nuint? limitBytes = null)
    {
        this.limitBytes = limitBytes;

        var handle = GCHandle.Alloc(this, GCHandleType.Normal);
        userData = GCHandle.ToIntPtr(handle);
    }

    internal static lua_Alloc Callback => callback;

    internal void* UserData => userData.ToPointer();

    internal nuint? LimitBytes => limitBytes;

    internal nuint CurrentBytes => currentBytes;

    internal nuint PeakBytes => peakBytes;

    internal LuauAllocatorFailure LastFailure => (LuauAllocatorFailure)Volatile.Read(ref lastFailure);

    internal nuint LastAttemptedBytes => lastAttemptedBytes;

    internal bool IsDisposed => userData == IntPtr.Zero;

    internal void ResetLastFailure()
    {
        lastAttemptedBytes = 0;
        Volatile.Write(ref lastFailure, (int)LuauAllocatorFailure.None);
    }

    /// <summary>
    /// Arms a one-shot quota failure for the next allocation that grows the
    /// allocator's logical byte count. This internal fault-injection seam is
    /// used to verify protected allocation failures without depending on a
    /// particular Luau revision's allocation sizes.
    /// </summary>
    internal void ArmQuotaFailureOnNextGrowth()
    {
        if (limitBytes is not { } limit || limit == NativeUIntMaxValue)
        {
            throw new InvalidOperationException(
                "A representable quota limit is required to inject a quota failure.");
        }

        Volatile.Write(ref failNextGrowthWithQuota, 1);
    }

    /// <summary>
    /// Releases userdata after <c>lua_newstate</c> returned null. This is
    /// intentionally the same idempotent release as normal post-close disposal.
    /// </summary>
    internal void ReleaseAfterFailedStateCreation()
    {
        Dispose();
    }

    [AOT.MonoPInvokeCallback(typeof(lua_Alloc))]
    static void* Allocate(void* userData, void* block, nuint oldSize, nuint newSize)
    {
        if (userData == null)
        {
            return null;
        }

        LuauTrackedAllocator? allocator = null;

        try
        {
            var handle = GCHandle.FromIntPtr((IntPtr)userData);
            allocator = handle.Target as LuauTrackedAllocator;

            if (allocator == null || allocator.IsDisposed)
            {
                return null;
            }

            return allocator.Reallocate(block, oldSize, newSize);
        }
        catch
        {
            allocator?.SetFailure(LuauAllocatorFailure.SystemOutOfMemory);
            return null;
        }
    }

    void* Reallocate(void* block, nuint oldSize, nuint newSize)
    {
        if (newSize == 0)
        {
            free(block);
            currentBytes = SubtractSaturating(currentBytes, oldSize);
            return null;
        }

        var retainedBytes = SubtractSaturating(currentBytes, oldSize);
        if (newSize > NativeUIntMaxValue - retainedBytes)
        {
            SetFailure(
                limitBytes.HasValue ? LuauAllocatorFailure.QuotaExceeded : LuauAllocatorFailure.SystemOutOfMemory,
                limitBytes is { } overflowLimit && overflowLimit < NativeUIntMaxValue
                    ? overflowLimit + 1
                    : NativeUIntMaxValue);
            return null;
        }

        var requestedBytes = retainedBytes + newSize;
        var isGrowth = newSize > oldSize;

        if (isGrowth &&
            Volatile.Read(ref failNextGrowthWithQuota) != 0 &&
            Interlocked.Exchange(ref failNextGrowthWithQuota, 0) != 0)
        {
            var injectedLimit = limitBytes!.Value;
            var attemptedBytes = requestedBytes > injectedLimit
                ? requestedBytes
                : injectedLimit + 1;
            SetFailure(LuauAllocatorFailure.QuotaExceeded, attemptedBytes);
            return null;
        }

        if (isGrowth && limitBytes is { } limit && requestedBytes > limit)
        {
            SetFailure(LuauAllocatorFailure.QuotaExceeded, requestedBytes);
            return null;
        }

        if (block != null && newSize == oldSize)
        {
            return block;
        }

        var result = realloc(block, newSize);
        if (result == null)
        {
            // Luau requires shrinking reallocations not to fail. Keeping the
            // original allocation is valid; future calls use the new logical size.
            if (block != null && newSize < oldSize)
            {
                currentBytes = requestedBytes;
                return block;
            }

            SetFailure(LuauAllocatorFailure.SystemOutOfMemory, requestedBytes);
            return null;
        }

        currentBytes = requestedBytes;
        if (requestedBytes > peakBytes)
        {
            peakBytes = requestedBytes;
        }

        return result;
    }

    void SetFailure(LuauAllocatorFailure failure, nuint attemptedBytes = 0)
    {
        lastAttemptedBytes = attemptedBytes;
        Volatile.Write(ref lastFailure, (int)failure);
    }

    static nuint SubtractSaturating(nuint value, nuint amount)
    {
        return amount <= value ? value - amount : 0;
    }

    static nuint NativeUIntMaxValue => ~((nuint)0);

    internal static long ToDiagnosticByteCount(nuint value)
    {
        return IntPtr.Size == sizeof(long) && (ulong)value > long.MaxValue
            ? long.MaxValue
            : (long)value;
    }

    public void Dispose()
    {
        var value = Interlocked.Exchange(ref userData, IntPtr.Zero);
        if (value == IntPtr.Zero)
        {
            return;
        }

        var handle = GCHandle.FromIntPtr(value);
        if (handle.IsAllocated)
        {
            handle.Free();
        }
    }
}
