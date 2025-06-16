using System.Runtime.InteropServices;
using Luau.Native;

namespace Luau;

internal unsafe static class LuaRequireHelper
{
    [AOT.MonoPInvokeCallback(typeof(luarequire_Configuration_init))]
    public static void Initialize(luarequire_Configuration* config)
    {
        config->is_require_allowed = Marshal.GetFunctionPointerForDelegate(IsRequireAllowed).ToPointer();
        config->reset = Marshal.GetFunctionPointerForDelegate(Reset).ToPointer();
        config->jump_to_alias = Marshal.GetFunctionPointerForDelegate(JumpToAlias).ToPointer();
        config->to_parent = Marshal.GetFunctionPointerForDelegate(ToParent).ToPointer();
        config->to_child = Marshal.GetFunctionPointerForDelegate(ToChild).ToPointer();
        config->is_module_present = Marshal.GetFunctionPointerForDelegate(IsModulePresent).ToPointer();
        config->get_chunkname = Marshal.GetFunctionPointerForDelegate(GetChunkName).ToPointer();
        config->get_loadname = Marshal.GetFunctionPointerForDelegate(GetLoadName).ToPointer();
        config->get_cache_key = Marshal.GetFunctionPointerForDelegate(GetCacheKey).ToPointer();
        config->is_config_present = Marshal.GetFunctionPointerForDelegate(IsConfigPresent).ToPointer();
        config->get_config = Marshal.GetFunctionPointerForDelegate(GetConfig).ToPointer();
        config->load = Marshal.GetFunctionPointerForDelegate(Load).ToPointer();
    }

    static bool IsRequireAllowed(lua_State* L, void* ctx, char* requirer_chunkname)
    {
        var requirer = (LuauRequirer)GCHandle.FromIntPtr((nint)ctx).Target!;
        try
        {
            return requirer.IsRequireAllowed(LuauState.GetCachedState(L), Marshal.PtrToStringUTF8((nint)requirer_chunkname) ?? "");
        }
        catch (Exception ex)
        {
            requirer.OnError(ex);
            return false;
        }
    }

    static luarequire_NavigateResult Reset(lua_State* L, void* ctx, char* requirer_chunkname)
    {
        var requirer = (LuauRequirer)GCHandle.FromIntPtr((nint)ctx).Target!;
        try
        {
            return (luarequire_NavigateResult)requirer.Reset(LuauState.GetCachedState(L), Marshal.PtrToStringUTF8((nint)requirer_chunkname) ?? "");
        }
        catch (Exception ex)
        {
            requirer.OnError(ex);
            return luarequire_NavigateResult.NAVIGATE_NOT_FOUND;
        }
    }

    static luarequire_NavigateResult JumpToAlias(lua_State* L, void* ctx, char* path)
    {
        var requirer = (LuauRequirer)GCHandle.FromIntPtr((nint)ctx).Target!;
        try
        {
            return (luarequire_NavigateResult)requirer.JumpToAlias(LuauState.GetCachedState(L), Marshal.PtrToStringUTF8((nint)path) ?? "");
        }
        catch (Exception ex)
        {
            requirer.OnError(ex);
            return luarequire_NavigateResult.NAVIGATE_NOT_FOUND;
        }
    }

    static luarequire_NavigateResult ToParent(lua_State* L, void* ctx)
    {
        var requirer = (LuauRequirer)GCHandle.FromIntPtr((nint)ctx).Target!;
        try
        {
            return (luarequire_NavigateResult)requirer.MoveToParent(LuauState.GetCachedState(L));
        }
        catch (Exception ex)
        {
            requirer.OnError(ex);
            return luarequire_NavigateResult.NAVIGATE_NOT_FOUND;
        }
    }

    static luarequire_NavigateResult ToChild(lua_State* L, void* ctx, char* name)
    {
        var requirer = (LuauRequirer)GCHandle.FromIntPtr((nint)ctx).Target!;
        try
        {
            return (luarequire_NavigateResult)requirer.MoveToChild(LuauState.GetCachedState(L), Marshal.PtrToStringUTF8((nint)name) ?? "");
        }
        catch (Exception ex)
        {
            requirer.OnError(ex);
            return luarequire_NavigateResult.NAVIGATE_NOT_FOUND;
        }
    }

    static bool IsModulePresent(lua_State* L, void* ctx)
    {
        var requirer = (LuauRequirer)GCHandle.FromIntPtr((nint)ctx).Target!;
        try
        {
            return requirer.IsModulePresent(LuauState.GetCachedState(L));
        }
        catch (Exception ex)
        {
            requirer.OnError(ex);
            return false;
        }
    }

