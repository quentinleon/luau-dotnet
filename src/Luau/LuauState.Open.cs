using Luau.Internal.Interop;
using static Luau.Internal.Interop.NativeMethods;

namespace Luau;

unsafe partial class LuauState
{
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

    /// <summary>
    /// Opens upstream Luau's signed 64-bit integer library. Integer values are
    /// deliberately distinct from ordinary Luau numbers; use
    /// <c>integer.tonumber</c> for an explicit lossy conversion in script.
    /// </summary>
    public void OpenIntegerLibrary()
    {
        OpenStandardLibrary(ProtectedStandardLibrary.Integer, "integer");
    }

    void OpenStandardLibrary(ProtectedStandardLibrary library, string name)
    {
        ThrowIfDisposed();
        ThrowIfLibrariesFrozen();
        using var access = EnterNativeAccess();
        var originalTop = luau_host_stack_get_top(l);

        try
        {
            var resultCount = 0;
            LuauNativeProtection.Prepare(context);
            var status = luau_host_open_library(l, (LuauHostLibrary)library, &resultCount);
            LuauNativeProtection.ThrowIfFailed(this, l, status, $"open the {name} library");
        }
        finally
        {
            SetTop(originalTop);
        }
    }

    public void OpenRequireLibrary(LuauRequirer requirer)
    {
        ThrowIfDisposed();
        ThrowIfLibrariesFrozen();

        this["require"] = CreateFunction("require", call =>
        {
            var path = call.Read<string>(0);
            if (requirer.TryLoad(call.State, path, out var result))
            {
                call.Return(result);
                return;
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
        Integer = 11,
    }
}
