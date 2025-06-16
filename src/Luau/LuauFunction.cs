using Luau.Native;

namespace Luau;

public abstract class LuauFunction(LuauState state) : IDisposable
{
    public LuauState State { get; } = state;

    public bool IsDisposed => disposed;
    bool disposed;

    public abstract ValueTask<int> InvokeAsync(int argumentCount, CancellationToken cancellationToken = default);
    public unsafe abstract void* AsPointer();
    public abstract lua_CFunction AsCFunction();

    protected virtual void DisposeCore() { }

    public void Dispose()
    {
        if (!disposed)
        {
            DisposeCore();
            disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    protected void ThrowIfDiposed()
    {
        if (IsDisposed) ThrowHelper.ThrowObjectDisposedException(nameof(LuauFunction));
    }
}