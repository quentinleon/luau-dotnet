#pragma warning disable IDE1006
#pragma warning disable IDE0005

using System;

namespace Luau.Native
{
    // Since rust-bindgen cannot parse macros, we define the equivalent constants and functions here.

    unsafe partial class NativeMethods
    {
        public const int LUA_MULTRET = -1;
        public const int LUA_REGISTRYINDEX = (int)(-LUAI_MAXCSTACK - 2000);
        public const int LUA_ENVIRONINDEX = (int)(-LUAI_MAXCSTACK - 2001);
        public const int LUA_GLOBALSINDEX = (int)(-LUAI_MAXCSTACK - 2002);
        public const int LUA_TNONE = -1;

        public static long lua_upvalueindex(int i)
        {
            return LUA_GLOBALSINDEX - i;
        }

        public static bool lua_ispseudo(int i)
        {
            return (i) <= LUA_REGISTRYINDEX;
        }

        public static int lua_getref(lua_State* L, int n)
        {
            return lua_rawgeti(L, LUA_REGISTRYINDEX, n);
        }

        public static double lua_tonumber(lua_State* L, int i)
        {
            return lua_tonumberx(L, i, null);
        }

        public static int lua_tointeger(lua_State* L, int i)
        {
            return lua_tointegerx(L, i, null);
        }

        public static uint lua_tounsigned(lua_State* L, int i)
        {
            return lua_tounsignedx(L, i, null);
        }

        public static void lua_pop(lua_State* L, int n)
        {
            lua_settop(L, -n - 1);
        }

        public static void lua_newtable(lua_State* L)
        {
            lua_createtable(L, 0, 0);
        }

        public static void* lua_newuserdata(lua_State* L, nuint s)
        {
            return lua_newuserdatatagged(L, s, 0);
        }

        public static int lua_strlen(lua_State* L, int i)
        {
            return lua_objlen(L, i);
        }

        public static bool lua_isfunction(lua_State* L, int n)
        {
            return lua_type(L, n) == (int)lua_Type.LUA_TFUNCTION;
        }

        public static bool lua_istable(lua_State* L, int n)
        {
            return lua_type(L, n) == (int)lua_Type.LUA_TTABLE;
        }

        public static bool lua_islightuserdata(lua_State* L, int n)
        {
            return lua_type(L, n) == (int)lua_Type.LUA_TLIGHTUSERDATA;
        }

        public static bool lua_isnil(lua_State* L, int n)
        {
            return lua_type(L, n) == (int)lua_Type.LUA_TNIL;
        }

        public static bool lua_isboolean(lua_State* L, int n)
        {
            return lua_type(L, n) == (int)lua_Type.LUA_TBOOLEAN;
        }

        public static bool lua_isvector(lua_State* L, int n)
        {
            return lua_type(L, n) == (int)lua_Type.LUA_TVECTOR;
        }

        public static bool lua_isthread(lua_State* L, int n)
        {
            return lua_type(L, n) == (int)lua_Type.LUA_TTHREAD;
        }

        public static bool lua_isbuffer(lua_State* L, int n)
        {
            return lua_type(L, n) == (int)lua_Type.LUA_TBUFFER;
        }

        public static bool lua_isnone(lua_State* L, int n)
        {
            return lua_type(L, n) == LUA_TNONE;
        }

        public static bool lua_isnoneornil(lua_State* L, int n)
        {
            return lua_type(L, n) <= (int)lua_Type.LUA_TNIL;
        }

        public static void lua_pushliteral(lua_State* L, ReadOnlySpan<byte> s)
        {
            fixed (byte* ptr = s)
            {
                lua_pushlstring(L, ptr, (nuint)s.Length);
            }
        }

        public static void lua_pushcfunction(lua_State* L, lua_CFunction fn, byte* debugname)
        {
            lua_pushcclosurek(L, fn, debugname, 0, null);
        }

        public static void lua_pushcclosure(lua_State* L, lua_CFunction fn, byte* debugname, int nup)
        {
            lua_pushcclosurek(L, fn, debugname, nup, null);
        }

        public static void lua_pushlightuserdata(lua_State* L, void* p)
        {
            lua_pushlightuserdatatagged(L, p, 0);
        }

        public static void lua_setglobal(lua_State* L, byte* s)
        {
            lua_setfield(L, LUA_GLOBALSINDEX, s);
        }

        public static int lua_getglobal(lua_State* L, byte* s)
        {
            return lua_getfield(L, LUA_GLOBALSINDEX, s);
        }

        public static byte* lua_tostring(lua_State* L, int i)
        {
            return lua_tolstring(L, i, null);
        }
    }
}

#pragma warning restore IDE1006
#pragma warning restore IDE0005