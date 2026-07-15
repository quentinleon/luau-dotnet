use std::{
    env,
    fs,
    path::{Path, PathBuf},
};

const ANDROID_API_LEVEL: &str = "26";

fn main() {
    println!("cargo:rerun-if-env-changed=ANDROID_NDK_HOME");
    println!("cargo:rerun-if-env-changed=ANDROID_NDK_HOST_TAG");

    let target = build_target::target_triple().unwrap();
    if cfg!(windows) && (target == "aarch64-linux-android" || target == "x86_64-linux-android") {
        // rustc 1.86's Windows-hosted Android linker script can list compiler
        // builtins that are not pulled into the final cdylib. LLD 18 rejects
        // those harmless absent version entries unless this is enabled.
        println!("cargo:rustc-link-arg=-Wl,--undefined-version");
    }

    if target == "wasm32-unknown-emscripten" {
        if let Ok(em_dir) = env::var("EM_DIR") {
            fn exec_path(path: &str) -> String {
                if cfg!(windows) {
                    format!("{path}.bat")
                } else {
                    path.to_string()
                }
            }
            env::set_var(
                "EMCMAKE",
                exec_path(&format!("{em_dir}/emscripten/emcmake")),
            );
            env::set_var("EMMAKE", exec_path(&format!("{em_dir}/emscripten/emmake")));
            env::set_var("EM_CONFIG", format!("{em_dir}/.emscripten"));
        }
    }

    build_protected_bridge(&target);

    let luau_cmake_output = new_cmake_config().build_target("Luau.Compiler").build();
    println!(
        "cargo:warning=CMake configure (Luau.Compiler) completed: {}",
        luau_cmake_output.display()
    );

    let vm_cmake_output = new_cmake_config().build_target("Luau.VM").build();
    println!(
        "cargo:warning=CMake configure (Luau.VM) completed: {}",
        vm_cmake_output.display()
    );
    assert_eq!(
        luau_cmake_output, vm_cmake_output,
        "Luau.Compiler and Luau.VM must share one CMake output directory"
    );

    let target = build_target::target_triple().unwrap();
    if target == "aarch64-unknown-linux-gnu" {
        println!(
            "cargo:rustc-link-search=native={}/build",
            luau_cmake_output.display()
        );
        println!("cargo:rustc-link-lib=dylib=stdc++");
    } else if target == "x86_64-pc-windows-msvc" {
        println!(
            "cargo:rustc-link-search=native={}/build/Release",
            luau_cmake_output.display()
        );
    } else {
        println!(
            "cargo:rustc-link-search=native={}/build",
            luau_cmake_output.display()
        );
        println!("cargo:rustc-link-lib=dylib=c++");
    }

    println!("cargo:rustc-link-lib=static=Luau.Ast");
    println!("cargo:rustc-link-lib=static=Luau.Compiler");
    println!("cargo:rustc-link-lib=static=Luau.VM");

    println!("cargo:rerun-if-env-changed=LUAU_FFI_SKIP_BINDGEN");
    if env::var_os("LUAU_FFI_SKIP_BINDGEN").is_none() {
        bindgen::Builder::default()
            .headers([
                "../../luau/VM/include/lua.h",
                "../../luau/VM/include/lualib.h",
                "../../luau/Compiler/include/luacode.h",
            ])
            .clang_arg(format!("--target={}", target))
            .clang_arg("-fvisibility=default")
            .layout_tests(false)
            .generate()
            .unwrap()
            .write_to_file("src/luau.rs")
            .unwrap();
    }

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
    normalize_generated_rust_file("src/luau_ffi.rs");

    cs.csharp_dll_name_if("(UNITY_IOS || UNITY_WEBGL) && !UNITY_EDITOR", "__Internal")
        .generate_csharp_file("../../src/Luau.Unity/Assets/Luau.Unity/Native/NativeMethods.g.cs")
        .unwrap();

    let cs3 = new_csbindgen_builder("src/protected.rs")
        .rust_file_header(
            "
use super::luau::*;
use super::protected::*;
",
        )
        .csharp_disable_emit_dll_name(true)
        .csharp_file_header(
            "
using luau_ffi_protected_pushcclosurek_function_delegate = Luau.Native.lua_CFunction;
using luau_ffi_protected_pushcclosurek_continuation_delegate = Luau.Native.lua_Continuation;
using luau_ffi_protected_newuserdatadtor_destructor_delegate = Luau.Native.NativeMethods.lua_newuserdatadtor_dtor_delegate;
",
        );

    cs3.generate_to_file(
        "src/protected_ffi.rs",
        "../../src/Luau.Native/NativeMethods.Protected.g.cs",
    )
    .unwrap();
    normalize_generated_rust_file("src/protected_ffi.rs");

    cs3.generate_csharp_file(
        "../../src/Luau.Unity/Assets/Luau.Unity/Native/NativeMethods.Protected.g.cs",
    )
    .unwrap();
}

fn normalize_generated_rust_file(path: &str) {
    let contents = fs::read_to_string(path).unwrap();
    let normalized = contents
        .lines()
        .map(str::trim_end)
        .collect::<Vec<_>>()
        .join("\n");
    fs::write(path, normalized.trim_end().to_owned() + "\n").unwrap();
}

