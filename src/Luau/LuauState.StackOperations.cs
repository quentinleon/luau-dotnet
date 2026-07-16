using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using Luau.Native;
using static Luau.Native.NativeMethods;

namespace Luau;

public unsafe partial class LuauState
{
    public void Call(
        int numOfargs,
        int numOfresults,
        LuauExecutionOptions? executionOptions = null)
    {
        ThrowIfDisposed();
        if (numOfargs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numOfargs));
        }

        if (numOfresults < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(numOfresults));
        }

        using var operation = BeginOperation(
            chunkName: null,
            options: executionOptions,
            cancellationToken: default,
            isAsync: false);
        var baseTop = GetTop() - numOfargs - 1;
        if (baseTop < 0)
        {
            throw new InvalidOperationException("The Luau stack does not contain a function and the requested arguments.");
        }

        using var runner = ScriptRunner.Rent();
        var actualResults = runner.RunToStack(operation, numOfargs);

        if (numOfresults < 0 || actualResults == numOfresults)
        {
            return;
        }

        if (actualResults > numOfresults)
        {
            SetTop(baseTop + numOfresults);
            return;
        }

        for (var i = actualResults; i < numOfresults; i++)
        {
            PushNil();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetTop()
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        return lua_gettop(l);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetTop(int top)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        lua_settop(l, top);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetAbsIndex(int index)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        return lua_absindex(l, index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Insert(LuauValue value, int index)
    {
        using var access = EnterNativeAccess();
        Push(value);
        lua_insert(l, index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Insert(int index)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        lua_insert(l, index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Replace(LuauValue value, int index)
    {
        using var access = EnterNativeAccess();
        Push(value);
        lua_replace(l, index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Replace(int index)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        lua_replace(l, index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Remove(int index)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        lua_remove(l, index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CheckStack(int size)
    {
        ThrowIfDisposed();
        if (size < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        using var access = EnterNativeAccess();
        var result = 0;
        LuauNativeProtection.Prepare(context);
        var status = luau_ffi_protected_checkstack(l, size, &result);
        LuauNativeProtection.ThrowIfFailed(this, l, status, "grow the Luau stack");

        if (result == 0)
        {
            throw new InvalidOperationException($"The Luau stack cannot grow by {size} slots.");
        }
    }

    public unsafe LuauType GetLuauType(int index)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();

        var type = lua_type(l, index);

        return MapNativeType((lua_Type)type);
    }

    internal static LuauType MapNativeType(lua_Type luauType)
    {
        switch (luauType)
        {
            case lua_Type.LUA_TNIL: return LuauType.Nil;
            case lua_Type.LUA_TBOOLEAN: return LuauType.Boolean;
            case lua_Type.LUA_TLIGHTUSERDATA: return LuauType.LightUserData;
            case lua_Type.LUA_TNUMBER: return LuauType.Number;
            case lua_Type.LUA_TINTEGER: return LuauType.Integer;
            case lua_Type.LUA_TVECTOR: return LuauType.Vector;
            case lua_Type.LUA_TSTRING: return LuauType.String;
            case lua_Type.LUA_TTABLE: return LuauType.Table;
            case lua_Type.LUA_TFUNCTION: return LuauType.Funciton;
            case lua_Type.LUA_TUSERDATA: return LuauType.UserData;
            case lua_Type.LUA_TTHREAD: return LuauType.Thread;
            case lua_Type.LUA_TBUFFER: return LuauType.Buffer;
            case lua_Type.LUA_TCLASS:
            case lua_Type.LUA_TOBJECT:
                ThrowHelper.ThrowUnsupportedValue(luauType);
                break;
        }

        ThrowHelper.ThrowTypeIsNotSupported(luauType);
        return default; // dummy
    }

    public unsafe LuauValue ToValue(int index)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();

        return ToValueCore(index, (lua_Type)lua_type(l, index));
    }

    /// <summary>
    /// Internal fixture for exercising native tag policy without enabling
    /// upstream class/object libraries in production states.
    /// </summary>
    internal LuauValue ToValueForNativeTypeFixture(int index, lua_Type nativeType)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();

        return ToValueCore(index, nativeType);
    }

    LuauValue ToValueCore(int index, lua_Type luauType)
    {
        switch (luauType)
        {
            case lua_Type.LUA_TNIL:
                return LuauValue.Nil;
            case lua_Type.LUA_TBOOLEAN:
                return LuauValue.FromBoolean(lua_toboolean(l, index) == 1);
            case lua_Type.LUA_TLIGHTUSERDATA:
#pragma warning disable CS0618 // Transitional internal light-userdata protocol.
                return LuauValue.FromLightUserData((IntPtr)lua_tolightuserdata(l, index));
#pragma warning restore CS0618
            case lua_Type.LUA_TNUMBER:
                return LuauValue.FromNumber(lua_tonumber(l, index));
            case lua_Type.LUA_TINTEGER:
                var isInteger = 0;
                var integer = lua_tointeger64(l, index, &isInteger);
                if (isInteger != 1)
                {
                    ThrowHelper.ThrowInvalidOperationException($"The value at {index} is not a 64-bit integer");
                }
                return LuauValue.FromInteger(integer);
            case lua_Type.LUA_TVECTOR:
                var vecPtr = lua_tovector(l, index);
                return LuauValue.FromVector(new(vecPtr[0], vecPtr[1], vecPtr[2]));
            case lua_Type.LUA_TSTRING:
                return LuauValue.FromString(ReadStackString(index, "read a Luau string"));
            case lua_Type.LUA_TTABLE:
                var table = new LuauTable(
                    this,
                    LuauReferenceHelper.CreateReference(this, index, "retain a Luau table"));
                return LuauValue.FromTable(table);
            case lua_Type.LUA_TFUNCTION:
                var function = new LuauScriptFunction(
                    this,
                    LuauReferenceHelper.CreateReference(this, index, "retain a Luau function"));
                return LuauValue.FromFunction(function);
            case lua_Type.LUA_TUSERDATA:
                var userData = new LuauUserData(
                    this,
                    LuauReferenceHelper.CreateReference(this, index, "retain Luau userdata"));
                return LuauValue.FromUserData(userData);
            case lua_Type.LUA_TTHREAD:
                var thread = context.GetOrCreateThread(l, lua_tothread(l, index), index);
                return LuauValue.FromThread(thread);
            case lua_Type.LUA_TBUFFER:
                var buffer = new LuauBuffer(
                    this,
                    LuauReferenceHelper.CreateReference(this, index, "retain a Luau buffer"));
                return LuauValue.FromBuffer(buffer);
            case lua_Type.LUA_TCLASS:
            case lua_Type.LUA_TOBJECT:
                ThrowHelper.ThrowUnsupportedValue(luauType);
                break;
        }

        ThrowHelper.ThrowTypeIsNotSupported(luauType);
        return default; // dummy
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ToBoolean(int index)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        return lua_toboolean(l, index) == 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Obsolete(LuauCompatibilityDiagnostics.NativePointer)]
    public IntPtr ToLightUserData(int index)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        return (IntPtr)lua_tolightuserdata(l, index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ToNumber(int index)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();

        if ((lua_Type)lua_type(l, index) == lua_Type.LUA_TINTEGER)
        {
            int isInteger;
            var integer = lua_tointeger64(l, index, &isInteger);
            if (isInteger != 1)
            {
                ThrowHelper.ThrowInvalidOperationException($"The value at {index} is not a number or integer");
            }

            if (!MathEx.TryConvertToDoubleExact(integer, out var exact))
            {
                ThrowHelper.ThrowInvalidOperationException(
                    $"The integer at {index} cannot be represented exactly as a double. Use {nameof(ToNumberLossy)} for an explicit lossy conversion.");
            }

            return exact;
        }

        int isNum;
        var result = lua_tonumberx(l, index, &isNum);

        if (isNum != 1)
        {
            ThrowHelper.ThrowInvalidOperationException($"The value at {index} is not a number");
        }

        return result;
    }

    /// <summary>
    /// Converts a native number or 64-bit integer to <see cref="double"/>,
    /// explicitly allowing integer precision loss.
    /// </summary>
    public double ToNumberLossy(int index)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();

        if ((lua_Type)lua_type(l, index) == lua_Type.LUA_TINTEGER)
        {
            int isInteger;
            var integer = lua_tointeger64(l, index, &isInteger);
            if (isInteger != 1)
            {
                ThrowHelper.ThrowInvalidOperationException($"The value at {index} is not a number or integer");
            }

            return integer;
        }

        int isNum;
        var result = lua_tonumberx(l, index, &isNum);
        if (isNum != 1)
        {
            ThrowHelper.ThrowInvalidOperationException($"The value at {index} is not a number or integer");
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ToInteger(int index)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();

        if ((lua_Type)lua_type(l, index) == lua_Type.LUA_TINTEGER)
        {
            int isInteger;
            var integer = lua_tointeger64(l, index, &isInteger);
            if (isInteger != 1 || integer < int.MinValue || integer > int.MaxValue)
            {
                ThrowHelper.ThrowInvalidOperationException($"The value at {index} is outside the Int32 range");
            }

            return (int)integer;
        }

        int isNum;
        var result = lua_tointegerx(l, index, &isNum);

        if (isNum != 1)
        {
            ThrowHelper.ThrowInvalidOperationException($"The value at {index} is not a integer");
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ToInteger64(int index)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();

        if ((lua_Type)lua_type(l, index) == lua_Type.LUA_TINTEGER)
        {
            int isInteger;
            var integer = lua_tointeger64(l, index, &isInteger);
            if (isInteger != 1)
            {
                ThrowHelper.ThrowInvalidOperationException($"The value at {index} is not a 64-bit integer");
            }

            return integer;
        }

        int isNum;
        var number = lua_tonumberx(l, index, &isNum);
        if (isNum != 1 || !MathEx.IsInt64(number))
        {
            ThrowHelper.ThrowInvalidOperationException($"The value at {index} is not an exact 64-bit integer");
        }

        return (long)number;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ToUnsigned(int index)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();

        if ((lua_Type)lua_type(l, index) == lua_Type.LUA_TINTEGER)
        {
            int isInteger;
            var integer = lua_tointeger64(l, index, &isInteger);
            if (isInteger != 1 || integer < uint.MinValue || integer > uint.MaxValue)
            {
                ThrowHelper.ThrowInvalidOperationException($"The value at {index} is outside the UInt32 range");
            }

            return (uint)integer;
        }

        int isNum;
        var result = lua_tounsignedx(l, index, &isNum);

        if (isNum != 1)
        {
            ThrowHelper.ThrowInvalidOperationException($"The value at {index} is not an unsigned integer");
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3 ToVector(int index)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();

        var ptr = lua_tovector(l, index);
        return new(ptr[0], ptr[1], ptr[2]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ToString(int index)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        return ReadStackString(index, "convert a Luau value to a string");
    }

    string ReadStackString(int index, string operation)
    {
        byte* text = null;
        nuint length = 0;

        LuauNativeProtection.Prepare(context);
        var status = luau_ffi_protected_tolstring(l, index, &text, &length);
        LuauNativeProtection.ThrowIfFailed(this, l, status, operation);

        if (text == null)
        {
            throw new InvalidOperationException($"The value at {index} cannot be converted to a string.");
        }

        if (length > int.MaxValue)
        {
            throw new InvalidOperationException("The Luau string is too large for a managed string.");
        }

        return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(text, (int)length));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuauTable ToTable(int index)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        return new LuauTable(
            this,
            LuauReferenceHelper.CreateReference(this, index, "retain a Luau table"));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuauFunction ToFunction(int index)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        return new LuauScriptFunction(
            this,
            LuauReferenceHelper.CreateReference(this, index, "retain a Luau function"));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuauState ToThread(int index)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        return context.GetOrCreateThread(l, lua_tothread(l, index), index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuauBuffer ToBuffer(int index)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        return new LuauBuffer(
            this,
            LuauReferenceHelper.CreateReference(this, index, "retain a Luau buffer"));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuauUserData ToUserData(int index)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        return new LuauUserData(
            this,
            LuauReferenceHelper.CreateReference(this, index, "retain Luau userdata"));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T ToUserData<T>(int index)
        where T : unmanaged
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        return *(T*)lua_touserdata(l, index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Obsolete(LuauCompatibilityDiagnostics.NativeCallback)]
    public lua_CFunction ToCFunction(int index)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        return lua_tocfunction(l, index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Obsolete(LuauCompatibilityDiagnostics.NativePointer)]
    public void* ToPointer(int index)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        return lua_topointer(l, index);
    }

    public void Push(LuauValue value)
    {
        switch (value.Type)
        {
            case LuauType.Nil:
                PushNil();
                break;
            case LuauType.Boolean:
                PushBoolean(value.Read<bool>());
                break;
            case LuauType.LightUserData:
#pragma warning disable CS0618 // Transitional runtime call; the public pointer API remains unsupported.
                PushLightUserData(value.Read<IntPtr>().ToPointer());
#pragma warning restore CS0618
                break;
            case LuauType.Number:
                PushNumber(value.Read<double>());
                break;
            case LuauType.Integer:
                PushInteger(value.Read<long>());
                break;
            case LuauType.Vector:
                PushVector(value.Read<Vector3>());
                break;
            case LuauType.String:
                PushString(value.Read<string>());
                break;
            case LuauType.Table:
                PushTable(value.Read<LuauTable>());
                break;
            case LuauType.Funciton:
                PushFunction(value.Read<LuauFunction>());
                break;
            case LuauType.UserData:
                PushUserData(value.Read<LuauUserData>());
                break;
            case LuauType.Thread:
                PushThread(value.Read<LuauState>());
                break;
            case LuauType.Buffer:
                PushBuffer(value.Read<LuauBuffer>());
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushNil()
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        LuauNativeProtection.Prepare(context);
        var status = luau_ffi_protected_pushnil(l);
        LuauNativeProtection.ThrowIfFailed(this, l, status, "push nil onto the Luau stack");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushBoolean(bool value)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        LuauNativeProtection.Prepare(context);
        var status = luau_ffi_protected_pushboolean(l, value ? 1 : 0);
        LuauNativeProtection.ThrowIfFailed(this, l, status, "push a boolean onto the Luau stack");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Obsolete(LuauCompatibilityDiagnostics.NativePointer)]
    public void PushLightUserData(void* value)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        LuauNativeProtection.Prepare(context);
        var status = luau_ffi_protected_pushlightuserdatatagged(l, value, 0);
        LuauNativeProtection.ThrowIfFailed(this, l, status, "push light userdata onto the Luau stack");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushInteger(int value)
    {
        PushInteger((long)value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushInteger(long value)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        LuauNativeProtection.Prepare(context);
        var status = luau_ffi_protected_pushinteger64(l, value);
        LuauNativeProtection.ThrowIfFailed(this, l, status, "push a 64-bit integer onto the Luau stack");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushUnsigned(uint value)
    {
        PushInteger((long)value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushNumber(double value)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        LuauNativeProtection.Prepare(context);
        var status = luau_ffi_protected_pushnumber(l, value);
        LuauNativeProtection.ThrowIfFailed(this, l, status, "push a number onto the Luau stack");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushVector(Vector3 value)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        LuauNativeProtection.Prepare(context);
        var status = luau_ffi_protected_pushvector(l, value.X, value.Y, value.Z);
        LuauNativeProtection.ThrowIfFailed(this, l, status, "push a vector onto the Luau stack");
    }

    public void PushString(string value)
    {
        ThrowIfDisposed();
        if (value == null) throw new ArgumentNullException(nameof(value));

        var byteCount = Encoding.UTF8.GetByteCount(value);
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, byteCount));
        try
        {
            var utf8Count = Encoding.UTF8.GetBytes(value, buffer);
            using var access = EnterNativeAccess();
            fixed (byte* stringPtr = buffer)
            {
                LuauNativeProtection.Prepare(context);
                var status = luau_ffi_protected_pushlstring(l, stringPtr, (nuint)utf8Count);
                LuauNativeProtection.ThrowIfFailed(this, l, status, "push a string onto the Luau stack");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void PushString(ReadOnlySpan<byte> utf8Value)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        fixed (byte* stringPtr = utf8Value)
        {
            LuauNativeProtection.Prepare(context);
            var status = luau_ffi_protected_pushlstring(l, stringPtr, (nuint)utf8Value.Length);
            LuauNativeProtection.ThrowIfFailed(this, l, status, "push a string onto the Luau stack");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushTable(LuauTable value)
    {
        PushReference(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushThread(LuauState value)
    {
        PushReference(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushBuffer(LuauBuffer value)
    {
        PushReference(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushUserData(LuauUserData value)
    {
        PushReference(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushUserData<T>(T value)
        where T : unmanaged
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        void* rawPointer = null;
        LuauNativeProtection.Prepare(context);
        var status = luau_ffi_protected_newuserdatatagged(
            l,
            (nuint)sizeof(T),
            0,
            &rawPointer);
        LuauNativeProtection.ThrowIfFailed(this, l, status, "create Luau userdata");

        var ptr = (T*)rawPointer;
        *ptr = value;
    }

    public void PushFunction(LuauFunction value)
    {
        ThrowIfDisposed();
        using var functionAccess = value.AcquireForPush();
        using var access = EnterNativeAccess();
        if (value is LuauScriptFunction scriptFunc)
        {
            PushReference(scriptFunc);
        }
        else
        {
            // Managed callbacks carry a full-userdata registration token so
            // Luau GC can release the managed delegate as soon as the native
            // closure becomes unreachable. Custom LuauFunction subclasses keep
            // the legacy light-userdata upvalue contract.
            if (value is ILuauManagedCallbackFunction managedCallback)
            {
                if (!ReferenceEquals(functionAccess.State.Context, context))
                {
                    throw new InvalidOperationException("Cannot push a managed callback from another Luau VM.");
                }

                var registrationId = managedCallback.RegistrationId;
                if (registrationId == 0)
                {
                    ThrowHelper.ThrowObjectDisposedException(nameof(LuauFunction));
                }

                void* rawToken = null;
                LuauNativeProtection.Prepare(context);
                var tokenStatus = luau_ffi_protected_newuserdatadtor(
                    l,
                    (nuint)sizeof(int),
                    LuauManagedCallbackLifetime.Destructor,
                    &rawToken);
                LuauNativeProtection.ThrowIfFailed(
                    this,
                    l,
                    tokenStatus,
                    "create a managed callback registration token");

                var token = (int*)rawToken;
                *token = 0;
                context.AddManagedCallbackNativeReference(registrationId);
                *token = registrationId;
            }
            else
            {
                LuauNativeProtection.Prepare(context);
#pragma warning disable CS0618 // Transitional runtime call until Stage 4 internalizes native callback plumbing.
                var tokenStatus = luau_ffi_protected_pushlightuserdatatagged(
                    l,
                    value.AsPointer(),
                    0);
#pragma warning restore CS0618
                LuauNativeProtection.ThrowIfFailed(
                    this,
                    l,
                    tokenStatus,
                    "push a managed callback token");
            }

            LuauNativeProtection.Prepare(context);
#pragma warning disable CS0618 // Transitional runtime call until Stage 4 internalizes native callback plumbing.
            var closureStatus = luau_ffi_protected_pushcclosurek(
                l,
                value.AsCFunction(),
                null,
                1,
                null);
#pragma warning restore CS0618
            LuauNativeProtection.ThrowIfFailed(
                this,
                l,
                closureStatus,
                "create a managed callback closure");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Obsolete(LuauCompatibilityDiagnostics.NativeCallback)]
    public void PushCFunction(lua_CFunction value, ReadOnlySpan<byte> debugName = default)
    {
        ThrowIfDisposed();

        if (debugName.IsEmpty)
        {
            using var access = EnterNativeAccess();
            LuauNativeProtection.Prepare(context);
            var status = luau_ffi_protected_pushcclosurek(l, value, null, 0, null);
            LuauNativeProtection.ThrowIfFailed(this, l, status, "create a native callback closure");
        }
        else
        {
            if (debugName.IndexOf((byte)0) >= 0)
            {
                throw new ArgumentException("Debug names cannot contain a NUL byte.", nameof(debugName));
            }

            var buffer = ArrayPool<byte>.Shared.Rent(checked(debugName.Length + 1));
            try
            {
                debugName.CopyTo(buffer);
                buffer[debugName.Length] = 0;
                using var access = EnterNativeAccess();
                fixed (byte* d = buffer)
                {
                    LuauNativeProtection.Prepare(context);
                    var status = luau_ffi_protected_pushcclosurek(l, value, d, 0, null);
                    LuauNativeProtection.ThrowIfFailed(this, l, status, "create a native callback closure");
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Obsolete(LuauCompatibilityDiagnostics.NativeCallback)]
    public void PushCClosure(lua_CFunction value, ReadOnlySpan<byte> debugName = default, int upvalues = 0)
    {
        ThrowIfDisposed();
        if (upvalues < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(upvalues));
        }

        if (debugName.IsEmpty)
        {
            using var access = EnterNativeAccess();
            if (lua_gettop(l) < upvalues)
            {
                throw new InvalidOperationException("The Luau stack does not contain the requested closure upvalues.");
            }

            LuauNativeProtection.Prepare(context);
            var status = luau_ffi_protected_pushcclosurek(l, value, null, upvalues, null);
            LuauNativeProtection.ThrowIfFailed(this, l, status, "create a native callback closure");
        }
        else
        {
            if (debugName.IndexOf((byte)0) >= 0)
            {
                throw new ArgumentException("Debug names cannot contain a NUL byte.", nameof(debugName));
            }

            var buffer = ArrayPool<byte>.Shared.Rent(checked(debugName.Length + 1));
            try
            {
                debugName.CopyTo(buffer);
                buffer[debugName.Length] = 0;
                using var access = EnterNativeAccess();
                fixed (byte* d = buffer)
                {
                    if (lua_gettop(l) < upvalues)
                    {
                        throw new InvalidOperationException("The Luau stack does not contain the requested closure upvalues.");
                    }

                    LuauNativeProtection.Prepare(context);
                    var status = luau_ffi_protected_pushcclosurek(l, value, d, upvalues, null);
                    LuauNativeProtection.ThrowIfFailed(this, l, status, "create a native callback closure");
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    void PushReference<T>(T value)
        where T : ILuauReference
    {
        ThrowIfDisposed();
        using var referenceAccess = value.AcquireReference();
        if (!ReferenceEquals(referenceAccess.State.Context, context))
        {
            throw new InvalidOperationException("Cannot push a reference from another Luau VM.");
        }

        LuauReferenceHelper.PushReference(
            this,
            referenceAccess.Reference,
            "push a managed Luau reference");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuauValue Pop()
    {
        using var access = EnterNativeAccess();
        var value = ToValue(-1);
        lua_pop(l, 1);
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Pop(int n)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        lua_pop(l, n);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Pop(int n, Span<LuauValue> destination)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();

        if (destination.Length < n)
        {
            ThrowHelper.ThrowArgumentException(nameof(destination), "Destination is too short");
        }

        var top = lua_gettop(l);
        for (int i = 0; i < n; i++)
        {
            destination[i] = ToValue(top - i);
        }

        lua_pop(l, n);
    }

    public void XMove(LuauState destination, int n)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        using var access = EnterNativeAccess();
        destination.ThrowIfDisposed();
        if (!ReferenceEquals(context, destination.context))
        {
            throw new InvalidOperationException("Cannot move values between independent Luau VMs.");
        }

        var from = l;
        var to = destination.l;
        lua_xmove(from, to, n);
    }
}
