using System.Runtime.InteropServices;

namespace Luau;

internal sealed class GCHandleDisposable(GCHandle handle) : IDisposable
{
    public void Dispose()
    {
        handle.Free();
    }
}