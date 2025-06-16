fn main() {
    cmake::Config::new("../../luau")
        .build_target("Luau.Compiler")
        .build();

    let dst = cmake::Config::new("../../luau")
        .build_target("Luau.Require")
        .build();

    println!("cargo:rustc-link-search=native={}/build", dst.display());
    println!("cargo:rustc-link-lib=static=Luau.VM");
    println!("cargo:rustc-link-lib=static=Luau.Ast");
    println!("cargo:rustc-link-lib=static=Luau.Compiler");
    println!("cargo:rustc-link-lib=static=Luau.Config");
    println!("cargo:rustc-link-lib=static=Luau.RequireNavigator");
    println!("cargo:rustc-link-lib=static=Luau.Require");
    println!("cargo:rustc-link-lib=dylib=c++");

    bindgen::Builder::default()
        .headers([
            "../../luau/VM/include/lua.h",
            "../../luau/VM/include/lualib.h",
            "../../luau/Compiler/include/luacode.h",
        ])
        .generate()
        .unwrap()
        .write_to_file("src/luau.rs")
        .unwrap();

    let cs = new_csbindgen_builder("src/luau.rs")
        .rust_file_header("use super::luau::*;")
        .csharp_file_header(
            "
using lua_newstate_f_delegate = Luau.Native.lua_Alloc;
using lua_pushcclosurek_fn__delegate = Luau.Native.lua_CFunction;
using lua_tocfunction_return_delegate = Luau.Native.lua_CFunction;
using lua_pushcclosurek_cont_delegate = Luau.Native.lua_Continuation;
using lua_setuserdatadtor_dtor_delegate = Luau.Native.lua_Destructor;
using lua_getuserdatadtor_return_delegate = Luau.Native.lua_Destructor;
using lua_getallocf_return_delegate = Luau.Native.lua_Alloc;
using lua_getcoverage_callback_delegate = Luau.Native.lua_Coverage;
",
        );

    cs.generate_to_file(
        "src/luau_ffi.rs",
        "../../src/Luau.Native/NativeMethods.g.cs",
    )
    .unwrap();

    cs.csharp_dll_name_if(
        "UNITY_IOS && !UNITY_EDITOR",
        "__Internal",
    )
    .generate_csharp_file("../../src/Luau.Unity/Assets/Luau.Unity/Native/NativeMethods.g.cs")
    .unwrap();

    bindgen::Builder::default()
        .headers(["../../luau/Require/Runtime/include/Luau/Require.h"])
        .clang_arg("-x")
        .clang_arg("c++")
        .clang_arg("-std=c++17")
        .clang_arg("-I../../luau/Compiler/include")
        .clang_arg("-I../../luau/VM/include")
        .clang_arg("-I../../luau/Require/Runtime/include")
        .clang_arg("-DLUA_API=extern\"C\"")
        .allowlist_function("luarequire_.*")
        .allowlist_function("luaopen_require")
        .allowlist_type("luarequire_.*")
        .blocklist_type("lua_State")
        .raw_line("use super::luau::*;")
        .generate()
        .unwrap()
        .write_to_file("src/luau_require.rs")
        .unwrap();

    let cs2 = new_csbindgen_builder("src/luau_require.rs")
        .rust_file_header(
            "
use super::luau::*;
use super::luau_require::*;
",
        )
        .csharp_disable_emit_dll_name(true)
        .csharp_file_header(
            "
using luarequire_pushrequire_config_init_delegate = Luau.Native.luarequire_Configuration_init;     
using luaopen_require_config_init_delegate = Luau.Native.luarequire_Configuration_init;    
using luarequire_pushproxyrequire_config_init_delegate = Luau.Native.luarequire_Configuration_init;   
",
        );

    cs2.generate_to_file(
        "src/luau_require_ffi.rs",
        "../../src/Luau.Native/NativeMethods.Require.g.cs",
    )
    .unwrap();

    cs2.generate_csharp_file(
        "../../src/Luau.Unity/Assets/Luau.Unity/Native/NativeMethods.Require.g.cs",
    )
    .unwrap();
}

fn new_csbindgen_builder(src: &'static str) -> csbindgen::Builder {
    csbindgen::Builder::default()
        .input_bindgen_file(src)
        .rust_method_prefix("ffi_")
        .csharp_entry_point_prefix("ffi_")
        .csharp_method_prefix("")
        .csharp_namespace("Luau.Native")
        .csharp_dll_name("libluau")
        .csharp_class_accessibility("public")
        .csharp_generate_const_filter(|x| x.starts_with("LUA"))
        .csharp_use_function_pointer(false)
}
