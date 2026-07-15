using System.Text;
using Luau.Native;
using static Luau.Native.NativeMethods;

namespace Luau.Tests;

public sealed unsafe class ProtectedNativeApiTests
{
    [Fact]
    public void ProtectedAbiIsPresent()
    {
        Assert.Equal(2, luau_ffi_protected_abi_version());
    }

    [Fact]
    public void ProtectedPushesGrowTheStackInsideTheNativeErrorFrame()
    {
        using var allocator = new LuauTrackedAllocator();
        var state = lua_newstate(LuauTrackedAllocator.Callback, allocator.UserData);
        Assert.NotEqual(IntPtr.Zero, (IntPtr)state);

        try
        {
            for (var index = 0; index < 512; index++)
            {
                Assert.Equal((int)lua_Status.LUA_OK, luau_ffi_protected_pushnil(state));
            }

            Assert.Equal(512, lua_gettop(state));
            lua_settop(state, 0);
        }
        finally
        {
            lua_close(state);
        }
    }

    [Fact]
    public void ProtectedAllocationReturnsMemoryStatusAndLeavesStateUsable()
    {
        using var allocator = new LuauTrackedAllocator(1_048_576);
        var state = lua_newstate(LuauTrackedAllocator.Callback, allocator.UserData);
        Assert.NotEqual(IntPtr.Zero, (IntPtr)state);

        try
        {
            var originalTop = lua_gettop(state);
            void* buffer = (void*)1;

            var status = luau_ffi_protected_newbuffer(state, 2_097_152, &buffer);

            Assert.Equal((int)lua_Status.LUA_ERRMEM, status);
            Assert.Equal(IntPtr.Zero, (IntPtr)buffer);
            Assert.Equal(LuauAllocatorFailure.QuotaExceeded, allocator.LastFailure);
            Assert.Equal(originalTop + 1, lua_gettop(state));
            Assert.Contains("memory", ReadTopString(state), StringComparison.OrdinalIgnoreCase);

            lua_settop(state, originalTop);
            allocator.ResetLastFailure();

            status = luau_ffi_protected_newbuffer(state, 16, &buffer);
            Assert.Equal((int)lua_Status.LUA_OK, status);
            Assert.NotEqual(IntPtr.Zero, (IntPtr)buffer);
            Assert.Equal(originalTop + 1, lua_gettop(state));
        }
        finally
        {
            lua_close(state);
        }
    }

    [Fact]
    public void ProtectedCompileReturnsOwnedBytecodeAndCatchesCppFailures()
    {
        byte* output = null;
        nuint outputSize = 0;

        fixed (byte* source = "return 42"u8)
        {
            var status = luau_ffi_protected_compile(source, 9, null, &output, &outputSize);
            Assert.Equal((int)LUAU_PROTECTED_COMPILE_OK, status);
        }

        try
        {
            Assert.NotEqual(IntPtr.Zero, (IntPtr)output);
            Assert.True(outputSize > 0);
        }
        finally
        {
            free(output);
        }

        byte marker = 0;
        output = (byte*)1;
        outputSize = 123;
        var failureStatus = luau_ffi_protected_compile(
            &marker,
            nuint.MaxValue,
            null,
            &output,
            &outputSize);

        Assert.True(
            failureStatus is (int)LUAU_PROTECTED_COMPILE_OUT_OF_MEMORY or (int)LUAU_PROTECTED_COMPILE_ERROR,
            $"Unexpected protected compiler status {failureStatus}.");
        Assert.Equal(IntPtr.Zero, (IntPtr)output);
        Assert.Equal((nuint)0, outputSize);
    }

    [Fact]
    public void ProtectedLoadContainsQuotaFailureAndRestoresItsStackBoundary()
    {
        const int payloadLength = 2_097_152;
        var source = new byte["return \""u8.Length + payloadLength + 1];
        "return \""u8.CopyTo(source);
        source.AsSpan("return \""u8.Length, payloadLength).Fill((byte)'x');
        source[^1] = (byte)'"';

        byte* bytecode = null;
        nuint bytecodeSize = 0;
        fixed (byte* sourcePointer = source)
        {
            var compileStatus = luau_ffi_protected_compile(
                sourcePointer,
                (nuint)source.Length,
                null,
                &bytecode,
                &bytecodeSize);
            Assert.Equal((int)LUAU_PROTECTED_COMPILE_OK, compileStatus);
        }

        try
        {
            using var allocator = new LuauTrackedAllocator(1_048_576);
            var state = lua_newstate(LuauTrackedAllocator.Callback, allocator.UserData);
            Assert.NotEqual(IntPtr.Zero, (IntPtr)state);

            try
            {
                var originalTop = lua_gettop(state);
                var loadResult = -1;
                fixed (byte* chunkName = "@protected-load-oom\0"u8)
                {
                    var status = luau_ffi_protected_load(
                        state,
                        chunkName,
                        bytecode,
                        bytecodeSize,
                        0,
                        &loadResult);

                    Assert.Equal((int)lua_Status.LUA_ERRMEM, status);
                }

                Assert.Equal(-1, loadResult);
                Assert.Equal(LuauAllocatorFailure.QuotaExceeded, allocator.LastFailure);
                Assert.Equal(originalTop + 1, lua_gettop(state));
                Assert.Contains("memory", ReadTopString(state), StringComparison.OrdinalIgnoreCase);

                lua_settop(state, originalTop);
                allocator.ResetLastFailure();

                void* smallBuffer = null;
                Assert.Equal(
                    (int)lua_Status.LUA_OK,
                    luau_ffi_protected_newbuffer(state, 32, &smallBuffer));
                Assert.NotEqual(IntPtr.Zero, (IntPtr)smallBuffer);
            }
            finally
            {
                lua_close(state);
            }
        }
        finally
        {
            free(bytecode);
        }
    }

    [Fact]
    public void HighLevelPushContainsQuotaFailureAndLeavesStateUsable()
    {
        using var state = LuauState.Create(new LuauStateOptions
        {
            MemoryLimitBytes = 1_048_576,
        });
        var originalTop = state.GetTop();

        Assert.Throws<LuauMemoryLimitException>(
            () => state.PushString(new string('x', 2_097_152)));

        Assert.Equal(originalTop, state.GetTop());
        state.PushString("still usable");
        Assert.Equal("still usable", state.Pop().Read<string>());
    }

    static string ReadTopString(lua_State* state)
    {
        nuint length = 0;
        var pointer = lua_tolstring(state, -1, &length);
        Assert.NotEqual(IntPtr.Zero, (IntPtr)pointer);
        Assert.True(length <= int.MaxValue);
        return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(pointer, (int)length));
    }
}
