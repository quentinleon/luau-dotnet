using Luau.Native;

namespace Luau;

internal static class ThrowHelper
{
    public static void ThrowObjectDisposedException(string? objectName)
    {
        throw new ObjectDisposedException(objectName);
    }

    public static void ThrowArgumentOutOfRangeException(string? paramName, string? message)
    {
        throw new ArgumentOutOfRangeException(paramName, message);
    }

    public static void ThrowArgumentException(string? paramName, string? message)
    {
        throw new ArgumentException(paramName, message);
    }

    public static void ThrowInvalidOperationException(string? message)
    {
        throw new InvalidOperationException(message);
    }

    public static void ThrowTypeIsNotSupported(lua_Type type)
    {
        throw new InvalidOperationException($"Type: {type} is not supported");
    }
}