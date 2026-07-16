using System.Text;
using System.Runtime.InteropServices;
using Luau.Native;
using static Luau.Native.NativeMethods;

namespace Luau;

/// <summary>
/// Verifies and translates the native protected-call ABI. The native bridge
/// owns Luau's setjmp/longjmp boundary; managed callers only receive statuses.
/// </summary>
internal static unsafe class LuauNativeProtection
{
    internal const uint ExpectedAbiMagic = 0x4841554cU;
    internal const ushort ExpectedAbiMajor = 1;
    internal const ushort MinimumAbiMinor = 0;
    internal const uint ExpectedAbiRecordSize = 112;
    internal const uint ExpectedFeatureFlags = 0x1ffU;
    internal const ulong ExpectedUpstreamRevisionHash = 0xc45f010aabf167acUL;
    internal const ulong ExpectedHostBuildFingerprint = 0x105716f226c3f69fUL;
    internal const int AbiQueryOk = (int)LuauHostStatus.Ok;
    internal const int AbiQueryInvalidArgument = (int)LuauHostStatus.InvalidArgument;

    internal static LuauNativeAbiVerifier AbiVerifier { get; } = new(QueryNativeAbiInfo);

    internal static void EnsureAvailable()
    {
        AbiVerifier.EnsureAvailable();
    }

