namespace Luau;

public sealed record LuauCompileOptions
{
    public static LuauCompileOptions Default { get; } = new();

    public int OptimizationLevel { get; init; } = 1;

    public int DebugLevel { get; init; } = 1;

    public int TypeInfoLevel { get; init; } = 1;

    /// <summary>
    /// Gets the coverage instrumentation level. Production compilation does
    /// not instrument coverage by default; tooling must opt in explicitly.
    /// </summary>
    public int CoverageLevel { get; init; } = 0;
}
