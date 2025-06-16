using Luau.Native;

namespace Luau;

public record LuauCompileOptions
{
    public static readonly LuauCompileOptions Default = new();

    public LuauCompileOptions()
    {
    }

    public LuauCompileOptions(lua_CompileOptions options)
    {
        this.options = options;
    }

    public int OptimizationLevel
    {
        get => options.optimizationLevel;
        init => options.optimizationLevel = value;
    }

    public int DebugLevel
    {
        get => options.debugLevel;
        init => options.debugLevel = value;
    }

    public int TypeInfoLevel
    {
        get => options.typeInfoLevel;
        init => options.typeInfoLevel = value;
    }

    public int CoverageLevel
    {
        get => options.coverageLevel;
        init => options.coverageLevel = value;
    }

    internal lua_CompileOptions options = new()
    {
        debugLevel = 1,
        optimizationLevel = 1,
        typeInfoLevel = 1,
        coverageLevel = 2,
    };
}