    static luarequire_WriteResult GetChunkName(lua_State* L, void* ctx, char* buffer, nuint buffer_size, nuint* size_out)
    {
        var requirer = (LuauRequirer)GCHandle.FromIntPtr((nint)ctx).Target!;
        try
        {
            var span = new Span<byte>((byte*)buffer, (int)buffer_size);
            if (!requirer.TryGetChunkName(LuauState.GetCachedState(L), span, out var bytesWritten))
            {
                *size_out = (nuint)Math.Max((int)(*size_out * 4), 1024);
                return luarequire_WriteResult.WRITE_BUFFER_TOO_SMALL;
            }

            *size_out = (nuint)bytesWritten;

            return luarequire_WriteResult.WRITE_SUCCESS;
        }
        catch (Exception ex)
        {
            requirer.OnError(ex);
            return luarequire_WriteResult.WRITE_FAILURE;
        }
    }

    static luarequire_WriteResult GetLoadName(lua_State* L, void* ctx, char* buffer, nuint buffer_size, nuint* size_out)
    {
        var requirer = (LuauRequirer)GCHandle.FromIntPtr((nint)ctx).Target!;
        try
        {
            var span = new Span<byte>((byte*)buffer, (int)buffer_size);
            if (!requirer.TryGetLoadName(LuauState.GetCachedState(L), span, out var bytesWritten))
            {
                *size_out = (nuint)Math.Max((int)(*size_out * 4), 1024);
                return luarequire_WriteResult.WRITE_BUFFER_TOO_SMALL;
            }

            *size_out = (nuint)bytesWritten;

            return luarequire_WriteResult.WRITE_SUCCESS;
        }
        catch (Exception ex)
        {
            requirer.OnError(ex);
            return luarequire_WriteResult.WRITE_FAILURE;
        }
    }

    static luarequire_WriteResult GetCacheKey(lua_State* L, void* ctx, char* buffer, nuint buffer_size, nuint* size_out)
    {
        var requirer = (LuauRequirer)GCHandle.FromIntPtr((nint)ctx).Target!;
        try
        {
            var span = new Span<byte>((byte*)buffer, (int)buffer_size);
            if (!requirer.TryGetCacheKey(LuauState.GetCachedState(L), span, out var bytesWritten))
            {
                *size_out = (nuint)Math.Max((int)(*size_out * 2), 1024);
                return luarequire_WriteResult.WRITE_BUFFER_TOO_SMALL;
            }

            *size_out = (nuint)bytesWritten;
            return luarequire_WriteResult.WRITE_SUCCESS;
        }
        catch (Exception ex)
        {
            requirer.OnError(ex);
            return luarequire_WriteResult.WRITE_FAILURE;
        }
    }

    static bool IsConfigPresent(lua_State* L, void* ctx)
    {
        var requirer = (LuauRequirer)GCHandle.FromIntPtr((nint)ctx).Target!;
        try
        {
            return requirer.IsConfigPresent(LuauState.GetCachedState(L));
        }
        catch (Exception ex)
        {
            requirer.OnError(ex);
            return false;
        }
    }

    static luarequire_WriteResult GetConfig(lua_State* L, void* ctx, char* buffer, nuint buffer_size, nuint* size_out)
    {
        var requirer = (LuauRequirer)GCHandle.FromIntPtr((nint)ctx).Target!;
        try
        {
            var span = new Span<byte>((byte*)buffer, (int)buffer_size);
            if (!requirer.TryGetConfig(LuauState.GetCachedState(L), span, out var bytesWritten))
            {
                *size_out = (nuint)Math.Max((int)(*size_out * 4), 1024);
                return luarequire_WriteResult.WRITE_BUFFER_TOO_SMALL;
            }

            *size_out = (nuint)bytesWritten;

            return luarequire_WriteResult.WRITE_SUCCESS;
        }
        catch (Exception ex)
        {
            requirer.OnError(ex);
            return luarequire_WriteResult.WRITE_FAILURE;
        }
    }

    static int Load(lua_State* L, void* ctx, char* path, char* chunkname, char* loadname)
    {
        var requirer = (LuauRequirer)GCHandle.FromIntPtr((nint)ctx).Target!;

        var filePath = Marshal.PtrToStringUTF8((IntPtr)path)!;
        var chunkName = Marshal.PtrToStringUTF8((IntPtr)chunkname)!;
        var loadName = Marshal.PtrToStringUTF8((IntPtr)loadname)!;

        var state = LuauState.GetCachedState(L);

        var thread = state.CreateThread();
        try
        {
            var count = requirer.Load(thread, filePath, chunkName, loadName);
            thread.XMove(state, count);
            return count;
        }
        catch (Exception ex)
        {
            requirer.OnError(ex);
            state.Push(ex.Message);
            return 1;
        }
    }
}