
using System;
using System.Runtime.InteropServices;

namespace Luau.Native
{
    internal static unsafe partial class NativeMethods
    {
        internal const string __DllName =
#if (UNITY_IOS || UNITY_WEBGL) && !UNITY_EDITOR
            "__Internal";
#else
            "luau_host";
#endif

        internal static LuauHostStatus luau_host_compile(
            byte* source,
            ulong sourceSize,
            LuauHostCompileOptions* options,
            LuauHostBuffer* output)
        {
            return HostNativeMethods.luau_host_compile(source, sourceSize, options, output);
        }

        internal static void luau_host_buffer_free(LuauHostBuffer* buffer)
        {
            HostNativeMethods.luau_host_buffer_free(buffer);
        }

        internal static LuauHostStatus luau_host_state_create(
            LuauHostStateOptions* options,
            lua_State** output,
            LuauHostMemoryInfo* failureInfo)
        {
            return HostNativeMethods.luau_host_state_create(options, output, failureInfo);
        }

        internal static void luau_host_state_close(lua_State* root)
        {
            HostNativeMethods.luau_host_state_close(root);
        }

        internal static LuauHostStatus luau_host_memory_get(lua_State* state, LuauHostMemoryInfo* output)
        {
            return HostNativeMethods.luau_host_memory_get(state, output);
        }

        internal static LuauHostStatus luau_host_memory_reset_failure(lua_State* state)
        {
            return HostNativeMethods.luau_host_memory_reset_failure(state);
        }

        internal static LuauHostStatus luau_host_memory_arm_quota_failure(lua_State* state)
        {
            return HostNativeMethods.luau_host_memory_arm_quota_failure(state);
        }
    }

