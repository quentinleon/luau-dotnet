using System.Runtime.CompilerServices;

namespace Luau;

internal static class MathEx
{
    const double Int64UpperBoundExclusive = 9_223_372_036_854_775_808d;
    const double UInt64UpperBoundExclusive = 18_446_744_073_709_551_616d;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInteger(double value)
    {
        return double.IsFinite(value) && value == Math.Truncate(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInt64(double value)
    {
        return IsInteger(value) && value >= long.MinValue && value < Int64UpperBoundExclusive;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsUInt64(double value)
    {
        return IsInteger(value) && value >= 0 && value < UInt64UpperBoundExclusive;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryConvertToDoubleExact(long value, out double result)
    {
        result = value;
        return result < Int64UpperBoundExclusive && (long)result == value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryConvertToSingleExact(long value, out float result)
    {
        result = value;
        var widened = (double)result;
        return widened >= long.MinValue &&
               widened < Int64UpperBoundExclusive &&
               (long)widened == value;
    }
}
