using Luau.Native;
using static Luau.Native.NativeMethods;

namespace Luau;

internal sealed unsafe class LuauCSharpAsyncFunction : LuauFunction, ILuauManagedCallbackFunction
{
    static readonly lua_CFunction callback = Call;
    readonly Func<LuauState, CancellationToken, ValueTask<int>> csharpDelegate;
    readonly LuauVmContext context;
    int registrationId;

    public LuauCSharpAsyncFunction(
        LuauState state,
        Func<LuauState, CancellationToken, ValueTask<int>> func,
        string? name = null) : base(state)
    {
        csharpDelegate = func;
        context = state.Context;
        registrationId = context.RegisterManagedCallback(name, func);
    }

    int ILuauManagedCallbackFunction.RegistrationId => Volatile.Read(ref registrationId);

    public override ValueTask<int> InvokeAsync(int argumentCount, CancellationToken cancellationToken = default)
    {
        using var access = AcquireFunctionAccess();
        return csharpDelegate(access.State, cancellationToken);
    }

    [Obsolete(LuauCompatibilityDiagnostics.NativePointer)]
    public unsafe override void* AsPointer()
    {
        using var access = AcquireFunctionAccess();
        return (void*)(nint)registrationId;
    }

    [Obsolete(LuauCompatibilityDiagnostics.NativeCallback)]
    public unsafe override lua_CFunction AsCFunction()
    {
        using var access = AcquireFunctionAccess();
        return callback;
    }

    [AOT.MonoPInvokeCallback(typeof(lua_CFunction))]
    static unsafe int Call(lua_State* l)
    {
        ScriptOperation? operation = null;
        LuauManagedCallbackRegistration? registration = null;

        try
        {
            if (!LuauVmContext.TryGetContext(l, out var context) ||
                (operation = context.GetActiveOperation()) == null)
            {
                return 0;
            }

            var idPointer = (int*)lua_touserdata(l, (int)lua_upvalueindex(1));
            if (idPointer == null)
            {
                operation.RecordCallbackFailure(
                    null,
                    new InvalidOperationException("The managed callback registration token is missing."));
                return YieldFailureIfPossible(l);
            }

            var id = *idPointer;
            if (!context.TryGetManagedCallback(id, out registration) || registration.AsynchronousCallback == null)
            {
                operation.RecordCallbackFailure(
                    registration?.Name,
                    new ObjectDisposedException(nameof(LuauFunction), "The managed callback is no longer registered."));
                return YieldFailureIfPossible(l);
            }

            if (!operation.IsAsync)
            {
                operation.RecordCallbackFailure(
                    registration.Name,
                    new InvalidOperationException(
                        "An asynchronous managed callback cannot run inside a synchronous Luau execution."));
                return YieldFailureIfPossible(l);
            }

            if (lua_isyieldable(l) == 0)
            {
                operation.RecordCallbackFailure(
                    registration.Name,
                    new InvalidOperationException(
                        "An asynchronous managed callback requires yieldable Luau execution."));
                return 0;
            }

            // Do not invoke managed user code while lua_resume is still on the
            // native stack. The runner dispatches the delegate only after it has
            // observed LUA_YIELD, so synchronous portions and fast continuations
            // can safely use the callback state.
            operation.QueueAsyncCallback(registration);
            // Preserve the Luau arguments as the internal yield payload. The
            // runner does not expose this yield to the host; it dispatches the
            // managed callback after lua_resume has unwound, where generated
            // async wrappers can safely read the original argument stack.
            return lua_yield(l, lua_gettop(l));
        }
        catch (Exception ex)
        {
            operation?.RecordCallbackFailure(registration?.Name, ex);
            return YieldFailureIfPossible(l);
        }
    }

    static unsafe int YieldFailureIfPossible(lua_State* state)
    {
        return lua_isyieldable(state) != 0 ? lua_yield(state, 0) : 0;
    }

    public override string ToString()
    {
        return $"function: (C# delegate)";
    }

    protected override void DisposeCore()
    {
        var id = Interlocked.Exchange(ref registrationId, 0);
        if (id != 0)
        {
            context.ReleaseManagedCallbackWrapper(id);
        }
    }

    ~LuauCSharpAsyncFunction()
    {
        try
        {
            var id = Interlocked.Exchange(ref registrationId, 0);
            if (id != 0)
            {
                context.ReleaseManagedCallbackWrapper(id);
            }
        }
        catch
        {
            // Finalizers must never surface registration cleanup failures.
        }
    }
}
