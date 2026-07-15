using static Luau.Native.NativeMethods;

namespace Luau;

unsafe partial class LuauState
{
    /// <summary>
    /// Opens every upstream standard library, including privileged OS and
    /// debug capabilities. This bypasses the reviewed-library contract and
    /// must never be exposed to untrusted mods.
    /// </summary>
    [Obsolete(LuauCompatibilityDiagnostics.OpenAllLibraries)]
    public void OpenLibraries()
    {
        ThrowIfDisposed();
        ThrowIfLibrariesFrozen();
        using var access = EnterNativeAccess();
        var originalTop = lua_gettop(l);
        try
        {
            LuauNativeProtection.Prepare(context);
            var status = luau_ffi_protected_openlibs(l);
            LuauNativeProtection.ThrowIfFailed(this, l, status, "open the standard libraries");
        }
        finally
        {
            lua_settop(l, originalTop);
        }
    }

    public void OpenBaseLibrary()
    {
        OpenStandardLibrary(ProtectedStandardLibrary.Base, "base");
    }

    public void OpenMathLibrary()
    {
        OpenStandardLibrary(ProtectedStandardLibrary.Math, "math");
    }

    public void OpenTableLibrary()
    {
        OpenStandardLibrary(ProtectedStandardLibrary.Table, "table");
    }

    public void OpenStringLibrary()
    {
        OpenStandardLibrary(ProtectedStandardLibrary.String, "string");
    }

    public void OpenCoroutineLibrary()
    {
        OpenStandardLibrary(ProtectedStandardLibrary.Coroutine, "coroutine");
    }

    public void OpenBit32Library()
    {
        OpenStandardLibrary(ProtectedStandardLibrary.Bit32, "bit32");
    }

    public void OpenUtf8Library()
    {
        OpenStandardLibrary(ProtectedStandardLibrary.Utf8, "utf8");
    }

    /// <summary>
    /// Opens the privileged OS library before root sandboxing. This capability
    /// is host authority and must never be exposed to untrusted mods.
    /// </summary>
    public void OpenOSLibrary()
    {
        OpenStandardLibrary(ProtectedStandardLibrary.OS, "os");
    }

    /// <summary>
    /// Opens the privileged debug library before root sandboxing. This
    /// capability weakens script isolation and must never be exposed to
    /// untrusted mods.
    /// </summary>
    public void OpenDebugLibrary()
    {
        OpenStandardLibrary(ProtectedStandardLibrary.Debug, "debug");
    }

    public void OpenBufferLibrary()
    {
        OpenStandardLibrary(ProtectedStandardLibrary.Buffer, "buffer");
    }

    public void OpenVectorLibrary()
    {
        OpenStandardLibrary(ProtectedStandardLibrary.Vector, "vector");
    }

    void OpenStandardLibrary(ProtectedStandardLibrary library, string name)
    {
        ThrowIfDisposed();
        ThrowIfLibrariesFrozen();
        using var access = EnterNativeAccess();
        var originalTop = lua_gettop(l);

        try
        {
            var resultCount = 0;
            LuauNativeProtection.Prepare(context);
            var status = luau_ffi_protected_openlibrary(l, (int)library, &resultCount);
            LuauNativeProtection.ThrowIfFailed(this, l, status, $"open the {name} library");
        }
        finally
        {
            lua_settop(l, originalTop);
        }
    }

    public void OpenRequireLibrary(LuauRequirer requirer)
    {
        ThrowIfDisposed();
        ThrowIfLibrariesFrozen();

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
        ThrowIfLibrariesFrozen();
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

    void ThrowIfLibrariesFrozen()
    {
        if (context.IsRootSandboxed)
        {
            ThrowHelper.ThrowInvalidOperationException(
                "Libraries and host APIs must be registered before SandboxRoot is applied.");
        }
    }

    enum ProtectedStandardLibrary
    {
        Base = 0,
        Coroutine = 1,
        Table = 2,
        OS = 3,
        String = 4,
        Bit32 = 5,
        Buffer = 6,
        Utf8 = 7,
        Math = 8,
        Debug = 9,
        Vector = 10,
    }
}
