using System.Runtime.InteropServices;

namespace Luau.Native
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void* lua_Alloc(void* ud, void* ptr, nuint osize, nuint nsize);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate int lua_CFunction(lua_State* L);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate int lua_Continuation(lua_State* L, int status);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void lua_Destructor(lua_State* L, void* userdata);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void lua_Coverage(void* context, byte* function, int linedefined, int depth, int* hits, nuint size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void lua_UserdataDestructor(void* userdata);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void lua_CounterFunction(void* context, byte* function, int linedefined);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void lua_CounterValue(void* context, int kind, int line, ulong hits);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void lua_UserdataDirectAccess(lua_State* L, void* data, int atom, ushort* cachedslot, int utag);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate int lua_UserdataDirectNamecall(lua_State* L, void* data, int atom, ushort* cachedslot, int utag);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void lua_UserdataDirectFieldGet(void* userdata, void* result);
}
