using System.Runtime.InteropServices;
using static Luau.Native.NativeMethods;

namespace Luau;

unsafe partial class LuauState
{
    public void OpenLibraries()
    {
        ThrowIfDisposed();
        luaL_openlibs(l);
    }

    public void OpenBaseLibrary()
    {
        ThrowIfDisposed();
        _ = luaopen_base(l);
    }

    public void OpenMathLibrary()
    {
        ThrowIfDisposed();
        _ = luaopen_math(l);
    }

    public void OpenTableLibrary()
    {
        ThrowIfDisposed();
        _ = luaopen_table(l);
    }

    public void OpenStringLibrary()
    {
        ThrowIfDisposed();
        _ = luaopen_string(l);
    }

    public void OpenCoroutineLibrary()
    {
        ThrowIfDisposed();
        _ = luaopen_coroutine(l);
    }

    public void OpenBit32Library()
    {
        ThrowIfDisposed();
        _ = luaopen_bit32(l);
    }

    public void OpenUtf8Library()
    {
        ThrowIfDisposed();
        _ = luaopen_utf8(l);
    }

    public void OpenOSLibrary()
    {
        ThrowIfDisposed();
        _ = luaopen_os(l);
    }

    public void OpenDebugLibrary()
    {
        ThrowIfDisposed();
        _ = luaopen_debug(l);
    }

    public void OpenBufferLibrary()
    {
        ThrowIfDisposed();
        _ = luaopen_buffer(l);
    }

    public void OpenVectorLibrary()
    {
        ThrowIfDisposed();
        _ = luaopen_vector(l);
    }

    public void OpenRequireLibrary(LuauRequirer requirer)
    {
        ThrowIfDisposed();

        this["require"] = CreateFunction(state =>
        {
            var path = state.ToString(-1);
            if (requirer.TryLoad(state, path))
            {
                return 1;
            }

            throw new LuauException($"module '{path}' not found");
        });
    }

    public void OpenLibrary<T>(T library)
        where T : ILuauLibrary
    {
        ThrowIfDisposed();
        library.RegisterTo(this);

        if (library is IDisposable disposable)
        {
            disposables.Add(disposable);
        }
    }

    public void OpenLibrary<T>()
        where T : ILuauLibrary, new()
    {
        OpenLibrary(new T());
    }
}
