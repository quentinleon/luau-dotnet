using System.Runtime.CompilerServices;

namespace Luau;

internal static class MathEx
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInteger(double value)
    {
        return double.IsFinite(value) && value == Math.Truncate(value);
    }
}