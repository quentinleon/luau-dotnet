using System.Text;
using Luau.Native;
using static Luau.Native.NativeMethods;

namespace Luau;

internal unsafe static class LuauReferenceHelper
{
    public static int CreateReference(LuauState state, int index, string operation)
    {
        using var access = state.EnterNativeAccess();
        var pointer = state.PointerUnsafe;
        var reference = -1;

        LuauNativeProtection.Prepare(state.Context);
        var status = luau_ffi_protected_ref(pointer, index, &reference);
        LuauNativeProtection.ThrowIfFailed(state, pointer, status, operation);
        return reference;
    }

    public static void PushReference(LuauState state, int reference, string operation)
    {
        using var access = state.EnterNativeAccess();
        var pointer = state.PointerUnsafe;
        var ignoredType = 0;

        LuauNativeProtection.Prepare(state.Context);
        var status = luau_ffi_protected_rawgeti(
            pointer,
            LUA_REGISTRYINDEX,
            reference,
            &ignoredType);
        LuauNativeProtection.ThrowIfFailed(state, pointer, status, operation);
    }

    public static void* GetRefPointer(LuauState state, int reference)
    {
        using var access = state.EnterNativeAccess();
        var pointer = state.PointerUnsafe;
        var originalTop = lua_gettop(pointer);
        try
        {
            PushReference(state, reference, "read a managed Luau reference");
            return lua_topointer(pointer, -1);
        }
        finally
        {
            lua_settop(pointer, originalTop);
        }
    }

    public static string RefToString(LuauState state, int reference)
    {
        using var access = state.EnterNativeAccess();
        var pointer = state.PointerUnsafe;

        // A root state has no registry reference. Format it directly without
        // asking Luau to execute a __tostring metamethod.
        if (reference < 0)
        {
            return $"thread: 0x{(nuint)(nint)pointer:x}";
        }

        var originalTop = lua_gettop(pointer);
        try
        {
            PushReference(state, reference, "format a managed Luau reference");
            var type = lua_type(pointer, -1);
            var typeName = ReadTypeName(pointer, type);
            var valuePointer = (nuint)(nint)lua_topointer(pointer, -1);
            return valuePointer == 0
                ? typeName
                : $"{typeName}: 0x{valuePointer:x}";
        }
        finally
        {
            lua_settop(pointer, originalTop);
        }
    }

    static string ReadTypeName(lua_State* state, int type)
    {
        var text = lua_typename(state, type);
        if (text == null)
        {
            return "value";
        }

        var length = 0;
        while (length < 64 && text[length] != 0)
        {
            length++;
        }

        return length == 0
            ? "value"
            : Encoding.UTF8.GetString(new ReadOnlySpan<byte>(text, length));
    }
}
