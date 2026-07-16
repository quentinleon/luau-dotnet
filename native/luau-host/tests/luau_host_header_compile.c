#include "luau_host.h"

_Static_assert(sizeof(luau_host_buffer) == 16, "luau_host_buffer must be stable in C");
_Static_assert(sizeof(luau_host_state_options) == 16, "luau_host_state_options must be stable in C");

int luau_host_c_header_compile_probe(void)
{
    luau_host_abi_info info = {0};
    luau_host_compile_options options = {0};
    luau_host_callback_table callbacks = {0};

    info.struct_size = (uint32_t)sizeof(info);
    options.struct_size = (uint32_t)sizeof(options);
    callbacks.struct_size = (uint32_t)sizeof(callbacks);

    return (int)(info.struct_size + options.struct_size + callbacks.struct_size);
}
