cmake_minimum_required(VERSION 3.25)

foreach(_required BINARY ALLOWLIST EXPORT_TOOL EXPORT_TOOL_KIND)
    if(NOT DEFINED ${_required} OR "${${_required}}" STREQUAL "")
        message(FATAL_ERROR "AuditExports.cmake requires -D${_required}=...")
    endif()
endforeach()

if(NOT EXISTS "${BINARY}")
    message(FATAL_ERROR "Native library does not exist: ${BINARY}")
endif()

if(NOT EXISTS "${ALLOWLIST}")
    message(FATAL_ERROR "Export allowlist does not exist: ${ALLOWLIST}")
endif()

if(NOT EXISTS "${EXPORT_TOOL}")
    message(FATAL_ERROR "Export inspection tool does not exist: ${EXPORT_TOOL}")
endif()

file(STRINGS "${ALLOWLIST}" _allowlist_lines)
set(_expected_exports)

foreach(_line IN LISTS _allowlist_lines)
    string(STRIP "${_line}" _line)
    if(_line STREQUAL "" OR _line MATCHES "^#")
        continue()
    endif()
    if(NOT _line MATCHES "^luau_host_[A-Za-z0-9_]+$")
        message(FATAL_ERROR "Invalid allowlist entry: ${_line}")
    endif()
    list(APPEND _expected_exports "${_line}")
endforeach()

if(NOT _expected_exports)
    message(FATAL_ERROR "The export allowlist is empty: ${ALLOWLIST}")
endif()

list(REMOVE_DUPLICATES _expected_exports)
list(SORT _expected_exports)

if(EXPORT_TOOL_KIND STREQUAL "MSVC")
    execute_process(
        COMMAND "${EXPORT_TOOL}" /nologo /exports "${BINARY}"
        RESULT_VARIABLE _tool_result
        OUTPUT_VARIABLE _tool_output
        ERROR_VARIABLE _tool_error
    )
elseif(EXPORT_TOOL_KIND STREQUAL "NM")
    execute_process(
        COMMAND "${EXPORT_TOOL}" -D --defined-only --extern-only --format=posix "${BINARY}"
        RESULT_VARIABLE _tool_result
        OUTPUT_VARIABLE _tool_output
        ERROR_VARIABLE _tool_error
    )
else()
    message(FATAL_ERROR "Unsupported EXPORT_TOOL_KIND: ${EXPORT_TOOL_KIND}")
endif()

if(NOT _tool_result EQUAL 0)
    message(FATAL_ERROR
        "Export inspection failed (${_tool_result})\n${_tool_output}\n${_tool_error}")
endif()

string(REPLACE "\r\n" "\n" _tool_output "${_tool_output}")
string(REPLACE "\r" "\n" _tool_output "${_tool_output}")
string(REPLACE "\n" ";" _tool_lines "${_tool_output}")
set(_actual_exports)

foreach(_line IN LISTS _tool_lines)
    if(EXPORT_TOOL_KIND STREQUAL "MSVC")
        if(_line MATCHES
            "^[ \t]+[0-9]+[ \t]+[0-9A-Fa-f]+[ \t]+[0-9A-Fa-f]+[ \t]+([^ \t=]+)")
            list(APPEND _actual_exports "${CMAKE_MATCH_1}")
        endif()
    elseif(_line MATCHES "^([^ \t]+)[ \t]+[A-Za-z][ \t]+")
        list(APPEND _actual_exports "${CMAKE_MATCH_1}")
    endif()
endforeach()

list(REMOVE_DUPLICATES _actual_exports)
list(SORT _actual_exports)

set(_missing_exports ${_expected_exports})
set(_unexpected_exports ${_actual_exports})

foreach(_actual IN LISTS _actual_exports)
    list(REMOVE_ITEM _missing_exports "${_actual}")
endforeach()

foreach(_expected IN LISTS _expected_exports)
    list(REMOVE_ITEM _unexpected_exports "${_expected}")
endforeach()

if(_missing_exports OR _unexpected_exports)
    string(JOIN "\n  " _missing_text ${_missing_exports})
    string(JOIN "\n  " _unexpected_text ${_unexpected_exports})
    if(_missing_text STREQUAL "")
        set(_missing_text "<none>")
    endif()
    if(_unexpected_text STREQUAL "")
        set(_unexpected_text "<none>")
    endif()
    message(FATAL_ERROR
        "luau_host export audit failed\n"
        "Missing:\n  ${_missing_text}\n"
        "Unexpected:\n  ${_unexpected_text}")
endif()

list(LENGTH _actual_exports _export_count)
message(STATUS "luau_host export audit passed (${_export_count} symbols)")

