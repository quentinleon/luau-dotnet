#include "luau_host.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <limits>

namespace
{
thread_local int32_t callbackAction = 0;

int32_t LUAU_HOST_CALL fuzz_callback(luau_host_state* state)
{
    const uint64_t registrationId = luau_host_callback_registration_id(state);
    switch (callbackAction & 3)
    {
    case 0: return 0;
    case 1:
        return luau_host_push_integer(state, static_cast<int64_t>(registrationId)) == LUAU_HOST_STATUS_OK ? 1 : 0;
    case 2: return LUAU_HOST_CALLBACK_ERROR;
    default: return std::numeric_limits<int32_t>::max();
    }
}

int32_t LUAU_HOST_CALL fuzz_interrupt(luau_host_state*, luau_host_interrupt_kind)
{
    return callbackAction & 1;
}

uint64_t read_u64(const uint8_t* data, size_t size)
{
    uint64_t value = 0;
    const size_t copied = size < sizeof(value) ? size : sizeof(value);
    if (copied != 0)
        std::memcpy(&value, data, copied);
    return value;
}
} // namespace

extern "C" int LLVMFuzzerTestOneInput(const uint8_t* data, size_t size)
{
    if (!data || size > 4096)
        return 0;

    const uint8_t selector = size == 0 ? 0 : data[0];
    callbackAction = selector;

    alignas(luau_host_abi_info) std::array<uint8_t, sizeof(luau_host_abi_info)> abiBytes = {};
    const uint32_t callerSize = size > 1
        ? uint32_t(read_u64(data + 1, size - 1))
        : uint32_t(selector);
    (void)luau_host_get_abi_info(callerSize, reinterpret_cast<luau_host_abi_info*>(abiBytes.data()));
    (void)luau_host_get_abi_info(callerSize, nullptr);

    luau_host_state_options options = {};
    options.struct_size = sizeof(options);
    options.version = LUAU_HOST_STATE_OPTIONS_VERSION;
    options.flags = LUAU_HOST_STATE_OPTION_TRACK_MEMORY;
    options.memory_limit_bytes = UINT64_C(16) * 1024 * 1024;

    luau_host_state* state = nullptr;
    if (luau_host_state_create(&options, &state, nullptr) != LUAU_HOST_STATUS_OK || !state)
        return 0;

    const size_t payloadOffset = size < 9 ? size : 9;
    const uint8_t* payload = data + payloadOffset;
    const uint64_t payloadSize = uint64_t(size - payloadOffset);
    (void)luau_host_push_string(state, payload, payloadSize);

    int32_t token = 0;
    if (luau_host_stack_get_top(state) > 0 &&
        luau_host_reference_create(state, -1, &token) == LUAU_HOST_STATUS_OK)
    {
        int32_t type = -1;
        (void)luau_host_reference_push(state, token, &type);
        (void)luau_host_stack_set_top(state, 1);
        (void)luau_host_reference_release(state, token);
        (void)luau_host_reference_push(state, token, &type);
        (void)luau_host_reference_release(state, token);
    }

    const int32_t arbitraryToken = static_cast<int32_t>(read_u64(data, size));
    int32_t ignoredType = -1;
    (void)luau_host_reference_push(state, arbitraryToken, &ignoredType);
    (void)luau_host_reference_release(state, arbitraryToken);

    (void)luau_host_stack_set_top(state, 0);
    luau_host_callback_table callbacks = {};
    callbacks.struct_size = sizeof(callbacks);
    callbacks.version = LUAU_HOST_CALLBACK_TABLE_VERSION;
    callbacks.registration_id = read_u64(data, size);
    callbacks.managed_function = fuzz_callback;
    if (luau_host_push_callback(state, &callbacks, payload, payloadSize, 0, nullptr, nullptr) == LUAU_HOST_STATUS_OK)
        (void)luau_host_pcall(state, 0, 1, 0);

    (void)luau_host_stack_set_top(state, 0);
    callbacks = {};
    callbacks.struct_size = sizeof(callbacks);
    callbacks.version = LUAU_HOST_CALLBACK_TABLE_VERSION;
    callbacks.interrupt_poll = fuzz_interrupt;
    if (luau_host_interrupt_install(state, &callbacks) == LUAU_HOST_STATUS_OK)
    {
        (void)luau_host_collect(state);
        luau_host_interrupt_uninstall(state);
    }

    luau_host_state_close(state);
    return 0;
}
