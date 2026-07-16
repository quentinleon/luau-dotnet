
#pragma warning disable IDE1006

using System;
using System.Runtime.InteropServices;

namespace Luau.Native
{
    /// <summary>
    /// Internal compatibility facade over the narrow repository-owned
    /// <c>luau_host</c> ABI. Stage 4 removes the remaining upstream-shaped
    /// managed helpers with the rest of the transitional public surface.
    /// </summary>
    internal static unsafe partial class NativeMethods
    {
        const int ProtectedResultMarker = 1 << 30;
        const int ProtectedResultErrorObject = 1 << 8;
        const int ProtectedResultStatusMask = 0xff;

        static readonly lua_UserdataDestructor callbackDelegateOwnerDestructor = ReleaseCallbackDelegateOwner;
        static readonly lua_UserdataDestructor userdataDestructorTrampoline = DispatchUserdataDestructor;
        static readonly object userdataDestructorOwnerSync = new object();
        static UserdataDestructorOwner? userdataDestructorOwners;

        sealed class CallbackDelegateOwner
        {
            internal CallbackDelegateOwner(lua_CFunction function)
            {
                Function = function;
            }

            internal lua_CFunction Function { get; }
        }

        sealed class UserdataDestructorOwner
        {
            internal UserdataDestructorOwner(lua_UserdataDestructor destructor)
            {
                Destructor = destructor;
            }

            internal lua_UserdataDestructor Destructor { get; }
            internal IntPtr Userdata { get; set; }
            internal UserdataDestructorOwner? Next { get; set; }
        }

        public const uint LUAI_MAXCSTACK = 8000;
        public const int LUA_MULTRET = -1;
        public const int LUA_REGISTRYINDEX = (int)(-LUAI_MAXCSTACK - 2000);
        public const int LUA_ENVIRONINDEX = (int)(-LUAI_MAXCSTACK - 2001);
        public const int LUA_GLOBALSINDEX = (int)(-LUAI_MAXCSTACK - 2002);
        public const int LUA_TNONE = -1;

        public static long lua_upvalueindex(int i)
        {
            return LUA_GLOBALSINDEX - i;
        }

        public static lua_State* luaL_newstate()
        {
            var options = new LuauHostStateOptions
            {
                struct_size = checked((uint)sizeof(LuauHostStateOptions)),
                version = 1,
                flags = LuauHostStateOptionFlags.None,
            };
            var failure = new LuauHostMemoryInfo
            {
                struct_size = checked((uint)sizeof(LuauHostMemoryInfo)),
            };
            lua_State* state = null;
            return luau_host_state_create(&options, &state, &failure) == LuauHostStatus.Ok
                ? state
                : null;
        }

        public static void lua_close(lua_State* state)
        {
            luau_host_state_close(state);
        }

        public static lua_State* lua_mainthread(lua_State* state)
        {
            return HostNativeMethods.luau_host_main_thread(state);
        }

        public static int lua_absindex(lua_State* state, int index)
        {
            return HostNativeMethods.luau_host_stack_abs_index(state, index);
        }

        public static int lua_gettop(lua_State* state)
        {
            return HostNativeMethods.luau_host_stack_get_top(state);
        }

        public static void lua_settop(lua_State* state, int index)
        {
            RequireCompatibilitySuccess(HostNativeMethods.luau_host_stack_set_top(state, index), "set the stack top");
        }

        public static void lua_pop(lua_State* state, int count)
        {
            lua_settop(state, -count - 1);
        }

        public static void lua_insert(lua_State* state, int index)
        {
            RequireCompatibilitySuccess(HostNativeMethods.luau_host_stack_insert(state, index), "insert a stack value");
        }

        public static void lua_remove(lua_State* state, int index)
        {
            RequireCompatibilitySuccess(HostNativeMethods.luau_host_stack_remove(state, index), "remove a stack value");
        }

        public static void lua_replace(lua_State* state, int index)
        {
            RequireCompatibilitySuccess(HostNativeMethods.luau_host_stack_replace(state, index), "replace a stack value");
        }

