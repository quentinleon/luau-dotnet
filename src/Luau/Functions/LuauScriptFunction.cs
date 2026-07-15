using System.Runtime.InteropServices;
using Luau.Native;
using static Luau.Native.NativeMethods;

namespace Luau;

internal sealed class LuauScriptFunction(LuauState state, int reference) : LuauFunction(state), ILuauReference
{
    int reference = reference;
    public int Reference => Volatile.Read(ref reference);
    LuauReferenceAccess ILuauReference.AcquireReference() => AcquireReference(Reference);

    protected override LuauState ResolvePublicState(LuauState owningState) =>
        owningState.GetMainThread();

    public unsafe override lua_CFunction AsCFunction()
    {
        using var access = AcquireFunctionAccess();
        throw new InvalidOperationException("A Luau script function is not a native C callback.");
    }

    public unsafe override void* AsPointer()
    {
        using var access = AcquireReference(Reference);
        return LuauReferenceHelper.GetRefPointer(access.State, access.Reference);
    }

    public override async ValueTask<int> InvokeAsync(
        int argumentCount,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDiposed();
        var state = State;
        using var operation = state.BeginOperation(
            chunkName: null,
            options: null,
            cancellationToken,
            isAsync: true);
        using var runner = ScriptRunner.Rent();
        return await runner.RunCountAsync(operation, argumentCount).ConfigureAwait(false);
    }

    internal async ValueTask<LuauValue[]> InvokeWithArgumentsAsync(
        ReadOnlyMemory<LuauValue> arguments,
        CancellationToken cancellationToken)
    {
        ThrowIfDiposed();
        var state = State;
        using var operation = state.BeginOperation(
            chunkName: null,
            options: null,
            cancellationToken,
            isAsync: true);
        using var runner = ScriptRunner.Rent();

        state.Push(this);
        for (var i = 0; i < arguments.Length; i++)
        {
            state.Push(arguments.Span[i]);
        }

        return await runner.RunAsync(operation, state, arguments.Length).ConfigureAwait(false);
    }

    public override string ToString()
    {
        using var access = AcquireReference(Reference);
        return LuauReferenceHelper.RefToString(access.State, access.Reference);
    }

    protected override void DisposeCore()
    {
        var currentReference = Interlocked.Exchange(ref reference, -1);
        if (currentReference >= 0)
        {
            OwningState.TryReleaseReference(currentReference);
        }
    }

    ~LuauScriptFunction()
    {
        DisposeFromFinalizer();
    }
}
