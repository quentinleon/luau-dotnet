using Luau.Internal.Interop;
using static Luau.Internal.Interop.NativeMethods;

namespace Luau;

internal sealed unsafe class LuauCSharpFunction : LuauFunction, ILuauManagedCallbackFunction
{
    static readonly LuauHostManagedFunction callback = Call;
    readonly Func<LuauState, CancellationToken, int> csharpDelegate;
    readonly LuauVmContext context;
    int registrationId;

    public LuauCSharpFunction(
        LuauState state,
        Func<LuauState, CancellationToken, int> func,
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
        return new(csharpDelegate(access.State, cancellationToken));
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
            if (!context.TryGetManagedCallback(id, out registration) || registration.SynchronousCallback == null)
            {
                operation.RecordCallbackFailure(
                    registration?.Name,
                    new ObjectDisposedException(nameof(LuauFunction), "The managed callback is no longer registered."));
                return YieldFailureIfPossible(l);
            }

            var state = LuauState.GetCachedState(l);
            var resultCount = registration.SynchronousCallback(state, operation.CancellationToken);
            if (resultCount < 0 || resultCount > luau_host_stack_get_top(l))
            {
                throw new InvalidOperationException(
                    $"Managed callback returned invalid result count {resultCount} for a stack containing {luau_host_stack_get_top(l)} values.");
            }

            return resultCount;
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

    ~LuauCSharpFunction()
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