        public static void lua_xmove(lua_State* from, lua_State* to, int count)
        {
            RequireCompatibilitySuccess(HostNativeMethods.luau_host_stack_move(from, to, count), "move stack values");
        }

        public static int luau_ffi_protected_settop(lua_State* state, int index)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_stack_set_top(state, index));
        }

        public static int luau_ffi_protected_xmove(lua_State* from, lua_State* to, int count)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_stack_move(from, to, count));
        }

        public static int lua_type(lua_State* state, int index)
        {
            return HostNativeMethods.luau_host_type(state, index);
        }

        public static byte* lua_typename(lua_State* state, int type)
        {
            return HostNativeMethods.luau_host_type_name(state, type);
        }

        public static int lua_rawequal(lua_State* state, int left, int right)
        {
            if (left == LUA_GLOBALSINDEX && right == LUA_GLOBALSINDEX)
            {
                return 1;
            }
            if (left == LUA_GLOBALSINDEX)
            {
                return HostNativeMethods.luau_host_is_global(state, right);
            }
            if (right == LUA_GLOBALSINDEX)
            {
                return HostNativeMethods.luau_host_is_global(state, left);
            }
            RejectUnsupportedPseudoIndex(left);
            RejectUnsupportedPseudoIndex(right);
            return HostNativeMethods.luau_host_raw_equal(state, left, right);
        }

        public static double lua_tonumberx(lua_State* state, int index, int* isNumber)
        {
            return HostNativeMethods.luau_host_to_number(state, index, isNumber);
        }

        public static double lua_tonumber(lua_State* state, int index)
        {
            return lua_tonumberx(state, index, null);
        }

        public static int lua_tointegerx(lua_State* state, int index, int* isInteger)
        {
            return HostNativeMethods.luau_host_to_integer32(state, index, isInteger);
        }

        public static uint lua_tounsignedx(lua_State* state, int index, int* isInteger)
        {
            return HostNativeMethods.luau_host_to_unsigned32(state, index, isInteger);
        }

        public static long lua_tointeger64(lua_State* state, int index, int* isInteger)
        {
            return HostNativeMethods.luau_host_to_integer64(state, index, isInteger);
        }

        public static float* lua_tovector(lua_State* state, int index)
        {
            return HostNativeMethods.luau_host_to_vector(state, index);
        }

        public static int lua_toboolean(lua_State* state, int index)
        {
            return HostNativeMethods.luau_host_to_boolean(state, index);
        }

        public static byte* lua_tolstring(lua_State* state, int index, nuint* length)
        {
            ulong hostLength = 0;
            var result = HostNativeMethods.luau_host_to_string_view(state, index, &hostLength);
            WriteNativeSize(length, hostLength);
            return result;
        }

        public static int lua_objlen(lua_State* state, int index)
        {
            return HostNativeMethods.luau_host_object_length(state, index);
        }

        public static lua_CFunction lua_tocfunction(lua_State* state, int index)
        {
            var pointer = HostNativeMethods.luau_host_to_function(state, index);
            return pointer == IntPtr.Zero
                ? null!
                : (lua_CFunction)Marshal.GetDelegateForFunctionPointer(pointer, typeof(lua_CFunction));
        }

        public static void* lua_tolightuserdata(lua_State* state, int index)
        {
            return HostNativeMethods.luau_host_to_light_userdata(state, index);
        }

        public static void* lua_touserdata(lua_State* state, int index)
        {
            if (TryGetCallbackUpvalue(index, out var upvalue))
            {
                return HostNativeMethods.luau_host_callback_userdata(state, upvalue);
            }
            RejectUnsupportedPseudoIndex(index);
            return HostNativeMethods.luau_host_to_userdata(state, index);
        }

        public static lua_State* lua_tothread(lua_State* state, int index)
        {
            return HostNativeMethods.luau_host_to_thread(state, index);
        }

        public static void* lua_tobuffer(lua_State* state, int index, nuint* length)
        {
            ulong hostLength = 0;
            var result = HostNativeMethods.luau_host_to_buffer(state, index, &hostLength);
            WriteNativeSize(length, hostLength);
            return result;
        }

        public static void* lua_topointer(lua_State* state, int index)
        {
            return HostNativeMethods.luau_host_to_pointer(state, index);
        }

        public static void lua_setreadonly(lua_State* state, int index, int enabled)
        {
            RequireCompatibilitySuccess(
                HostNativeMethods.luau_host_table_set_readonly(state, index, enabled),
                "change table readonly state");
        }

        public static int lua_pcall(lua_State* state, int argumentCount, int resultCount, int errorFunction)
        {
            return ToExecutionStatus(HostNativeMethods.luau_host_pcall(state, argumentCount, resultCount, errorFunction));
        }

        public static int lua_yield(lua_State* state, int resultCount)
        {
            return HostNativeMethods.luau_host_yield(state, resultCount);
        }

        public static int lua_resume(lua_State* state, lua_State* from, int argumentCount)
        {
            return ToExecutionStatus(HostNativeMethods.luau_host_resume(state, from, argumentCount));
        }

        public static int lua_resumeerror(lua_State* state, lua_State* from)
        {
            return ToExecutionStatus(HostNativeMethods.luau_host_resume_error(state, from));
        }

        public static int lua_status(lua_State* state)
        {
            return ToExecutionStatus(HostNativeMethods.luau_host_thread_status(state));
        }

        public static int lua_isyieldable(lua_State* state)
        {
            return HostNativeMethods.luau_host_is_yieldable(state);
        }

        public static int lua_gc(lua_State* state, int operation, int data)
        {
            var result = 0;
            RequireCompatibilitySuccess(
                HostNativeMethods.luau_host_collect(state, operation, data, &result),
                "collect Luau garbage");
            return result;
        }

        public static void lua_unref(lua_State* state, int reference)
        {
            RequireCompatibilitySuccess(
                HostNativeMethods.luau_host_reference_release(state, reference),
                "release a registry reference");
        }

        public static int luau_ffi_protected_checkstack(lua_State* state, int size, int* result)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_stack_check(state, size, result));
        }

        public static int luau_ffi_protected_newthread(lua_State* state, lua_State** result)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_thread_create(state, result));
        }

        public static int luau_ffi_protected_resetthread(lua_State* state)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_thread_reset(state));
        }

        public static int luau_ffi_protected_install_interrupt(lua_State* state, void* poll)
        {
            if (state == null || poll == null)
            {
                return 0;
            }

            var callbacks = CreateCallbackTable();
            callbacks.interrupt_poll = (IntPtr)poll;
            return HostNativeMethods.luau_host_interrupt_install(state, &callbacks) == LuauHostStatus.Ok ? 1 : 0;
        }

        public static void luau_ffi_protected_uninstall_interrupt(lua_State* state)
        {
            HostNativeMethods.luau_host_interrupt_uninstall(state);
        }

        public static int luau_ffi_protected_pushvalue(lua_State* state, int index)
        {
            if (index == LUA_GLOBALSINDEX)
            {
                return ToLuaStatus(HostNativeMethods.luau_host_global_push(state));
            }
            RejectUnsupportedPseudoIndex(index);
            return ToLuaStatus(HostNativeMethods.luau_host_push_value(state, index));
        }

        public static int luau_ffi_protected_pushnil(lua_State* state)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_push_nil(state));
        }

        public static int luau_ffi_protected_pushboolean(lua_State* state, int value)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_push_boolean(state, value));
        }

        public static int luau_ffi_protected_pushinteger64(lua_State* state, long value)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_push_integer(state, value));
        }

        public static int luau_ffi_protected_pushnumber(lua_State* state, double value)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_push_number(state, value));
        }

        public static int luau_ffi_protected_pushvector(lua_State* state, float x, float y, float z)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_push_vector(state, x, y, z));
        }

        public static int luau_ffi_protected_pushlstring(lua_State* state, byte* value, nuint length)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_push_string(state, value, checked((ulong)length)));
        }

        public static int luau_ffi_protected_pushcclosurek(
            lua_State* state,
            lua_CFunction function,
            byte* debugName,
            int upvalues)
        {
            if (function == null)
            {
                return ToProtectedResult(LuauHostStatus.InvalidArgument, false);
            }

            var owner = GCHandle.Alloc(new CallbackDelegateOwner(function));
            try
            {
                var callbacks = CreateCallbackTable();
                callbacks.userdata = GCHandle.ToIntPtr(owner);
                callbacks.userdata_destructor = Marshal.GetFunctionPointerForDelegate(callbackDelegateOwnerDestructor);
                callbacks.managed_function = Marshal.GetFunctionPointerForDelegate(function);
                var ownerTransferred = 0;
                var errorObject = 0;
                var status = HostNativeMethods.luau_host_push_callback(
                    state,
                    &callbacks,
                    debugName,
                    CStringLength(debugName),
                    upvalues,
                    &ownerTransferred,
                    &errorObject);
                if (ownerTransferred != 0)
                {
                    // The hidden native closure owner now releases this handle
                    // when Luau collects it, including after a later protected
                    // closure-allocation failure, or when the root closes.
                    owner = default;
                }

                if (status != LuauHostStatus.Ok && errorObject == 0)
                {
                    if (status == LuauHostStatus.SystemOutOfMemory)
                    {
                        throw new OutOfMemoryException("The Luau host could not retain the native callback owner.");
                    }

                    throw new InvalidOperationException(
                        $"The Luau host rejected callback closure creation with status {(int)status} before changing the stack.");
                }

                GC.KeepAlive(function);
                GC.KeepAlive(callbackDelegateOwnerDestructor);
                return ToProtectedResult(status, errorObject != 0);
            }
            finally
            {
                if (owner.IsAllocated)
                {
                    owner.Free();
                }
            }
        }

        internal static int luau_host_push_rooted_callback(
            lua_State* state,
            IntPtr function,
            byte* debugName,
            int upvalues)
        {
            if (function == IntPtr.Zero)
            {
                return ToProtectedResult(LuauHostStatus.InvalidArgument, false);
            }

            var callbacks = CreateCallbackTable();
            callbacks.managed_function = function;
            var ownerTransferred = 0;
            var errorObject = 0;
            var status = HostNativeMethods.luau_host_push_callback(
                state,
                &callbacks,
                debugName,
                CStringLength(debugName),
                upvalues,
                &ownerTransferred,
                &errorObject);
            return ToProtectedResult(status, errorObject != 0);
        }

        [AOT.MonoPInvokeCallback(typeof(lua_UserdataDestructor))]
        static void ReleaseCallbackDelegateOwner(void* userdata)
        {
            try
            {
                if (userdata == null)
                {
                    return;
                }

                var owner = GCHandle.FromIntPtr((IntPtr)userdata);
                if (owner.IsAllocated)
                {
                    owner.Free();
                }
            }
            catch
            {
                // Native finalizer callbacks must never unwind into Luau.
            }
        }

        public static int luau_ffi_protected_pushlightuserdatatagged(lua_State* state, void* pointer, int tag)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_push_light_userdata(state, pointer, tag));
        }

        public static int luau_ffi_protected_pushthread(lua_State* state, int* result)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_push_thread(state, result));
        }

        public static int luau_ffi_protected_newuserdatatagged(
            lua_State* state,
            nuint size,
            int tag,
            void** result)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_userdata_create(state, checked((ulong)size), tag, result));
        }

        public static int luau_ffi_protected_newuserdatadtor(
            lua_State* state,
            nuint size,
            lua_UserdataDestructor destructor,
            void** result)
        {
            if (destructor == null)
            {
                return ToProtectedResult(LuauHostStatus.InvalidArgument, false);
            }

            UserdataDestructorOwner owner;
            try
            {
                owner = new UserdataDestructorOwner(destructor);
            }
            catch (OutOfMemoryException)
            {
                if (result != null)
                {
                    *result = null;
                }
                return ToProtectedResult(LuauHostStatus.SystemOutOfMemory, false);
            }

            var callbacks = CreateCallbackTable();
            callbacks.userdata_destructor = Marshal.GetFunctionPointerForDelegate(userdataDestructorTrampoline);
            void* nativeResult = null;
            var status = HostNativeMethods.luau_host_userdata_create_with_destructor(
                state,
                checked((ulong)size),
                &callbacks,
                &nativeResult);
            if (status == LuauHostStatus.Ok)
            {
                owner.Userdata = (IntPtr)nativeResult;
                lock (userdataDestructorOwnerSync)
                {
                    owner.Next = userdataDestructorOwners;
                    userdataDestructorOwners = owner;
                }

                if (result != null)
                {
                    *result = nativeResult;
                }
            }
            else if (result != null)
            {
                *result = null;
            }

            GC.KeepAlive(destructor);
            GC.KeepAlive(userdataDestructorTrampoline);
            return ToLuaStatus(status);
        }

        [AOT.MonoPInvokeCallback(typeof(lua_UserdataDestructor))]
        static void DispatchUserdataDestructor(void* userdata)
        {
            UserdataDestructorOwner? owner = null;
            try
            {
                lock (userdataDestructorOwnerSync)
                {
                    UserdataDestructorOwner? previous = null;
                    var current = userdataDestructorOwners;
                    while (current != null)
                    {
                        if (current.Userdata == (IntPtr)userdata)
                        {
                            if (previous == null)
                            {
                                userdataDestructorOwners = current.Next;
                            }
                            else
                            {
                                previous.Next = current.Next;
                            }

                            current.Next = null;
                            owner = current;
                            break;
                        }

                        previous = current;
                        current = current.Next;
                    }
                }

                owner?.Destructor(userdata);
            }
            catch
            {
                // Native finalizer callbacks must never unwind into Luau.
            }
        }

        public static int luau_ffi_protected_newbuffer(lua_State* state, nuint size, void** result)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_buffer_create(state, checked((ulong)size), result));
        }

        public static int luau_ffi_protected_gettable(lua_State* state, int index, int* result)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_table_get(state, index, result));
        }

        public static int luau_ffi_protected_getfield(lua_State* state, int index, byte* key, int* result)
        {
            if (index == LUA_GLOBALSINDEX)
            {
                return ToLuaStatus(HostNativeMethods.luau_host_global_get(state, key, result));
            }

            RejectUnsupportedPseudoIndex(index);
            var absoluteIndex = HostNativeMethods.luau_host_stack_abs_index(state, index);
            var status = HostNativeMethods.luau_host_push_string(state, key, CStringLength(key));
            return status == LuauHostStatus.Ok
                ? ToLuaStatus(HostNativeMethods.luau_host_table_get(state, absoluteIndex, result))
                : ToLuaStatus(status);
        }

        public static int luau_ffi_protected_rawget(lua_State* state, int index, int* result)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_table_raw_get(state, index, result));
        }

        public static int luau_ffi_protected_rawgeti(lua_State* state, int index, int item, int* result)
        {
            if (index == LUA_REGISTRYINDEX)
            {
                return ToLuaStatus(HostNativeMethods.luau_host_reference_push(state, item, result));
            }

            RejectUnsupportedPseudoIndex(index);
            var absoluteIndex = HostNativeMethods.luau_host_stack_abs_index(state, index);
            var status = HostNativeMethods.luau_host_push_integer(state, item);
            return status == LuauHostStatus.Ok
                ? ToLuaStatus(HostNativeMethods.luau_host_table_raw_get(state, absoluteIndex, result))
                : ToLuaStatus(status);
        }

        public static int luau_ffi_protected_next(lua_State* state, int index, int* result)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_table_next(state, index, result));
        }

        public static int luau_ffi_protected_createtable(lua_State* state, int arraySize, int recordSize)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_table_create(state, arraySize, recordSize));
        }

        public static int luau_ffi_protected_getmetatable(lua_State* state, int index, int* result)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_metatable_get(state, index, result));
        }

        public static int luau_ffi_protected_settable(lua_State* state, int index)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_table_set(state, index));
        }

        public static int luau_ffi_protected_setfield(lua_State* state, int index, byte* key)
        {
            if (index == LUA_GLOBALSINDEX)
            {
                return ToLuaStatus(HostNativeMethods.luau_host_global_set(state, key));
            }

            RejectUnsupportedPseudoIndex(index);
            var originalTop = HostNativeMethods.luau_host_stack_get_top(state);
            var absoluteIndex = HostNativeMethods.luau_host_stack_abs_index(state, index);
            var status = HostNativeMethods.luau_host_push_string(state, key, CStringLength(key));
            if (status != LuauHostStatus.Ok)
            {
                NormalizeSetFieldFailure(state, originalTop);
                return ToLuaStatus(status);
            }

            status = HostNativeMethods.luau_host_stack_insert(state, -2);
            if (status != LuauHostStatus.Ok)
            {
                NormalizeSetFieldFailure(state, originalTop);
                return ToLuaStatus(status);
            }

            return ToLuaStatus(HostNativeMethods.luau_host_table_set(state, absoluteIndex));
        }

        public static int luau_ffi_protected_rawset(lua_State* state, int index)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_table_raw_set(state, index));
        }

        public static int luau_ffi_protected_setmetatable(lua_State* state, int index, int* result)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_metatable_set(state, index, result));
        }

        public static int luau_ffi_protected_load(
            lua_State* state,
            byte* chunkName,
            byte* bytecode,
            nuint size,
            int environment,
            int* result)
        {
            var loadStatus = LuauHostStatus.Ok;
            var outerStatus = HostNativeMethods.luau_host_load(
                state,
                chunkName,
                bytecode,
                checked((ulong)size),
                environment,
                &loadStatus);
            if (outerStatus == LuauHostStatus.Ok && result != null)
            {
                *result = ToExecutionStatus(loadStatus);
            }
            return ToLuaStatus(outerStatus);
        }

        public static int luau_ffi_protected_gc(lua_State* state, int operation, int data, int* result)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_collect(state, operation, data, result));
        }

        public static int luau_ffi_protected_cleartable(lua_State* state, int index)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_table_clear(state, index));
        }

        public static int luau_ffi_protected_clonetable(lua_State* state, int index)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_table_clone(state, index));
        }

        public static int luau_ffi_protected_ref(lua_State* state, int index, int* result)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_reference_create(state, index, result));
        }

        public static int luau_ffi_protected_tolstring(lua_State* state, int index, byte** result, nuint* length)
        {
            ulong hostLength = 0;
            var status = HostNativeMethods.luau_host_to_string(state, index, result, &hostLength);
            if (status == LuauHostStatus.Ok)
            {
                WriteNativeSize(length, hostLength);
            }
            return ToLuaStatus(status);
        }

        public static int luau_ffi_protected_luaL_tolstring(lua_State* state, int index, byte** result, nuint* length)
        {
            ulong hostLength = 0;
            var status = HostNativeMethods.luau_host_to_display_string(state, index, result, &hostLength);
            if (status == LuauHostStatus.Ok)
            {
                WriteNativeSize(length, hostLength);
            }
            return ToLuaStatus(status);
        }

        public static int luau_ffi_protected_openlibrary(lua_State* state, int library, int* result)
        {
            if (library < (int)LuauHostLibrary.Base || library > (int)LuauHostLibrary.Integer)
            {
                return ToProtectedResult(LuauHostStatus.InvalidArgument, false);
            }
            return ToLuaStatus(HostNativeMethods.luau_host_open_library(state, (LuauHostLibrary)library, result));
        }

        public static int luau_ffi_protected_openlibs(lua_State* state)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_open_all_libraries(state));
        }

        public static int luau_ffi_protected_sandbox(lua_State* state)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_sandbox_root(state));
        }

        public static int luau_ffi_protected_sandboxthread(lua_State* state)
        {
            return ToLuaStatus(HostNativeMethods.luau_host_sandbox_thread(state));
        }

        static LuauHostCallbackTable CreateCallbackTable()
        {
            return new LuauHostCallbackTable
            {
                struct_size = checked((uint)sizeof(LuauHostCallbackTable)),
                version = 1,
            };
        }

        static int ToLuaStatus(LuauHostStatus status)
        {
            var hasErrorObject = status == LuauHostStatus.LuaError ||
                status == LuauHostStatus.MemoryQuota ||
                status == LuauHostStatus.SystemOutOfMemory ||
                status == LuauHostStatus.Canceled;
            return ToProtectedResult(status, hasErrorObject);
        }

        static int ToProtectedResult(LuauHostStatus status, bool hasErrorObject)
        {
            if (status == LuauHostStatus.Ok)
            {
                return 0;
            }

            return ProtectedResultMarker |
                (hasErrorObject ? ProtectedResultErrorObject : 0) |
                ((int)status & ProtectedResultStatusMask);
        }

        internal static bool TryDecodeProtectedResult(
            int result,
            out LuauHostStatus status,
            out bool hasErrorObject)
        {
            if (result == 0)
            {
                status = LuauHostStatus.Ok;
                hasErrorObject = false;
                return true;
            }
            if ((result & ProtectedResultMarker) == 0)
            {
                status = default;
                hasErrorObject = false;
                return false;
            }

            status = (LuauHostStatus)(result & ProtectedResultStatusMask);
            hasErrorObject = (result & ProtectedResultErrorObject) != 0;
            return true;
        }

        static int ToExecutionStatus(LuauHostStatus status)
        {
            switch (status)
            {
                case LuauHostStatus.Ok:
                    return (int)lua_Status.LUA_OK;
                case LuauHostStatus.Yielded:
                    return (int)lua_Status.LUA_YIELD;
                case LuauHostStatus.Break:
                    return (int)lua_Status.LUA_BREAK;
                case LuauHostStatus.MemoryQuota:
                case LuauHostStatus.SystemOutOfMemory:
                case LuauHostStatus.Canceled:
                    return (int)lua_Status.LUA_ERRMEM;
                case LuauHostStatus.CompilerError:
                    return (int)lua_Status.LUA_ERRSYNTAX;
                case LuauHostStatus.TerminalReset:
                case LuauHostStatus.InvalidArgument:
                case LuauHostStatus.Unsupported:
                    return (int)lua_Status.LUA_ERRERR;
                default:
                    return (int)lua_Status.LUA_ERRRUN;
            }
        }

        static void RequireCompatibilitySuccess(LuauHostStatus status, string operation)
        {
            if (status != LuauHostStatus.Ok)
            {
                throw new InvalidOperationException(
                    $"The Luau host returned status {(int)status} while attempting to {operation}.");
            }
        }

        static void RejectUnsupportedPseudoIndex(int index)
        {
            if (index <= LUA_REGISTRYINDEX)
            {
                throw new PlatformNotSupportedException(
                    $"The Luau host does not expose legacy pseudo-index {index}; use an explicit host global, reference, or callback helper.");
            }
        }

        static bool TryGetCallbackUpvalue(int index, out int upvalue)
        {
            if (index < LUA_GLOBALSINDEX)
            {
                upvalue = LUA_GLOBALSINDEX - index;
                return upvalue > 0;
            }
            upvalue = 0;
            return false;
        }

        static ulong CStringLength(byte* value)
        {
            if (value == null)
            {
                return 0;
            }
            ulong length = 0;
            while (value[length] != 0)
            {
                length++;
            }
            return length;
        }

        static void WriteNativeSize(nuint* destination, ulong value)
        {
            if (destination == null)
            {
                return;
            }
            if (sizeof(nuint) == sizeof(uint) && value > uint.MaxValue)
            {
                throw new OverflowException("The native Luau value is too large for this managed platform.");
            }
            *destination = checked((nuint)value);
        }

        static void NormalizeSetFieldFailure(lua_State* state, int originalTop)
        {
            if (originalTop <= 0 || HostNativeMethods.luau_host_stack_get_top(state) <= originalTop)
            {
                return;
            }

            // Preserve the host error at the consumed value's slot, then trim
            // any temporary key left by the compatibility sequence.
            _ = HostNativeMethods.luau_host_stack_replace(state, originalTop);
            _ = HostNativeMethods.luau_host_stack_set_top(state, originalTop);
        }
    }

}

#pragma warning restore IDE1006
