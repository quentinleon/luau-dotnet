#include "luau_host.h"

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

namespace
{
class TestFailure final : public std::runtime_error
{
public:
    TestFailure(const char* expression, const char* file, int line)
        : std::runtime_error(
              std::string(file) + ":" + std::to_string(line) + ": requirement failed: " + expression)
    {
    }
};

#define REQUIRE(expression)                                                                                              \
    do                                                                                                                   \
    {                                                                                                                    \
        if (!(expression))                                                                                               \
            throw TestFailure(#expression, __FILE__, __LINE__);                                                         \
    } while (false)

const uint8_t* bytes(const char* value)
{
    return reinterpret_cast<const uint8_t*>(value);
}

struct Root
{
    luau_host_state* state = nullptr;

    Root() = default;
    Root(const Root&) = delete;
    Root& operator=(const Root&) = delete;

    Root(Root&& other) noexcept
        : state(std::exchange(other.state, nullptr))
    {
    }

    Root& operator=(Root&& other) noexcept
    {
        if (this != &other)
        {
            close();
            state = std::exchange(other.state, nullptr);
        }
        return *this;
    }

    ~Root()
    {
        close();
    }

    void close()
    {
        if (state)
        {
            luau_host_state_close(state);
            state = nullptr;
        }
    }
};

Root create_root(const luau_host_state_options* options = nullptr)
{
    Root root;
    luau_host_memory_info failure = {};
    failure.struct_size = sizeof(failure);
    REQUIRE(luau_host_state_create(options, &root.state, &failure) == LUAU_HOST_STATUS_OK);
    REQUIRE(root.state != nullptr);
    return root;
}

struct CompiledBuffer
{
    luau_host_buffer value = {};

    CompiledBuffer() = default;
    CompiledBuffer(const CompiledBuffer&) = delete;
    CompiledBuffer& operator=(const CompiledBuffer&) = delete;

    CompiledBuffer(CompiledBuffer&& other) noexcept
        : value(std::exchange(other.value, {}))
    {
    }

    ~CompiledBuffer()
    {
        luau_host_buffer_free(&value);
    }
};

CompiledBuffer compile_source(const std::string& source)
{
    CompiledBuffer buffer;
    REQUIRE(
        luau_host_compile(bytes(source.data()), static_cast<uint64_t>(source.size()), nullptr, &buffer.value) ==
        LUAU_HOST_STATUS_OK);
    REQUIRE(buffer.value.data != nullptr);
    REQUIRE(buffer.value.size > 0);
    return buffer;
}

luau_host_status compile_and_load(luau_host_state* state, const std::string& source, const char* chunkName)
{
    CompiledBuffer bytecode = compile_source(source);
    luau_host_status loadStatus = LUAU_HOST_STATUS_INVALID_ARGUMENT;
    REQUIRE(
        luau_host_load(
            state,
            bytes(chunkName),
            bytecode.value.data,
            bytecode.value.size,
            0,
            &loadStatus) == LUAU_HOST_STATUS_OK);
    return loadStatus;
}

std::string string_at(luau_host_state* state, int32_t index)
{
    uint64_t length = 0;
    const uint8_t* value = luau_host_to_string_view(state, index, &length);
    REQUIRE(value != nullptr);
    return std::string(reinterpret_cast<const char*>(value), static_cast<size_t>(length));
}

luau_host_abi_info query_abi()
{
    luau_host_abi_info info = {};
    REQUIRE(luau_host_get_abi_info(static_cast<uint32_t>(sizeof(info)), &info) == LUAU_HOST_STATUS_OK);
    return info;
}

void test_abi_query()
{
    REQUIRE(luau_host_get_abi_info(static_cast<uint32_t>(sizeof(luau_host_abi_info)), nullptr) == LUAU_HOST_STATUS_INVALID_ARGUMENT);

    const luau_host_abi_info info = query_abi();
    REQUIRE(info.struct_size == sizeof(luau_host_abi_info));
    REQUIRE(info.magic == LUAU_HOST_ABI_MAGIC);
    REQUIRE(info.abi_major == LUAU_HOST_ABI_MAJOR);
    REQUIRE(info.abi_minor == LUAU_HOST_ABI_MINOR);
    REQUIRE(info.pointer_size == sizeof(void*));
    REQUIRE(info.size_t_size == sizeof(size_t));
    REQUIRE(info.little_endian == 1);
    REQUIRE(info.compile_options_size == sizeof(luau_host_compile_options));
    REQUIRE(info.callback_table_size == sizeof(luau_host_callback_table));
    REQUIRE(info.state_options_size == sizeof(luau_host_state_options));
    REQUIRE(info.memory_info_size == sizeof(luau_host_memory_info));
    REQUIRE(info.buffer_size == sizeof(luau_host_buffer));
    REQUIRE(info.upstream_revision_hash != 0);
    REQUIRE(info.host_build_fingerprint != 0);

    constexpr uint32_t fixedPrefixSize = static_cast<uint32_t>(offsetof(luau_host_abi_info, compile_options_size));
    alignas(luau_host_abi_info) std::array<uint8_t, sizeof(luau_host_abi_info) + 16> storage = {};

    storage.fill(0xa5);
    REQUIRE(
        luau_host_get_abi_info(fixedPrefixSize - 1, reinterpret_cast<luau_host_abi_info*>(storage.data())) ==
        LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(std::all_of(storage.begin() + fixedPrefixSize - 1, storage.end(), [](uint8_t value) { return value == 0xa5; }));

    storage.fill(0xa5);
    REQUIRE(
        luau_host_get_abi_info(fixedPrefixSize, reinterpret_cast<luau_host_abi_info*>(storage.data())) ==
        LUAU_HOST_STATUS_OK);
    REQUIRE(reinterpret_cast<const luau_host_abi_info*>(storage.data())->struct_size == sizeof(luau_host_abi_info));
    REQUIRE(std::all_of(storage.begin() + fixedPrefixSize, storage.end(), [](uint8_t value) { return value == 0xa5; }));

    storage.fill(0xa5);
    REQUIRE(
        luau_host_get_abi_info(static_cast<uint32_t>(storage.size()), reinterpret_cast<luau_host_abi_info*>(storage.data())) ==
        LUAU_HOST_STATUS_OK);
    REQUIRE(std::all_of(storage.begin() + sizeof(luau_host_abi_info), storage.end(), [](uint8_t value) { return value == 0xa5; }));
}

void test_compile_and_buffer_ownership()
{
    REQUIRE(luau_host_compile(nullptr, 0, nullptr, nullptr) == LUAU_HOST_STATUS_INVALID_ARGUMENT);

    // A nonempty output record remains caller-owned. Reject it without
    // clearing or freeing the pointer, even when the source itself is valid.
    luau_host_buffer output = {reinterpret_cast<uint8_t*>(static_cast<uintptr_t>(1)), 123};
    REQUIRE(
        luau_host_compile(bytes("return 1"), 8, nullptr, &output) ==
        LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(output.data == reinterpret_cast<uint8_t*>(static_cast<uintptr_t>(1)));
    REQUIRE(output.size == 123);

    output = {};
    REQUIRE(luau_host_compile(nullptr, 1, nullptr, &output) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(output.data == nullptr);
    REQUIRE(output.size == 0);

    luau_host_compile_options invalidOptions = {};
    invalidOptions.struct_size = sizeof(invalidOptions);
    invalidOptions.version = LUAU_HOST_COMPILE_OPTIONS_VERSION + 1;
    REQUIRE(luau_host_compile(bytes("return 1"), 8, &invalidOptions, &output) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(output.data == nullptr);
    REQUIRE(output.size == 0);

    REQUIRE(luau_host_compile(bytes("return 40 + 2"), 13, nullptr, &output) == LUAU_HOST_STATUS_OK);
    REQUIRE(output.data != nullptr);
    REQUIRE(output.size > 0);
    luau_host_buffer_free(&output);
    REQUIRE(output.data == nullptr);
    REQUIRE(output.size == 0);
    luau_host_buffer_free(&output);
    REQUIRE(output.data == nullptr);
    REQUIRE(output.size == 0);
    luau_host_buffer_free(nullptr);

    // The retired Rust bridge caught allocation/length failures around the
    // upstream compiler. The narrow host rejects an unrepresentable source
    // before dereferencing it; an initially empty output remains empty.
    uint8_t marker = 0;
    REQUIRE(
        luau_host_compile(&marker, std::numeric_limits<uint64_t>::max(), nullptr, &output) ==
        LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(output.data == nullptr);
    REQUIRE(output.size == 0);

    Root root = create_root();
    const luau_host_status compilerErrorStatus = compile_and_load(root.state, "return )", "@compiler-error");
    if (compilerErrorStatus != LUAU_HOST_STATUS_LUA_ERROR)
        std::cerr << "compiler error load status was " << compilerErrorStatus << '\n';
    REQUIRE(compilerErrorStatus == LUAU_HOST_STATUS_LUA_ERROR);
    REQUIRE(luau_host_stack_get_top(root.state) == 1);
    REQUIRE(!string_at(root.state, -1).empty());
}

void test_root_and_thread_lifecycle()
{
    luau_host_state_close(nullptr);

    luau_host_state* invalidState = reinterpret_cast<luau_host_state*>(static_cast<uintptr_t>(1));
    luau_host_memory_info failure = {};
    failure.struct_size = sizeof(failure);
    REQUIRE(luau_host_state_create(nullptr, nullptr, &failure) == LUAU_HOST_STATUS_INVALID_ARGUMENT);

    luau_host_state_options invalidOptions = {};
    invalidOptions.struct_size = sizeof(invalidOptions);
    invalidOptions.version = LUAU_HOST_STATE_OPTIONS_VERSION + 1;
    REQUIRE(luau_host_state_create(&invalidOptions, &invalidState, &failure) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(invalidState == nullptr);

    Root root = create_root();
    REQUIRE(luau_host_main_thread(root.state) == root.state);
    REQUIRE(luau_host_thread_status(root.state) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_stack_get_top(root.state) == 0);

    luau_host_state* child = nullptr;
    REQUIRE(luau_host_thread_create(root.state, nullptr) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_thread_create(root.state, &child) == LUAU_HOST_STATUS_OK);
    REQUIRE(child != nullptr);
    REQUIRE(child != root.state);
    REQUIRE(luau_host_main_thread(child) == root.state);
    REQUIRE(luau_host_thread_status(child) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_stack_get_top(root.state) == 1);
    REQUIRE(luau_host_to_thread(root.state, -1) == child);
    REQUIRE(luau_host_thread_reset(child) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_is_thread_reset(child) != 0);
}

void test_execution_error_containment_and_reuse()
{
    Root root = create_root();

    REQUIRE(compile_and_load(root.state, "return 6 * 7", "@success") == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_pcall(root.state, 0, 1, 0) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_stack_get_top(root.state) == 1);
    int32_t isNumber = 0;
    REQUIRE(luau_host_to_number(root.state, -1, &isNumber) == 42.0);
    REQUIRE(isNumber != 0);
    REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);

    REQUIRE(compile_and_load(root.state, "local value = nil; return value.missing", "@ordinary-error") == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_pcall(root.state, 0, 1, 0) == LUAU_HOST_STATUS_LUA_ERROR);
    REQUIRE(luau_host_stack_get_top(root.state) == 1);
    REQUIRE(!string_at(root.state, -1).empty());
    REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);

    REQUIRE(compile_and_load(root.state, "return 7", "@reused") == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_pcall(root.state, 0, 1, 0) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_stack_get_top(root.state) == 1);
    REQUIRE(luau_host_to_number(root.state, -1, &isNumber) == 7.0);
    REQUIRE(isNumber != 0);
}

void test_stack_tables_and_references()
{
    const luau_host_abi_info abi = query_abi();
    Root root = create_root();

    REQUIRE(luau_host_push_integer(root.state, 1) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_push_integer(root.state, 2) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_push_integer(root.state, 3) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_stack_abs_index(root.state, -1) == 3);
    REQUIRE(luau_host_stack_insert(root.state, 1) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_stack_remove(root.state, 2) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_stack_replace(root.state, 1) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_stack_get_top(root.state) == 1);
    int32_t isInteger = 0;
    REQUIRE(luau_host_to_integer64(root.state, 1, &isInteger) == 2);
    REQUIRE(isInteger != 0);
    REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);

    REQUIRE(luau_host_table_create(root.state, 0, 2) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_type(root.state, 1) == abi.type_table);
    REQUIRE(luau_host_push_string(root.state, bytes("answer"), 6) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_push_integer(root.state, 42) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_table_raw_set(root.state, 1) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_stack_get_top(root.state) == 1);

    REQUIRE(luau_host_push_string(root.state, bytes("answer"), 6) == LUAU_HOST_STATUS_OK);
    int32_t valueType = abi.type_nil;
    REQUIRE(luau_host_table_raw_get(root.state, 1, &valueType) == LUAU_HOST_STATUS_OK);
    REQUIRE(valueType == abi.type_integer);
    REQUIRE(luau_host_to_integer64(root.state, -1, &isInteger) == 42);
    REQUIRE(isInteger != 0);

    int32_t reference = 0;
    REQUIRE(luau_host_reference_create(root.state, -1, &reference) == LUAU_HOST_STATUS_OK);
    REQUIRE(reference > 0);
    REQUIRE(luau_host_stack_set_top(root.state, 1) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_reference_push(root.state, reference, &valueType) == LUAU_HOST_STATUS_OK);
    REQUIRE(valueType == abi.type_integer);
    REQUIRE(luau_host_to_integer64(root.state, -1, &isInteger) == 42);
    REQUIRE(isInteger != 0);
    REQUIRE(luau_host_reference_release(root.state, reference) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_stack_set_top(root.state, 1) == LUAU_HOST_STATUS_OK);

    REQUIRE(luau_host_table_clone(root.state, 1) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_raw_equal(root.state, 1, 2) == 0);
    REQUIRE(luau_host_stack_set_top(root.state, 1) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_table_clear(root.state, 1) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_push_string(root.state, bytes("answer"), 6) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_table_raw_get(root.state, 1, &valueType) == LUAU_HOST_STATUS_OK);
    REQUIRE(valueType == abi.type_nil);

    // Preserve lua_next's exact stack contract through the narrow host: an
    // iteration hit replaces the key with key+value, and exhaustion consumes
    // the retained key without disturbing the table.
    REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_table_create(root.state, 1, 0) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_push_integer(root.state, 1) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_push_integer(root.state, 42) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_table_raw_set(root.state, 1) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_push_nil(root.state) == LUAU_HOST_STATUS_OK);

    int32_t hasNext = -1;
    REQUIRE(luau_host_table_next(root.state, 1, &hasNext) == LUAU_HOST_STATUS_OK);
    REQUIRE(hasNext == 1);
    REQUIRE(luau_host_stack_get_top(root.state) == 3);
    REQUIRE(luau_host_to_integer64(root.state, -2, &isInteger) == 1);
    REQUIRE(isInteger != 0);
    REQUIRE(luau_host_to_integer64(root.state, -1, &isInteger) == 42);
    REQUIRE(isInteger != 0);

    REQUIRE(luau_host_stack_set_top(root.state, 2) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_table_next(root.state, 1, &hasNext) == LUAU_HOST_STATUS_OK);
    REQUIRE(hasNext == 0);
    REQUIRE(luau_host_stack_get_top(root.state) == 1);
}

void test_numeric_conversion_boundaries()
{
    Root root = create_root();

    auto rejectSigned = [&](double value) {
        REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);
        REQUIRE(luau_host_push_number(root.state, value) == LUAU_HOST_STATUS_OK);
        int32_t isInteger = 123;
        REQUIRE(luau_host_to_integer32(root.state, -1, &isInteger) == 0);
        REQUIRE(isInteger == 0);
    };
    auto rejectUnsigned = [&](double value) {
        REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);
        REQUIRE(luau_host_push_number(root.state, value) == LUAU_HOST_STATUS_OK);
        int32_t isInteger = 123;
        REQUIRE(luau_host_to_unsigned32(root.state, -1, &isInteger) == 0);
        REQUIRE(isInteger == 0);
    };
    auto acceptSigned = [&](double value, int32_t expected) {
        REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);
        REQUIRE(luau_host_push_number(root.state, value) == LUAU_HOST_STATUS_OK);
        int32_t isInteger = 0;
        REQUIRE(luau_host_to_integer32(root.state, -1, &isInteger) == expected);
        REQUIRE(isInteger != 0);
    };
    auto acceptUnsigned = [&](double value, uint32_t expected) {
        REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);
        REQUIRE(luau_host_push_number(root.state, value) == LUAU_HOST_STATUS_OK);
        int32_t isInteger = 0;
        REQUIRE(luau_host_to_unsigned32(root.state, -1, &isInteger) == expected);
        REQUIRE(isInteger != 0);
    };

