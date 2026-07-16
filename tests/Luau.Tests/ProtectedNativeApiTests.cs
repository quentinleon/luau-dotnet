using System.Text;
using Luau.Native;
using static Luau.Native.NativeMethods;

namespace Luau.Tests;

public sealed unsafe class ProtectedNativeApiTests
{

    [Fact]
    public void ProtectedPushesGrowTheStackInsideTheNativeErrorFrame()
    {
        var state = luaL_newstate();
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
    public void HighLevelPushContainsQuotaFailureAndLeavesStateUsable()
    {
        using var state = LuauState.Create(new LuauStateOptions
        {
            MemoryLimitBytes = 1_048_576,
        });
        var originalTop = state.GetTop();
        state.Context.ArmQuotaFailureOnNextGrowth();

        var exception = Assert.Throws<LuauMemoryLimitException>(
            () => state.PushString(new string('x', 1_025)));

        Assert.Equal(1_048_576, exception.LimitBytes);
        Assert.True(exception.Usage.IsTracked);
        Assert.True(exception.AttemptedBytes > exception.LimitBytes);
        Assert.Equal(originalTop, state.GetTop());
        state.PushString("still usable");
        Assert.Equal("still usable", state.Pop().Read<string>());
    }

}
