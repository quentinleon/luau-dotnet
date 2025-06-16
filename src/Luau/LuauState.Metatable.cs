using static Luau.Native.NativeMethods;

namespace Luau;

unsafe partial class LuauState
{
    public LuauTable? GetMetatable(LuauValue value)
    {
        Push(value);
        lua_getmetatable(l, -1);
        var result = Pop();
        return result.IsNil ? null : result.Read<LuauTable>();
    }

    public void SetMetatable(LuauValue value, LuauTable? metatable)
    {
        Push(value);
        Push(metatable ?? LuauValue.Nil);
        lua_setmetatable(l, -2);
        lua_pop(l, 1);
    }
}