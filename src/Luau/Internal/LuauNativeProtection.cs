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
    internal const int ExpectedAbiVersion = 2;
    static int abiVerified;

    internal static void EnsureAvailable()
    {
        if (Volatile.Read(ref abiVerified) != 0)
        {
            return;
        }

        int actual;
        try
        {
            actual = luau_ffi_protected_abi_version();
        }
        catch (EntryPointNotFoundException exception)
        {
            throw new PlatformNotSupportedException(
                "The native Luau plugin does not provide the protected host-call ABI required by this managed runtime. " +
                "Rebuild and deploy the matching native plugin.",
                exception);
        }

        if (actual != ExpectedAbiVersion)
        {
            throw new PlatformNotSupportedException(
                $"The native Luau protected-call ABI is version {actual}; version {ExpectedAbiVersion} is required.");
        }

        Volatile.Write(ref abiVerified, 1);
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
