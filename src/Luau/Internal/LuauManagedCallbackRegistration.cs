using Luau.Native;

namespace Luau;

internal interface ILuauManagedCallbackFunction
{
    int RegistrationId { get; }
}

internal sealed class LuauManagedCallbackRegistration
{
    internal LuauManagedCallbackRegistration(
        int id,
        string? name,
        Func<LuauState, int> callback)
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
    internal Func<LuauState, int>? SynchronousCallback { get; }
    internal Func<LuauState, CancellationToken, ValueTask<int>>? AsynchronousCallback { get; }
    internal bool IsAsync => AsynchronousCallback != null;
}

internal static unsafe class LuauManagedCallbackLifetime
{
    static readonly NativeMethods.lua_newuserdatadtor_dtor_delegate destructor = Destroy;

    internal static NativeMethods.lua_newuserdatadtor_dtor_delegate Destructor => destructor;

    [AOT.MonoPInvokeCallback(typeof(NativeMethods.lua_newuserdatadtor_dtor_delegate))]
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
