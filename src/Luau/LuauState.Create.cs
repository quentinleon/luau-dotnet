using System.Numerics;
using System.Runtime.CompilerServices;
using Luau.Internal.Interop;
using static Luau.Internal.Interop.NativeMethods;

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
        var status = luau_host_table_create(l, nArr, nRec);
        LuauNativeProtection.ThrowIfFailed(this, l, status, "create a table");

        try
        {
            return new(this, ReferenceTopValue("retain a table"));
        }
        finally
        {
            SetTop(-2);
        }
    }

    public LuauTable CreateTable(ReadOnlySpan<LuauValue> values)
    {
        var table = CreateTable(values.Length, 0);

        for (int i = 0; i < values.Length; i++)
        {
            // Array slots are Luau number keys. Managed integral values map to
            // the distinct upstream 64-bit integer kind in Stage 2.
            table.RawSet(LuauValue.FromNumber(i + 1), values[i]);
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

    public LuauFunction CreateFunction(Action<LuauCallContext> callback)
    {
        return CreateFunction(name: null, callback);
    }

    public LuauFunction CreateFunction(string? name, Action<LuauCallContext> callback)
    {
        ThrowIfDisposed();
        if (callback == null) throw new ArgumentNullException(nameof(callback));

        return CreateRawFunction(
            name,
            (state, cancellationToken) =>
            {
                var frame = new LuauCallFrame(state, name, state.GetTop(), cancellationToken);
                try
                {
                    callback(new LuauCallContext(frame));
                    return frame.Complete();
                }
                finally
                {
                    frame.Invalidate();
                }
            });
    }

    public LuauFunction CreateAsyncFunction(Func<LuauCallContext, ValueTask> callback)
    {
        return CreateAsyncFunction(name: null, callback);
    }

    public LuauFunction CreateAsyncFunction(
        string? name,
        Func<LuauCallContext, ValueTask> callback)
    {
        ThrowIfDisposed();
        if (callback == null) throw new ArgumentNullException(nameof(callback));

        return CreateRawAsyncFunction(
            name,
            (state, cancellationToken) => LuauManagedCallbackInvoker.InvokeAsync(
                state,
                name,
                callback,
                cancellationToken));
    }

    internal LuauFunction CreateRawFunction(
        string? name,
        Func<LuauState, CancellationToken, int> callback)
    {
        ThrowIfDisposed();
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        return new LuauCSharpFunction(this, callback, name);
    }

    internal LuauFunction CreateRawAsyncFunction(
        string? name,
        Func<LuauState, CancellationToken, ValueTask<int>> callback)
    {
        ThrowIfDisposed();
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        return new LuauCSharpAsyncFunction(this, callback, name);
    }

    public unsafe LuauState CreateThread()
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();

        LuauHostState* threadPtr = null;
        LuauNativeProtection.Prepare(context);
        var status = luau_host_thread_create(l, &threadPtr);
        LuauNativeProtection.ThrowIfFailed(this, l, status, "create a Luau thread");

        try
        {
            return context.GetOrCreateThread(l, threadPtr, -1);
        }
        finally
        {
            SetTop(-2);
        }
    }

    public unsafe LuauBuffer CreateBuffer(int size)
    {
        ThrowIfDisposed();
        if (size < 0) ThrowHelper.ThrowArgumentException(nameof(size), "Buffer size must be greater than 0");
        using var access = EnterNativeAccess();

        void* data = null;
        LuauNativeProtection.Prepare(context);
        var status = luau_host_buffer_create(l, (ulong)size, &data);
        LuauNativeProtection.ThrowIfFailed(this, l, status, "create a buffer");

        try
        {
            return new LuauBuffer(this, ReferenceTopValue("retain a buffer"));
        }
        finally
        {
            SetTop(-2);
        }
    }

    public unsafe LuauBuffer CreateBuffer(ReadOnlySpan<byte> str)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        void* data = null;
        LuauNativeProtection.Prepare(context);
        var status = luau_host_buffer_create(l, (ulong)str.Length, &data);
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
            SetTop(-2);
        }
    }

    public unsafe LuauUserData CreateUserData<T>(T value)
        where T : unmanaged
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();

        var size = sizeof(T);
        void* ptr = null;
        LuauNativeProtection.Prepare(context);
        var status = luau_host_userdata_create(l, (ulong)size, 0, &ptr);
        LuauNativeProtection.ThrowIfFailed(this, l, status, "create userdata");

        try
        {
            Unsafe.CopyBlock(ptr, &value, (uint)size);

            return new LuauUserData(this, ReferenceTopValue("retain userdata"));
        }
        finally
        {
            SetTop(-2);
        }
    }

    public unsafe LuauValue CreateFrom<T>(T? value)
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

        if (typeof(T) == typeof(byte)) return LuauValue.FromInteger(Unsafe.As<T, byte>(ref value));
        if (typeof(T) == typeof(sbyte)) return LuauValue.FromInteger(Unsafe.As<T, sbyte>(ref value));
        if (typeof(T) == typeof(short)) return LuauValue.FromInteger(Unsafe.As<T, short>(ref value));
        if (typeof(T) == typeof(ushort)) return LuauValue.FromInteger(Unsafe.As<T, ushort>(ref value));
        if (typeof(T) == typeof(int)) return LuauValue.FromInteger(Unsafe.As<T, int>(ref value));
        if (typeof(T) == typeof(uint)) return LuauValue.FromInteger(Unsafe.As<T, uint>(ref value));
        if (typeof(T) == typeof(long)) return LuauValue.FromInteger(Unsafe.As<T, long>(ref value));
        if (typeof(T) == typeof(ulong))
        {
            return LuauValue.FromInteger(checked((long)Unsafe.As<T, ulong>(ref value)));
        }
        if (typeof(T) == typeof(float)) return LuauValue.FromNumber(Unsafe.As<T, float>(ref value));
        if (typeof(T) == typeof(double)) return LuauValue.FromNumber(Unsafe.As<T, double>(ref value));

        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
#pragma warning disable CS8500
            var size = sizeof(T);
            void* ptr = null;
            LuauNativeProtection.Prepare(context);
            var status = luau_host_userdata_create(l, (ulong)size, 0, &ptr);
            LuauNativeProtection.ThrowIfFailed(this, l, status, "create userdata");

            try
            {
                Unsafe.CopyBlock(ptr, &value, (uint)size);

                var reference = ReferenceTopValue("retain userdata");
                return LuauValue.FromUserData(new LuauUserData(this, reference));
            }
            finally
            {
                SetTop(-2);
            }
#pragma warning restore CS8500
        }

        ThrowHelper.ThrowArgumentException(nameof(value), $"Cannot convert {typeof(T).Name} to LuauValue");
        return default; // dummy
    }

    unsafe int ReferenceTopValue(string operation)
    {
        var reference = -1;
        LuauNativeProtection.Prepare(context);
        var status = luau_host_reference_create(l, -1, &reference);
        LuauNativeProtection.ThrowIfFailed(this, l, status, operation);
        return reference;
    }
}

internal static class LuauManagedCallbackInvoker
{
    internal static async ValueTask<int> InvokeAsync(
        LuauState state,
        string? name,
        Func<LuauCallContext, ValueTask> callback,
        CancellationToken cancellationToken)
    {
        var frame = new LuauCallFrame(state, name, state.GetTop(), cancellationToken);
        try
        {
            await callback(new LuauCallContext(frame)).ConfigureAwait(false);
            return frame.Complete();
        }
        finally
        {
            frame.Invalidate();
        }
    }
}