    const double notANumber = std::numeric_limits<double>::quiet_NaN();
    const double positiveInfinity = std::numeric_limits<double>::infinity();
    const double negativeInfinity = -std::numeric_limits<double>::infinity();
    const double huge = std::numeric_limits<double>::max();
    for (const double invalid : {notANumber, positiveInfinity, negativeInfinity, huge, -huge, 2147483648.0, -2147483649.0})
        rejectSigned(invalid);
    for (const double invalid : {notANumber, positiveInfinity, negativeInfinity, huge, -huge, -1.0, 4294967296.0})
        rejectUnsigned(invalid);

    acceptSigned(42.75, 42);
    acceptSigned(-42.75, -42);
    acceptSigned(2147483647.0, std::numeric_limits<int32_t>::max());
    acceptSigned(-2147483648.0, std::numeric_limits<int32_t>::min());
    acceptUnsigned(42.75, 42);
    acceptUnsigned(4294967295.0, std::numeric_limits<uint32_t>::max());
    REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);
}

void test_invalid_observer_and_stack_boundaries()
{
    Root root = create_root();
    Root otherRoot = create_root();
    constexpr int32_t noType = -1;
    constexpr std::array<int32_t, 5> invalidIndices = {
        0,
        1,
        -1,
        std::numeric_limits<int32_t>::min(),
        std::numeric_limits<int32_t>::max(),
    };

    REQUIRE(luau_host_stack_get_top(root.state) == 0);
    for (const int32_t index : invalidIndices)
    {
        REQUIRE(luau_host_stack_abs_index(root.state, index) == 0);
        REQUIRE(luau_host_type(root.state, index) == noType);
        REQUIRE(luau_host_raw_equal(root.state, index, index) == 0);
        REQUIRE(luau_host_object_length(root.state, index) == 0);
        REQUIRE(luau_host_to_boolean(root.state, index) == 0);

        int32_t isNumber = 123;
        REQUIRE(luau_host_to_number(root.state, index, &isNumber) == 0.0);
        REQUIRE(isNumber == 0);
        int32_t isInteger = 123;
        REQUIRE(luau_host_to_integer32(root.state, index, &isInteger) == 0);
        REQUIRE(isInteger == 0);
        isInteger = 123;
        REQUIRE(luau_host_to_unsigned32(root.state, index, &isInteger) == 0);
        REQUIRE(isInteger == 0);
        isInteger = 123;
        REQUIRE(luau_host_to_integer64(root.state, index, &isInteger) == 0);
        REQUIRE(isInteger == 0);

        REQUIRE(luau_host_to_vector(root.state, index) == nullptr);
        uint64_t length = 123;
        REQUIRE(luau_host_to_string_view(root.state, index, &length) == nullptr);
        REQUIRE(length == 0);
        REQUIRE(luau_host_to_light_userdata(root.state, index) == nullptr);
        REQUIRE(luau_host_to_userdata(root.state, index) == nullptr);
        REQUIRE(luau_host_to_thread(root.state, index) == nullptr);
        length = 123;
        REQUIRE(luau_host_to_buffer(root.state, index, &length) == nullptr);
        REQUIRE(length == 0);
        REQUIRE(luau_host_to_pointer(root.state, index) == nullptr);
        REQUIRE(luau_host_to_function(root.state, index) == nullptr);
        REQUIRE(luau_host_is_global(root.state, index) == 0);

        const uint8_t* stringOutput = reinterpret_cast<const uint8_t*>(static_cast<uintptr_t>(1));
        length = 123;
        REQUIRE(luau_host_to_string(root.state, index, &stringOutput, &length) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
        REQUIRE(stringOutput == nullptr);
        REQUIRE(length == 0);
        stringOutput = reinterpret_cast<const uint8_t*>(static_cast<uintptr_t>(1));
        length = 123;
        REQUIRE(luau_host_to_display_string(root.state, index, &stringOutput, &length) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
        REQUIRE(stringOutput == nullptr);
        REQUIRE(length == 0);

        int32_t reference = 123;
        REQUIRE(luau_host_reference_create(root.state, index, &reference) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
        REQUIRE(reference <= 0);
        REQUIRE(luau_host_push_value(root.state, index) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
        REQUIRE(luau_host_stack_insert(root.state, index) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
        REQUIRE(luau_host_stack_remove(root.state, index) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
        REQUIRE(luau_host_stack_replace(root.state, index) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
        int32_t tableResult = 123;
        REQUIRE(luau_host_table_get(root.state, index, &tableResult) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
        REQUIRE(luau_host_table_set(root.state, index) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
        REQUIRE(luau_host_table_raw_get(root.state, index, &tableResult) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
        REQUIRE(luau_host_table_raw_set(root.state, index) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
        REQUIRE(luau_host_table_next(root.state, index, &tableResult) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
        REQUIRE(luau_host_table_clear(root.state, index) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
        REQUIRE(luau_host_table_clone(root.state, index) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
        REQUIRE(luau_host_metatable_get(root.state, index, &tableResult) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
        REQUIRE(luau_host_metatable_set(root.state, index, &tableResult) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
        REQUIRE(luau_host_table_set_readonly(root.state, index, 1) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
        REQUIRE(luau_host_stack_get_top(root.state) == 0);
    }

    REQUIRE(luau_host_type_name(root.state, std::numeric_limits<int32_t>::min()) == nullptr);
    REQUIRE(luau_host_type_name(root.state, std::numeric_limits<int32_t>::max()) == nullptr);
    REQUIRE(luau_host_type_name(nullptr, 0) == nullptr);
    REQUIRE(luau_host_callback_userdata(root.state, -1) == nullptr);
    REQUIRE(luau_host_callback_userdata(root.state, 0) == nullptr);
    REQUIRE(luau_host_callback_userdata(root.state, 1) == nullptr);
    REQUIRE(luau_host_callback_userdata(root.state, 256) == nullptr);

    REQUIRE(luau_host_stack_set_top(root.state, -2) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_stack_set_top(root.state, std::numeric_limits<int32_t>::min()) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_stack_check(root.state, -1, nullptr) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_stack_move(root.state, root.state, -1) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_stack_move(root.state, root.state, 1) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_stack_move(root.state, otherRoot.state, 0) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_stack_get_top(root.state) == 0);
    REQUIRE(luau_host_stack_get_top(otherRoot.state) == 0);
}

void test_invalid_table_reference_and_load_boundaries()
{
    Root root = create_root();

    // The raw table APIs are assertion-prone upstream and therefore reject an
    // ordinary non-table target before consuming keys or values.
    REQUIRE(luau_host_push_integer(root.state, 7) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_push_string(root.state, bytes("key"), 3) == LUAU_HOST_STATUS_OK);
    int32_t output = 123;
    REQUIRE(luau_host_table_raw_get(root.state, 1, &output) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_stack_get_top(root.state) == 2);
    REQUIRE(luau_host_push_integer(root.state, 9) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_table_raw_set(root.state, 1) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_stack_get_top(root.state) == 3);
    REQUIRE(luau_host_stack_set_top(root.state, 1) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_push_nil(root.state) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_table_next(root.state, 1, &output) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_stack_get_top(root.state) == 2);
    REQUIRE(luau_host_stack_set_top(root.state, 1) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_table_clear(root.state, 1) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_table_clone(root.state, 1) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_table_set_readonly(root.state, 1, 1) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_stack_get_top(root.state) == 1);

    REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_table_create(root.state, 0, 0) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_push_integer(root.state, 10) == LUAU_HOST_STATUS_OK);
    output = 123;
    REQUIRE(luau_host_metatable_set(root.state, 1, &output) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_stack_get_top(root.state) == 2);

    REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_push_integer(root.state, 42) == LUAU_HOST_STATUS_OK);
    int32_t firstReference = 0;
    REQUIRE(luau_host_reference_create(root.state, 1, &firstReference) == LUAU_HOST_STATUS_OK);
    REQUIRE(firstReference > 0);
    REQUIRE(luau_host_reference_release(root.state, firstReference) == LUAU_HOST_STATUS_OK);
    const int32_t nullOutputTop = luau_host_stack_get_top(root.state);
    REQUIRE(luau_host_reference_create(root.state, 1, nullptr) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_stack_get_top(root.state) == nullOutputTop);

    int32_t reference = 0;
    REQUIRE(luau_host_reference_create(root.state, 1, &reference) == LUAU_HOST_STATUS_OK);
    REQUIRE(reference == firstReference);
    const int32_t referenceTop = luau_host_stack_get_top(root.state);
    REQUIRE(luau_host_reference_release(root.state, reference) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_stack_get_top(root.state) == referenceTop);
    REQUIRE(luau_host_reference_release(root.state, reference) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_reference_push(root.state, reference, &output) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_reference_release(root.state, std::numeric_limits<int32_t>::max()) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_reference_push(root.state, std::numeric_limits<int32_t>::max(), &output) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_reference_push(root.state, 0, &output) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_reference_release(root.state, 0) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_reference_release(root.state, -1) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_stack_get_top(root.state) == referenceTop);

    // A load environment must be zero or an in-range table. An invalid
    // environment is rejected before the bytecode or value stack is touched.
    CompiledBuffer bytecode = compile_source("return 1");
    luau_host_status loadStatus = LUAU_HOST_STATUS_CANCELED;
    const int32_t loadTop = luau_host_stack_get_top(root.state);
    REQUIRE(
        luau_host_load(
            root.state,
            bytes("@invalid-environment"),
            bytecode.value.data,
            bytecode.value.size,
            1,
            &loadStatus) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_stack_get_top(root.state) == loadTop);
    REQUIRE(
        luau_host_load(
            root.state,
            bytes("@missing-environment"),
            bytecode.value.data,
            bytecode.value.size,
            loadTop + 1,
            &loadStatus) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_stack_get_top(root.state) == loadTop);
}

void test_invalid_execution_boundaries()
{
    Root root = create_root();
    Root otherRoot = create_root();

    REQUIRE(luau_host_pcall(root.state, 0, 0, 0) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_pcall(root.state, -1, 0, 0) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_pcall(root.state, 0, -2, 0) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_stack_get_top(root.state) == 0);

    REQUIRE(compile_and_load(root.state, "return 1", "@invalid-pcall") == LUAU_HOST_STATUS_OK);
    const int32_t pcallTop = luau_host_stack_get_top(root.state);
    REQUIRE(luau_host_pcall(root.state, 0, 0, 2) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(
        luau_host_pcall(root.state, 0, 0, std::numeric_limits<int32_t>::min()) ==
        LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_stack_get_top(root.state) == pcallTop);
    REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);

    luau_host_state* child = nullptr;
    REQUIRE(luau_host_thread_create(root.state, &child) == LUAU_HOST_STATUS_OK);
    REQUIRE(child != nullptr);
    REQUIRE(luau_host_stack_get_top(child) == 0);
    REQUIRE(luau_host_resume(child, root.state, -1) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_resume(child, root.state, 1) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_resume(child, otherRoot.state, 0) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_resume_error(child, root.state) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_stack_get_top(child) == 0);

    REQUIRE(luau_host_push_string(child, bytes("error"), 5) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_resume_error(child, otherRoot.state) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_resume(child, root.state, 2) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_stack_get_top(child) == 1);
    REQUIRE(luau_host_stack_set_top(child, 0) == LUAU_HOST_STATUS_OK);

    // Yield is only meaningful from an active, yieldable managed callback.
    // Invalid calls return the neutral callback sentinel without mutating the
    // state or triggering Luau's non-yieldable-call assertion.
    REQUIRE(luau_host_yield(root.state, -1) == 0);
    REQUIRE(luau_host_yield(root.state, 0) == 0);
    REQUIRE(luau_host_yield(root.state, 1) == 0);
    REQUIRE(luau_host_yield(child, 0) == 0);
    REQUIRE(luau_host_thread_status(root.state) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_thread_status(child) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_stack_get_top(root.state) == 1);
    REQUIRE(luau_host_stack_get_top(child) == 0);
    REQUIRE(luau_host_thread_reset(child) == LUAU_HOST_STATUS_OK);

    // lua_gc shifts the set-step-size input by 10 internally. Values beyond
    // this boundary and setter combinations that overflow upstream signed
    // arithmetic must be rejected before mutating collector settings.
    constexpr int32_t gcSetGoal = LUAU_HOST_GC_SET_GOAL_PERCENT;
    constexpr int32_t gcSetStepMultiplier = LUAU_HOST_GC_SET_STEP_MULTIPLIER_PERCENT;
    constexpr int32_t gcSetStepSize = LUAU_HOST_GC_SET_STEP_SIZE_KIB;
    int32_t gcResult = 123;
    REQUIRE(
        luau_host_collect(root.state, gcSetStepMultiplier, 0, &gcResult) ==
        LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(gcResult == 123);
    REQUIRE(
        luau_host_collect(
            root.state,
            gcSetStepMultiplier,
            std::numeric_limits<int32_t>::max(),
            &gcResult) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(gcResult == 123);
    REQUIRE(
        luau_host_collect(
            root.state,
            gcSetGoal,
            std::numeric_limits<int32_t>::max(),
            &gcResult) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(gcResult == 123);
    REQUIRE(
        luau_host_collect(
            root.state,
            gcSetStepSize,
            (std::numeric_limits<int32_t>::max() >> 10) + 1,
            &gcResult) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(gcResult == 123);

    int32_t previousGoal = 0;
    REQUIRE(luau_host_collect(root.state, gcSetGoal, 1'000'000, &previousGoal) == LUAU_HOST_STATUS_OK);
    REQUIRE(previousGoal > 0);
    REQUIRE(
        luau_host_collect(root.state, gcSetStepMultiplier, 3'000, &gcResult) ==
        LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(luau_host_collect(root.state, gcSetGoal, previousGoal, &gcResult) == LUAU_HOST_STATUS_OK);

    int32_t previousStepMultiplier = 0;
    REQUIRE(
        luau_host_collect(root.state, gcSetStepMultiplier, 100, &previousStepMultiplier) ==
        LUAU_HOST_STATUS_OK);
    REQUIRE(previousStepMultiplier > 0);
    REQUIRE(
        luau_host_collect(
            root.state,
            gcSetStepSize,
            std::numeric_limits<int32_t>::max() >> 10,
            &gcResult) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
    REQUIRE(
        luau_host_collect(root.state, gcSetStepMultiplier, previousStepMultiplier, &gcResult) ==
        LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_stack_get_top(root.state) == 1);
}

void test_allocator_quota_and_recovery()
{
    constexpr uint64_t memoryLimit = UINT64_C(1024) * 1024;
    luau_host_state_options options = {};
    options.struct_size = sizeof(options);
    options.version = LUAU_HOST_STATE_OPTIONS_VERSION;
    options.flags = LUAU_HOST_STATE_OPTION_TRACK_MEMORY;
    options.memory_limit_bytes = memoryLimit;

    Root root = create_root(&options);
    luau_host_memory_info before = {};
    before.struct_size = sizeof(before);
    REQUIRE(luau_host_memory_get(root.state, &before) == LUAU_HOST_STATUS_OK);
    REQUIRE(before.struct_size == sizeof(before));
    REQUIRE(before.tracked == 1);
    REQUIRE(before.limit_bytes == memoryLimit);
    REQUIRE(before.current_bytes > 0);
    REQUIRE(before.peak_bytes >= before.current_bytes);
    REQUIRE(before.failure == LUAU_HOST_ALLOCATOR_FAILURE_NONE);

    constexpr uint32_t memoryInfoFixedPrefixSize =
        static_cast<uint32_t>(offsetof(luau_host_memory_info, current_bytes));
    alignas(luau_host_memory_info) std::array<uint8_t, memoryInfoFixedPrefixSize + 1> shortStorage = {};
    shortStorage.fill(0xa5);
    auto* shortInfo = reinterpret_cast<luau_host_memory_info*>(shortStorage.data());
    shortInfo->struct_size = memoryInfoFixedPrefixSize;
    for (int query = 0; query < 2; ++query)
    {
        REQUIRE(luau_host_memory_get(root.state, shortInfo) == LUAU_HOST_STATUS_OK);
        REQUIRE(shortInfo->struct_size == memoryInfoFixedPrefixSize);
        REQUIRE(shortStorage[memoryInfoFixedPrefixSize] == 0xa5);
    }

    REQUIRE(luau_host_memory_arm_quota_failure(root.state) == LUAU_HOST_STATUS_OK);
    const std::vector<uint8_t> largeString(2048, static_cast<uint8_t>('x'));
    const int32_t originalTop = luau_host_stack_get_top(root.state);
    REQUIRE(
        luau_host_push_string(root.state, largeString.data(), static_cast<uint64_t>(largeString.size())) ==
        LUAU_HOST_STATUS_MEMORY_QUOTA);
    REQUIRE(luau_host_stack_get_top(root.state) == originalTop + 1);

    luau_host_memory_info failed = {};
    failed.struct_size = sizeof(failed);
    REQUIRE(luau_host_memory_get(root.state, &failed) == LUAU_HOST_STATUS_OK);
    REQUIRE(failed.failure == LUAU_HOST_ALLOCATOR_FAILURE_QUOTA);
    REQUIRE(failed.last_attempted_bytes > failed.limit_bytes);
    REQUIRE(failed.current_bytes <= failed.limit_bytes);

    REQUIRE(luau_host_stack_set_top(root.state, originalTop) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_memory_reset_failure(root.state) == LUAU_HOST_STATUS_OK);
    REQUIRE(
        luau_host_push_string(root.state, largeString.data(), static_cast<uint64_t>(largeString.size())) ==
        LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_stack_get_top(root.state) == originalTop + 1);

    luau_host_memory_info recovered = {};
    recovered.struct_size = sizeof(recovered);
    REQUIRE(luau_host_memory_get(root.state, &recovered) == LUAU_HOST_STATUS_OK);
    REQUIRE(recovered.failure == LUAU_HOST_ALLOCATOR_FAILURE_NONE);
    REQUIRE(recovered.last_attempted_bytes == 0);
    REQUIRE(recovered.current_bytes <= recovered.limit_bytes);

    // Port the retired bridge's protected buffer-allocation coverage to the
    // host-owned allocator contract.  Failure leaves one contained error;
    // clearing it and resetting telemetry makes the same root reusable.
    REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_memory_arm_quota_failure(root.state) == LUAU_HOST_STATUS_OK);
    void* nativeBuffer = reinterpret_cast<void*>(static_cast<uintptr_t>(1));
    REQUIRE(
        luau_host_buffer_create(root.state, UINT64_C(2) * 1024 * 1024, &nativeBuffer) ==
        LUAU_HOST_STATUS_MEMORY_QUOTA);
    REQUIRE(nativeBuffer == nullptr);
    REQUIRE(luau_host_stack_get_top(root.state) == 1);

    REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_memory_reset_failure(root.state) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_buffer_create(root.state, 16, &nativeBuffer) == LUAU_HOST_STATUS_OK);
    REQUIRE(nativeBuffer != nullptr);
    REQUIRE(luau_host_stack_get_top(root.state) == 1);
    REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);

    // Upstream luau_load contains allocator failure in its own protected
    // frame.  The host call therefore succeeds while load_status reports the
    // ordinary load error, and the root remains usable after stack cleanup.
    const std::string loadSource = "return [[" + std::string(1025, 'x') + "]]";
    CompiledBuffer loadBytecode = compile_source(loadSource);
    REQUIRE(luau_host_memory_arm_quota_failure(root.state) == LUAU_HOST_STATUS_OK);
    luau_host_status loadStatus = LUAU_HOST_STATUS_INVALID_ARGUMENT;
    REQUIRE(
        luau_host_load(
            root.state,
            bytes("@protected-load-oom"),
            loadBytecode.value.data,
            loadBytecode.value.size,
            0,
            &loadStatus) == LUAU_HOST_STATUS_OK);
    REQUIRE(loadStatus == LUAU_HOST_STATUS_LUA_ERROR);
    REQUIRE(luau_host_stack_get_top(root.state) == 1);

    luau_host_memory_info loadFailure = {};
    loadFailure.struct_size = sizeof(loadFailure);
    REQUIRE(luau_host_memory_get(root.state, &loadFailure) == LUAU_HOST_STATUS_OK);
    REQUIRE(loadFailure.failure == LUAU_HOST_ALLOCATOR_FAILURE_QUOTA);
    REQUIRE(loadFailure.last_attempted_bytes > loadFailure.limit_bytes);

    REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_memory_reset_failure(root.state) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_push_integer(root.state, 7) == LUAU_HOST_STATUS_OK);
    int32_t recoveredInteger = 0;
    REQUIRE(luau_host_to_integer64(root.state, -1, &recoveredInteger) == 7);
    REQUIRE(recoveredInteger != 0);
    REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);

    // Positive lua_settop and lua_xmove destination growth must be reserved by
    // the host even when upstream LuauAutoStack is disabled. A failed reserve
    // leaves one protected error, preserves the caller's values, and the root
    // must remain safe to reuse and close.
    const int32_t setTopBoundary = luau_host_stack_get_top(root.state);
    REQUIRE(luau_host_memory_arm_quota_failure(root.state) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_stack_set_top(root.state, 4096) == LUAU_HOST_STATUS_MEMORY_QUOTA);
    REQUIRE(luau_host_stack_get_top(root.state) == setTopBoundary + 1);
    REQUIRE(luau_host_stack_set_top(root.state, setTopBoundary) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_memory_reset_failure(root.state) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_push_integer(root.state, 42) == LUAU_HOST_STATUS_OK);
    int32_t isInteger = 0;
    REQUIRE(luau_host_to_integer64(root.state, -1, &isInteger) == 42);
    REQUIRE(isInteger != 0);

    REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);
    luau_host_state* child = nullptr;
    REQUIRE(luau_host_thread_create(root.state, &child) == LUAU_HOST_STATUS_OK);
    REQUIRE(child != nullptr);
    int32_t childReference = 0;
    REQUIRE(luau_host_reference_create(root.state, -1, &childReference) == LUAU_HOST_STATUS_OK);
    REQUIRE(childReference > 0);
    REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);
    for (int32_t value = 0; value < 1024; ++value)
        REQUIRE(luau_host_push_integer(root.state, value) == LUAU_HOST_STATUS_OK);

    const int32_t moveBoundary = luau_host_stack_get_top(root.state);
    const int32_t childBoundary = luau_host_stack_get_top(child);
    REQUIRE(luau_host_memory_arm_quota_failure(root.state) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_stack_move(root.state, child, moveBoundary) == LUAU_HOST_STATUS_MEMORY_QUOTA);
    REQUIRE(luau_host_stack_get_top(root.state) == moveBoundary + 1);
    REQUIRE(luau_host_stack_get_top(child) == childBoundary);
    REQUIRE(luau_host_stack_set_top(root.state, moveBoundary) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_to_integer64(root.state, -1, &isInteger) == 1023);
    REQUIRE(isInteger != 0);
    REQUIRE(luau_host_memory_reset_failure(root.state) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_reference_release(root.state, childReference) == LUAU_HOST_STATUS_OK);

    luau_host_state_options tiny = options;
    tiny.memory_limit_bytes = 1;
    luau_host_state* rejected = reinterpret_cast<luau_host_state*>(static_cast<uintptr_t>(1));
    luau_host_memory_info createFailure = {};
    createFailure.struct_size = sizeof(createFailure);
    REQUIRE(luau_host_state_create(&tiny, &rejected, &createFailure) == LUAU_HOST_STATUS_MEMORY_QUOTA);
    REQUIRE(rejected == nullptr);
    REQUIRE(createFailure.failure == LUAU_HOST_ALLOCATOR_FAILURE_QUOTA);
    REQUIRE(createFailure.limit_bytes == 1);
    REQUIRE(createFailure.last_attempted_bytes > createFailure.limit_bytes);
}

int callbackInvocations = 0;
luau_host_status callbackPushStatus = LUAU_HOST_STATUS_INVALID_ARGUMENT;
int destructorInvocations = 0;
int destructorPayload = 0;
int userdataPayloadDestructions = 0;
int maximumOwnerDestructions = 0;
void* expectedUserdataDestructorPointer = nullptr;
int userdataDestructorPhase = 0;
std::array<int, 4> userdataDestructorPhaseCounts = {};
int userdataDestructorPointerMismatches = 0;
int interruptPolls = 0;
int nonYieldableInterruptPolls = 0;
int executionInterruptKinds = 0;
int gcInterruptKinds = 0;
int invalidInterruptKinds = 0;
int invalidCallbackReturn = 0;
bool invalidCallbackReturnsTopPlusOne = false;

int32_t LUAU_HOST_CALL managed_callback(luau_host_state* state)
{
    int* value = static_cast<int*>(luau_host_callback_userdata(state, 1));
    if (value)
        ++callbackInvocations;
    callbackPushStatus = luau_host_push_integer(state, value ? *value : -1);
    return callbackPushStatus == LUAU_HOST_STATUS_OK ? 1 : 0;
}

void LUAU_HOST_CALL userdata_destructor(void* userdata)
{
    ++destructorInvocations;
    if (userdata)
    {
        destructorPayload = *static_cast<int*>(userdata);
        if (destructorPayload == 1234)
            ++userdataPayloadDestructions;
        if (destructorPayload == 254)
            ++maximumOwnerDestructions;
    }
}

void LUAU_HOST_CALL recording_userdata_destructor(void* userdata)
{
    if (userdata != expectedUserdataDestructorPointer)
        ++userdataDestructorPointerMismatches;
    if (userdataDestructorPhase >= 0 &&
        static_cast<size_t>(userdataDestructorPhase) < userdataDestructorPhaseCounts.size())
        ++userdataDestructorPhaseCounts[static_cast<size_t>(userdataDestructorPhase)];
}

void record_interrupt_kind(luau_host_interrupt_kind kind)
{
    if (kind == LUAU_HOST_INTERRUPT_EXECUTION)
        ++executionInterruptKinds;
    else if (kind == LUAU_HOST_INTERRUPT_GC)
        ++gcInterruptKinds;
    else
        ++invalidInterruptKinds;
}

void reset_interrupt_kind_counts()
{
    executionInterruptKinds = 0;
    gcInterruptKinds = 0;
    invalidInterruptKinds = 0;
}

int32_t LUAU_HOST_CALL interrupt_poll(luau_host_state*, luau_host_interrupt_kind kind)
{
    record_interrupt_kind(kind);
    ++interruptPolls;
    return 1;
}

int32_t LUAU_HOST_CALL continue_interrupt_poll(luau_host_state*, luau_host_interrupt_kind kind)
{
    record_interrupt_kind(kind);
    return 0;
}

int32_t LUAU_HOST_CALL nonyieldable_interrupt_poll(
    luau_host_state* state,
    luau_host_interrupt_kind kind)
{
    record_interrupt_kind(kind);
    if (kind != LUAU_HOST_INTERRUPT_EXECUTION || luau_host_is_yieldable(state) != 0)
        return 0;

    ++nonYieldableInterruptPolls;
    return 1;
}

int32_t LUAU_HOST_CALL invalid_return_callback(luau_host_state* state)
{
    return invalidCallbackReturnsTopPlusOne
        ? luau_host_stack_get_top(state) + 1
        : invalidCallbackReturn;
}

luau_host_callback_table callback_table()
{
    luau_host_callback_table callbacks = {};
    callbacks.struct_size = sizeof(callbacks);
    callbacks.version = LUAU_HOST_CALLBACK_TABLE_VERSION;
    callbacks.registration_id = 1;
    return callbacks;
}

void test_callback_and_destructor_lifetime()
{
    callbackInvocations = 0;
    callbackPushStatus = LUAU_HOST_STATUS_INVALID_ARGUMENT;
    destructorInvocations = 0;
    destructorPayload = 0;
    userdataPayloadDestructions = 0;
    maximumOwnerDestructions = 0;

    Root root = create_root();
    int callbackValue = 99;
    int callbackOwnerValue = 4321;
    REQUIRE(luau_host_push_light_userdata(root.state, &callbackValue, 0) == LUAU_HOST_STATUS_OK);

    {
        luau_host_callback_table callbacks = callback_table();
        callbacks.userdata = &callbackOwnerValue;
        callbacks.userdata_destructor = userdata_destructor;
        callbacks.managed_function = managed_callback;
        int32_t ownerTransferred = 0;
        int32_t errorObject = 0;
        REQUIRE(luau_host_push_callback(root.state, &callbacks, bytes("conformance_callback"), 20, 1, &ownerTransferred, &errorObject) == LUAU_HOST_STATUS_OK);
        REQUIRE(ownerTransferred == 1);
        REQUIRE(errorObject == 0);
    }

    REQUIRE(luau_host_pcall(root.state, 0, 1, 0) == LUAU_HOST_STATUS_OK);
    REQUIRE(callbackInvocations == 1);
    REQUIRE(callbackPushStatus == LUAU_HOST_STATUS_OK);
    int32_t isInteger = 0;
    REQUIRE(luau_host_to_integer64(root.state, -1, &isInteger) == callbackValue);
    REQUIRE(isInteger != 0);
    REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);
    int32_t gcResult = 0;
    REQUIRE(luau_host_collect(root.state, LUAU_HOST_GC_COLLECT, 0, &gcResult) == LUAU_HOST_STATUS_OK);
    REQUIRE(destructorInvocations == 1);
    REQUIRE(destructorPayload == callbackOwnerValue);

    destructorInvocations = 0;
    destructorPayload = 0;

    {
        luau_host_callback_table callbacks = callback_table();
        callbacks.userdata_destructor = userdata_destructor;
        void* payload = nullptr;
        REQUIRE(luau_host_userdata_create_with_destructor(root.state, sizeof(int), &callbacks, &payload) == LUAU_HOST_STATUS_OK);
        REQUIRE(payload != nullptr);
        *static_cast<int*>(payload) = 1234;
    }

    for (int index = 0; index < 255; ++index)
        REQUIRE(luau_host_push_nil(root.state) == LUAU_HOST_STATUS_OK);
    {
        luau_host_callback_table callbacks = callback_table();
        callbacks.managed_function = managed_callback;
        int32_t ownerTransferred = 123;
        int32_t errorObject = 123;
        REQUIRE(
            luau_host_push_callback(
                root.state,
                &callbacks,
                bytes("x"),
                std::numeric_limits<uint64_t>::max(),
                255,
                &ownerTransferred,
                &errorObject) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
        REQUIRE(ownerTransferred == 0);
        REQUIRE(errorObject == 0);
        REQUIRE(luau_host_stack_get_top(root.state) == 256);
    }
    REQUIRE(luau_host_stack_set_top(root.state, 1) == LUAU_HOST_STATUS_OK);

    int rejectedMaximumUpvalueOwner = 255;
    for (int index = 0; index < 255; ++index)
        REQUIRE(luau_host_push_nil(root.state) == LUAU_HOST_STATUS_OK);
    {
        luau_host_callback_table callbacks = callback_table();
        callbacks.userdata = &rejectedMaximumUpvalueOwner;
        callbacks.userdata_destructor = userdata_destructor;
        callbacks.managed_function = managed_callback;
        int32_t ownerTransferred = 123;
        int32_t errorObject = 123;
        REQUIRE(
            luau_host_push_callback(
                root.state,
                &callbacks,
                bytes("owned_maximum_upvalues"),
                22,
                255,
                &ownerTransferred,
                &errorObject) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
        REQUIRE(ownerTransferred == 0);
        REQUIRE(errorObject == 0);
        REQUIRE(destructorInvocations == 0);
        REQUIRE(luau_host_stack_get_top(root.state) == 256);
    }
    REQUIRE(luau_host_stack_set_top(root.state, 1) == LUAU_HOST_STATUS_OK);

    for (int index = 0; index < 255; ++index)
        REQUIRE(luau_host_push_nil(root.state) == LUAU_HOST_STATUS_OK);
    {
        luau_host_callback_table callbacks = callback_table();
        callbacks.managed_function = managed_callback;
        int32_t ownerTransferred = 123;
        int32_t errorObject = 123;
        REQUIRE(
            luau_host_push_callback(
                root.state,
                &callbacks,
                nullptr,
                0,
                255,
                &ownerTransferred,
                &errorObject) == LUAU_HOST_STATUS_INVALID_ARGUMENT);
        REQUIRE(ownerTransferred == 0);
        REQUIRE(errorObject == 0);
        REQUIRE(luau_host_stack_get_top(root.state) == 256);
    }
    REQUIRE(luau_host_stack_set_top(root.state, 1) == LUAU_HOST_STATUS_OK);

    int maximumUpvalueOwner = 254;
    for (int index = 0; index < 254; ++index)
        REQUIRE(luau_host_push_nil(root.state) == LUAU_HOST_STATUS_OK);
    {
        luau_host_callback_table callbacks = callback_table();
        callbacks.userdata = &maximumUpvalueOwner;
        callbacks.userdata_destructor = userdata_destructor;
        callbacks.managed_function = managed_callback;
        int32_t ownerTransferred = 0;
        int32_t errorObject = 0;
        REQUIRE(
            luau_host_push_callback(
                root.state,
                &callbacks,
                bytes("maximum_upvalues"),
                16,
                254,
                &ownerTransferred,
                &errorObject) == LUAU_HOST_STATUS_OK);
        REQUIRE(ownerTransferred == 1);
        REQUIRE(errorObject == 0);
    }
    REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);

    REQUIRE(destructorInvocations == 0);
    root.close();
    REQUIRE(destructorInvocations == 2);
    REQUIRE(userdataPayloadDestructions == 1);
    REQUIRE(maximumOwnerDestructions == 1);
}

void test_callback_return_validation_and_recovery()
{
    Root root = create_root();

    auto pushInvalidCallback = [](luau_host_state* state) {
        luau_host_callback_table callbacks = callback_table();
        callbacks.managed_function = invalid_return_callback;
        int32_t ownerTransferred = 123;
        int32_t errorObject = 123;
        REQUIRE(
            luau_host_push_callback(
                state,
                &callbacks,
                nullptr,
                0,
                0,
                &ownerTransferred,
                &errorObject) == LUAU_HOST_STATUS_OK);
        REQUIRE(ownerTransferred == 0);
        REQUIRE(errorObject == 0);
    };

    invalidCallbackReturnsTopPlusOne = true;
    pushInvalidCallback(root.state);
    REQUIRE(luau_host_pcall(root.state, 0, 0, 0) == LUAU_HOST_STATUS_LUA_ERROR);
    REQUIRE(luau_host_stack_get_top(root.state) == 1);
    REQUIRE(!string_at(root.state, -1).empty());
    REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);

    invalidCallbackReturnsTopPlusOne = false;
    invalidCallbackReturn = std::numeric_limits<int32_t>::max();
    pushInvalidCallback(root.state);
    REQUIRE(luau_host_pcall(root.state, 0, 0, 0) == LUAU_HOST_STATUS_LUA_ERROR);
    REQUIRE(luau_host_stack_get_top(root.state) == 1);
    REQUIRE(!string_at(root.state, -1).empty());
    REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);

    luau_host_state* child = nullptr;
    REQUIRE(luau_host_thread_create(root.state, &child) == LUAU_HOST_STATUS_OK);
    REQUIRE(child != nullptr);

    for (const int32_t invalidResult : {-2, -1})
    {
        invalidCallbackReturn = invalidResult;
        pushInvalidCallback(child);
        REQUIRE(luau_host_resume(child, root.state, 0) == LUAU_HOST_STATUS_LUA_ERROR);
        REQUIRE(luau_host_thread_status(child) == LUAU_HOST_STATUS_LUA_ERROR);
        REQUIRE(luau_host_stack_get_top(child) == 1);
        REQUIRE(!string_at(child, -1).empty());
        REQUIRE(luau_host_thread_reset(child) == LUAU_HOST_STATUS_OK);
        REQUIRE(luau_host_is_thread_reset(child) != 0);
    }

    REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);
    REQUIRE(compile_and_load(root.state, "return 7", "@callback-return-recovery") == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_pcall(root.state, 0, 1, 0) == LUAU_HOST_STATUS_OK);
    int32_t isNumber = 0;
    REQUIRE(luau_host_to_number(root.state, -1, &isNumber) == 7.0);
    REQUIRE(isNumber != 0);
}

void test_userdata_wrapper_visibility_and_destructor_lifetime()
{
    userdataDestructorPhaseCounts.fill(0);
    userdataDestructorPointerMismatches = 0;
    expectedUserdataDestructorPointer = nullptr;
    userdataDestructorPhase = 0;

    Root root = create_root();
    luau_host_callback_table callbacks = callback_table();
    callbacks.userdata_destructor = recording_userdata_destructor;

    void* sized = nullptr;
    REQUIRE(
        luau_host_userdata_create_with_destructor(root.state, 8, &callbacks, &sized) ==
        LUAU_HOST_STATUS_OK);
    REQUIRE(sized != nullptr);
    REQUIRE(reinterpret_cast<uintptr_t>(sized) % alignof(std::max_align_t) == 0);
    REQUIRE(luau_host_object_length(root.state, -1) == 8);
    REQUIRE(luau_host_to_userdata(root.state, -1) == sized);
    REQUIRE(luau_host_to_pointer(root.state, -1) == sized);
    std::memset(sized, 0x5a, 8);

    expectedUserdataDestructorPointer = sized;
    userdataDestructorPhase = 1;
    REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);
    int32_t gcResult = 0;
    REQUIRE(
        luau_host_collect(root.state, LUAU_HOST_GC_COLLECT, 0, &gcResult) ==
        LUAU_HOST_STATUS_OK);
    REQUIRE(userdataDestructorPhaseCounts[1] == 1);
    REQUIRE(userdataDestructorPointerMismatches == 0);

    void* zeroCollected = nullptr;
    REQUIRE(
        luau_host_userdata_create_with_destructor(root.state, 0, &callbacks, &zeroCollected) ==
        LUAU_HOST_STATUS_OK);
    REQUIRE(zeroCollected != nullptr);
    REQUIRE(luau_host_object_length(root.state, -1) == 0);
    REQUIRE(luau_host_to_userdata(root.state, -1) == zeroCollected);
    REQUIRE(luau_host_to_pointer(root.state, -1) == zeroCollected);

    expectedUserdataDestructorPointer = zeroCollected;
    userdataDestructorPhase = 2;
    REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);
    REQUIRE(
        luau_host_collect(root.state, LUAU_HOST_GC_COLLECT, 0, &gcResult) ==
        LUAU_HOST_STATUS_OK);
    REQUIRE(userdataDestructorPhaseCounts[2] == 1);
    REQUIRE(userdataDestructorPointerMismatches == 0);

    // Ordinary tagged userdata can contain the wrapper's private magic bytes
    // without being shifted: the reserved destructor tag is also required.
    constexpr uint64_t mimickedPrivateMagic = UINT64_C(0x6c75617575646174);
    void* ordinary = nullptr;
    REQUIRE(luau_host_userdata_create(root.state, 32, 0, &ordinary) == LUAU_HOST_STATUS_OK);
    REQUIRE(ordinary != nullptr);
    std::memcpy(ordinary, &mimickedPrivateMagic, sizeof(mimickedPrivateMagic));
    REQUIRE(luau_host_object_length(root.state, -1) == 32);
    REQUIRE(luau_host_to_userdata(root.state, -1) == ordinary);
    REQUIRE(luau_host_to_pointer(root.state, -1) == ordinary);
    REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);

    int lightPayload = 42;
    REQUIRE(luau_host_push_light_userdata(root.state, &lightPayload, 0) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_to_light_userdata(root.state, -1) == &lightPayload);
    REQUIRE(luau_host_to_userdata(root.state, -1) == &lightPayload);
    REQUIRE(luau_host_to_pointer(root.state, -1) == &lightPayload);
    REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);

    void* zeroClosed = nullptr;
    REQUIRE(
        luau_host_userdata_create_with_destructor(root.state, 0, &callbacks, &zeroClosed) ==
        LUAU_HOST_STATUS_OK);
    REQUIRE(zeroClosed != nullptr);
    REQUIRE(luau_host_object_length(root.state, -1) == 0);
    REQUIRE(luau_host_to_userdata(root.state, -1) == zeroClosed);
    REQUIRE(luau_host_to_pointer(root.state, -1) == zeroClosed);

    expectedUserdataDestructorPointer = zeroClosed;
    userdataDestructorPhase = 3;
    root.close();
    REQUIRE(userdataDestructorPhaseCounts[3] == 1);
    REQUIRE(userdataDestructorPointerMismatches == 0);
    REQUIRE(
        userdataDestructorPhaseCounts[1] +
            userdataDestructorPhaseCounts[2] +
            userdataDestructorPhaseCounts[3] ==
        3);
}

void test_interrupt_yield_and_recovery()
{
    interruptPolls = 0;
    reset_interrupt_kind_counts();
    Root root = create_root();
    luau_host_state* child = nullptr;
    REQUIRE(luau_host_thread_create(root.state, &child) == LUAU_HOST_STATUS_OK);
    REQUIRE(child != nullptr);
    REQUIRE(compile_and_load(child, "while true do end", "@interrupt-yield") == LUAU_HOST_STATUS_OK);

    luau_host_callback_table callbacks = callback_table();
    callbacks.interrupt_poll = interrupt_poll;
    REQUIRE(luau_host_interrupt_install(child, &callbacks) == LUAU_HOST_STATUS_OK);
    const luau_host_status result = luau_host_resume(child, root.state, 0);
    luau_host_interrupt_uninstall(child);

    REQUIRE(result == LUAU_HOST_STATUS_YIELDED);
    REQUIRE(interruptPolls > 0);
    REQUIRE(invalidInterruptKinds == 0);
    REQUIRE(executionInterruptKinds + gcInterruptKinds == interruptPolls);
    REQUIRE(luau_host_thread_status(child) == LUAU_HOST_STATUS_YIELDED);
    REQUIRE(luau_host_thread_reset(child) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_is_thread_reset(child) != 0);

    // Installing the same poll twice must not double-count the state.  A
    // single uninstall must permit a different function pointer, matching the
    // Unity domain-reload lifecycle exercised by the retired bridge test.
    REQUIRE(luau_host_interrupt_install(child, &callbacks) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_interrupt_install(child, &callbacks) == LUAU_HOST_STATUS_OK);
    luau_host_interrupt_uninstall(child);

    luau_host_callback_table continueCallbacks = callback_table();
    continueCallbacks.interrupt_poll = continue_interrupt_poll;
    REQUIRE(luau_host_interrupt_install(child, &continueCallbacks) == LUAU_HOST_STATUS_OK);
    luau_host_interrupt_uninstall(child);
    REQUIRE(luau_host_interrupt_install(child, &callbacks) == LUAU_HOST_STATUS_OK);
    luau_host_interrupt_uninstall(child);
}

void test_nonyieldable_interrupt_hard_unwind_and_recovery()
{
    nonYieldableInterruptPolls = 0;
    reset_interrupt_kind_counts();
    Root root = create_root();
    int32_t resultCount = 0;
    REQUIRE(
        luau_host_open_library(root.state, LUAU_HOST_LIBRARY_STRING, &resultCount) ==
        LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_stack_set_top(root.state, 0) == LUAU_HOST_STATUS_OK);

    luau_host_state* child = nullptr;
    REQUIRE(luau_host_thread_create(root.state, &child) == LUAU_HOST_STATUS_OK);
    REQUIRE(child != nullptr);
    REQUIRE(
        compile_and_load(
            child,
            "local haystack = string.rep('x', 100); "
            "local pattern = string.rep('x?', 100) .. string.rep('x', 100); "
            "return string.find(haystack, pattern)",
            "@nonyieldable-interrupt") == LUAU_HOST_STATUS_OK);

    luau_host_callback_table callbacks = callback_table();
    callbacks.interrupt_poll = nonyieldable_interrupt_poll;
    REQUIRE(luau_host_interrupt_install(child, &callbacks) == LUAU_HOST_STATUS_OK);
    const luau_host_status result = luau_host_resume(child, root.state, 0);
    luau_host_interrupt_uninstall(child);

    REQUIRE(result == LUAU_HOST_STATUS_CANCELED);
    REQUIRE(nonYieldableInterruptPolls > 0);
    REQUIRE(invalidInterruptKinds == 0);
    REQUIRE(executionInterruptKinds + gcInterruptKinds >= nonYieldableInterruptPolls);
    REQUIRE(luau_host_thread_status(child) == LUAU_HOST_STATUS_CANCELED);
    REQUIRE(luau_host_thread_status(child) == LUAU_HOST_STATUS_CANCELED);
    REQUIRE(luau_host_memory_reset_failure(root.state) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_thread_reset(child) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_is_thread_reset(child) != 0);
    REQUIRE(luau_host_thread_status(child) == LUAU_HOST_STATUS_OK);
}

void test_multi_root_interrupt_uninstall_preserves_other_root()
{
    interruptPolls = 0;
    reset_interrupt_kind_counts();
    Root firstRoot = create_root();
    Root secondRoot = create_root();
    luau_host_state* firstChild = nullptr;
    luau_host_state* secondChild = nullptr;
    REQUIRE(luau_host_thread_create(firstRoot.state, &firstChild) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_thread_create(secondRoot.state, &secondChild) == LUAU_HOST_STATUS_OK);
    REQUIRE(compile_and_load(firstChild, "while true do end", "@first-root-interrupt") == LUAU_HOST_STATUS_OK);
    REQUIRE(compile_and_load(secondChild, "while true do end", "@second-root-interrupt") == LUAU_HOST_STATUS_OK);

    luau_host_callback_table callbacks = callback_table();
    callbacks.interrupt_poll = interrupt_poll;
    REQUIRE(luau_host_interrupt_install(firstChild, &callbacks) == LUAU_HOST_STATUS_OK);
    REQUIRE(luau_host_interrupt_install(secondChild, &callbacks) == LUAU_HOST_STATUS_OK);

    // Removing one root must not clear the process-wide poll while another
    // independent root still owns an installation.
    luau_host_interrupt_uninstall(firstChild);
    REQUIRE(luau_host_resume(secondChild, secondRoot.state, 0) == LUAU_HOST_STATUS_YIELDED);
    REQUIRE(interruptPolls > 0);
    REQUIRE(invalidInterruptKinds == 0);
    REQUIRE(executionInterruptKinds + gcInterruptKinds == interruptPolls);
    REQUIRE(luau_host_thread_reset(secondChild) == LUAU_HOST_STATUS_OK);
    luau_host_interrupt_uninstall(secondChild);

    // Once the last owner uninstalls, a different poll pointer is legal.
    luau_host_callback_table continueCallbacks = callback_table();
    continueCallbacks.interrupt_poll = continue_interrupt_poll;
    REQUIRE(luau_host_interrupt_install(firstChild, &continueCallbacks) == LUAU_HOST_STATUS_OK);
    luau_host_interrupt_uninstall(firstChild);
}

using TestFunction = void (*)();

int run_test(const char* name, TestFunction test)
{
    try
    {
        test();
        std::cout << "[PASS] " << name << '\n';
        return 0;
    }
    catch (const std::exception& exception)
    {
        std::cerr << "[FAIL] " << name << ": " << exception.what() << '\n';
        return 1;
    }
    catch (...)
    {
        std::cerr << "[FAIL] " << name << ": unknown exception\n";
        return 1;
    }
}
} // namespace

int main()
{
    int failures = 0;
    failures += run_test("ABI query, truncation, and invalid arguments", test_abi_query);
    failures += run_test("compiler and host-buffer ownership", test_compile_and_buffer_ownership);
    failures += run_test("root and child-thread lifecycle", test_root_and_thread_lifecycle);
    failures += run_test("load, pcall, ordinary error containment, and reuse", test_execution_error_containment_and_reuse);
    failures += run_test("stack, table, and registry-reference operations", test_stack_tables_and_references);
    failures += run_test("numeric conversion finite-range validation", test_numeric_conversion_boundaries);
    failures += run_test("invalid observer and stack boundaries", test_invalid_observer_and_stack_boundaries);
    failures += run_test("invalid table, reference, and load boundaries", test_invalid_table_reference_and_load_boundaries);
    failures += run_test("invalid execution and yield boundaries", test_invalid_execution_boundaries);
    failures += run_test("tracked allocator quota telemetry and recovery", test_allocator_quota_and_recovery);
    failures += run_test("managed callback and userdata-destructor lifetime", test_callback_and_destructor_lifetime);
    failures += run_test("managed callback return validation and recovery", test_callback_return_validation_and_recovery);
    failures += run_test("userdata wrapper visibility and destructor lifetime", test_userdata_wrapper_visibility_and_destructor_lifetime);
    failures += run_test("interrupt-driven coroutine yield and reset", test_interrupt_yield_and_recovery);
    failures += run_test(
        "non-yieldable interrupt hard unwind and reset",
        test_nonyieldable_interrupt_hard_unwind_and_recovery);
    failures += run_test(
        "multi-root interrupt uninstall ownership",
        test_multi_root_interrupt_uninstall_preserves_other_root);

    if (failures != 0)
        std::cerr << failures << " conformance test group(s) failed\n";

    return failures == 0 ? 0 : 1;
}
