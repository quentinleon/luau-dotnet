using System.Runtime.InteropServices;
using Luau.Internal.Interop;
using static Luau.Internal.Interop.NativeMethods;

namespace Luau;

internal sealed class LuauScriptFunction(LuauState state, int reference) : LuauFunction(state), ILuauReference
{
    int reference = reference;
    public int Reference => Volatile.Read(ref reference);
    LuauReferenceAccess ILuauReference.AcquireReference() => AcquireReference(Reference);

    protected override LuauState ResolvePublicState(LuauState owningState) =>
        owningState.GetMainThread();

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

        var baseTop = state.GetTop();
        try
        {
            state.Push(this);
            for (var i = 0; i < arguments.Length; i++)
            {
                state.Push(arguments.Span[i]);
            }
        }
        catch
        {
            state.SetTop(baseTop);
            throw;
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
