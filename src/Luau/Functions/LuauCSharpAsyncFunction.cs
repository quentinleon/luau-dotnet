using Luau.Internal.Interop;
using static Luau.Internal.Interop.NativeMethods;

namespace Luau;

internal sealed unsafe class LuauCSharpAsyncFunction : LuauFunction, ILuauManagedCallbackFunction
{
    static readonly LuauHostManagedFunction callback = Call;
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
    LuauHostManagedFunction ILuauManagedCallbackFunction.Callback => callback;

    internal override ValueTask<int> InvokeAsync(int argumentCount, CancellationToken cancellationToken = default)
    {
        using var access = AcquireFunctionAccess();
        return csharpDelegate(access.State, cancellationToken);
    }

    [AOT.MonoPInvokeCallback(typeof(LuauHostManagedFunction))]
    static unsafe int Call(LuauHostState* l)
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

            var idPointer = (int*)luau_host_callback_userdata(l, 1);
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

            if (luau_host_is_yieldable(l) == 0)
            {
                operation.RecordCallbackFailure(
                    registration.Name,
                    new InvalidOperationException(
                        "An asynchronous managed callback requires yieldable Luau execution."));
                return 0;
            }

            // Do not invoke managed user code while luau_host_resume is still on the
            // native stack. The runner dispatches the delegate only after it has
            // observed LUA_YIELD, so synchronous portions and fast continuations
            // can safely use the callback state.
            operation.QueueAsyncCallback(registration);
            // Preserve the Luau arguments as the internal yield payload. The
            // runner does not expose this yield to the host; it dispatches the
            // managed callback after luau_host_resume has unwound, where generated
            // async wrappers can safely read the original argument stack.
            return luau_host_yield(l, luau_host_stack_get_top(l));
        }
        catch (Exception ex)
        {
            operation?.RecordCallbackFailure(registration?.Name, ex);
            return YieldFailureIfPossible(l);
        }
    }

    static unsafe int YieldFailureIfPossible(LuauHostState* state)
    {
        return luau_host_is_yieldable(state) != 0 ? luau_host_yield(state, 0) : 0;
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
