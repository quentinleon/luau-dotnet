using System.Numerics;
using System.Runtime.CompilerServices;
using Luau.Native;
using static Luau.Native.NativeMethods;

namespace Luau;

unsafe partial class LuauState
{
    public LuauTable CreateTable()
    {
        return CreateTable(0, 0);
    }

    public LuauTable CreateTable(int nArr, int nRec)
    {
        ThrowIfDisposed();
        if (nArr < 0) throw new ArgumentOutOfRangeException(nameof(nArr));
        if (nRec < 0) throw new ArgumentOutOfRangeException(nameof(nRec));
        using var access = EnterNativeAccess();

        LuauNativeProtection.Prepare(context);
        var status = luau_ffi_protected_createtable(l, nArr, nRec);
        LuauNativeProtection.ThrowIfFailed(this, l, status, "create a table");

        try
        {
            return new(this, ReferenceTopValue("retain a table"));
        }
        finally
        {
            lua_pop(l, 1);
        }
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
        return CreateFunction(name: null, func);
    }

    public LuauFunction CreateFunction(string? name, Func<LuauState, int> func)
    {
        ThrowIfDisposed();
        if (func == null) throw new ArgumentNullException(nameof(func));

        var function = new LuauCSharpFunction(this, func, name);
        return function;
    }

    public LuauFunction CreateFunction(Func<LuauState, CancellationToken, ValueTask<int>> func)
    {
        return CreateFunction(name: null, func);
    }

    public LuauFunction CreateFunction(
        string? name,
        Func<LuauState, CancellationToken, ValueTask<int>> func)
    {
        ThrowIfDisposed();
        if (func == null) throw new ArgumentNullException(nameof(func));

        var function = new LuauCSharpAsyncFunction(this, func, name);
        return function;
    }

    public LuauState CreateThread()
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();

        lua_State* threadPtr = null;
        LuauNativeProtection.Prepare(context);
        var status = luau_ffi_protected_newthread(l, &threadPtr);
        LuauNativeProtection.ThrowIfFailed(this, l, status, "create a Luau thread");

        try
        {
            return context.GetOrCreateThread(l, threadPtr, -1);
        }
        finally
        {
            lua_pop(l, 1);
        }
    }

    public LuauBuffer CreateBuffer(int size)
    {
        ThrowIfDisposed();
        if (size < 0) ThrowHelper.ThrowArgumentException(nameof(size), "Buffer size must be greater than 0");
        using var access = EnterNativeAccess();

        void* data = null;
        LuauNativeProtection.Prepare(context);
        var status = luau_ffi_protected_newbuffer(l, (nuint)size, &data);
        LuauNativeProtection.ThrowIfFailed(this, l, status, "create a buffer");

        try
        {
            return new LuauBuffer(this, ReferenceTopValue("retain a buffer"));
        }
        finally
        {
            lua_pop(l, 1);
        }
    }

    public LuauBuffer CreateBuffer(ReadOnlySpan<byte> str)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        void* data = null;
        LuauNativeProtection.Prepare(context);
        var status = luau_ffi_protected_newbuffer(l, (nuint)str.Length, &data);
        LuauNativeProtection.ThrowIfFailed(this, l, status, "create a buffer");

        try
        {
            fixed (byte* val = str)
            {
                Unsafe.CopyBlock(data, val, (uint)str.Length);
            }

            return new LuauBuffer(this, ReferenceTopValue("retain a buffer"));
        }
        finally
        {
            lua_pop(l, 1);
        }
    }

    public LuauUserData CreateUserData<T>(T value)
        where T : unmanaged
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();

        var size = sizeof(T);
        void* ptr = null;
        LuauNativeProtection.Prepare(context);
        var status = luau_ffi_protected_newuserdatatagged(l, (nuint)size, 0, &ptr);
        LuauNativeProtection.ThrowIfFailed(this, l, status, "create userdata");

        try
        {
            Unsafe.CopyBlock(ptr, &value, (uint)size);

            return new LuauUserData(this, ReferenceTopValue("retain userdata"));
        }
        finally
        {
            lua_pop(l, 1);
        }
    }

    public LuauValue CreateFrom<T>(T? value)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();

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
            void* ptr = null;
            LuauNativeProtection.Prepare(context);
            var status = luau_ffi_protected_newuserdatatagged(l, (nuint)size, 0, &ptr);
            LuauNativeProtection.ThrowIfFailed(this, l, status, "create userdata");

            try
            {
                Unsafe.CopyBlock(ptr, &value, (uint)size);

                var reference = ReferenceTopValue("retain userdata");
                return LuauValue.FromUserData(new LuauUserData(this, reference));
            }
            finally
            {
                lua_pop(l, 1);
            }
#pragma warning restore CS8500
        }

        ThrowHelper.ThrowArgumentException(nameof(value), $"Cannot convert {typeof(T).Name} to LuauValue");
        return default; // dummy
    }

    int ReferenceTopValue(string operation)
    {
        var reference = -1;
        LuauNativeProtection.Prepare(context);
        var status = luau_ffi_protected_ref(l, -1, &reference);
        LuauNativeProtection.ThrowIfFailed(this, l, status, operation);
        return reference;
    }
}
