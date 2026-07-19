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

    /// <summary>Creates an opaque userdata value from a managed object capability.</summary>
    public static LuauValue FromObjectHandle(LuauObjectHandle value)
    {
        return new(
            LuauType.UserData,
            default,
            value ?? throw new ArgumentNullException(nameof(value)));
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
            LuauType.UserData => reference switch
            {
                LuauObjectHandle handle => handle.ToString(),
                LuauUserData userData => userData.ToString(),
                _ => "userdata",
            },
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

    /// <summary>Reads this value as the requested managed representation.</summary>
    /// <remarks>
    /// Reading a Luau script closure as <see cref="LuauFunction"/> produces a
    /// host-invokable function. Managed callback capabilities created by
    /// <see cref="LuauState.CreateFunction(Action{LuauCallContext})"/> remain
    /// callable only from Luau.
    /// </remarks>
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
            return Assign(this, out result);
        }

        switch (Type)
        {
            case LuauType.Nil:
                if (default(T) is null)
                {
                    result = default!;
                    return true;
                }
                break;
            case LuauType.Boolean:
                if (typeof(T) == typeof(bool))
                {
                    return Assign(value.BooleanValue, out result);
                }
                if (typeof(T) == typeof(object))
                {
                    return Assign((object)value.BooleanValue, out result);
                }
                break;
            case LuauType.UserData:
                if (typeof(T) == typeof(LuauObjectHandle) && reference is LuauObjectHandle objectHandle)
                {
                    return Assign(objectHandle, out result);
                }
                if (typeof(T) == typeof(LuauUserData))
                {
                    if (reference is LuauUserData userData)
                    {
                        return Assign(userData, out result);
                    }
                    break;
                }
                if (typeof(T) == typeof(object))
                {
                    return Assign(reference!, out result);
                }
                break;
            case LuauType.LightUserData:
                break;
            case LuauType.Number:
                var number = value.NumberValue;
                if (typeof(T) == typeof(double))
                {
                    return Assign(number, out result);
                }
                if (typeof(T) == typeof(float))
                {
                    return Assign((float)number, out result);
                }
                if (typeof(T) == typeof(byte) && MathEx.IsInteger(number) && number >= byte.MinValue && number <= byte.MaxValue)
                {
                    return Assign((byte)number, out result);
                }
                if (typeof(T) == typeof(sbyte) && MathEx.IsInteger(number) && number >= sbyte.MinValue && number <= sbyte.MaxValue)
                {
                    return Assign((sbyte)number, out result);
                }
                if (typeof(T) == typeof(short) && MathEx.IsInteger(number) && number >= short.MinValue && number <= short.MaxValue)
                {
                    return Assign((short)number, out result);
                }
                if (typeof(T) == typeof(ushort) && MathEx.IsInteger(number) && number >= ushort.MinValue && number <= ushort.MaxValue)
                {
                    return Assign((ushort)number, out result);
                }
                if (typeof(T) == typeof(int) && MathEx.IsInteger(number) && number >= int.MinValue && number <= int.MaxValue)
                {
                    return Assign((int)number, out result);
                }
                if (typeof(T) == typeof(long) && MathEx.IsInt64(number))
                {
                    return Assign((long)number, out result);
                }
                if (typeof(T) == typeof(uint) && MathEx.IsInteger(number) && number >= uint.MinValue && number <= uint.MaxValue)
                {
                    return Assign((uint)number, out result);
                }
                if (typeof(T) == typeof(ulong) && MathEx.IsUInt64(number))
                {
                    return Assign((ulong)number, out result);
                }
                if (typeof(T) == typeof(object))
                {
                    return Assign((object)number, out result);
                }
                break;
            case LuauType.Integer:
                var integer = value.IntegerValue;
                if (typeof(T) == typeof(long))
                {
                    return Assign(integer, out result);
                }
                if (typeof(T) == typeof(byte) && integer >= byte.MinValue && integer <= byte.MaxValue)
                {
                    return Assign((byte)integer, out result);
                }
                if (typeof(T) == typeof(sbyte) && integer >= sbyte.MinValue && integer <= sbyte.MaxValue)
                {
                    return Assign((sbyte)integer, out result);
                }
                if (typeof(T) == typeof(short) && integer >= short.MinValue && integer <= short.MaxValue)
                {
                    return Assign((short)integer, out result);
                }
                if (typeof(T) == typeof(ushort) && integer >= ushort.MinValue && integer <= ushort.MaxValue)
                {
                    return Assign((ushort)integer, out result);
                }
                if (typeof(T) == typeof(int) && integer >= int.MinValue && integer <= int.MaxValue)
                {
                    return Assign((int)integer, out result);
                }
                if (typeof(T) == typeof(uint) && integer >= uint.MinValue && integer <= uint.MaxValue)
                {
                    return Assign((uint)integer, out result);
                }
                if (typeof(T) == typeof(ulong) && integer >= 0)
                {
                    return Assign((ulong)integer, out result);
                }
                if (typeof(T) == typeof(double) && MathEx.TryConvertToDoubleExact(integer, out var exactDouble))
                {
                    return Assign(exactDouble, out result);
                }
                if (typeof(T) == typeof(float) && MathEx.TryConvertToSingleExact(integer, out var exactSingle))
                {
                    return Assign(exactSingle, out result);
                }
                if (typeof(T) == typeof(object))
                {
                    return Assign((object)integer, out result);
                }
                break;
            case LuauType.Vector:
                if (typeof(T) == typeof(Vector3))
                {
                    return Assign(value.VectorValue, out result);
                }
                if (typeof(T) == typeof(object))
                {
                    return Assign((object)value.VectorValue, out result);
                }
                break;
            case LuauType.String:
                if (typeof(T) == typeof(string))
                {
                    return Assign((string)reference!, out result);
                }
                if (typeof(T) == typeof(object))
                {
                    return Assign((object)(string)reference!, out result);
                }
                break;
            case LuauType.Table:
                if (typeof(T) == typeof(LuauTable))
                {
                    return Assign((LuauTable)reference!, out result);
                }
                if (typeof(T) == typeof(object))
                {
                    return Assign((object)(LuauTable)reference!, out result);
                }
                break;
            case LuauType.Function:
                if (typeof(T) == typeof(LuauFunction))
                {
                    return Assign((LuauFunction)reference!, out result);
                }
                if (typeof(T) == typeof(object))
                {
                    return Assign((object)(LuauFunction)reference!, out result);
                }
                break;
            case LuauType.Thread:
                if (typeof(T) == typeof(LuauState))
                {
                    return Assign((LuauState)reference!, out result);
                }
                if (typeof(T) == typeof(object))
                {
                    return Assign((object)(LuauState)reference!, out result);
                }
                break;
            case LuauType.Buffer:
                if (typeof(T) == typeof(LuauBuffer))
                {
                    return Assign((LuauBuffer)reference!, out result);
                }
                if (typeof(T) == typeof(object))
                {
                    return Assign((object)(LuauBuffer)reference!, out result);
                }
                break;
        }

        result = default!;
        return false;
    }

    static bool Assign<T>(object value, out T result)
    {
        result = (T)value;
        return true;
    }

    public bool Equals(LuauValue other)
    {
        if (type != other.type) return false;

        return type switch
        {
            LuauType.Nil => true,
            LuauType.Boolean => value.BooleanValue == other.value.BooleanValue,
            LuauType.LightUserData => value.PointerValue == other.value.PointerValue,
            LuauType.UserData => ReferenceEquals(reference, other.reference),
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
        return type switch
        {
            LuauType.Nil => HashCode.Combine(type),
            LuauType.Boolean => HashCode.Combine(type, value.BooleanValue),
            LuauType.LightUserData => HashCode.Combine(type, value.PointerValue),
            LuauType.Number => HashCode.Combine(type, value.NumberValue),
            LuauType.Integer => HashCode.Combine(type, value.IntegerValue),
            LuauType.Vector => HashCode.Combine(type, value.VectorValue),
            LuauType.String => HashCode.Combine(type, (string)reference!),
            _ => HashCode.Combine(type, RuntimeHelpers.GetHashCode(reference!)),
        };
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
    public static implicit operator LuauValue(LuauObjectHandle value) => FromObjectHandle(value);
}
