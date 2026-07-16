using Luau.Internal.Interop;

namespace Luau;

internal interface ILuauManagedCallbackFunction
{
    int RegistrationId { get; }
    LuauHostManagedFunction Callback { get; }
}

internal sealed class LuauManagedCallbackRegistration
{
    internal LuauManagedCallbackRegistration(
        int id,
        string? name,
        Func<LuauState, CancellationToken, int> callback)
    {
        Id = id;
        Name = name;
        SynchronousCallback = callback;
    }

    internal LuauManagedCallbackRegistration(
        int id,
        string? name,
        Func<LuauState, CancellationToken, ValueTask<int>> callback)
    {
        Id = id;
        Name = name;
        AsynchronousCallback = callback;
    }

    internal int Id { get; }
    internal string? Name { get; }
    internal Func<LuauState, CancellationToken, int>? SynchronousCallback { get; }
    internal Func<LuauState, CancellationToken, ValueTask<int>>? AsynchronousCallback { get; }
    internal bool IsAsync => AsynchronousCallback != null;
}

internal static unsafe class LuauManagedCallbackLifetime
{
    static readonly LuauHostUserdataDestructor destructor = Destroy;

    internal static LuauHostUserdataDestructor Destructor => destructor;

    [AOT.MonoPInvokeCallback(typeof(LuauHostUserdataDestructor))]
    static void Destroy(void* userdata)
    {
        try
        {
            if (userdata != null)
            {
                LuauVmContext.ReleaseManagedCallbackFromNative(*(int*)userdata);
            }
        }
        catch
        {
            // Native GC/finalization callbacks must never unwind into Luau.
        }
    }
}
