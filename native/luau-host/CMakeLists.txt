cmake_minimum_required(VERSION 3.25)

project(luau_host LANGUAGES C CXX)

include(CTest)

option(LUAU_HOST_BUILD_TESTS "Build the native luau_host conformance executable" ON)
set(LUAU_HOST_UPSTREAM_REVISION "6e9b580e2e24643214caf0f4bbbb3db911ca30f3")
set(
    LUAU_HOST_BUILD_CONFIGURATION
    ""
    CACHE STRING
    "Build marker included in the ABI fingerprint; empty selects the active CMake configuration"
)

set(LUAU_HOST_ROOT "${CMAKE_CURRENT_SOURCE_DIR}")
get_filename_component(
    LUAU_UPSTREAM_ROOT
    "${CMAKE_CURRENT_SOURCE_DIR}/../../luau"
    REALPATH
)

if(NOT EXISTS "${LUAU_UPSTREAM_ROOT}/CMakeLists.txt")
    message(
        FATAL_ERROR
        "The pinned Luau submodule is unavailable at ${LUAU_UPSTREAM_ROOT}. "
        "Initialize submodules before configuring luau_host."
    )
endif()

# The ABI record must describe the sources that were actually compiled. A
# caller-provided cache value is not sufficient: without this check, a build
# from a different Luau checkout could advertise the approved revision and
# pass the managed handshake.
find_package(Git REQUIRED)
execute_process(
    COMMAND "${GIT_EXECUTABLE}" -C "${LUAU_UPSTREAM_ROOT}" rev-parse HEAD
    RESULT_VARIABLE _luau_host_git_result
    OUTPUT_VARIABLE _luau_host_actual_upstream_revision
    ERROR_VARIABLE _luau_host_git_error
    OUTPUT_STRIP_TRAILING_WHITESPACE
)
if(NOT _luau_host_git_result EQUAL 0)
    message(FATAL_ERROR "Unable to read the Luau submodule revision: ${_luau_host_git_error}")
endif()
if(NOT _luau_host_actual_upstream_revision STREQUAL LUAU_HOST_UPSTREAM_REVISION)
    message(
        FATAL_ERROR
        "Luau submodule revision mismatch: expected ${LUAU_HOST_UPSTREAM_REVISION}, "
        "actual ${_luau_host_actual_upstream_revision}"
    )
endif()

execute_process(
    COMMAND "${GIT_EXECUTABLE}" -C "${LUAU_UPSTREAM_ROOT}" status --porcelain --untracked-files=no
    RESULT_VARIABLE _luau_host_status_result
    OUTPUT_VARIABLE _luau_host_dirty_upstream
    ERROR_VARIABLE _luau_host_status_error
    OUTPUT_STRIP_TRAILING_WHITESPACE
)
if(NOT _luau_host_status_result EQUAL 0)
    message(FATAL_ERROR "Unable to inspect the Luau submodule worktree: ${_luau_host_status_error}")
endif()
if(NOT _luau_host_dirty_upstream STREQUAL "")
    message(
        FATAL_ERROR
        "The Luau submodule contains tracked local modifications. The host ABI may only be built from the exact approved commit.\n"
        "${_luau_host_dirty_upstream}"
    )
endif()

# Force an incremental build to reconfigure before it can compile changed
# provenance inputs using stale definitions. The submodule is detached in the
# supported checkout, so its Git HEAD file is also a direct revision trigger.
execute_process(
    COMMAND "${GIT_EXECUTABLE}" -C "${LUAU_UPSTREAM_ROOT}" rev-parse --git-path HEAD
    OUTPUT_VARIABLE _luau_host_git_head_path
    OUTPUT_STRIP_TRAILING_WHITESPACE
    COMMAND_ERROR_IS_FATAL ANY
)
if(NOT IS_ABSOLUTE "${_luau_host_git_head_path}")
    get_filename_component(
        _luau_host_git_head_path
        "${_luau_host_git_head_path}"
        ABSOLUTE
        BASE_DIR "${LUAU_UPSTREAM_ROOT}"
    )
