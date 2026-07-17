namespace Luau;

internal interface ILuauReference : IDisposable
{
    bool IsDisposed { get; }
    LuauReferenceAccess AcquireReference();
}

internal readonly ref struct LuauReferenceAccess
{
    readonly object? lifetimeGate;
    readonly LuauNativeAccess nativeAccess;

    internal LuauState State { get; }
    internal int Reference { get; }

    internal LuauReferenceAccess(
        LuauState state,
        int reference,
        object? lifetimeGate,
        LuauNativeAccess nativeAccess)
    {
        State = state;
        Reference = reference;
        this.lifetimeGate = lifetimeGate;
        this.nativeAccess = nativeAccess;
    }

    public void Dispose()
    {
        if (lifetimeGate != null)
        {
            Monitor.Exit(lifetimeGate);
        }
        nativeAccess.Dispose();
    }
}
