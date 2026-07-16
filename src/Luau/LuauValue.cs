using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Luau;

[StructLayout(LayoutKind.Auto)]
public readonly struct LuauValue : IEquatable<LuauValue>
{
    [StructLayout(LayoutKind.Explicit)]
    struct ValueUnion
    {
        [FieldOffset(0)] public bool BooleanValue;
        [FieldOffset(0)] public double NumberValue;
        [FieldOffset(0)] public long IntegerValue;
        [FieldOffset(0)] public IntPtr PointerValue;
        [FieldOffset(0)] public Vector3 VectorValue;
    }

    public static readonly LuauValue Nil = default;

    public static LuauValue FromNumber(double value)
    {
        return new(LuauType.Number, new() { NumberValue = value }, null);
    }

    public static LuauValue FromInteger(long value)
    {
        return new(LuauType.Integer, new() { IntegerValue = value }, null);
    }

    public static LuauValue FromBoolean(bool value)
    {
        return new(LuauType.Boolean, new() { BooleanValue = value }, null);
    }

    public static LuauValue FromString(string value)
    {
        return new(LuauType.String, default, value);
    }

    internal static LuauValue FromLightUserData(IntPtr value)
    {
        return new(LuauType.LightUserData, new() { PointerValue = value }, null);
    }

    public static LuauValue FromUserData(LuauUserData value)
    {
        return new(LuauType.UserData, default, value);
    }

    public static LuauValue FromVector(Vector3 value)
    {
        return new(LuauType.Vector, new() { VectorValue = value }, null);
    }

    public static LuauValue FromTable(LuauTable value)
    {
        return new(LuauType.Table, default, value);
    }

    public static LuauValue FromFunction(LuauFunction value)
    {
        return new(LuauType.Function, default, value);
    }

    public static LuauValue FromThread(LuauState value)
    {
        return new(LuauType.Thread, default, value);
    }

    public static LuauValue FromBuffer(LuauBuffer value)
    {
        return new(LuauType.Buffer, default, value);
    }

    readonly LuauType type;
    readonly ValueUnion value;
    readonly object? reference;

    public LuauType Type => type;

    internal IntPtr LightUserDataPointer => type == LuauType.LightUserData
        ? value.PointerValue
        : throw new InvalidOperationException($"Cannot read {type} as light userdata.");

    LuauValue(LuauType type, ValueUnion value, object? reference)
    {
        this.type = type;
        this.value = value;
        this.reference = reference;
    }

    public unsafe override string ToString()
    {
        return type switch
        {
            LuauType.Nil => "nil",
            LuauType.Boolean => value.BooleanValue ? "true" : "false",
            LuauType.LightUserData => $"lightuserdata: 0x{value.PointerValue:X}",
            LuauType.Number => value.NumberValue.ToString(),
            LuauType.Integer => value.IntegerValue.ToString(),
            LuauType.Vector => VectorToString(value.VectorValue),
            LuauType.String => ((string)reference!).ToString(),
            LuauType.Table => ((LuauTable)reference!).ToString(),
            LuauType.Function => ((LuauFunction)reference!).ToString()!,
            LuauType.UserData => ((LuauUserData)reference!).ToString(),
            LuauType.Thread => ((LuauState)reference!).ToString()!,
            LuauType.Buffer => ((LuauBuffer)reference!).ToString()!,
            _ => "",
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static string VectorToString(Vector3 vector)
    {
        return $"{vector.X}, {vector.Y}, {vector.Z}";
    }

    public bool IsNil => Type == LuauType.Nil;

    public T Read<T>()
    {
        if (TryRead<T>(out var result)) return result;
        throw new InvalidOperationException($"Cannot convert {Type} to {typeof(T).Name}");
    }

    /// <summary>
    /// Reads a numeric value as <see cref="double"/>, explicitly allowing a
    /// 64-bit integer to lose precision.
    /// </summary>
    public double ReadDoubleLossy()
    {
        return Type switch
        {
            LuauType.Number => value.NumberValue,
            LuauType.Integer => value.IntegerValue,
            _ => throw new InvalidOperationException($"Cannot convert {Type} to Double"),
        };
    }

    public bool TryRead<T>(out T result)
    {
        if (typeof(T) == typeof(LuauValue))
        {
            var r = this;
            result = Unsafe.As<LuauValue, T>(ref r);
            return true;
        }

        switch (Type)
        {
            case LuauType.Nil:
                if (typeof(T) == typeof(object))
                {
                    result = Unsafe.NullRef<T>();
                    return true;
                }
                break;
            case LuauType.Boolean:
                if (typeof(T) == typeof(bool))
                {
                    var r = value.BooleanValue;
                    result = Unsafe.As<bool, T>(ref r);
                    return true;
                }
                if (typeof(T) == typeof(object))
                {
                    var r = (object)value.BooleanValue;
                    result = Unsafe.As<object, T>(ref r);
                    return true;
                }
                break;
            case LuauType.UserData:
                if (typeof(T) == typeof(LuauUserData))
                {
                    var r = (LuauUserData)reference!;
                    result = Unsafe.As<LuauUserData, T>(ref r);
                    return true;
                }
                if (typeof(T) == typeof(object))
                {
                    var r = (object)(LuauUserData)reference!;
                    result = Unsafe.As<object, T>(ref r);
                    return true;
                }
                if (reference is LuauUserData userData && userData.TryRead<T>(out var userDataResult))
                {
                    result = userDataResult;
                    return true;
                }
                break;
            case LuauType.LightUserData:
                break;
            case LuauType.Number:
                var number = value.NumberValue;
                if (typeof(T) == typeof(double))
                {
                    var r = number;
                    result = Unsafe.As<double, T>(ref r);
                    return true;
                }
                if (typeof(T) == typeof(float))
                {
                    var r = (float)number;
                    result = Unsafe.As<float, T>(ref r);
                    return true;
                }
                if (typeof(T) == typeof(byte) && MathEx.IsInteger(number) && number >= byte.MinValue && number <= byte.MaxValue)
                {
                    var r = (byte)number;
                    result = Unsafe.As<byte, T>(ref r);
                    return true;
                }
                if (typeof(T) == typeof(sbyte) && MathEx.IsInteger(number) && number >= sbyte.MinValue && number <= sbyte.MaxValue)
                {
                    var r = (sbyte)number;
                    result = Unsafe.As<sbyte, T>(ref r);
                    return true;
                }
                if (typeof(T) == typeof(short) && MathEx.IsInteger(number) && number >= short.MinValue && number <= short.MaxValue)
                {
                    var r = (short)number;
                    result = Unsafe.As<short, T>(ref r);
                    return true;
                }
                if (typeof(T) == typeof(ushort) && MathEx.IsInteger(number) && number >= ushort.MinValue && number <= ushort.MaxValue)
                {
                    var r = (ushort)number;
                    result = Unsafe.As<ushort, T>(ref r);
                    return true;
                }
                if (typeof(T) == typeof(int) && MathEx.IsInteger(number) && number >= int.MinValue && number <= int.MaxValue)
                {
                    var r = (int)number;
                    result = Unsafe.As<int, T>(ref r);
                    return true;
                }
                if (typeof(T) == typeof(long) && MathEx.IsInt64(number))
                {
                    var r = (long)number;
                    result = Unsafe.As<long, T>(ref r);
                    return true;
                }
                if (typeof(T) == typeof(uint) && MathEx.IsInteger(number) && number >= uint.MinValue && number <= uint.MaxValue)
                {
                    var r = (uint)number;
                    result = Unsafe.As<uint, T>(ref r);
                    return true;
                }
                if (typeof(T) == typeof(ulong) && MathEx.IsUInt64(number))
                {
                    var r = (ulong)number;
                    result = Unsafe.As<ulong, T>(ref r);
                    return true;
                }
                if (typeof(T) == typeof(object))
                {
                    var r = (object)number;
                    result = Unsafe.As<object, T>(ref r);
                    return true;
                }
                break;
            case LuauType.Integer:
                var integer = value.IntegerValue;
                if (typeof(T) == typeof(long))
                {
                    var r = integer;
                    result = Unsafe.As<long, T>(ref r);
                    return true;
                }
                if (typeof(T) == typeof(byte) && integer >= byte.MinValue && integer <= byte.MaxValue)
                {
                    var r = (byte)integer;
                    result = Unsafe.As<byte, T>(ref r);
                    return true;
                }
                if (typeof(T) == typeof(sbyte) && integer >= sbyte.MinValue && integer <= sbyte.MaxValue)
                {
                    var r = (sbyte)integer;
                    result = Unsafe.As<sbyte, T>(ref r);
                    return true;
                }
                if (typeof(T) == typeof(short) && integer >= short.MinValue && integer <= short.MaxValue)
                {
                    var r = (short)integer;
                    result = Unsafe.As<short, T>(ref r);
                    return true;
                }
                if (typeof(T) == typeof(ushort) && integer >= ushort.MinValue && integer <= ushort.MaxValue)
                {
                    var r = (ushort)integer;
                    result = Unsafe.As<ushort, T>(ref r);
                    return true;
                }
                if (typeof(T) == typeof(int) && integer >= int.MinValue && integer <= int.MaxValue)
                {
                    var r = (int)integer;
                    result = Unsafe.As<int, T>(ref r);
                    return true;
                }
                if (typeof(T) == typeof(uint) && integer >= uint.MinValue && integer <= uint.MaxValue)
                {
                    var r = (uint)integer;
                    result = Unsafe.As<uint, T>(ref r);
                    return true;
                }
                if (typeof(T) == typeof(ulong) && integer >= 0)
                {
                    var r = (ulong)integer;
                    result = Unsafe.As<ulong, T>(ref r);
                    return true;
                }
                if (typeof(T) == typeof(double) && MathEx.TryConvertToDoubleExact(integer, out var exactDouble))
                {
                    result = Unsafe.As<double, T>(ref exactDouble);
                    return true;
                }
                if (typeof(T) == typeof(float) && MathEx.TryConvertToSingleExact(integer, out var exactSingle))
                {
                    result = Unsafe.As<float, T>(ref exactSingle);
                    return true;
                }
                if (typeof(T) == typeof(object))
                {
                    var r = (object)integer;
                    result = Unsafe.As<object, T>(ref r);
                    return true;
                }
                break;
            case LuauType.Vector:
                if (typeof(T) == typeof(Vector3))
                {
                    var r = value.VectorValue;
                    result = Unsafe.As<Vector3, T>(ref r);
                    return true;
                }
                if (typeof(T) == typeof(object))
                {
                    var r = (object)value.VectorValue;
                    result = Unsafe.As<object, T>(ref r);
                    return true;
                }
                break;
            case LuauType.String:
                if (typeof(T) == typeof(string))
                {
                    var r = (string)reference!;
                    result = Unsafe.As<string, T>(ref r);
                    return true;
                }
                if (typeof(T) == typeof(object))
                {
                    var r = (object)(string)reference!;
                    result = Unsafe.As<object, T>(ref r);
                    return true;
                }
                break;
            case LuauType.Table:
                if (typeof(T) == typeof(LuauTable))
                {
                    var r = (LuauTable)reference!;
                    result = Unsafe.As<LuauTable, T>(ref r);
                    return true;
                }
                if (typeof(T) == typeof(object))
                {
                    var r = (object)(LuauTable)reference!;
                    result = Unsafe.As<object, T>(ref r);
                    return true;
                }
                break;
            case LuauType.Function:
                if (typeof(T) == typeof(LuauFunction))
                {
                    var r = (LuauFunction)reference!;
                    result = Unsafe.As<LuauFunction, T>(ref r);
                    return true;
                }
                if (typeof(T) == typeof(object))
                {
                    var r = (object)(LuauFunction)reference!;
                    result = Unsafe.As<object, T>(ref r);
                    return true;
                }
                break;
            case LuauType.Thread:
                if (typeof(T) == typeof(LuauState))
                {
                    var r = (LuauState)reference!;
                    result = Unsafe.As<LuauState, T>(ref r);
                    return true;
                }
                if (typeof(T) == typeof(object))
                {
                    var r = (object)(LuauState)reference!;
                    result = Unsafe.As<object, T>(ref r);
                    return true;
                }
                break;
            case LuauType.Buffer:
                if (typeof(T) == typeof(LuauBuffer))
                {
                    var r = (LuauBuffer)reference!;
                    result = Unsafe.As<LuauBuffer, T>(ref r);
                    return true;
                }
                if (typeof(T) == typeof(object))
                {
                    var r = (object)(LuauBuffer)reference!;
                    result = Unsafe.As<object, T>(ref r);
                    return true;
                }
                break;
        }

        Unsafe.SkipInit(out result);
        return false;
    }

    public bool Equals(LuauValue other)
    {
        if (type != other.type) return false;

        return type switch
        {
            LuauType.Nil => true,
            LuauType.Boolean => value.BooleanValue == other.value.BooleanValue,
            LuauType.LightUserData or LuauType.UserData => value.PointerValue == other.value.PointerValue,
            LuauType.Number => value.NumberValue == other.value.NumberValue,
            LuauType.Integer => value.IntegerValue == other.value.IntegerValue,
            LuauType.Vector => value.VectorValue == other.value.VectorValue,
            LuauType.String => ((string)reference!).Equals((string)other.reference!),
            _ => reference == other.reference,
        };
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        return obj is LuauValue other && Equals(other);
    }

    public override int GetHashCode()
    {
        if (type == LuauType.Integer)
        {
            return HashCode.Combine(type, value.IntegerValue);
        }

        return HashCode.Combine(type, value, reference);
    }

    public static bool operator ==(LuauValue left, LuauValue right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(LuauValue left, LuauValue right)
    {
        return !(left == right);
    }

    public static implicit operator LuauValue(double value) => FromNumber(value);
    public static implicit operator LuauValue(byte value) => FromInteger(value);
    public static implicit operator LuauValue(sbyte value) => FromInteger(value);
    public static implicit operator LuauValue(short value) => FromInteger(value);
    public static implicit operator LuauValue(ushort value) => FromInteger(value);
    public static implicit operator LuauValue(int value) => FromInteger(value);
    public static implicit operator LuauValue(uint value) => FromInteger(value);
    public static implicit operator LuauValue(long value) => FromInteger(value);
    public static implicit operator LuauValue(ulong value) => FromInteger(checked((long)value));
    public static implicit operator LuauValue(bool value) => FromBoolean(value);
    public static implicit operator LuauValue(string value) => FromString(value);
    public static implicit operator LuauValue(Vector3 value) => FromVector(value);
    public static implicit operator LuauValue(LuauTable value) => FromTable(value);
    public static implicit operator LuauValue(LuauFunction value) => FromFunction(value);
    public static implicit operator LuauValue(LuauState value) => FromThread(value);
    public static implicit operator LuauValue(LuauBuffer value) => FromBuffer(value);
    public static implicit operator LuauValue(LuauUserData value) => FromUserData(value);
}
