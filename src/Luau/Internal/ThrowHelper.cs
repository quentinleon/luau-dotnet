using System.Diagnostics.CodeAnalysis;
using Luau.Internal.Interop;

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
    public static void ThrowTypeIsNotSupported(LuauHostType type)
    {
        throw new InvalidOperationException($"Type: {type} is not supported");
    }

    [DoesNotReturn]
    public static void ThrowUnsupportedValue(LuauHostType type)
    {
        var kind = type switch
        {
            LuauHostType.Class => "class",
            LuauHostType.Object => "object",
            _ => type.ToString(),
        };

        throw new LuauUnsupportedValueException(kind);
    }
}