endif()
file(
    GLOB_RECURSE
    _luau_host_upstream_provenance_inputs
    LIST_DIRECTORIES false
    "${LUAU_UPSTREAM_ROOT}/*.c"
    "${LUAU_UPSTREAM_ROOT}/*.cc"
    "${LUAU_UPSTREAM_ROOT}/*.cpp"
    "${LUAU_UPSTREAM_ROOT}/*.h"
    "${LUAU_UPSTREAM_ROOT}/*.hpp"
    "${LUAU_UPSTREAM_ROOT}/CMakeLists.txt"
)
set_property(
    DIRECTORY
    APPEND
    PROPERTY CMAKE_CONFIGURE_DEPENDS
        "${LUAU_HOST_ROOT}/include/luau_host.h"
        "${LUAU_HOST_ROOT}/src/luau_host.cpp"
        "${LUAU_HOST_ROOT}/src/tracked_allocation.h"
        "${LUAU_HOST_ROOT}/exports/luau_host.exports"
        "${_luau_host_git_head_path}"
        ${_luau_host_upstream_provenance_inputs}
)

# Hash ABI-relevant repository inputs rather than a hand-maintained label.
# Paths and timestamps are intentionally excluded so identical source trees
# produce the same compatibility fingerprint on every machine.
file(SHA256 "${LUAU_HOST_ROOT}/include/luau_host.h" _luau_host_header_hash)
file(SHA256 "${LUAU_HOST_ROOT}/src/luau_host.cpp" _luau_host_source_hash)
file(SHA256 "${LUAU_HOST_ROOT}/src/tracked_allocation.h" _luau_host_allocator_hash)
file(SHA256 "${LUAU_HOST_ROOT}/exports/luau_host.exports" _luau_host_exports_hash)
string(
    SHA256
    LUAU_HOST_BUILD_INPUT_FINGERPRINT
    "abi=1.0;upstream=${_luau_host_actual_upstream_revision};header=${_luau_host_header_hash};source=${_luau_host_source_hash};allocator=${_luau_host_allocator_hash};exports=${_luau_host_exports_hash}"
)

# These values must be established before upstream creates its static targets.
# Hidden visibility on luau_host alone is not sufficient because the final
# shared library incorporates objects from the upstream archives.
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CMAKE_C_VISIBILITY_PRESET hidden)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_VISIBILITY_INLINES_HIDDEN ON)