    static int QueryNativeAbiInfo(LuauNativeAbiInfo* info, uint infoSize)
    {
        // This is the sole process-wide compatibility handshake. Calling the
        // raw query here keeps the internal facade free of a second verifier.
        return (int)HostNativeMethods.luau_host_get_abi_info(
            infoSize,
            (LuauHostAbiInfo*)info);
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
        if (!TryDecodeProtectedResult(status, out var hostStatus, out var hasErrorObject))
        {
            throw new LuauException(
                $"The Luau host returned an invalid managed result code {status} while attempting to {operation}.");
        }
        if (hasErrorObject)
        {
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

        if (hostStatus == LuauHostStatus.Ok)
        {
            return;
        }

        var allocatorFailure = context.AllocatorFailure;

        if (allocatorFailure == LuauAllocatorFailure.QuotaExceeded
            || hostStatus == LuauHostStatus.MemoryQuota
            )
        {
            var usage = context.MemoryUsage;
            var limit = usage.LimitBytes!.Value;
            var attempted = Math.Max(limit + 1, context.LastAttemptedAllocationBytes);
            throw new LuauMemoryLimitException(chunkName, usage, attempted);
        }

        if (allocatorFailure == LuauAllocatorFailure.SystemOutOfMemory ||
            hostStatus == LuauHostStatus.SystemOutOfMemory)
        {
            throw new OutOfMemoryException(
                LuauDiagnosticMessages.WithChunk(
                    $"The Luau VM could not allocate memory while attempting to {operation}.",
                    chunkName));
        }

        if (hostStatus == LuauHostStatus.InvalidArgument)
        {
            throw new InvalidOperationException(
                $"The Luau host rejected the arguments supplied to {operation}.");
        }
        if (hostStatus == LuauHostStatus.Unsupported)
        {
            throw new PlatformNotSupportedException(
                $"The Luau host does not support the requested operation to {operation}.");
        }

        nativeMessage ??= $"The Luau host failed with status {(int)hostStatus} while attempting to {operation}.";
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


[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct LuauNativeAbiInfo
{
    internal uint struct_size;
    internal uint magic;
    internal ushort abi_major;
    internal ushort abi_minor;
    internal uint feature_flags;
    internal byte pointer_size;
    internal byte size_t_size;
    internal byte little_endian;
    internal byte reserved0;
    internal uint compile_options_size;
    internal uint callback_table_size;
    internal uint state_options_size;
    internal uint memory_info_size;
    internal uint buffer_size;
    internal int type_nil;
    internal int type_boolean;
    internal int type_lightuserdata;
    internal int type_number;
    internal int type_integer;
    internal int type_vector;
    internal int type_string;
    internal int type_table;
    internal int type_function;
    internal int type_userdata;
    internal int type_thread;
    internal int type_buffer;
    internal int type_class;
    internal int type_object;
    internal ulong upstream_revision_hash;
    internal ulong host_build_fingerprint;
}

internal unsafe delegate int LuauNativeAbiInfoQuery(LuauNativeAbiInfo* info, uint infoSize);

/// <summary>
/// Performs the process-wide host ABI handshake. The test-visible descriptor
/// mirrors the fixed C record without exposing the internal interop assembly.
/// </summary>
internal sealed unsafe class LuauNativeAbiVerifier
{
    static readonly (LuauHostFeature Feature, string Name)[] RequiredFeatures =
    [
        (LuauHostFeature.SelfDescription, "self-description"),
        (LuauHostFeature.ProtectedOperations, "protected operations"),
        (LuauHostFeature.HostBuffer, "host-owned compiler buffers"),
        (LuauHostFeature.TrackedAllocator, "tracked allocator"),
        (LuauHostFeature.ManagedCallbacks, "managed callbacks"),
        (LuauHostFeature.Interrupt, "interrupt callbacks"),
        (LuauHostFeature.TerminalReset, "terminal reset"),
        (LuauHostFeature.IntegerValues, "integer values"),
        (LuauHostFeature.Sandbox, "sandbox"),
    ];

    readonly LuauNativeAbiInfoQuery query;
    readonly Lazy<LuauNativeAbiInfo> verification;

    internal LuauNativeAbiVerifier(LuauNativeAbiInfoQuery query)
    {
        this.query = query ?? throw new ArgumentNullException(nameof(query));
        verification = new Lazy<LuauNativeAbiInfo>(Verify, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal LuauNativeAbiInfo Info => verification.Value;

    internal void EnsureAvailable()
    {
        _ = verification.Value;
    }

    LuauNativeAbiInfo Verify()
    {
        if (sizeof(LuauNativeAbiInfo) != LuauNativeProtection.ExpectedAbiRecordSize)
        {
            throw new PlatformNotSupportedException(
                $"The managed Luau host ABI declaration is {sizeof(LuauNativeAbiInfo)} bytes; " +
                $"the fixed ABI record requires {LuauNativeProtection.ExpectedAbiRecordSize} bytes.");
        }

        var info = default(LuauNativeAbiInfo);
        int status;
        try
        {
            status = query(&info, checked((uint)sizeof(LuauNativeAbiInfo)));
        }
        catch (EntryPointNotFoundException exception)
        {
            throw new PlatformNotSupportedException(
                "The native Luau host does not provide the self-description query required by this managed runtime. " +
                "Rebuild and deploy the matching host plugin.",
                exception);
        }

        if (status != LuauNativeProtection.AbiQueryOk)
        {
            var context = FingerprintContext(info);
            if (status == LuauNativeProtection.AbiQueryInvalidArgument)
            {
                throw new PlatformNotSupportedException(
                    $"The native Luau host rejected an ABI query with a {sizeof(LuauNativeAbiInfo)}-byte caller record; " +
                    $"the host reports {info.struct_size} bytes. {context}");
            }

            throw new PlatformNotSupportedException(
                $"The native Luau host returned unknown ABI query status {status}. {context}");
        }

        Validate(info);
        return info;
    }

    internal static void Validate(LuauNativeAbiInfo info)
    {
        var context = FingerprintContext(info);
        var managedRecordSize = checked((uint)sizeof(LuauNativeAbiInfo));

        if (info.struct_size < managedRecordSize)
        {
            throw new PlatformNotSupportedException(
                $"The native Luau host ABI record is {info.struct_size} bytes; " +
                $"at least {managedRecordSize} bytes are required. {context}");
        }
        if (info.magic != LuauNativeProtection.ExpectedAbiMagic)
        {
            throw new PlatformNotSupportedException(
                $"The native Luau host ABI magic is 0x{info.magic:x8}; " +
                $"expected 0x{LuauNativeProtection.ExpectedAbiMagic:x8}. {context}");
        }
        if (info.abi_major != LuauNativeProtection.ExpectedAbiMajor ||
            info.abi_minor < LuauNativeProtection.MinimumAbiMinor)
        {
            throw new PlatformNotSupportedException(
                $"The native Luau host ABI is {info.abi_major}.{info.abi_minor}; " +
                $"expected {LuauNativeProtection.ExpectedAbiMajor}.{LuauNativeProtection.MinimumAbiMinor} " +
                $"or a compatible newer minor. {context}");
        }

        ValidateSize("pointer", info.pointer_size, sizeof(void*), context);
        ValidateSize("size_t", info.size_t_size, sizeof(nuint), context);

        if (!BitConverter.IsLittleEndian || info.little_endian != 1)
        {
            var nativeEndianness = info.little_endian == 1 ? "little-endian" : "big-endian";
            var managedEndianness = BitConverter.IsLittleEndian ? "little-endian" : "big-endian";
            throw new PlatformNotSupportedException(
                $"The native Luau host is {nativeEndianness} and the managed runtime is {managedEndianness}; " +
                $"matching little-endian components are required. {context}");
        }

        ValidateSize("compile-options", info.compile_options_size, sizeof(LuauHostCompileOptions), context);
        ValidateSize("callback-table", info.callback_table_size, sizeof(LuauHostCallbackTable), context);
        ValidateSize("state-options", info.state_options_size, sizeof(LuauHostStateOptions), context);
        ValidateSize("memory-info", info.memory_info_size, sizeof(LuauHostMemoryInfo), context);
        ValidateSize("buffer", info.buffer_size, sizeof(LuauHostBuffer), context);

        foreach (var (feature, name) in RequiredFeatures)
        {
            if ((info.feature_flags & (uint)feature) == 0)
            {
                throw new PlatformNotSupportedException(
                    $"The native Luau host ABI is missing the required {name} feature. {context}");
            }
        }

        ValidateTypeTag("nil", info.type_nil, lua_Type.LUA_TNIL, context);
        ValidateTypeTag("boolean", info.type_boolean, lua_Type.LUA_TBOOLEAN, context);
        ValidateTypeTag("light userdata", info.type_lightuserdata, lua_Type.LUA_TLIGHTUSERDATA, context);
        ValidateTypeTag("number", info.type_number, lua_Type.LUA_TNUMBER, context);
        ValidateTypeTag("integer", info.type_integer, lua_Type.LUA_TINTEGER, context);
        ValidateTypeTag("vector", info.type_vector, lua_Type.LUA_TVECTOR, context);
        ValidateTypeTag("string", info.type_string, lua_Type.LUA_TSTRING, context);
        ValidateTypeTag("table", info.type_table, lua_Type.LUA_TTABLE, context);
        ValidateTypeTag("function", info.type_function, lua_Type.LUA_TFUNCTION, context);
        ValidateTypeTag("userdata", info.type_userdata, lua_Type.LUA_TUSERDATA, context);
        ValidateTypeTag("thread", info.type_thread, lua_Type.LUA_TTHREAD, context);
        ValidateTypeTag("buffer", info.type_buffer, lua_Type.LUA_TBUFFER, context);
        ValidateTypeTag("class", info.type_class, lua_Type.LUA_TCLASS, context);
        ValidateTypeTag("object", info.type_object, lua_Type.LUA_TOBJECT, context);

        if (info.upstream_revision_hash != LuauNativeProtection.ExpectedUpstreamRevisionHash)
        {
            throw new PlatformNotSupportedException(
                $"The native Luau host upstream fingerprint is 0x{info.upstream_revision_hash:x16}; " +
                $"the managed runtime requires 0x{LuauNativeProtection.ExpectedUpstreamRevisionHash:x16}. {context}");
        }
        if (info.host_build_fingerprint != LuauNativeProtection.ExpectedHostBuildFingerprint)
        {
            throw new PlatformNotSupportedException(
                $"The native Luau host build fingerprint is 0x{info.host_build_fingerprint:x16}; " +
                $"the managed runtime requires 0x{LuauNativeProtection.ExpectedHostBuildFingerprint:x16}. {context}");
        }
    }

    static void ValidateSize(string name, uint actual, int expected, string context)
    {
        if (actual != (uint)expected)
        {
            throw new PlatformNotSupportedException(
                $"The native Luau host {name} size is {actual} bytes; " +
                $"the managed binding requires {expected} bytes. {context}");
        }
    }

    static void ValidateSize(string name, byte actual, int expected, string context)
    {
        if (actual != expected)
        {
            throw new PlatformNotSupportedException(
                $"The native Luau host {name} size is {actual} bytes; " +
                $"the managed binding requires {expected} bytes. {context}");
        }
    }

    static void ValidateTypeTag(string name, int actual, lua_Type expected, string context)
    {
        if (actual != (int)expected)
        {
            throw new PlatformNotSupportedException(
                $"The native Luau host {name} type tag is {actual}; " +
                $"the managed binding requires {(int)expected}. {context}");
        }
    }

    static string FingerprintContext(LuauNativeAbiInfo info)
    {
        return $"Upstream fingerprint 0x{info.upstream_revision_hash:x16}; " +
            $"host build fingerprint 0x{info.host_build_fingerprint:x16}.";
    }
}
