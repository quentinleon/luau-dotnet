namespace Luau;

public unsafe partial class LuauState
{
    internal LuauHostOperationLease BeginHostOperationIfNeeded()
    {
        ThrowIfDisposed();
        var active = context.GetActiveOperation();
        if (active != null)
        {
            return new LuauHostOperationLease(
                active,
                ownedOperation: null,
                initialThreadStatus: null);
        }

        var initialThreadStatus = GetStatus();
        var created = BeginOperation(
            chunkName: null,
            options: null,
            cancellationToken: default,
            isAsync: false);
        return new LuauHostOperationLease(created, created, initialThreadStatus);
    }
}

internal readonly ref struct LuauHostOperationLease
{
    readonly ScriptOperation? ownedOperation;
    readonly LuauThreadStatus? initialThreadStatus;

    internal LuauHostOperationLease(
        ScriptOperation operation,
        ScriptOperation? ownedOperation,
        LuauThreadStatus? initialThreadStatus)
    {
        Operation = operation;
        this.ownedOperation = ownedOperation;
        this.initialThreadStatus = initialThreadStatus;
    }

    internal ScriptOperation Operation { get; }

    internal bool IsOwnedOperationSuspended =>
        ownedOperation != null &&
        initialThreadStatus == LuauThreadStatus.Running &&
        ownedOperation.State.GetStatus() != LuauThreadStatus.Running;

    internal void AbortSuspendedOperation()
    {
        if (ownedOperation == null)
        {
            return;
        }

        ScriptRunner.AbortHostOperation(ownedOperation);
    }

    public void Dispose()
    {
        ownedOperation?.Dispose();
    }
}
