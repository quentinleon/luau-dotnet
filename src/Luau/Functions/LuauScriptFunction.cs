using System.Runtime.InteropServices;
using Luau.Native;
using static Luau.Native.NativeMethods;

namespace Luau;

internal unsafe sealed class LuauScriptFunction(LuauState state, int reference) : LuauFunction(state), ILuauReference
{
    readonly lua_CFunction cache = (lua_CFunction)Marshal.GetDelegateForFunctionPointer((IntPtr)state.AsPointer(), typeof(lua_CFunction));
    public int Reference => reference;

    public unsafe override lua_CFunction AsCFunction()
    {
        ThrowIfDiposed();
        return cache;
    }

    public unsafe override void* AsPointer()
    {
        ThrowIfDiposed();
        return LuauReferenceHelper.GetRefPointer(State, reference);
    }

    public unsafe override ValueTask<int> InvokeAsync(int argumentCount, CancellationToken cancellationToken = default)
    {
        ThrowIfDiposed();

        var statePtr = State.AsPointer();

        int topBefore = lua_gettop(statePtr);
        var status = lua_pcall(statePtr, argumentCount, -1, 0);

        if (status != (int)lua_Status.LUA_OK)
        {
            var message = State.Pop().Read<string>();
            throw new LuauException(message);
        }

        int topAfter = lua_gettop(statePtr);
        int returnCount = topAfter - topBefore + argumentCount + 1;

        return new ValueTask<int>(returnCount);
    }

    public override string ToString()
    {
        ThrowIfDiposed();
        return LuauReferenceHelper.RefToString(State, Reference);
    }

    protected override void DisposeCore()
    {
        lua_unref(State.AsPointer(), reference);
    }

    ~LuauScriptFunction()
    {
        Dispose();
    }
}