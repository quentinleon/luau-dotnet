namespace Luau;

public record LuauCompileOptions
{
    public static readonly LuauCompileOptions Default = new();

    public int OptimizationLevel { get; init; } = 1;

    public int DebugLevel { get; init; } = 1;

    public int TypeInfoLevel { get; init; } = 1;

    public int CoverageLevel { get; init; } = 2;
}
