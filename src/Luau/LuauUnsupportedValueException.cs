namespace Luau;

/// <summary>
/// Thrown when the native VM produces a value kind whose identity and lifetime
/// semantics are not part of the supported managed value model.
/// </summary>
public sealed class LuauUnsupportedValueException : LuauException
{
    internal LuauUnsupportedValueException(string valueKind)
        : base($"The Luau value kind '{valueKind}' is not supported by the managed runtime.")
    {
        ValueKind = valueKind;
    }

    /// <summary>
    /// Gets the upstream value-kind name reported by the managed conversion
    /// boundary.
    /// </summary>
    public string ValueKind { get; }
}
