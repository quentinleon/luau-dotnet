use super::luau::{lua_CFunction, lua_CompileOptions, lua_Continuation, lua_State};
use std::os::raw::{c_char, c_double, c_float, c_int, c_uint, c_void};

pub type luau_ffi_protected_destructor =
    ::std::option::Option<unsafe extern "C" fn(userdata: *mut c_void)>;

pub const LUAU_PROTECTED_COMPILE_OK: u32 = 0;
pub const LUAU_PROTECTED_COMPILE_OUT_OF_MEMORY: u32 = 1;
pub const LUAU_PROTECTED_COMPILE_ERROR: u32 = 2;
pub const LUAU_ABI_INFO_OK: u32 = 0;
pub const LUAU_ABI_INFO_BUFFER_TOO_SMALL: u32 = 1;
pub const LUAU_ABI_INFO_INVALID_ARGUMENT: u32 = 2;

#[repr(C)]
#[derive(Debug, Copy, Clone)]
pub struct luau_ffi_abi_info_v2 {
    pub struct_size: u32,
    pub protected_abi_version: u32,
    pub pointer_size: u8,
    pub size_t_size: u8,
    pub little_endian: u8,
    pub reserved0: u8,
    pub compile_options_size: u32,
    pub callbacks_size: u32,
    pub type_nil: i32,
    pub type_boolean: i32,
    pub type_lightuserdata: i32,
    pub type_number: i32,
    pub type_vector: i32,
    pub type_string: i32,
    pub type_table: i32,
    pub type_function: i32,
    pub type_userdata: i32,
    pub type_thread: i32,
    pub type_buffer: i32,
    pub type_integer: i32,
    pub type_class: i32,
    pub type_object: i32,
}

