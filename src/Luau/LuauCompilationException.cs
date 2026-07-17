namespace Luau;

/// <summary>Reports a source diagnostic produced by the Luau compiler.</summary>
public sealed class LuauCompilationException : LuauException
{
    /// <summary>Initializes an exception with the compiler diagnostic.</summary>
    public LuauCompilationException(string message)
        : base(message)
    {
    }
}
