#pragma warning disable IDE1006

namespace Luau.Native
{
    public enum lua_Status
    {
        LUA_OK = 0,
        LUA_YIELD,
        LUA_ERRRUN,
        LUA_ERRSYNTAX, // legacy error code, preserved for compatibility
        LUA_ERRMEM,
        LUA_ERRERR,
        LUA_BREAK, // yielded for a debug breakpoint
    };

    public enum lua_CoStatus
    {
        LUA_CORUN = 0, // running
        LUA_COSUS,     // suspended
        LUA_CONOR,     // 'normal' (it resumed another coroutine)
        LUA_COFIN,     // finished
        LUA_COERR,     // finished with error
    };

    public enum lua_Type
    {
        LUA_TNIL = 0,     // must be 0 due to lua_isnoneornil
        LUA_TBOOLEAN = 1, // must be 1 due to l_isfalse

        LUA_TLIGHTUSERDATA,
        LUA_TNUMBER,
        LUA_TVECTOR,

        LUA_TSTRING, // all types above this must be value types, all types below this must be GC types - see iscollectable

        LUA_TTABLE,
        LUA_TFUNCTION,
        LUA_TUSERDATA,
        LUA_TTHREAD,
        LUA_TBUFFER,

        // values below this line are used in GCObject tags but may never show up in TValue type tags
        LUA_TPROTO,
        LUA_TUPVAL,
        LUA_TDEADKEY,

        // the count of TValue type tags
        LUA_T_COUNT = LUA_TPROTO
    };

    public enum luarequire_NavigateResult
    {
        NAVIGATE_SUCCESS,
        NAVIGATE_AMBIGUOUS,
        NAVIGATE_NOT_FOUND
    };

    public enum luarequire_WriteResult
    {
        WRITE_SUCCESS,
        WRITE_BUFFER_TOO_SMALL,
        WRITE_FAILURE
    };
}

#pragma warning restore IDE1006