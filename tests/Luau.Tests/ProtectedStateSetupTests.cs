#pragma warning disable CS0618 // Deliberate regression coverage for transitional unsupported APIs.

using Luau.Native;
using static Luau.Native.NativeMethods;

namespace Luau.Tests;

public sealed unsafe class ProtectedStateSetupTests
{
    [Theory]
    [InlineData(nameof(LuauState.OpenLibraries))]
    [InlineData(nameof(LuauState.OpenBaseLibrary))]
    [InlineData(nameof(LuauState.OpenMathLibrary))]
    [InlineData(nameof(LuauState.OpenTableLibrary))]
    [InlineData(nameof(LuauState.OpenStringLibrary))]
    [InlineData(nameof(LuauState.OpenCoroutineLibrary))]
    [InlineData(nameof(LuauState.OpenBit32Library))]
    [InlineData(nameof(LuauState.OpenUtf8Library))]
    [InlineData(nameof(LuauState.OpenOSLibrary))]
    [InlineData(nameof(LuauState.OpenDebugLibrary))]
    [InlineData(nameof(LuauState.OpenBufferLibrary))]
    [InlineData(nameof(LuauState.OpenVectorLibrary))]
    public void StandardLibraryRegistrationIsStackNeutral(string openMethod)
    {
        using var state = LuauState.Create();
        state.PushString("stack-sentinel");
        var originalTop = state.GetTop();

        OpenStandardLibrary(state, openMethod);

        Assert.Equal(originalTop, state.GetTop());
        Assert.Equal("stack-sentinel", state.ToString(-1));
    }

    [Fact]
    public void OversizedTableCreationReportsQuotaAndRestoresTheState()
    {
        const long memoryLimit = 1_048_576;
        using var state = LuauState.Create(new LuauStateOptions
        {
            MemoryLimitBytes = memoryLimit,
            BytecodePolicy = LuauBytecodePolicy.Reject,
        });
        var originalTop = state.GetTop();

        var exception = Assert.Throws<LuauMemoryLimitException>(
            () => state.CreateTable(100_000, 0));

        Assert.Equal(memoryLimit, exception.LimitBytes);
        Assert.Equal(originalTop, state.GetTop());
        Assert.InRange(state.MemoryUsage.CurrentBytes, 1, memoryLimit);

        using var smallTable = state.CreateTable(4, 4);
        var results = state.DoString("return 6 * 7", "@protected-table-recovery");

        Assert.Single(results);
        Assert.Equal(42, results[0].Read<int>());
        Assert.Equal(originalTop, state.GetTop());
    }

    [Fact]
    public void LibraryQuotaFailureRestoresTheStackAndCanBeRetriedAfterCollection()
    {
        const long memoryLimit = 1_048_576;
        using var state = LuauState.Create(new LuauStateOptions
        {
            MemoryLimitBytes = memoryLimit,
            BytecodePolicy = LuauBytecodePolicy.Reject,
        });

        var fillerSize = checked((int)(memoryLimit - state.MemoryUsage.CurrentBytes - 32_768));
        using var filler = state.CreateBuffer(fillerSize);
        var originalTop = state.GetTop();

        Assert.Throws<LuauMemoryLimitException>(state.OpenLibraries);
        Assert.Equal(originalTop, state.GetTop());

        filler.Dispose();
        Collect(state);

        state.OpenLibraries();
        var results = state.DoString(
            "return math.floor(41.9) + 1, type(string.byte) == 'function'",
            "@protected-library-recovery");

        Assert.Equal(2, results.Length);
        Assert.Equal(42, results[0].Read<int>());
        Assert.True(results[1].Read<bool>());
        Assert.Equal(originalTop, state.GetTop());
    }

    [Fact]
    public void SandboxingAndMetatableSetupRemainStackBalanced()
    {
        using var root = LuauState.Create(new LuauStateOptions
        {
            MemoryLimitBytes = 1_048_576,
            BytecodePolicy = LuauBytecodePolicy.Reject,
        });
        root.OpenBaseLibrary();
        var originalTop = root.GetTop();

        using var value = root.CreateTable();
        Assert.Null(root.GetMetatable(value));
        Assert.Equal(originalTop, root.GetTop());

        using var metatable = root.CreateTable();
        root.SetMetatable(value, metatable);
        using var observedMetatable = root.GetMetatable(value);

        Assert.NotNull(observedMetatable);
        Assert.Equal(originalTop, root.GetTop());

        root.SandboxRoot();
        using var child = root.CreateSandboxedThread();
        var results = child.DoString("local value = 40; return value + 2");

        Assert.Single(results);
        Assert.Equal(42, results[0].Read<int>());
        Assert.Equal(originalTop, root.GetTop());
        Assert.Equal(0, child.GetTop());
    }

    [Fact]
    public void ProtectedCheckStackReportsAnImpossibleGrowthWithoutDamagingTheState()
    {
        using var allocator = new LuauTrackedAllocator();
        var pointer = lua_newstate(LuauTrackedAllocator.Callback, allocator.UserData);
        Assert.NotEqual(IntPtr.Zero, (IntPtr)pointer);

        try
        {
            var result = -1;
            var status = luau_ffi_protected_checkstack(pointer, int.MaxValue, &result);

            Assert.Equal((int)lua_Status.LUA_OK, status);
            Assert.Equal(0, result);
            Assert.Equal(0, lua_gettop(pointer));

            status = luau_ffi_protected_checkstack(pointer, 64, &result);
            Assert.Equal((int)lua_Status.LUA_OK, status);
            Assert.Equal(1, result);
            Assert.Equal(0, lua_gettop(pointer));
        }
        finally
        {
            lua_close(pointer);
        }
    }

    static void Collect(LuauState state)
    {
        using var access = state.EnterNativeAccess();
        var result = 0;
        LuauNativeProtection.Prepare(state.Context);
        var status = luau_ffi_protected_gc(
            state.PointerUnsafe,
            operation: 2, // LUA_GCCOLLECT
            data: 0,
            &result);
        LuauNativeProtection.ThrowIfFailed(
            state,
            state.PointerUnsafe,
            status,
            "collect unreachable test allocations");
    }

    static void OpenStandardLibrary(LuauState state, string openMethod)
    {
        switch (openMethod)
        {
            case nameof(LuauState.OpenLibraries):
                state.OpenLibraries();
                break;
            case nameof(LuauState.OpenBaseLibrary):
                state.OpenBaseLibrary();
                break;
            case nameof(LuauState.OpenMathLibrary):
                state.OpenMathLibrary();
                break;
            case nameof(LuauState.OpenTableLibrary):
                state.OpenTableLibrary();
                break;
            case nameof(LuauState.OpenStringLibrary):
                state.OpenStringLibrary();
                break;
            case nameof(LuauState.OpenCoroutineLibrary):
                state.OpenCoroutineLibrary();
                break;
            case nameof(LuauState.OpenBit32Library):
                state.OpenBit32Library();
                break;
            case nameof(LuauState.OpenUtf8Library):
                state.OpenUtf8Library();
                break;
            case nameof(LuauState.OpenOSLibrary):
                state.OpenOSLibrary();
                break;
            case nameof(LuauState.OpenDebugLibrary):
                state.OpenDebugLibrary();
                break;
            case nameof(LuauState.OpenBufferLibrary):
                state.OpenBufferLibrary();
                break;
            case nameof(LuauState.OpenVectorLibrary):
                state.OpenVectorLibrary();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(openMethod));
        }
    }
}
