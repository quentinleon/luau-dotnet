using static Luau.Native.NativeMethods;

namespace Luau;

internal unsafe static class LuauReferenceHelper
{
    public static void* GetRefPointer(LuauState state, int reference)
    {
        var l = state.AsPointer();
        lua_getref(l, reference);
        var result = lua_topointer(l, -1);
        lua_pop(l, 1);
        return result;
    }

    public static string RefToString(LuauState state, int reference)
    {
        var l = state.AsPointer();
        lua_getref(l, reference);
        luaL_tolstring(l, -1, null);
        var result = state.ToValue(-1).Read<string>();
        lua_pop(l, 2);
        return result;
    }
}