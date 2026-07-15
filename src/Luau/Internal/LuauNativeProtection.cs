using System.Text;
using Luau.Native;
using static Luau.Native.NativeMethods;

namespace Luau;

/// <summary>
/// Verifies and translates the native protected-call ABI. The native bridge
/// owns Luau's setjmp/longjmp boundary; managed callers only receive statuses.
/// </summary>
internal static unsafe class LuauNativeProtection
{
    internal const uint ExpectedAbiVersion = 2;

    internal static LuauNativeAbiVerifier AbiVerifier { get; } = new(QueryNativeAbiInfo);

    internal static void EnsureAvailable()
    {
        AbiVerifier.EnsureAvailable();
    }

    static int QueryNativeAbiInfo(luau_ffi_abi_info_v2* info, uint infoSize)
    {
        return luau_ffi_protected_abi_info_v2(info, infoSize);
    }

    internal static void Prepare(LuauVmContext context)
    {
        context.ResetAllocatorFailure();
    }

    internal static void ThrowIfFailed(
        LuauState state,
        lua_State* pointer,
        int status,
        string operation,
        string? chunkName = null)
    {
        var context = state.Context;
        string? nativeMessage = null;
        if (status != (int)lua_Status.LUA_OK)
        {
            // All ordinary protected wrappers guarantee exactly one error
            // object on failure. Consume it before a managed operation outcome
            // wins so the caller's surrounding stack boundary remains exact.
            nativeMessage = ReadProtectedError(pointer, operation);
        }

        var activeOperation = context.GetActiveOperation();
        var hardStop = activeOperation?.GetHardStopException();
        if (hardStop != null)
        {
            throw hardStop;
        }

        var callbackFailure = activeOperation?.TakeUninjectedCallbackFailure();
        if (callbackFailure != null)
        {
            throw callbackFailure;
        }

        if (status == (int)lua_Status.LUA_OK)
        {
            return;
        }

        var allocatorFailure = context.AllocatorFailure;

        if (allocatorFailure == LuauAllocatorFailure.QuotaExceeded)
        {
            var usage = context.MemoryUsage;
            var limit = usage.LimitBytes!.Value;
            var attempted = Math.Max(limit + 1, context.LastAttemptedAllocationBytes);
            throw new LuauMemoryLimitException(chunkName, usage, attempted);
        }

        if (allocatorFailure == LuauAllocatorFailure.SystemOutOfMemory ||
            status == (int)lua_Status.LUA_ERRMEM)
        {
            throw new OutOfMemoryException(
                LuauDiagnosticMessages.WithChunk(
                    $"The Luau VM could not allocate memory while attempting to {operation}.",
                    chunkName));
        }

        throw new LuauException(
            LuauDiagnosticMessages.WithChunk(nativeMessage!, chunkName),
            chunkName);
    }

    static string ReadProtectedError(lua_State* pointer, string operation)
    {
        try
        {
            // lua_tolstring may allocate while coercing a number. Native errors
            // may be arbitrary Luau values, so only inspect an existing string.
            if (lua_gettop(pointer) > 0 &&
                (lua_Type)lua_type(pointer, -1) == lua_Type.LUA_TSTRING)
            {
                nuint length = 0;
                var value = lua_tolstring(pointer, -1, &length);
                if (value != null && length <= int.MaxValue)
                {
                    return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(value, (int)length));
                }
            }

            return $"The Luau VM failed while attempting to {operation}.";
        }
        finally
        {
            if (lua_gettop(pointer) > 0)
            {
                lua_pop(pointer, 1);
            }
        }
    }
}

internal unsafe delegate int LuauNativeAbiInfoQuery(luau_ffi_abi_info_v2* info, uint infoSize);

/// <summary>
/// Performs the process-wide native ABI handshake. Tests construct an
/// independent verifier with a purpose-built query; production never mutates
/// the query or the result cached by this instance.
/// </summary>
internal sealed unsafe class LuauNativeAbiVerifier
{
    readonly LuauNativeAbiInfoQuery query;
    readonly Lazy<bool> verification;

