using System.Runtime.InteropServices;
using Luau.Native;
using static Luau.Native.NativeMethods;

namespace Luau.Tests;

public unsafe class NativeMethodTests
{
    [Fact]
    public void CreateAndCloseState()
    {
        var l = luaL_newstate();
        Assert.NotEqual(IntPtr.Zero, (IntPtr)l);
        lua_close(l);
    }

    [Fact]
    public void PushAndPopNumber()
    {
        var l = luaL_newstate();
        Assert.NotEqual(IntPtr.Zero, (IntPtr)l);
        try
        {
            Assert.Equal((int)lua_Status.LUA_OK, luau_ffi_protected_pushnumber(l, 42.5));
            var v = lua_tonumber(l, -1);
            Assert.Equal(42.5, v);
            lua_pop(l, 1);
        }
        finally
        {
            lua_close(l);
        }
    }

}
