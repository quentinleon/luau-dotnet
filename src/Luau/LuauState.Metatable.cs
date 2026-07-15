using static Luau.Native.NativeMethods;

namespace Luau;

unsafe partial class LuauState
{
    public LuauTable? GetMetatable(LuauValue value)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        var originalTop = lua_gettop(l);
        try
        {
            Push(value);

            var hasMetatable = 0;
            LuauNativeProtection.Prepare(context);
            var status = luau_ffi_protected_getmetatable(l, -1, &hasMetatable);
            LuauNativeProtection.ThrowIfFailed(this, l, status, "get a value's metatable");

            return hasMetatable == 0 ? null : Pop().Read<LuauTable>();
        }
        finally
        {
            lua_settop(l, originalTop);
        }
    }

    public void SetMetatable(LuauValue value, LuauTable? metatable)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        var originalTop = lua_gettop(l);
        try
        {
            Push(value);
            Push(metatable ?? LuauValue.Nil);

            var result = 0;
            LuauNativeProtection.Prepare(context);
            var status = luau_ffi_protected_setmetatable(l, -2, &result);
            LuauNativeProtection.ThrowIfFailed(this, l, status, "set a value's metatable");
        }
        finally
        {
            lua_settop(l, originalTop);
        }
    }
}
