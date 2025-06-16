#[allow(dead_code)]
#[allow(non_snake_case)]
#[allow(non_camel_case_types)]
#[allow(non_upper_case_globals)]
mod luau;

#[allow(dead_code)]
#[allow(non_snake_case)]
#[allow(non_camel_case_types)]
#[allow(non_upper_case_globals)]
mod luau_ffi;

#[allow(dead_code)]
#[allow(non_snake_case)]
#[allow(non_camel_case_types)]
#[allow(non_upper_case_globals)]
mod luau_require;

#[allow(dead_code)]
#[allow(non_snake_case)]
#[allow(non_camel_case_types)]
#[allow(non_upper_case_globals)]
mod luau_require_ffi;

#[cfg(test)]
mod test {
    use crate::luau::{luaL_newstate, lua_close};

    #[test]
    pub fn sandbox() {
        unsafe {
            let l = luaL_newstate();
            lua_close(l);
        }
    }
}