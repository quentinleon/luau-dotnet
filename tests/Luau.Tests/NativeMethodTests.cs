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
        lua_pushnumber(l, 42.5);
        var v = lua_tonumber(l, -1);
        Assert.Equal(42.5, v);
        lua_pop(l, 1);
        lua_close(l);
    }

    [Fact]
    public void CreateBufferAndPushResult()
    {
        var l = luaL_newstate();
        luaL_Strbuf b;
        luaL_buffinit(l, &b);

        lua_pushinteger(l, 12345);
        luaL_addvalue(&b);

        fixed (byte* s = "hello"u8)
        {
            luaL_addlstring(&b, s, 5);
        }
        luaL_pushresult(&b);

        var ret = lua_tostring(l, -1);
        Assert.Equal("12345hello", Marshal.PtrToStringAnsi((IntPtr)ret));
    }
}
