using System.Text;
using Luau.Internal.Interop;
using static Luau.Internal.Interop.NativeMethods;

namespace Luau;

internal unsafe static class LuauReferenceHelper
{
    public static int CreateReference(LuauState state, int index, string operation)
    {
        using var access = state.EnterNativeAccess();
        var pointer = state.PointerUnsafe;
        var reference = -1;

        LuauNativeProtection.Prepare(state.Context);
        var status = luau_host_reference_create(pointer, index, &reference);
        LuauNativeProtection.ThrowIfFailed(state, pointer, status, operation);
        return reference;
    }

    public static void PushReference(LuauState state, int reference, string operation)
    {
        using var access = state.EnterNativeAccess();
        var pointer = state.PointerUnsafe;
        var ignoredType = 0;

        LuauNativeProtection.Prepare(state.Context);
        var status = luau_host_reference_push(pointer, reference, &ignoredType);
        LuauNativeProtection.ThrowIfFailed(state, pointer, status, operation);
    }

    public static int RetainReference(LuauState state, int reference, string operation)
    {
        using var access = state.EnterNativeAccess();
        var originalTop = state.GetTop();
        try
        {
            PushReference(state, reference, operation);
            return CreateReference(state, -1, operation);
        }
        finally
        {
            state.SetTop(originalTop);
        }
    }

    public static string RefToString(LuauState state, int reference)
    {
        using var access = state.EnterNativeAccess();
        var pointer = state.PointerUnsafe;

        // A root state has no registry reference. Format it directly without
        // asking Luau to execute a __tostring metamethod.
        if (reference < 0)
        {
            return $"thread: 0x{(nuint)(nint)pointer:x}";
        }

        var originalTop = luau_host_stack_get_top(pointer);
        try
        {
            PushReference(state, reference, "format a managed Luau reference");
            var type = luau_host_type(pointer, -1);
            var typeName = ReadTypeName(pointer, type);
            var valuePointer = (nuint)(nint)luau_host_to_pointer(pointer, -1);
            return valuePointer == 0
                ? typeName
                : $"{typeName}: 0x{valuePointer:x}";
        }
        finally
        {
            state.SetTop(originalTop);
        }
    }

    static string ReadTypeName(LuauHostState* state, int type)
    {
        var text = luau_host_type_name(state, type);
        if (text == null)
        {
            return "value";
        }

        var length = 0;
        while (length < 64 && text[length] != 0)
        {
            length++;
        }

        return length == 0
            ? "value"
            : Encoding.UTF8.GetString(new ReadOnlySpan<byte>(text, length));
    }
}