set(LUAU_BUILD_CLI OFF CACHE BOOL "" FORCE)
set(LUAU_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(LUAU_BUILD_WEB OFF CACHE BOOL "" FORCE)
set(LUAU_BUILD_SHARED OFF CACHE BOOL "" FORCE)
set(LUAU_EXTERN_C ON CACHE BOOL "" FORCE)
set(LUAU_WERROR OFF CACHE BOOL "" FORCE)

add_subdirectory(
    "${LUAU_UPSTREAM_ROOT}"
    "${CMAKE_CURRENT_BINARY_DIR}/upstream-luau"
    EXCLUDE_FROM_ALL
)

add_library(
    luau_host
    SHARED
    src/luau_host.cpp
    src/tracked_allocation.h
)

target_compile_features(luau_host PRIVATE cxx_std_17)
if(LUAU_HOST_BUILD_CONFIGURATION STREQUAL "")
    if(CMAKE_CONFIGURATION_TYPES)
        set(_luau_host_build_configuration "$<CONFIG>")
    elseif(NOT CMAKE_BUILD_TYPE STREQUAL "")
        set(_luau_host_build_configuration "${CMAKE_BUILD_TYPE}")
    else()
        set(_luau_host_build_configuration "unknown")
    endif()
else()
    set(_luau_host_build_configuration "${LUAU_HOST_BUILD_CONFIGURATION}")
endif()
target_compile_definitions(
    luau_host
    PRIVATE
        LUAU_HOST_BUILDING_LIBRARY=1
        "LUAU_HOST_UPSTREAM_REVISION=\"${_luau_host_actual_upstream_revision}\""
        "LUAU_HOST_BUILD_INPUT_FINGERPRINT=\"${LUAU_HOST_BUILD_INPUT_FINGERPRINT}\""
        "LUAU_HOST_BUILD_CONFIGURATION=\"${_luau_host_build_configuration}\""
)
target_include_directories(
    luau_host
    PUBLIC
        "$<BUILD_INTERFACE:${LUAU_HOST_ROOT}/include>"
        "$<INSTALL_INTERFACE:include>"
)
target_link_libraries(
    luau_host
    PRIVATE
        Luau.Compiler
        Luau.VM
        Luau.VM.Internals
)

if(ANDROID)
    # The NDK runtime choice is part of the reviewed toolchain contract. The
    # wrapper preset deliberately selects the shared libc++ used by Unity.
    target_link_libraries(luau_host PRIVATE c++_shared)
endif()

set_target_properties(
    luau_host
    PROPERTIES
        OUTPUT_NAME "luau_host"
        CXX_VISIBILITY_PRESET hidden
        VISIBILITY_INLINES_HIDDEN YES
        WINDOWS_EXPORT_ALL_SYMBOLS OFF
)

if(MSVC)
    target_compile_options(luau_host PRIVATE /W4 /WX /permissive- /EHsc)
else()
    target_compile_options(luau_host PRIVATE -Wall -Wextra -Wpedantic -Werror)
    if(CMAKE_CXX_COMPILER_ID MATCHES "Clang")
        # The approved upstream VM internal headers use anonymous structs in
        # anonymous unions. Keep all other pedantic diagnostics fatal.
        target_compile_options(luau_host PRIVATE -Wno-nested-anon-types)
    endif()
endif()

# The version script is an additional ELF containment boundary. The exact
# checked-in allowlist is enforced after linking by luau_host_export_audit.
if(UNIX AND NOT APPLE)
    set(LUAU_HOST_ELF_VERSION_SCRIPT "${LUAU_HOST_ROOT}/exports/luau_host.map")
    target_link_options(
        luau_host
        PRIVATE
            "LINKER:--version-script=${LUAU_HOST_ELF_VERSION_SCRIPT}"
            "LINKER:--exclude-libs,ALL"
            "LINKER:--no-undefined"
    )
    set_property(
        TARGET luau_host
        APPEND
        PROPERTY LINK_DEPENDS "${LUAU_HOST_ELF_VERSION_SCRIPT}"
    )
endif()

set(LUAU_HOST_EXPORT_ALLOWLIST "${LUAU_HOST_ROOT}/exports/luau_host.exports")
set(LUAU_HOST_EXPORT_AUDIT_SCRIPT "${LUAU_HOST_ROOT}/cmake/AuditExports.cmake")

if(MSVC)
    get_filename_component(LUAU_HOST_MSVC_TOOL_DIRECTORY "${CMAKE_LINKER}" DIRECTORY)
    find_program(
        LUAU_HOST_EXPORT_TOOL
        NAMES dumpbin.exe dumpbin
        HINTS "${LUAU_HOST_MSVC_TOOL_DIRECTORY}"
        NO_DEFAULT_PATH
        REQUIRED
    )
    set(LUAU_HOST_EXPORT_TOOL_KIND "MSVC")
elseif(UNIX AND NOT APPLE)
    set(LUAU_HOST_EXPORT_TOOL "${CMAKE_NM}")
    set(LUAU_HOST_EXPORT_TOOL_KIND "NM")
else()
    set(LUAU_HOST_EXPORT_TOOL "")
    set(LUAU_HOST_EXPORT_TOOL_KIND "UNSUPPORTED")
endif()

if(NOT LUAU_HOST_EXPORT_TOOL_KIND STREQUAL "UNSUPPORTED")
    add_custom_target(
        luau_host_export_audit
        COMMAND
            "${CMAKE_COMMAND}"
            "-DBINARY=$<TARGET_FILE:luau_host>"
            "-DALLOWLIST=${LUAU_HOST_EXPORT_ALLOWLIST}"
            "-DEXPORT_TOOL=${LUAU_HOST_EXPORT_TOOL}"
            "-DEXPORT_TOOL_KIND=${LUAU_HOST_EXPORT_TOOL_KIND}"
            -P "${LUAU_HOST_EXPORT_AUDIT_SCRIPT}"
        DEPENDS luau_host
        COMMENT "Auditing luau_host exported symbols"
        VERBATIM
    )
endif()

# Compile the public contract as C11 independently of the C++ implementation.
# This is an object target by design: no platform executable or runtime is
# required, so cross-compiled Android builds exercise it as well.
add_library(
    luau_host_c_header_compile
    OBJECT
    tests/luau_host_header_compile.c
)
target_compile_features(luau_host_c_header_compile PRIVATE c_std_11)
target_include_directories(luau_host_c_header_compile PRIVATE "${LUAU_HOST_ROOT}/include")

if(MSVC)
    target_compile_options(luau_host_c_header_compile PRIVATE /W4 /WX)
else()
    target_compile_options(luau_host_c_header_compile PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

if(LUAU_HOST_BUILD_TESTS)
    add_executable(
        luau_host_conformance
        tests/luau_host_conformance.cpp
    )
    target_compile_features(luau_host_conformance PRIVATE cxx_std_17)
    target_link_libraries(luau_host_conformance PRIVATE luau_host)
    target_include_directories(luau_host_conformance PRIVATE "${LUAU_HOST_ROOT}/src")

    if(ANDROID)
        # The executable is only cross-compiled in Android CI, but it still
        # needs the same explicit Unity-compatible C++ runtime as the host.
        target_link_libraries(luau_host_conformance PRIVATE c++_shared)
    endif()

    if(MSVC)
        target_compile_options(luau_host_conformance PRIVATE /W4 /WX /permissive- /EHsc)
    else()
        target_compile_options(luau_host_conformance PRIVATE -Wall -Wextra -Wpedantic -Werror)
    endif()

    add_test(NAME luau_host_conformance COMMAND luau_host_conformance)

    # Purpose-built malformed ABI libraries exercise the managed handshake
    # without adding mutation hooks or test exports to the production host.
    # Each target compiles the same tiny C fixture with one fixed defect.
    set(
        _luau_host_invalid_abi_cases
        wrong_magic
        wrong_major
        missing_required_feature
        wrong_pointer_size
        wrong_compile_options_size
        wrong_callback_table_size
        shifted_tags
        truncated_record
    )
    set(_luau_host_invalid_abi_targets "")
    set(_luau_host_invalid_abi_case_id 0)

    foreach(_luau_host_invalid_abi_case IN LISTS _luau_host_invalid_abi_cases)
        math(EXPR _luau_host_invalid_abi_case_id "${_luau_host_invalid_abi_case_id} + 1")
        set(_luau_host_invalid_abi_target "luau_host_invalid_abi_${_luau_host_invalid_abi_case}")

        add_library(
            "${_luau_host_invalid_abi_target}"
            SHARED
            tests/luau_host_invalid_abi_fixture.c
        )
        target_compile_features("${_luau_host_invalid_abi_target}" PRIVATE c_std_11)
        target_compile_definitions(
            "${_luau_host_invalid_abi_target}"
            PRIVATE
                LUAU_HOST_BUILDING_LIBRARY=1
                "LUAU_HOST_INVALID_ABI_CASE=${_luau_host_invalid_abi_case_id}"
        )
        target_include_directories(
            "${_luau_host_invalid_abi_target}"
            PRIVATE
                "${LUAU_HOST_ROOT}/include"
        )
        set_target_properties(
            "${_luau_host_invalid_abi_target}"
            PROPERTIES
                C_VISIBILITY_PRESET hidden
                VISIBILITY_INLINES_HIDDEN YES
                WINDOWS_EXPORT_ALL_SYMBOLS OFF
                RUNTIME_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/invalid-abi-fixtures"
                LIBRARY_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/invalid-abi-fixtures"
                ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/invalid-abi-fixtures"
        )

        if(MSVC)
            target_compile_options("${_luau_host_invalid_abi_target}" PRIVATE /W4 /WX)
        else()
            target_compile_options(
                "${_luau_host_invalid_abi_target}"
                PRIVATE
                    -Wall
                    -Wextra
                    -Wpedantic
                    -Werror
            )
        endif()

        list(APPEND _luau_host_invalid_abi_targets "${_luau_host_invalid_abi_target}")
    endforeach()

    add_custom_target(
        luau_host_invalid_abi_fixtures
        DEPENDS ${_luau_host_invalid_abi_targets}
    )
endif()

install(
    TARGETS luau_host
    RUNTIME DESTINATION .
    LIBRARY DESTINATION .
    ARCHIVE DESTINATION .
)
install(FILES include/luau_host.h DESTINATION include)
install(
    FILES "${LUAU_HOST_EXPORT_ALLOWLIST}"
    DESTINATION share/luau-host
)
