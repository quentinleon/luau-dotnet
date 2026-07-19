using System.Numerics;
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

    /// <summary>
    /// Creates a managed callback capability that Luau code can call. Unlike a
    /// script closure returned by a load or value conversion, this capability
    /// cannot be invoked directly through <see cref="LuauFunction.Invoke"/>.
    /// </summary>
    public LuauFunction CreateFunction(Action<LuauCallContext> callback)
    {
        return CreateFunction(name: null, callback);
    }

    /// <summary>
    /// Creates a named managed callback capability that Luau code can call.
    /// Managed callers cannot invoke the returned capability directly.
    /// </summary>
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

    /// <summary>
    /// Creates an asynchronous managed callback capability for Luau code.
    /// Managed callers cannot invoke the returned capability directly.
    /// </summary>
    public LuauFunction CreateAsyncFunction(Func<LuauCallContext, ValueTask> callback)
    {
        return CreateAsyncFunction(name: null, callback);
    }

    /// <summary>
    /// Creates a named asynchronous managed callback capability for Luau code.
    /// Managed callers cannot invoke the returned capability directly.
    /// </summary>
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
        if (size < 0) ThrowHelper.ThrowArgumentException(nameof(size), "Buffer size must be non-negative");
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
            str.CopyTo(new Span<byte>(data, str.Length));

            return new LuauBuffer(this, ReferenceTopValue("retain a buffer"));
        }
        finally
        {
            SetTop(-2);
        }
    }

    public LuauValue CreateFrom<T>(T? value)
    {
        ThrowIfDisposed();

        if (value == null) return LuauValue.Nil;

        if (value is LuauValue luauValue) return luauValue;
        if (value is bool boolean) return LuauValue.FromBoolean(boolean);
        if (value is string text) return LuauValue.FromString(text);
        if (value is Vector3 vector) return LuauValue.FromVector(vector);
        if (value is LuauFunction function) return LuauValue.FromFunction(function);
        if (value is LuauTable table) return LuauValue.FromTable(table);
        if (value is LuauBuffer buffer) return LuauValue.FromBuffer(buffer);
        if (value is LuauState state) return LuauValue.FromThread(state);
        if (value is LuauUserData userData) return LuauValue.FromUserData(userData);
        if (value is LuauObjectHandle objectHandle) return LuauValue.FromObjectHandle(objectHandle);

        if (value is byte unsignedByte) return LuauValue.FromInteger(unsignedByte);
        if (value is sbyte signedByte) return LuauValue.FromInteger(signedByte);
        if (value is short signedShort) return LuauValue.FromInteger(signedShort);
        if (value is ushort unsignedShort) return LuauValue.FromInteger(unsignedShort);
        if (value is int signedInteger) return LuauValue.FromInteger(signedInteger);
        if (value is uint unsignedInteger) return LuauValue.FromInteger(unsignedInteger);
        if (value is long signedLong) return LuauValue.FromInteger(signedLong);
        if (value is ulong unsignedLong) return LuauValue.FromInteger(checked((long)unsignedLong));
        if (value is float single) return LuauValue.FromNumber(single);
        if (value is double number) return LuauValue.FromNumber(number);

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