fn build_protected_bridge(target: &str) {
    let mut build = cc::Build::new();
    build
        .cpp(true)
        .std("c++17")
        .file("src/protected.cpp")
        .include("../../luau/Common/include")
        .include("../../luau/Compiler/include")
        .include("../../luau/VM/include")
        .include("../../luau/VM/src")
        .define("LUAU_BUILD_AS_EXTERN_C", None)
        .define("LUA_USE_LONGJMP", "1")
        .warnings(true);

    if target == "aarch64-linux-android" || target == "x86_64-linux-android" {
        let toolchain = android_toolchain(target);

        build.compiler(toolchain.cxx);
        build.archiver(toolchain.ar);
        build.flag("-fPIC");
        build.flag("-ffunction-sections");
        build.flag("-fdata-sections");
    } else if target == "aarch64-unknown-linux-gnu" {
        if let Ok(cxx) = std::env::var("CXX") {
            if !cxx.is_empty() {
                build.compiler(cxx);
            }
        }
        build.flag("-fPIC");
        build.flag("-ffunction-sections");
        build.flag("-fdata-sections");
    } else if target == "wasm32-unknown-emscripten" {
        if let Ok(em_dir) = std::env::var("EM_DIR") {
            build.compiler(format!("{}/emscripten/em++", em_dir));
        }
        build.flag("-fPIC");
    } else if target == "x86_64-pc-windows-msvc" {
        if let Ok(cxx) = std::env::var("CXX") {
            if !cxx.is_empty() {
                build.compiler(cxx);
            }
        }
        build.flag("/EHsc");
    }

    build.compile("luau_ffi_protected");
    println!("cargo:rerun-if-changed=src/protected.cpp");
}

struct AndroidToolchain {
    ndk_home: PathBuf,
    c: PathBuf,
    cxx: PathBuf,
    ar: PathBuf,
}

fn android_toolchain(target: &str) -> AndroidToolchain {
    let ndk_home = PathBuf::from(
        env::var_os("ANDROID_NDK_HOME")
            .expect("ANDROID_NDK_HOME must point to an installed Android NDK"),
    );
    let prebuilt_root = ndk_home.join("toolchains").join("llvm").join("prebuilt");
    let host_tag = env::var_os("ANDROID_NDK_HOST_TAG")
        .map(PathBuf::from)
        .or_else(|| find_android_ndk_host_tag(&prebuilt_root))
        .unwrap_or_else(|| {
            panic!(
                "No Android NDK host toolchain was found under {}",
                prebuilt_root.display()
            )
        });
    let bin = prebuilt_root.join(host_tag).join("bin");
    let compiler_prefix = match target {
        "aarch64-linux-android" => "aarch64-linux-android",
        "x86_64-linux-android" => "x86_64-linux-android",
        _ => panic!("Unsupported Android Rust target: {target}"),
    };

    AndroidToolchain {
        ndk_home,
        c: android_ndk_tool(&bin, &format!("{compiler_prefix}{ANDROID_API_LEVEL}-clang")),
        cxx: android_ndk_tool(
            &bin,
            &format!("{compiler_prefix}{ANDROID_API_LEVEL}-clang++"),
        ),
        ar: android_ndk_tool(&bin, "llvm-ar"),
    }
}

fn set_android_target_tool_defaults(target: &str, toolchain: &AndroidToolchain) {
    let suffix = target.replace('-', "_");
    for (name, value) in [
        (format!("CC_{suffix}"), &toolchain.c),
        (format!("CXX_{suffix}"), &toolchain.cxx),
        (format!("AR_{suffix}"), &toolchain.ar),
    ] {
        if env::var_os(&name).is_none() {
            env::set_var(name, value);
        }
    }
}

fn find_android_ndk_host_tag(prebuilt_root: &Path) -> Option<PathBuf> {
    let candidates: &[&str] = if cfg!(windows) {
        &["windows-x86_64"]
    } else if cfg!(target_os = "macos") && cfg!(target_arch = "aarch64") {
        &["darwin-arm64", "darwin-x86_64"]
    } else if cfg!(target_os = "macos") {
        &["darwin-x86_64", "darwin-arm64"]
    } else {
        &["linux-x86_64"]
    };

    candidates
        .iter()
        .map(PathBuf::from)
        .find(|candidate| prebuilt_root.join(candidate).is_dir())
}

fn android_ndk_tool(bin: &Path, name: &str) -> PathBuf {
    let tool = bin.join(name);
    if cfg!(windows) {
        let command = tool.with_extension("cmd");
        if command.is_file() {
            return command;
        }

        let executable = tool.with_extension("exe");
        if executable.is_file() {
            return executable;
        }
    }

    if tool.is_file() {
        return tool;
    }

    panic!("Android NDK tool was not found: {}", tool.display());
}

fn cmake_path(path: &Path) -> String {
    path.to_string_lossy().replace('\\', "/")
}

