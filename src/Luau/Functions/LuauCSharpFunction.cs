using System.Runtime.InteropServices;
using Luau.Native;
using static Luau.Native.NativeMethods;

namespace Luau;

internal sealed class LuauCSharpFunction : LuauFunction
{
    readonly Func<LuauState, int> csharpDelegate;
    GCHandle handle;

    public LuauCSharpFunction(LuauState state, Func<LuauState, int> func) : base(state)
    {
        csharpDelegate = func;
        handle = GCHandle.Alloc(this);
        state.RegisterDisposable(this);
    }

    public override ValueTask<int> InvokeAsync(int argumentCount, CancellationToken cancellationToken = default)
    {
        ThrowIfDiposed();
        return new(csharpDelegate(State));
    }

    public unsafe override void* AsPointer()
    {
        ThrowIfDiposed();
        return GCHandle.ToIntPtr(handle).ToPointer();
    }

    public unsafe override lua_CFunction AsCFunction()
    {
        ThrowIfDiposed();
        return Call;
    }

    [AOT.MonoPInvokeCallback(typeof(lua_CFunction))]
    public unsafe static int Call(lua_State* l)
    {
        try
        {
            var state = LuauState.GetCachedState(l);
            var upval = lua_topointer(l, (int)lua_upvalueindex(1));
            var func = (LuauCSharpFunction)GCHandle.FromIntPtr((IntPtr)upval).Target!;
            return func.csharpDelegate(state);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            throw;
        }
    }

    public override string ToString()
    {
        return $"function: (C# delegate)";
    }

    protected override void DisposeCore()
    {
        handle.Free();
    }
}
