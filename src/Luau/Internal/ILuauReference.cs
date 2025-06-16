namespace Luau;

internal interface ILuauReference : IDisposable
{
    bool IsDisposed { get; }
    LuauState State { get; }
    int Reference { get; }
}