using System.Numerics;
using System.Runtime.CompilerServices;
using static Luau.Native.NativeMethods;

namespace Luau;

unsafe partial class LuauState
{
    public LuauTable CreateTable()
    {
        ThrowIfDisposed();

        lua_newtable(l);
        var reference = lua_ref(l, -1);
        lua_pop(l, 1);

        return new(this, reference);
    }

    public LuauTable CreateTable(int nArr, int nRec)
    {
        ThrowIfDisposed();

        lua_createtable(l, nArr, nRec);
        var reference = lua_ref(l, -1);
        lua_pop(l, 1);

        return new(this, reference);
    }

    public LuauTable CreateTable(ReadOnlySpan<LuauValue> values)
    {
        var table = CreateTable(values.Length, 0);

        for (int i = 0; i < values.Length; i++)
        {
            table.RawSet(i + 1, values[i]);
        }

        return table;
    }

    public LuauTable CreateTable(Dictionary<LuauValue, LuauValue> values)
    {
        var table = CreateTable(0, values.Count);

        foreach (var kv in values)
        {
            table.RawSet(kv.Key, kv.Value);
        }

        return table;
    }

    public LuauFunction CreateFunction(Func<LuauState, int> func)
    {
        ThrowIfDisposed();

        var function = new LuauCSharpFunction(this, func);
        return function;
    }

    public LuauFunction CreateFunction(Func<LuauState, CancellationToken, ValueTask<int>> func)
    {
        ThrowIfDisposed();

        var function = new LuauCSharpAsyncFunction(this, func);
        return function;
    }

    public LuauState CreateThread()
    {
        ThrowIfDisposed();

        var threadPtr = lua_newthread(l);
        var reference = lua_ref(l, -1);
        lua_pop(l, 1);

        return CreateStateInternal(threadPtr, l, reference);
    }

    public LuauBuffer CreateBuffer(int size)
    {
        ThrowIfDisposed();
        if (size < 0) ThrowHelper.ThrowArgumentException(nameof(size), "Buffer size must be greater than 0");

        lua_newbuffer(l, (nuint)size);

        var reference = lua_ref(l, -1);
        lua_pop(l, 1);

        return new LuauBuffer(this, reference);
    }

    public LuauBuffer CreateBuffer(ReadOnlySpan<byte> str)
    {
        var data = lua_newbuffer(l, (nuint)str.Length);

        fixed (byte* val = str)
        {
            Unsafe.CopyBlock(data, val, (uint)str.Length);
        }

        var reference = lua_ref(l, -1);
        lua_pop(l, 1);

        return new LuauBuffer(this, reference);
    }

    public LuauUserData CreateUserData<T>(T value)
        where T : unmanaged
    {
        ThrowIfDisposed();

        var size = sizeof(T);
        var ptr = lua_newuserdata(l, (nuint)size);

        Unsafe.CopyBlock(ptr, &value, (uint)size);

        var reference = lua_ref(l, -1);
        lua_pop(l, 1);

        return new LuauUserData(this, reference);
    }

    public LuauValue CreateFrom<T>(T? value)
    {
        ThrowIfDisposed();

        if (value == null) return LuauValue.Nil;

        if (typeof(T) == typeof(LuauValue)) return Unsafe.As<T, LuauValue>(ref value);

        if (typeof(T) == typeof(bool)) return LuauValue.FromBoolean(Unsafe.As<T, bool>(ref value));
        if (typeof(T) == typeof(string)) return LuauValue.FromString(Unsafe.As<T, string>(ref value));
        if (typeof(T) == typeof(Vector3)) return LuauValue.FromVector(Unsafe.As<T, Vector3>(ref value));
        if (typeof(T) == typeof(LuauFunction)) return LuauValue.FromFunction(Unsafe.As<T, LuauFunction>(ref value));
        if (typeof(T) == typeof(LuauTable)) return LuauValue.FromTable(Unsafe.As<T, LuauTable>(ref value));
        if (typeof(T) == typeof(LuauBuffer)) return LuauValue.FromBuffer(Unsafe.As<T, LuauBuffer>(ref value));
        if (typeof(T) == typeof(LuauState)) return LuauValue.FromThread(Unsafe.As<T, LuauState>(ref value));
        if (typeof(T) == typeof(LuauUserData)) return LuauValue.FromUserData(Unsafe.As<T, LuauUserData>(ref value));

        if (typeof(T) == typeof(byte)) return LuauValue.FromNumber(Unsafe.As<T, byte>(ref value));
        if (typeof(T) == typeof(sbyte)) return LuauValue.FromNumber(Unsafe.As<T, sbyte>(ref value));
        if (typeof(T) == typeof(short)) return LuauValue.FromNumber(Unsafe.As<T, short>(ref value));
        if (typeof(T) == typeof(ushort)) return LuauValue.FromNumber(Unsafe.As<T, ushort>(ref value));
        if (typeof(T) == typeof(int)) return LuauValue.FromNumber(Unsafe.As<T, int>(ref value));
        if (typeof(T) == typeof(uint)) return LuauValue.FromNumber(Unsafe.As<T, uint>(ref value));
        if (typeof(T) == typeof(long)) return LuauValue.FromNumber(Unsafe.As<T, long>(ref value));
        if (typeof(T) == typeof(ulong)) return LuauValue.FromNumber(Unsafe.As<T, ulong>(ref value));
        if (typeof(T) == typeof(float)) return LuauValue.FromNumber(Unsafe.As<T, float>(ref value));
        if (typeof(T) == typeof(double)) return LuauValue.FromNumber(Unsafe.As<T, double>(ref value));

        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
#pragma warning disable CS8500
            var size = sizeof(T);
            var ptr = lua_newuserdata(l, (nuint)size);

            Unsafe.CopyBlock(ptr, &value, (uint)size);

            var reference = lua_ref(l, -1);
            lua_pop(l, 1);

            return LuauValue.FromUserData(new LuauUserData(this, reference));
#pragma warning restore CS8500
        }

        ThrowHelper.ThrowArgumentException(nameof(value), $"Cannot convert {typeof(T).Name} to LuauValue");
        return default; // dummy
    }
}