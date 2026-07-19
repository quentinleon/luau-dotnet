#include "luau_host.h"

#include <cstddef>
#include <cstdint>

namespace
{
constexpr size_t kMaximumSourceBytes = 64 * 1024;

int option_value(uint8_t byte, int minimum, int span)
{
    return minimum + int(byte % uint8_t(span));
}
} // namespace

extern "C" int LLVMFuzzerTestOneInput(const uint8_t* data, size_t size)
{
    if (!data || size > kMaximumSourceBytes)
        return 0;

    // Ordinary corpus entries are source bytes and must reach the compiler
    // unchanged. A leading '#'+five control bytes opts into malformed option/
    // null-pointer fuzzing; keeping that envelope explicit prevents readable
    // hostile-source seeds from being rejected as invalid option records.
    const bool hasControlEnvelope = size >= 6 && data[0] == uint8_t('#');
    const uint8_t selector = hasControlEnvelope ? data[1] : 0;
    luau_host_compile_options options = {};
    options.struct_size = (selector & 0x01) != 0 ? sizeof(options) : uint32_t(selector);
    options.version = (selector & 0x02) != 0 ? LUAU_HOST_COMPILE_OPTIONS_VERSION : uint16_t(selector);
    options.optimization_level = option_value(hasControlEnvelope ? data[2] : 0, -2, 7);
    options.debug_level = option_value(hasControlEnvelope ? data[3] : 0, -2, 7);
    options.type_info_level = option_value(hasControlEnvelope ? data[4] : 0, -2, 6);
    options.coverage_level = option_value(hasControlEnvelope ? data[5] : 0, -2, 7);
    options.flags = (selector & 0x04) != 0 ? 0 : uint32_t(selector);
    options.reserved0 = (selector & 0x08) != 0 ? 0 : uint16_t(selector);
    options.reserved1 = (selector & 0x10) != 0 ? 0 : uint32_t(selector);

    const size_t sourceOffset = hasControlEnvelope ? 6 : 0;
    const uint8_t* source = hasControlEnvelope && (selector & 0x20) != 0
        ? nullptr
        : data + sourceOffset;
    const uint64_t sourceSize = uint64_t(size - sourceOffset);
    const luau_host_compile_options* selectedOptions = hasControlEnvelope && (selector & 0x40) != 0
        ? &options
        : nullptr;

    luau_host_buffer output = {};
    const luau_host_status status = luau_host_compile(source, sourceSize, selectedOptions, &output);
    if (status == LUAU_HOST_STATUS_OK)
    {
        if (!output.data || output.size == 0)
            __builtin_trap();
        luau_host_buffer_free(&output);
    }
    else if (output.data || output.size != 0)
    {
        __builtin_trap();
    }

    return 0;
}
