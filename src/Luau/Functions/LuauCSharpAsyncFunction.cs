using System.Runtime.InteropServices;
using Luau.Native;
using static Luau.Native.NativeMethods;

namespace Luau;

internal sealed class LuauCSharpAsyncFunction : LuauFunction
{
    readonly Func<LuauState, CancellationToken, ValueTask<int>> csharpDelegate;
    GCHandle handle;

    public LuauCSharpAsyncFunction(LuauState state, Func<LuauState, CancellationToken, ValueTask<int>> func) : base(state)
    {
        csharpDelegate = func;
        handle = GCHandle.Alloc(this);
        state.RegisterDisposable(this);
    }

    public override ValueTask<int> InvokeAsync(int argumentCount, CancellationToken cancellationToken = default)
    {
        ThrowIfDiposed();
        return csharpDelegate(State, cancellationToken);
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
    unsafe static int Call(lua_State* l)
    {
        try
        {
            var state = LuauState.GetCachedState(l);
            if (!state.Runner!.IsAsync)
            {
                state.Runner.TrySetException(new LuauException("An asynchronous function was called within a synchronously called Luau script"));
            }

            var upval = lua_topointer(l, (int)lua_upvalueindex(1));
            var func = (LuauCSharpAsyncFunction)GCHandle.FromIntPtr((IntPtr)upval).Target!;

            lua_callbacks(l)->interrupt = Marshal.GetFunctionPointerForDelegate(AsyncFunctionContinution).ToPointer();

            var awaiter = func.csharpDelegate(state, state.Runner.CancellationToken).GetAwaiter();
            awaiter.UnsafeOnCompleted(() =>
            {
                try
                {
                    var result = awaiter.GetResult();
                    state.Runner.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    state.Runner.TrySetException(ex);
                }
            });

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            throw;
        }
    }

    [AOT.MonoPInvokeCallback(typeof(lua_Continuation))]
    static unsafe void AsyncFunctionContinution(lua_State* l, int gc)
    {
        if (gc >= 0) return;
        lua_callbacks(l)->interrupt = null;
        LuauState.GetCachedState(l).Runner!.IsYieldFromInterrupt = true;
        lua_yield(l, 0);
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