using Luau.Native;
using static Luau.Native.NativeMethods;

namespace Luau;

internal sealed unsafe class LuauCSharpFunction : LuauFunction, ILuauManagedCallbackFunction
{
    static readonly lua_CFunction callback = Call;
    readonly Func<LuauState, int> csharpDelegate;
    readonly LuauVmContext context;
    int registrationId;

    public LuauCSharpFunction(LuauState state, Func<LuauState, int> func, string? name = null) : base(state)
    {
        csharpDelegate = func;
        context = state.Context;
        registrationId = context.RegisterManagedCallback(name, func);
    }

    int ILuauManagedCallbackFunction.RegistrationId => Volatile.Read(ref registrationId);

    public override ValueTask<int> InvokeAsync(int argumentCount, CancellationToken cancellationToken = default)
    {
        using var access = AcquireFunctionAccess();
        return new(csharpDelegate(access.State));
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
    public unsafe static int Call(lua_State* l)
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
            if (!context.TryGetManagedCallback(id, out registration) || registration.SynchronousCallback == null)
            {
                operation.RecordCallbackFailure(
                    registration?.Name,
                    new ObjectDisposedException(nameof(LuauFunction), "The managed callback is no longer registered."));
                return YieldFailureIfPossible(l);
            }

            var state = LuauState.GetCachedState(l);
            var resultCount = registration.SynchronousCallback(state);
            if (resultCount < 0 || resultCount > lua_gettop(l))
            {
                throw new InvalidOperationException(
                    $"Managed callback returned invalid result count {resultCount} for a stack containing {lua_gettop(l)} values.");
            }

            return resultCount;
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
