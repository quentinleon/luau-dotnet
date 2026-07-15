namespace Luau;

/// <summary>
/// Controls whether host-supplied precompiled bytecode may enter the native
/// loader. This policy does not apply to bytecode produced internally while
/// compiling source through a trusted compiler path.
/// </summary>
public enum LuauBytecodePolicy
{
    /// <summary>
    /// Allows precompiled bytecode without validation. This preserves the
    /// behavior of existing callers, but is not safe for untrusted bytecode.
    /// </summary>
    AllowUnvalidated = 0,

    /// <summary>
    /// Rejects all host-supplied precompiled bytecode.
    /// </summary>
    Reject = 1,

    /// <summary>
    /// Allows host-supplied precompiled bytecode only when the configured
    /// <see cref="ILuauBytecodeValidator"/> accepts it.
    /// </summary>
    RequireValidator = 2,
}

/// <summary>
/// Validates host-supplied bytecode before any native loader call occurs.
/// Implementations can enforce signatures, hashes, or another host-defined
/// trust policy. A size bound is not a structural bytecode validator.
/// </summary>
/// <remarks>
/// This interface uses spans and a direct interface call so validation does
/// not require delegate generation or reflection under IL2CPP/AOT. Validators
/// should be deterministic and should not invoke the target Luau VM.
/// </remarks>
public interface ILuauBytecodeValidator
{
    /// <summary>
    /// Returns whether the bytecode is trusted for the named chunk.
    /// </summary>
    /// <param name="bytecode">The complete bytecode payload.</param>
    /// <param name="utf8ChunkName">
    /// The host-provided chunk name encoded as UTF-8, if any.
    /// </param>
    bool IsValid(ReadOnlySpan<byte> bytecode, ReadOnlySpan<byte> utf8ChunkName);
}