    internal LuauNativeAbiVerifier(LuauNativeAbiInfoQuery query)
    {
        this.query = query ?? throw new ArgumentNullException(nameof(query));
        verification = new Lazy<bool>(Verify, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal void EnsureAvailable()
    {
        _ = verification.Value;
    }

    bool Verify()
    {
        var info = default(luau_ffi_abi_info_v2);
        int status;
        try
        {
            status = query(&info, checked((uint)sizeof(luau_ffi_abi_info_v2)));
        }
        catch (EntryPointNotFoundException exception)
        {
            throw new PlatformNotSupportedException(
                "The native Luau plugin does not provide the protected ABI information query required by this managed runtime. " +
                "Rebuild and deploy the matching native plugin.",
                exception);
        }

        switch ((uint)status)
        {
            case LUAU_ABI_INFO_OK:
                break;
            case LUAU_ABI_INFO_BUFFER_TOO_SMALL:
                throw new PlatformNotSupportedException(
                    $"The native Luau plugin requires an ABI information record of {info.struct_size} bytes, " +
                    $"but this managed runtime supplied {sizeof(luau_ffi_abi_info_v2)} bytes.");
            case LUAU_ABI_INFO_INVALID_ARGUMENT:
                throw new PlatformNotSupportedException(
                    "The native Luau plugin rejected the managed ABI information query.");
            default:
                throw new PlatformNotSupportedException(
                    $"The native Luau plugin returned unknown ABI information status {status}.");
        }

        Validate(info);
        return true;
    }

    internal static void Validate(luau_ffi_abi_info_v2 info)
    {
        var managedRecordSize = checked((uint)sizeof(luau_ffi_abi_info_v2));
        if (info.struct_size < managedRecordSize)
        {
            throw new PlatformNotSupportedException(
                $"The native Luau ABI information record is {info.struct_size} bytes; " +
                $"at least {managedRecordSize} bytes are required.");
        }

        if (info.protected_abi_version != LuauNativeProtection.ExpectedAbiVersion)
        {
            throw new PlatformNotSupportedException(
                $"The native Luau protected-call ABI is version {info.protected_abi_version}; " +
                $"version {LuauNativeProtection.ExpectedAbiVersion} is required.");
        }

        ValidateSize("pointer", info.pointer_size, sizeof(void*));
        ValidateSize("size_t", info.size_t_size, sizeof(nuint));

        if (!BitConverter.IsLittleEndian || info.little_endian != 1)
        {
            var nativeEndianness = info.little_endian == 1 ? "little-endian" : "big-endian";
            var managedEndianness = BitConverter.IsLittleEndian ? "little-endian" : "big-endian";
            throw new PlatformNotSupportedException(
                $"The native Luau plugin is {nativeEndianness} and the managed runtime is {managedEndianness}; " +
                "this runtime requires matching little-endian components.");
        }

        ValidateSize("lua_CompileOptions", info.compile_options_size, sizeof(lua_CompileOptions));
        ValidateSize("lua_Callbacks", info.callbacks_size, sizeof(lua_Callbacks));

        ValidateTypeTag("nil", info.type_nil, lua_Type.LUA_TNIL);
        ValidateTypeTag("boolean", info.type_boolean, lua_Type.LUA_TBOOLEAN);
        ValidateTypeTag("light userdata", info.type_lightuserdata, lua_Type.LUA_TLIGHTUSERDATA);
        ValidateTypeTag("number", info.type_number, lua_Type.LUA_TNUMBER);
        ValidateTypeTag("vector", info.type_vector, lua_Type.LUA_TVECTOR);
        ValidateTypeTag("string", info.type_string, lua_Type.LUA_TSTRING);
        ValidateTypeTag("table", info.type_table, lua_Type.LUA_TTABLE);
        ValidateTypeTag("function", info.type_function, lua_Type.LUA_TFUNCTION);
        ValidateTypeTag("userdata", info.type_userdata, lua_Type.LUA_TUSERDATA);
        ValidateTypeTag("thread", info.type_thread, lua_Type.LUA_TTHREAD);
        ValidateTypeTag("buffer", info.type_buffer, lua_Type.LUA_TBUFFER);
    }

    static void ValidateSize(string name, uint actual, int expected)
    {
        if (actual != (uint)expected)
        {
            throw new PlatformNotSupportedException(
                $"The native Luau {name} size is {actual} bytes; the managed binding requires {expected} bytes.");
        }
    }

    static void ValidateTypeTag(string name, int actual, lua_Type expected)
    {
        if (actual != (int)expected)
        {
            throw new PlatformNotSupportedException(
                $"The native Luau {name} type tag is {actual}; the managed binding requires {(int)expected}.");
        }
    }
}
