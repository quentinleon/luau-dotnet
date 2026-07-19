using static Luau.Internal.Interop.NativeMethods;

namespace Luau;

unsafe partial class LuauState
{
    /// <summary>
    /// Gets an independently disposable owner for <paramref name="value"/>'s
    /// metatable, or <see langword="null"/> when it has none.
    /// </summary>
    /// <param name="value">The value borrowed for the duration of the operation.</param>
    public LuauTable? GetMetatable(LuauValue value)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        var originalTop = luau_host_stack_get_top(l);
        try
        {
            Push(value);

            var hasMetatable = 0;
            LuauNativeProtection.Prepare(context);
            var status = luau_host_metatable_get(l, -1, &hasMetatable);
            LuauNativeProtection.ThrowIfFailed(this, l, status, "get a value's metatable");

            return hasMetatable == 0 ? null : Pop().Read<LuauTable>();
        }
        finally
        {
            SetTop(originalTop);
        }
    }

    /// <summary>
    /// Sets <paramref name="value"/>'s metatable, borrowing both wrappers for
    /// the duration of the operation. Pass <see langword="null"/> to clear it.
    /// </summary>
    public void SetMetatable(LuauValue value, LuauTable? metatable)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        var originalTop = luau_host_stack_get_top(l);
        try
        {
            Push(value);
            Push(metatable ?? LuauValue.Nil);

            var result = 0;
            LuauNativeProtection.Prepare(context);
            var status = luau_host_metatable_set(l, -2, &result);
            LuauNativeProtection.ThrowIfFailed(this, l, status, "set a value's metatable");
        }
        finally
        {
            SetTop(originalTop);
        }
    }
}
