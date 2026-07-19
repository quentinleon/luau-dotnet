foreach(_required IN ITEMS HEADER ALLOWLIST INTEROP MANAGED_PROTECTION)
    if(NOT DEFINED ${_required} OR NOT EXISTS "${${_required}}")
        message(FATAL_ERROR "${_required} must name an existing compatibility input")
    endif()
endforeach()

file(READ "${HEADER}" _header)
file(READ "${INTEROP}" _interop)
file(READ "${MANAGED_PROTECTION}" _managed)
file(STRINGS "${ALLOWLIST}" _allowlist_lines)

set(_allowlist "")
foreach(_line IN LISTS _allowlist_lines)
    string(STRIP "${_line}" _line)
    if(_line MATCHES "^luau_host_[a-z0-9_]+$")
        list(APPEND _allowlist "${_line}")
    endif()
endforeach()

string(REGEX MATCHALL "LUAU_HOST_API[^\r\n]*LUAU_HOST_CALL[ \t]+luau_host_[a-z0-9_]+" _header_matches "${_header}")
set(_header_exports "")
foreach(_match IN LISTS _header_matches)
    string(REGEX REPLACE ".*LUAU_HOST_CALL[ \t]+" "" _name "${_match}")
    list(APPEND _header_exports "${_name}")
endforeach()

string(REGEX MATCHALL "EntryPoint[ \t]*=[ \t]*\"luau_host_[a-z0-9_]+\"" _interop_matches "${_interop}")
set(_interop_exports "")
foreach(_match IN LISTS _interop_matches)
    string(REGEX REPLACE ".*\"(luau_host_[a-z0-9_]+)\"" "\\1" _name "${_match}")
    list(APPEND _interop_exports "${_name}")
endforeach()

list(SORT _allowlist)
list(SORT _header_exports)
list(SORT _interop_exports)
if(NOT _allowlist STREQUAL _header_exports)
    message(FATAL_ERROR "Native header declarations do not exactly match the export allowlist")
endif()
if(NOT _allowlist STREQUAL _interop_exports)
    message(FATAL_ERROR "Managed direct declarations do not exactly match the export allowlist")
endif()

string(REGEX MATCH "LUAU_HOST_ABI_MAJOR[ \t]*=[ \t]*([0-9]+)" _native_major_match "${_header}")
set(_native_major "${CMAKE_MATCH_1}")
string(REGEX MATCH "LUAU_HOST_ABI_MINOR[ \t]*=[ \t]*([0-9]+)" _native_minor_match "${_header}")
set(_native_minor "${CMAKE_MATCH_1}")
string(REGEX MATCH "ExpectedAbiMajor[ \t]*=[ \t]*([0-9]+)" _managed_major_match "${_managed}")
set(_managed_major "${CMAKE_MATCH_1}")
string(REGEX MATCH "(ExpectedAbiMinor|MinimumAbiMinor)[ \t]*=[ \t]*([0-9]+)" _managed_minor_match "${_managed}")
set(_managed_minor "${CMAKE_MATCH_2}")

if(NOT _native_major STREQUAL _managed_major OR NOT _native_minor STREQUAL _managed_minor)
    message(
        FATAL_ERROR
        "Managed/native exact ABI mismatch: native ${_native_major}.${_native_minor}, "
        "managed ${_managed_major}.${_managed_minor}"
    )
endif()

list(LENGTH _allowlist _export_count)
message(STATUS "luau_host compatibility audit passed (ABI ${_native_major}.${_native_minor}, ${_export_count} exact exports)")
