namespace Luau.Tests;

public sealed unsafe class LuauTrackedAllocatorTests
{
    [Fact]
    public void TracksAllocationReallocationShrinkAndFree()
    {
        using var allocator = new LuauTrackedAllocator();
        var callback = LuauTrackedAllocator.Callback;

        var block = callback(allocator.UserData, null, 0, 32);
        Assert.NotEqual(IntPtr.Zero, (IntPtr)block);
        Assert.Equal((nuint)32, allocator.CurrentBytes);
        Assert.Equal((nuint)32, allocator.PeakBytes);

        *(byte*)block = 0x5a;
        block = callback(allocator.UserData, block, 32, 96);
        Assert.NotEqual(IntPtr.Zero, (IntPtr)block);
        Assert.Equal(0x5a, *(byte*)block);
        Assert.Equal((nuint)96, allocator.CurrentBytes);
        Assert.Equal((nuint)96, allocator.PeakBytes);

        block = callback(allocator.UserData, block, 96, 24);
        Assert.NotEqual(IntPtr.Zero, (IntPtr)block);
        Assert.Equal(0x5a, *(byte*)block);
        Assert.Equal((nuint)24, allocator.CurrentBytes);
        Assert.Equal((nuint)96, allocator.PeakBytes);

        var released = callback(allocator.UserData, block, 24, 0);
        Assert.Equal(IntPtr.Zero, (IntPtr)released);
        Assert.Equal((nuint)0, allocator.CurrentBytes);
        Assert.Equal((nuint)96, allocator.PeakBytes);
        Assert.Equal(LuauAllocatorFailure.None, allocator.LastFailure);
    }

    [Fact]
    public void RejectsGrowthBeyondQuotaWithoutReleasingOriginalBlock()
    {
        using var allocator = new LuauTrackedAllocator(64);
        var callback = LuauTrackedAllocator.Callback;

        var block = callback(allocator.UserData, null, 0, 48);
        Assert.NotEqual(IntPtr.Zero, (IntPtr)block);
        *(byte*)block = 0x2a;

        var rejected = callback(allocator.UserData, block, 48, 80);
        Assert.Equal(IntPtr.Zero, (IntPtr)rejected);
        Assert.Equal(0x2a, *(byte*)block);
        Assert.Equal((nuint)48, allocator.CurrentBytes);
        Assert.Equal((nuint)48, allocator.PeakBytes);
        Assert.Equal(LuauAllocatorFailure.QuotaExceeded, allocator.LastFailure);

        block = callback(allocator.UserData, block, 48, 16);
        Assert.NotEqual(IntPtr.Zero, (IntPtr)block);
        Assert.Equal((nuint)16, allocator.CurrentBytes);
        Assert.Equal(LuauAllocatorFailure.QuotaExceeded, allocator.LastFailure);

        allocator.ResetLastFailure();
        Assert.Equal(LuauAllocatorFailure.None, allocator.LastFailure);

        callback(allocator.UserData, block, 16, 0);
        Assert.Equal((nuint)0, allocator.CurrentBytes);
    }

    [Fact]
    public void ClassifiesUnrepresentableUnlimitedGrowthAsSystemOutOfMemory()
    {
        using var allocator = new LuauTrackedAllocator();
        var callback = LuauTrackedAllocator.Callback;

        var block = callback(allocator.UserData, null, 0, 1);
        Assert.NotEqual(IntPtr.Zero, (IntPtr)block);

        var rejected = callback(allocator.UserData, null, 0, nuint.MaxValue);
        Assert.Equal(IntPtr.Zero, (IntPtr)rejected);
        Assert.Equal((nuint)1, allocator.CurrentBytes);
        Assert.Equal(LuauAllocatorFailure.SystemOutOfMemory, allocator.LastFailure);

        callback(allocator.UserData, block, 1, 0);
    }

    [Fact]
    public void SaturatesNativeByteCountsForManagedDiagnostics()
    {
        Assert.Equal(long.MaxValue, LuauTrackedAllocator.ToDiagnosticByteCount(nuint.MaxValue));
        Assert.Equal(123, LuauTrackedAllocator.ToDiagnosticByteCount(123));
    }

    [Fact]
    public void FailedCreationReleaseAndDisposeAreIdempotent()
    {
        var allocator = new LuauTrackedAllocator(0);
        var callback = LuauTrackedAllocator.Callback;

        allocator.ReleaseAfterFailedStateCreation();
        allocator.ReleaseAfterFailedStateCreation();
        allocator.Dispose();

        Assert.True(allocator.IsDisposed);
        Assert.Equal(IntPtr.Zero, (IntPtr)allocator.UserData);
        Assert.Equal(IntPtr.Zero, (IntPtr)callback(null, null, 0, 1));
    }
}
