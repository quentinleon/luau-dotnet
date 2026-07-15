using System.Runtime.InteropServices;
using Luau.Native;
using static Luau.Native.NativeMethods;

namespace Luau.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NativeInterruptAbiCollection
{
    public const string Name = "Native interrupt ABI";
}

[Collection(NativeInterruptAbiCollection.Name)]
public sealed unsafe class ProtectedNativeAbiV2Tests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InterruptPoll(lua_State* state, int gc);

    private static readonly InterruptPoll StopPoll = PollAndStop;
    private static readonly InterruptPoll ContinuePoll = PollAndContinue;
    private static readonly InterruptPoll NonYieldableStopPoll = PollAndStopWhenNonYieldable;
    private static int pollCount;

    [Fact]
    public void ProtectedNextPreservesNativeIterationStackSemantics()
    {
        var state = luaL_newstate();
        Assert.NotEqual(IntPtr.Zero, (IntPtr)state);

        try
        {
            Assert.Equal((int)lua_Status.LUA_OK, luau_ffi_protected_createtable(state, 1, 0));
            Assert.Equal((int)lua_Status.LUA_OK, luau_ffi_protected_pushinteger(state, 42));
            Assert.Equal((int)lua_Status.LUA_OK, luau_ffi_protected_rawseti(state, -2, 1));
            Assert.Equal((int)lua_Status.LUA_OK, luau_ffi_protected_pushnil(state));

            var hasNext = -1;
            Assert.Equal(
                (int)lua_Status.LUA_OK,
                luau_ffi_protected_next(state, -2, &hasNext));
            Assert.Equal(1, hasNext);
            Assert.Equal(3, lua_gettop(state));
            Assert.Equal(1, lua_tointeger(state, -2));
            Assert.Equal(42, lua_tointeger(state, -1));

            lua_pop(state, 1);
            Assert.Equal(
                (int)lua_Status.LUA_OK,
                luau_ffi_protected_next(state, -2, &hasNext));
            Assert.Equal(0, hasNext);
            Assert.Equal(1, lua_gettop(state));
        }
        finally
        {
            lua_close(state);
        }
    }

    [Fact]
    public void InterruptTrampolineYieldsAndFinalUninstallAllowsDifferentPoll()
    {
        var source = "while true do end"u8.ToArray();
        byte* bytecode = null;
        nuint bytecodeSize = 0;

        fixed (byte* sourcePointer = source)
        {
            Assert.Equal(
                (int)LUAU_PROTECTED_COMPILE_OK,
                luau_ffi_protected_compile(
                    sourcePointer,
                    (nuint)source.Length,
                    null,
                    &bytecode,
                    &bytecodeSize));
        }

        try
        {
            var root = luaL_newstate();
            Assert.NotEqual(IntPtr.Zero, (IntPtr)root);

            try
            {
                lua_State* thread = null;
                Assert.Equal(
                    (int)lua_Status.LUA_OK,
                    luau_ffi_protected_newthread(root, &thread));
                Assert.NotEqual(IntPtr.Zero, (IntPtr)thread);

                var loadResult = -1;
                fixed (byte* chunkName = "@native-interrupt-v2\0"u8)
                {
                    Assert.Equal(
                        (int)lua_Status.LUA_OK,
                        luau_ffi_protected_load(
                            thread,
                            chunkName,
                            bytecode,
                            bytecodeSize,
                            0,
                            &loadResult));
                }

                Assert.Equal((int)lua_Status.LUA_OK, loadResult);

                Volatile.Write(ref pollCount, 0);
                var stopPointer = Marshal.GetFunctionPointerForDelegate(StopPoll);
                var continuePointer = Marshal.GetFunctionPointerForDelegate(ContinuePoll);

                Assert.Equal(1, luau_ffi_protected_install_interrupt(root, stopPointer.ToPointer()));
                try
                {
                    Assert.Equal((int)lua_Status.LUA_YIELD, lua_resume(thread, null, 0));
                }
                finally
                {
                    luau_ffi_protected_uninstall_interrupt(root);
                }

                Assert.True(Volatile.Read(ref pollCount) > 0);
                Assert.Equal((int)lua_Status.LUA_OK, luau_ffi_protected_resetthread(thread));
                Assert.Equal(1, lua_isthreadreset(thread));

                // Installing twice on the same shared callback table must not
                // double-count. One uninstall must permit a different static
                // poll pointer, as required after a Unity domain reload.
                Assert.Equal(1, luau_ffi_protected_install_interrupt(root, continuePointer.ToPointer()));
                Assert.Equal(1, luau_ffi_protected_install_interrupt(root, continuePointer.ToPointer()));
                luau_ffi_protected_uninstall_interrupt(root);

                Assert.Equal(1, luau_ffi_protected_install_interrupt(root, stopPointer.ToPointer()));
                luau_ffi_protected_uninstall_interrupt(root);
            }
            finally
            {
                lua_close(root);
            }
        }
        finally
        {
            free(bytecode);
        }
    }

    [Fact]
    public void InterruptTrampolineHardUnwindsAfterNonYieldableManagedPollReturns()
    {
        var source = """
            local haystack = string.rep("x", 100)
            local pattern = string.rep("x?", 100) .. string.rep("x", 100)
            return string.find(haystack, pattern)
            """u8.ToArray();
        byte* bytecode = null;
        nuint bytecodeSize = 0;

        fixed (byte* sourcePointer = source)
        {
            Assert.Equal(
                (int)LUAU_PROTECTED_COMPILE_OK,
                luau_ffi_protected_compile(
                    sourcePointer,
                    (nuint)source.Length,
                    null,
                    &bytecode,
                    &bytecodeSize));
        }

        try
        {
            var root = luaL_newstate();
            Assert.NotEqual(IntPtr.Zero, (IntPtr)root);

            try
            {
                Assert.Equal((int)lua_Status.LUA_OK, luau_ffi_protected_openlibs(root));
                lua_settop(root, 0);

                lua_State* thread = null;
                Assert.Equal(
                    (int)lua_Status.LUA_OK,
                    luau_ffi_protected_newthread(root, &thread));

                var loadResult = -1;
                fixed (byte* chunkName = "@native-nonyieldable-interrupt-v2\0"u8)
                {
                    Assert.Equal(
                        (int)lua_Status.LUA_OK,
                        luau_ffi_protected_load(
                            thread,
                            chunkName,
                            bytecode,
                            bytecodeSize,
                            0,
                            &loadResult));
                }

                Assert.Equal((int)lua_Status.LUA_OK, loadResult);

                Volatile.Write(ref pollCount, 0);
                var pollPointer = Marshal.GetFunctionPointerForDelegate(NonYieldableStopPoll);
                Assert.Equal(1, luau_ffi_protected_install_interrupt(root, pollPointer.ToPointer()));
                try
                {
                    // LUA_ERRMEM is the allocation-free internal hard-stop
                    // sentinel. The managed layer gives its recorded watchdog
                    // reason priority over this native status.
                    Assert.Equal((int)lua_Status.LUA_ERRMEM, lua_resume(thread, null, 0));
                }
                finally
                {
                    luau_ffi_protected_uninstall_interrupt(root);
                }

                Assert.True(Volatile.Read(ref pollCount) > 0);
                Assert.Equal((int)lua_Status.LUA_OK, luau_ffi_protected_resetthread(thread));
            }
            finally
            {
                lua_close(root);
            }
        }
        finally
        {
            free(bytecode);
        }
    }

    private static int PollAndStop(lua_State* state, int gc)
    {
        if (gc >= 0)
        {
            return 0;
        }

        Interlocked.Increment(ref pollCount);
        return 1;
    }

    private static int PollAndContinue(lua_State* state, int gc)
    {
        return 0;
    }

    private static int PollAndStopWhenNonYieldable(lua_State* state, int gc)
    {
        if (gc >= 0 || lua_isyieldable(state) != 0)
        {
            return 0;
        }

        Interlocked.Increment(ref pollCount);
        return 1;
    }
}
