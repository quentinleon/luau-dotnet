namespace Luau;

internal static class LuauCompatibilityDiagnostics
{
    internal const string NativePointer =
        "Direct native pointer access is unsupported and will be removed; use managed Luau values and host-library APIs instead.";

    internal const string NativeCallback =
        "Raw Luau C callbacks are unsupported and will be removed; use CreateFunction or a managed host library instead.";

    internal const string NativeCompileOptions =
        "Constructing compile options from Luau.Native types is unsupported and will be removed; use the managed LuauCompileOptions properties instead.";

    internal const string OpenAllLibraries =
        "Opening every native library at once is unsupported and will be removed; open only individually reviewed libraries before sandboxing.";

    internal const string SandboxAlias =
        "Sandbox() is a transitional alias and will be removed; use SandboxRoot() to make the root operation explicit.";
}