fn new_cmake_config() -> cmake::Config {
    let mut config = cmake::Config::new("../../luau");

    let target = build_target::target_triple().unwrap();

    if target == "x86_64-pc-windows-msvc" {
        if let Ok(cc) = std::env::var("CC") {
            if !cc.is_empty() {
                config.define("CMAKE_C_COMPILER", cc);
            }
        }
        if let Ok(cxx) = std::env::var("CXX") {
            if !cxx.is_empty() {
                config.define("CMAKE_CXX_COMPILER", cxx);
            }
        }

        config.cxxflag("/EHsc");
    } else if target == "aarch64-unknown-linux-gnu" {
        config.define("CMAKE_SYSTEM_NAME", "Linux");
        config.define("CMAKE_SYSTEM_PROCESSOR", "aarch64");
        config.define("CMAKE_C_FLAGS", "-ffunction-sections -fdata-sections -fPIC");
        config.define(
            "CMAKE_CXX_FLAGS",
            "-ffunction-sections -fdata-sections -fPIC",
        );
        if let Ok(cc) = std::env::var("CC") {
            if !cc.is_empty() {
                config.define("CMAKE_C_COMPILER", cc);
            }
        }
        if let Ok(cxx) = std::env::var("CXX") {
            if !cxx.is_empty() {
                config.define("CMAKE_CXX_COMPILER", cxx);
            }
        }
    } else if target == "x86_64-apple-ios" {
        config.define("CMAKE_SYSTEM_NAME", "iOS");
        config.define("CMAKE_SYSTEM_PROCESSOR", "x86_64");
        config.define("CMAKE_OSX_ARCHITECTURES", "x86_64");
        config.define("CMAKE_OSX_SYSROOT", "/Applications/Xcode.app/Contents/Developer/Platforms/iPhoneSimulator.platform/Developer/SDKs/iPhoneSimulator.sdk");
        config.define(
            "CMAKE_C_FLAGS",
            "-fPIC -m64 --target=x86_64-apple-ios-simulator -mios-simulator-version-min=17.5",
        );
        config.define(
            "CMAKE_CXX_FLAGS",
            "-fPIC -m64 --target=x86_64-apple-ios-simulator -mios-simulator-version-min=17.5",
        );
    } else if target == "aarch64-apple-ios" {
        config.define("CMAKE_SYSTEM_NAME", "iOS");
        config.define("CMAKE_SYSTEM_PROCESSOR", "arm64");
        config.define("CMAKE_OSX_ARCHITECTURES", "arm64");
        config.define("CMAKE_OSX_SYSROOT", "/Applications/Xcode.app/Contents/Developer/Platforms/iPhoneOS.platform/Developer/SDKs/iPhoneOS.sdk");
        config.define(
            "CMAKE_C_FLAGS",
            "-fPIC --target=arm64-apple-ios -miphoneos-version-min=17.5",
        );
        config.define(
            "CMAKE_CXX_FLAGS",
            "-fPIC --target=arm64-apple-ios -miphoneos-version-min=17.5",
        );
    } else if target == "aarch64-linux-android" || target == "x86_64-linux-android" {
        let toolchain = android_toolchain(&target);
        set_android_target_tool_defaults(&target, &toolchain);
        let (processor, abi, architecture_flags) = if target == "aarch64-linux-android" {
            ("aarch64", "arm64-v8a", "")
        } else {
            ("x86_64", "x86_64", " -m64")
        };
        // MSVC's default Visual Studio generator does not understand the NDK
        // Android platform. Other hosts retain CMake's native default so an
        // otherwise-working Unix Makefiles setup does not gain a Ninja
        // dependency just for this crate.
        if cfg!(windows) {
            config.generator("Ninja");
        }
        config.define("CMAKE_SYSTEM_NAME", "Android");
        config.define("CMAKE_SYSTEM_PROCESSOR", processor);
        config.define("CMAKE_ANDROID_ARCH_ABI", abi);
        config.define("CMAKE_ANDROID_NDK", cmake_path(&toolchain.ndk_home));
        config.define("CMAKE_ANDROID_STL_TYPE", "c++_static");
        config.define("CMAKE_ANDROID_API", ANDROID_API_LEVEL);
        config.define(
            "CMAKE_C_FLAGS",
            format!("-DANDROID -ffunction-sections -fdata-sections -fPIC{architecture_flags}"),
        );
        config.define(
            "CMAKE_CXX_FLAGS",
            format!("-DANDROID -ffunction-sections -fdata-sections -fPIC{architecture_flags}"),
        );
    } else if target == "wasm32-unknown-emscripten" {
        if let Ok(em_dir) = env::var("EM_DIR") {
            // By default cmake crate overrides compiler paths with not qualified ones, causing missing compiler errors with no emscripten in PATH.
            config.define("CMAKE_C_COMPILER", format!("{}/emscripten/emcc", em_dir));
            config.define("CMAKE_CXX_COMPILER", format!("{}/emscripten/em++", em_dir));
        }
        config.define("CMAKE_C_FLAGS", "-ffunction-sections -fdata-sections -fPIC");
        config.define(
            "CMAKE_CXX_FLAGS",
            "-ffunction-sections -fdata-sections -fPIC",
        );
    }

    config
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
