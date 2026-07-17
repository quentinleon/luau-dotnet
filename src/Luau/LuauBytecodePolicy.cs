namespace Luau;

/// <summary>
/// Controls whether persistent bytecode artifacts may enter the native loader.
/// Same-process <see cref="LuauCompilerOutput"/> uses a separate capability.
/// </summary>
public enum LuauBytecodePolicy
{
    /// <summary>
    /// Rejects all persistent precompiled bytecode artifacts.
    /// </summary>
    Reject = 0,

    /// <summary>
    /// Allows a persistent bytecode artifact only when the configured
    /// <see cref="ILuauBytecodeValidator"/> accepts it.
    /// </summary>
    RequireValidator = 1,
}

/// <summary>
/// Establishes the provenance of a persistent artifact before native loading.
/// Artifact identity and hashes are claims and are not sufficient by themselves.
/// </summary>
/// <remarks>
/// This interface uses spans and a direct interface call so validation does
/// not require delegate generation or reflection under IL2CPP/AOT. Validators
/// should be deterministic and should not invoke the target Luau VM.
/// </remarks>
public interface ILuauBytecodeValidator
{
    /// <summary>
    /// Returns whether the artifact and its exact bytecode payload are trusted.
    /// </summary>
    /// <param name="artifact">The immutable persistent artifact envelope.</param>
    /// <param name="bytecode">The complete bytecode payload without copying.</param>
    bool IsValid(LuauBytecodeArtifact artifact, ReadOnlySpan<byte> bytecode);
}
