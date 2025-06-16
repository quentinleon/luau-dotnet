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
    public unsafe delegate void lua_Coverage(void* context, char* function, int linedefined, int depth, int* hits, nuint size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void luarequire_Configuration_init(luarequire_Configuration* config);
}