unsafe extern "C" {
    pub fn luau_ffi_protected_abi_version() -> c_int;
    pub fn luau_ffi_protected_abi_info_v2(
        info: *mut luau_ffi_abi_info_v2,
        infoSize: u32,
    ) -> c_int;
    pub fn luau_ffi_protected_compile(
        source: *const c_char,
        size: usize,
        options: *mut lua_CompileOptions,
        output: *mut *mut c_char,
        outputSize: *mut usize,
    ) -> c_int;
    pub fn luau_ffi_protected_checkstack(
        L: *mut lua_State,
        size: c_int,
        result: *mut c_int,
    ) -> c_int;
    pub fn luau_ffi_protected_newthread(L: *mut lua_State, result: *mut *mut lua_State) -> c_int;
    pub fn luau_ffi_protected_resetthread(L: *mut lua_State) -> c_int;

    pub fn luau_ffi_protected_install_interrupt(L: *mut lua_State, poll: *mut c_void) -> c_int;
    pub fn luau_ffi_protected_uninstall_interrupt(L: *mut lua_State);

    pub fn luau_ffi_protected_pushvalue(L: *mut lua_State, index: c_int) -> c_int;
    pub fn luau_ffi_protected_pushnil(L: *mut lua_State) -> c_int;
    pub fn luau_ffi_protected_pushboolean(L: *mut lua_State, value: c_int) -> c_int;
    pub fn luau_ffi_protected_pushinteger(L: *mut lua_State, value: c_int) -> c_int;
    pub fn luau_ffi_protected_pushinteger64(L: *mut lua_State, value: i64) -> c_int;
    pub fn luau_ffi_protected_pushunsigned(L: *mut lua_State, value: c_uint) -> c_int;
    pub fn luau_ffi_protected_pushnumber(L: *mut lua_State, value: c_double) -> c_int;
    pub fn luau_ffi_protected_pushvector(
        L: *mut lua_State,
        x: c_float,
        y: c_float,
        z: c_float,
    ) -> c_int;
    pub fn luau_ffi_protected_pushlstring(
        L: *mut lua_State,
        value: *const c_char,
        length: usize,
    ) -> c_int;
    pub fn luau_ffi_protected_pushcclosurek(
        L: *mut lua_State,
        function: lua_CFunction,
        debugName: *const c_char,
        upvalues: c_int,
        continuation: lua_Continuation,
    ) -> c_int;
    pub fn luau_ffi_protected_pushlightuserdatatagged(
        L: *mut lua_State,
        pointer: *mut c_void,
        tag: c_int,
    ) -> c_int;
    pub fn luau_ffi_protected_pushthread(L: *mut lua_State, result: *mut c_int) -> c_int;

    pub fn luau_ffi_protected_newuserdatatagged(
        L: *mut lua_State,
        size: usize,
        tag: c_int,
        result: *mut *mut c_void,
    ) -> c_int;
    pub fn luau_ffi_protected_newuserdatadtor(
        L: *mut lua_State,
        size: usize,
        destructor: luau_ffi_protected_destructor,
        result: *mut *mut c_void,
    ) -> c_int;
    pub fn luau_ffi_protected_newbuffer(
        L: *mut lua_State,
        size: usize,
        result: *mut *mut c_void,
    ) -> c_int;

    pub fn luau_ffi_protected_gettable(
        L: *mut lua_State,
        index: c_int,
        result: *mut c_int,
    ) -> c_int;
    pub fn luau_ffi_protected_getfield(
        L: *mut lua_State,
        index: c_int,
        key: *const c_char,
        result: *mut c_int,
    ) -> c_int;
    pub fn luau_ffi_protected_rawgetfield(
        L: *mut lua_State,
        index: c_int,
        key: *const c_char,
        result: *mut c_int,
    ) -> c_int;
    pub fn luau_ffi_protected_rawget(L: *mut lua_State, index: c_int, result: *mut c_int) -> c_int;
    pub fn luau_ffi_protected_rawgeti(
        L: *mut lua_State,
        index: c_int,
        item: c_int,
        result: *mut c_int,
    ) -> c_int;
    pub fn luau_ffi_protected_next(L: *mut lua_State, index: c_int, result: *mut c_int) -> c_int;
    pub fn luau_ffi_protected_createtable(
        L: *mut lua_State,
        arraySize: c_int,
        recordSize: c_int,
    ) -> c_int;
    pub fn luau_ffi_protected_getmetatable(
        L: *mut lua_State,
        index: c_int,
        result: *mut c_int,
    ) -> c_int;
    pub fn luau_ffi_protected_getfenv(L: *mut lua_State, index: c_int) -> c_int;

    pub fn luau_ffi_protected_settable(L: *mut lua_State, index: c_int) -> c_int;
    pub fn luau_ffi_protected_setfield(
        L: *mut lua_State,
        index: c_int,
        key: *const c_char,
    ) -> c_int;
    pub fn luau_ffi_protected_rawsetfield(
        L: *mut lua_State,
        index: c_int,
        key: *const c_char,
    ) -> c_int;
    pub fn luau_ffi_protected_rawset(L: *mut lua_State, index: c_int) -> c_int;
    pub fn luau_ffi_protected_rawseti(L: *mut lua_State, index: c_int, item: c_int) -> c_int;
    pub fn luau_ffi_protected_setmetatable(
        L: *mut lua_State,
        index: c_int,
        result: *mut c_int,
    ) -> c_int;
    pub fn luau_ffi_protected_setfenv(L: *mut lua_State, index: c_int, result: *mut c_int)
        -> c_int;

    pub fn luau_ffi_protected_load(
        L: *mut lua_State,
        chunkName: *const c_char,
        bytecode: *const c_char,
        size: usize,
        environment: c_int,
        result: *mut c_int,
    ) -> c_int;
    pub fn luau_ffi_protected_gc(
        L: *mut lua_State,
        operation: c_int,
        data: c_int,
        result: *mut c_int,
    ) -> c_int;
    pub fn luau_ffi_protected_concat(L: *mut lua_State, count: c_int) -> c_int;
    pub fn luau_ffi_protected_clonefunction(L: *mut lua_State, index: c_int) -> c_int;
    pub fn luau_ffi_protected_cleartable(L: *mut lua_State, index: c_int) -> c_int;
    pub fn luau_ffi_protected_clonetable(L: *mut lua_State, index: c_int) -> c_int;
    pub fn luau_ffi_protected_ref(L: *mut lua_State, index: c_int, result: *mut c_int) -> c_int;

    pub fn luau_ffi_protected_tolstring(
        L: *mut lua_State,
        index: c_int,
        result: *mut *const c_char,
        length: *mut usize,
    ) -> c_int;
    pub fn luau_ffi_protected_luaL_tolstring(
        L: *mut lua_State,
        index: c_int,
        result: *mut *const c_char,
        length: *mut usize,
    ) -> c_int;

    pub fn luau_ffi_protected_openlibrary(
        L: *mut lua_State,
        library: c_int,
        result: *mut c_int,
    ) -> c_int;
    pub fn luau_ffi_protected_openlibs(L: *mut lua_State) -> c_int;
    pub fn luau_ffi_protected_sandbox(L: *mut lua_State) -> c_int;
    pub fn luau_ffi_protected_sandboxthread(L: *mut lua_State) -> c_int;
}
