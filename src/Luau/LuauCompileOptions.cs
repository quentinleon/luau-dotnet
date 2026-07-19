namespace Luau;

/// <summary>
/// Selects official Luau compiler levels. Instances are immutable and are
/// copied at operation boundaries so later caller activity cannot change an
/// in-flight compilation.
/// </summary>
public sealed record LuauCompileOptions
{
    int optimizationLevel = 1;
    int debugLevel = 1;
    int typeInfoLevel = 1;
    int coverageLevel;

    /// <summary>Gets the production default compiler levels.</summary>
    public static LuauCompileOptions Default { get; } = new();

    /// <summary>Gets the compiler optimization level (0 through 2).</summary>
    public int OptimizationLevel
    {
        get => optimizationLevel;
        init => optimizationLevel = ValidateLevel(value, 0, 2, nameof(OptimizationLevel));
    }

    /// <summary>Gets the compiler debug-information level (0 through 2).</summary>
    public int DebugLevel
    {
        get => debugLevel;
        init => debugLevel = ValidateLevel(value, 0, 2, nameof(DebugLevel));
    }

    /// <summary>Gets the compiler type-information level (0 or 1).</summary>
    public int TypeInfoLevel
    {
        get => typeInfoLevel;
        init => typeInfoLevel = ValidateLevel(value, 0, 1, nameof(TypeInfoLevel));
    }

    /// <summary>
    /// Gets the coverage instrumentation level. Production compilation does
    /// not instrument coverage by default; tooling must opt in explicitly.
    /// </summary>
    public int CoverageLevel
    {
        get => coverageLevel;
        init => coverageLevel = ValidateLevel(value, 0, 2, nameof(CoverageLevel));
    }

    static int ValidateLevel(int value, int minimum, int maximum, string parameterName)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"The compiler level must be between {minimum} and {maximum}.");
        }

        return value;
    }
}
