using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Luau.Native;
using static Luau.Native.NativeMethods;

namespace Luau;

public unsafe partial class LuauState
{
    public void Call(int numOfargs, int numOfresults)
    {
        ThrowIfDisposed();
        var status = lua_pcall(l, numOfargs, numOfresults, 0);

        if (status != (int)lua_Status.LUA_OK)
        {
            var message = ToString(-1);
            lua_pop(l, 1);
            throw new LuauException(message);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetTop()
    {
        ThrowIfDisposed();
        return lua_gettop(l);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetTop(int top)
    {
        ThrowIfDisposed();
        lua_settop(l, top);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetAbsIndex(int index)
    {
        ThrowIfDisposed();
        return lua_absindex(l, index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Insert(LuauValue value, int index)
    {
        Push(value);
        lua_insert(l, index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Insert(int index)
    {
        ThrowIfDisposed();
        lua_insert(l, index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Replace(LuauValue value, int index)
    {
        Push(value);
        lua_replace(l, index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Replace(int index)
    {
        ThrowIfDisposed();
        lua_replace(l, index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Remove(int index)
    {
        ThrowIfDisposed();
        lua_remove(l, index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CheckStack(int size)
    {
        ThrowIfDisposed();
        lua_checkstack(l, size);
    }

    public unsafe LuauType GetLuauType(int index)
    {
        ThrowIfDisposed();

        var type = lua_type(l, index);

        var luauType = (lua_Type)type;
        switch (luauType)
        {
            case lua_Type.LUA_TNIL: return LuauType.Nil;
            case lua_Type.LUA_TBOOLEAN: return LuauType.Boolean;
            case lua_Type.LUA_TLIGHTUSERDATA: return LuauType.LightUserData;
            case lua_Type.LUA_TNUMBER: return LuauType.Number;
            case lua_Type.LUA_TVECTOR: return LuauType.Vector;
            case lua_Type.LUA_TSTRING: return LuauType.String;
            case lua_Type.LUA_TTABLE: return LuauType.Table;
            case lua_Type.LUA_TFUNCTION: return LuauType.Funciton;
            case lua_Type.LUA_TUSERDATA: return LuauType.UserData;
            case lua_Type.LUA_TTHREAD: return LuauType.Thread;
            case lua_Type.LUA_TBUFFER: return LuauType.Buffer;
        }

        ThrowHelper.ThrowTypeIsNotSupported(luauType);
        return default; // dummy
    }

    public unsafe LuauValue ToValue(int index)
    {
        ThrowIfDisposed();

        var type = lua_type(l, index);

        var luauType = (lua_Type)type;
        switch (luauType)
        {
            case lua_Type.LUA_TNIL:
                return LuauValue.Nil;
            case lua_Type.LUA_TBOOLEAN:
                return LuauValue.FromBoolean(lua_toboolean(l, index) == 1);
            case lua_Type.LUA_TLIGHTUSERDATA:
                return LuauValue.FromLightUserData((IntPtr)lua_tolightuserdata(l, index));
            case lua_Type.LUA_TNUMBER:
                return LuauValue.FromNumber(lua_tonumber(l, index));
            case lua_Type.LUA_TVECTOR:
                var vecPtr = lua_tovector(l, index);
                return LuauValue.FromVector(new(vecPtr[0], vecPtr[1], vecPtr[2]));
            case lua_Type.LUA_TSTRING:
                var str = Marshal.PtrToStringAuto((IntPtr)lua_tostring(l, index));
                return LuauValue.FromString(str!);
            case lua_Type.LUA_TTABLE:
                var table = new LuauTable(this, lua_ref(l, index));
                return LuauValue.FromTable(table);
            case lua_Type.LUA_TFUNCTION:
                var function = new LuauScriptFunction(this, lua_ref(l, index));
                return LuauValue.FromFunction(function);
            case lua_Type.LUA_TUSERDATA:
                var userData = new LuauUserData(this, lua_ref(l, index));
                return LuauValue.FromUserData(userData);
            case lua_Type.LUA_TTHREAD:
                var thread = GetCachedState(lua_tothread(l, index));
                return LuauValue.FromThread(thread);
            case lua_Type.LUA_TBUFFER:
                var buffer = new LuauBuffer(this, lua_ref(l, index));
                return LuauValue.FromBuffer(buffer);
        }

        ThrowHelper.ThrowTypeIsNotSupported(luauType);
        return default; // dummy
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ToBoolean(int index)
    {
        ThrowIfDisposed();
        return lua_toboolean(l, index) == 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IntPtr ToLightUserData(int index)
    {
        ThrowIfDisposed();
        return (IntPtr)lua_tolightuserdata(l, index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ToNumber(int index)
    {
        ThrowIfDisposed();

        int isNum;
        var result = lua_tonumberx(l, index, &isNum);

        if (isNum != 1)
        {
            ThrowHelper.ThrowInvalidOperationException($"The value at {index} is not a number");
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ToInteger(int index)
    {
        ThrowIfDisposed();

        int isNum;
        var result = lua_tointegerx(l, index, &isNum);

        if (isNum != 1)
        {
            ThrowHelper.ThrowInvalidOperationException($"The value at {index} is not a integer");
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ToUnsigned(int index)
    {
        ThrowIfDisposed();

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

        var ptr = lua_tovector(l, index);
        return new(ptr[0], ptr[1], ptr[2]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ToString(int index)
    {
        ThrowIfDisposed();
        return Marshal.PtrToStringAuto((IntPtr)lua_tostring(l, index))!;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuauTable ToTable(int index)
    {
        ThrowIfDisposed();
        return new LuauTable(this, lua_ref(l, index));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuauFunction ToFunction(int index)
    {
        ThrowIfDisposed();
        return new LuauScriptFunction(this, lua_ref(l, index));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuauState ToThread(int index)
    {
        ThrowIfDisposed();
        return GetCachedState(lua_tothread(l, index));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuauBuffer ToBuffer(int index)
    {
        ThrowIfDisposed();
        return new LuauBuffer(this, lua_ref(l, index));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuauUserData ToUserData(int index)
    {
        ThrowIfDisposed();
        return new LuauUserData(this, lua_ref(l, index));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T ToUserData<T>(int index)
        where T : unmanaged
    {
        ThrowIfDisposed();
        return *(T*)lua_touserdata(l, index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public lua_CFunction ToCFunction(int index)
    {
        ThrowIfDisposed();
        return lua_tocfunction(l, index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void* ToPointer(int index)
    {
        ThrowIfDisposed();
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
                PushLightUserData(value.Read<IntPtr>().ToPointer());
                break;
            case LuauType.Number:
                PushNumber(value.Read<double>());
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
        lua_pushnil(l);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushBoolean(bool value)
    {
        ThrowIfDisposed();
        lua_pushboolean(l, value ? 1 : 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushLightUserData(void* value)
    {
        ThrowIfDisposed();
        lua_pushlightuserdata(l, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushInteger(int value)
    {
        ThrowIfDisposed();
        lua_pushinteger(l, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushUnsigned(uint value)
    {
        ThrowIfDisposed();
        lua_pushunsigned(l, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushNumber(double value)
    {
        ThrowIfDisposed();
        lua_pushnumber(l, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushVector(Vector3 value)
    {
        ThrowIfDisposed();
        lua_pushvector(l, value.X, value.Y, value.Z);
    }

    public void PushString(string value)
    {
        ThrowIfDisposed();

        var buffer = ArrayPool<byte>.Shared.Rent(value.Length * 3);
        try
        {
            var utf8Count = Encoding.UTF8.GetBytes(value, buffer);
#if NET6_0_OR_GREATER
            var stringPtr = (byte*)NativeMemory.Alloc((nuint)(utf8Count + 1));
#else
            var stringPtr = (byte*)Marshal.AllocHGlobal(utf8Count + 1).ToPointer();
#endif
            buffer.AsSpan(0, utf8Count).CopyTo(new Span<byte>(stringPtr, utf8Count));
            stringPtr[utf8Count] = 0;
            lua_pushstring(l, stringPtr);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void PushString(ReadOnlySpan<byte> utf8Value)
    {
#if NET6_0_OR_GREATER
        var stringPtr = (byte*)NativeMemory.Alloc((nuint)utf8Value.Length);
#else
        var stringPtr = (byte*)Marshal.AllocHGlobal(utf8Value.Length).ToPointer();
#endif
        utf8Value.CopyTo(new Span<byte>(stringPtr, utf8Value.Length));
        lua_pushstring(l, stringPtr);
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
        var ptr = (T*)lua_newuserdata(l, (nuint)sizeof(T));
        *ptr = value;
    }

    public void PushFunction(LuauFunction value)
    {
        if (value is LuauScriptFunction scriptFunc)
        {
            PushReference(scriptFunc);
        }
        else
        {
            ThrowIfDisposed();

            // Create C-Closure
            lua_pushlightuserdata(l, value.AsPointer());
            lua_pushcclosure(l, value.AsCFunction(), null, 1);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushCFunction(lua_CFunction value, ReadOnlySpan<byte> debugName = default)
    {
        ThrowIfDisposed();

        if (debugName.IsEmpty)
        {
            lua_pushcfunction(l, value, null);
        }
        else
        {
            fixed (byte* d = debugName)
            {
                lua_pushcfunction(l, value, d);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushCClosure(lua_CFunction value, ReadOnlySpan<byte> debugName = default, int upvalues = 0)
    {
        ThrowIfDisposed();

        if (debugName.IsEmpty)
        {
            lua_pushcclosure(l, value, null, upvalues);
        }
        else
        {
            fixed (byte* d = debugName)
            {
                lua_pushcclosure(l, value, d, upvalues);
            }
        }
    }

    void PushReference<T>(T value)
        where T : ILuauReference
    {
        if (value.IsDisposed) ThrowHelper.ThrowObjectDisposedException(nameof(T));
        if (value.State != this)
        {
            throw new InvalidOperationException("Cannot push the table to another LuauState");
        }

        lua_getref(l, value.Reference);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuauValue Pop()
    {
        var value = ToValue(-1);
        lua_pop(l, 1);
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Pop(int n)
    {
        ThrowIfDisposed();
        lua_pop(l, n);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Pop(int n, Span<LuauValue> destination)
    {
        ThrowIfDisposed();

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
        var from = AsPointer();
        var to = destination.AsPointer();
        lua_xmove(from, to, n);
    }
}