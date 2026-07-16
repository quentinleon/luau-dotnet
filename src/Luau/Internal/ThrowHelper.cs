using System.Diagnostics.CodeAnalysis;
using Luau.Native;

namespace Luau;

internal static class ThrowHelper
{
    [DoesNotReturn]
    public static void ThrowObjectDisposedException(string? objectName)
    {
        throw new ObjectDisposedException(objectName);
    }

    [DoesNotReturn]
    public static void ThrowArgumentOutOfRangeException(string? paramName, string? message)
    {
        throw new ArgumentOutOfRangeException(paramName, message);
    }

    [DoesNotReturn]
    public static void ThrowArgumentException(string? paramName, string? message)
    {
        throw new ArgumentException(paramName, message);
    }

    [DoesNotReturn]
    public static void ThrowInvalidOperationException(string? message)
    {
        throw new InvalidOperationException(message);
    }

    [DoesNotReturn]
    public static void ThrowTypeIsNotSupported(lua_Type type)
    {
        throw new InvalidOperationException($"Type: {type} is not supported");
    }

    [DoesNotReturn]
    public static void ThrowUnsupportedValue(lua_Type type)
    {
        var kind = type switch
        {
            lua_Type.LUA_TCLASS => "class",
            lua_Type.LUA_TOBJECT => "object",
            _ => type.ToString(),
        };

        throw new LuauUnsupportedValueException(kind);
    }
}