    internal static unsafe class HostNativeMethods
    {
        const CallingConvention Call = CallingConvention.Cdecl;

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_get_abi_info", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_get_abi_info(uint callerSize, LuauHostAbiInfo* output);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_compile", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_compile(byte* source, ulong sourceSize, LuauHostCompileOptions* options, LuauHostBuffer* output);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_buffer_free", CallingConvention = Call, ExactSpelling = true)]
        internal static extern void luau_host_buffer_free(LuauHostBuffer* buffer);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_state_create", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_state_create(LuauHostStateOptions* options, lua_State** output, LuauHostMemoryInfo* failureInfo);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_state_close", CallingConvention = Call, ExactSpelling = true)]
        internal static extern void luau_host_state_close(lua_State* root);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_memory_get", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_memory_get(lua_State* state, LuauHostMemoryInfo* output);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_memory_reset_failure", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_memory_reset_failure(lua_State* state);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_memory_arm_quota_failure", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_memory_arm_quota_failure(lua_State* state);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_main_thread", CallingConvention = Call, ExactSpelling = true)]
        internal static extern lua_State* luau_host_main_thread(lua_State* state);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_is_thread_reset", CallingConvention = Call, ExactSpelling = true)]
        internal static extern int luau_host_is_thread_reset(lua_State* state);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_thread_status", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_thread_status(lua_State* state);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_thread_create", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_thread_create(lua_State* parent, lua_State** output);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_thread_reset", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_thread_reset(lua_State* state);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_stack_abs_index", CallingConvention = Call, ExactSpelling = true)]
        internal static extern int luau_host_stack_abs_index(lua_State* state, int index);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_stack_get_top", CallingConvention = Call, ExactSpelling = true)]
        internal static extern int luau_host_stack_get_top(lua_State* state);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_type", CallingConvention = Call, ExactSpelling = true)]
        internal static extern int luau_host_type(lua_State* state, int index);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_type_name", CallingConvention = Call, ExactSpelling = true)]
        internal static extern byte* luau_host_type_name(lua_State* state, int type);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_raw_equal", CallingConvention = Call, ExactSpelling = true)]
        internal static extern int luau_host_raw_equal(lua_State* state, int left, int right);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_object_length", CallingConvention = Call, ExactSpelling = true)]
        internal static extern int luau_host_object_length(lua_State* state, int index);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_is_yieldable", CallingConvention = Call, ExactSpelling = true)]
        internal static extern int luau_host_is_yieldable(lua_State* state);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_stack_set_top", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_stack_set_top(lua_State* state, int index);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_stack_insert", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_stack_insert(lua_State* state, int index);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_stack_remove", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_stack_remove(lua_State* state, int index);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_stack_replace", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_stack_replace(lua_State* state, int index);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_stack_move", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_stack_move(lua_State* from, lua_State* to, int count);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_stack_check", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_stack_check(lua_State* state, int size, int* result);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_to_boolean", CallingConvention = Call, ExactSpelling = true)]
        internal static extern int luau_host_to_boolean(lua_State* state, int index);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_to_number", CallingConvention = Call, ExactSpelling = true)]
        internal static extern double luau_host_to_number(lua_State* state, int index, int* isNumber);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_to_integer32", CallingConvention = Call, ExactSpelling = true)]
        internal static extern int luau_host_to_integer32(lua_State* state, int index, int* isInteger);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_to_unsigned32", CallingConvention = Call, ExactSpelling = true)]
        internal static extern uint luau_host_to_unsigned32(lua_State* state, int index, int* isInteger);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_to_integer64", CallingConvention = Call, ExactSpelling = true)]
        internal static extern long luau_host_to_integer64(lua_State* state, int index, int* isInteger);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_to_vector", CallingConvention = Call, ExactSpelling = true)]
        internal static extern float* luau_host_to_vector(lua_State* state, int index);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_to_string_view", CallingConvention = Call, ExactSpelling = true)]
        internal static extern byte* luau_host_to_string_view(lua_State* state, int index, ulong* length);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_to_light_userdata", CallingConvention = Call, ExactSpelling = true)]
        internal static extern void* luau_host_to_light_userdata(lua_State* state, int index);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_to_userdata", CallingConvention = Call, ExactSpelling = true)]
        internal static extern void* luau_host_to_userdata(lua_State* state, int index);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_to_thread", CallingConvention = Call, ExactSpelling = true)]
        internal static extern lua_State* luau_host_to_thread(lua_State* state, int index);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_to_buffer", CallingConvention = Call, ExactSpelling = true)]
        internal static extern void* luau_host_to_buffer(lua_State* state, int index, ulong* length);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_to_pointer", CallingConvention = Call, ExactSpelling = true)]
        internal static extern void* luau_host_to_pointer(lua_State* state, int index);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_to_function", CallingConvention = Call, ExactSpelling = true)]
        internal static extern IntPtr luau_host_to_function(lua_State* state, int index);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_callback_userdata", CallingConvention = Call, ExactSpelling = true)]
        internal static extern void* luau_host_callback_userdata(lua_State* state, int upvalue);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_push_value", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_push_value(lua_State* state, int index);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_push_nil", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_push_nil(lua_State* state);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_push_boolean", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_push_boolean(lua_State* state, int value);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_push_integer", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_push_integer(lua_State* state, long value);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_push_number", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_push_number(lua_State* state, double value);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_push_vector", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_push_vector(lua_State* state, float x, float y, float z);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_push_string", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_push_string(lua_State* state, byte* value, ulong length);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_push_light_userdata", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_push_light_userdata(lua_State* state, void* value, int tag);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_push_thread", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_push_thread(lua_State* state, int* isMainThread);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_push_callback", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_push_callback(lua_State* state, LuauHostCallbackTable* callbacks, byte* debugName, ulong debugNameSize, int upvalueCount, int* ownerTransferred, int* errorObject);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_userdata_create", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_userdata_create(lua_State* state, ulong size, int tag, void** output);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_userdata_create_with_destructor", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_userdata_create_with_destructor(lua_State* state, ulong size, LuauHostCallbackTable* callbacks, void** output);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_buffer_create", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_buffer_create(lua_State* state, ulong size, void** output);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_table_get", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_table_get(lua_State* state, int index, int* type);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_table_set", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_table_set(lua_State* state, int index);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_table_raw_get", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_table_raw_get(lua_State* state, int index, int* type);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_table_raw_set", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_table_raw_set(lua_State* state, int index);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_table_next", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_table_next(lua_State* state, int index, int* hasNext);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_table_create", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_table_create(lua_State* state, int arraySize, int recordSize);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_table_clear", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_table_clear(lua_State* state, int index);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_table_clone", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_table_clone(lua_State* state, int index);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_metatable_get", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_metatable_get(lua_State* state, int index, int* hasMetatable);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_metatable_set", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_metatable_set(lua_State* state, int index, int* result);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_table_set_readonly", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_table_set_readonly(lua_State* state, int index, int enabled);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_global_get", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_global_get(lua_State* state, byte* key, int* type);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_global_set", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_global_set(lua_State* state, byte* key);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_global_push", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_global_push(lua_State* state);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_is_global", CallingConvention = Call, ExactSpelling = true)]
        internal static extern int luau_host_is_global(lua_State* state, int index);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_reference_create", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_reference_create(lua_State* state, int index, int* reference);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_reference_push", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_reference_push(lua_State* state, int reference, int* type);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_reference_release", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_reference_release(lua_State* state, int reference);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_to_string", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_to_string(lua_State* state, int index, byte** output, ulong* length);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_to_display_string", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_to_display_string(lua_State* state, int index, byte** output, ulong* length);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_load", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_load(lua_State* state, byte* chunkName, byte* bytecode, ulong bytecodeSize, int environment, LuauHostStatus* loadStatus);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_pcall", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_pcall(lua_State* state, int argumentCount, int resultCount, int errorFunction);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_resume", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_resume(lua_State* state, lua_State* from, int argumentCount);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_resume_error", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_resume_error(lua_State* state, lua_State* from);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_yield", CallingConvention = Call, ExactSpelling = true)]
        internal static extern int luau_host_yield(lua_State* state, int resultCount);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_collect", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_collect(lua_State* state, int operation, int data, int* result);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_open_library", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_open_library(lua_State* state, LuauHostLibrary library, int* resultCount);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_open_all_libraries", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_open_all_libraries(lua_State* state);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_sandbox_root", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_sandbox_root(lua_State* state);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_sandbox_thread", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_sandbox_thread(lua_State* state);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_interrupt_install", CallingConvention = Call, ExactSpelling = true)]
        internal static extern LuauHostStatus luau_host_interrupt_install(lua_State* state, LuauHostCallbackTable* callbacks);

        [DllImport(NativeMethods.__DllName, EntryPoint = "luau_host_interrupt_uninstall", CallingConvention = Call, ExactSpelling = true)]
        internal static extern void luau_host_interrupt_uninstall(lua_State* state);
    }

}
