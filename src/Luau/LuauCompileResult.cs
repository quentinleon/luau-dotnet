namespace Luau;

/// <summary>Identifies the outcome of a compilation-service request.</summary>
public enum LuauCompileResultKind
{
    /// <summary>The compiler produced loadable same-process output.</summary>
    Success = 0,

    /// <summary>The compiler rejected the source with a diagnostic.</summary>
    Diagnostic = 1,

    /// <summary>The request was canceled before its output was published.</summary>
    Canceled = 2,

    /// <summary>The compiler backend or its resource policy failed.</summary>
    InfrastructureFailure = 3,
}

/// <summary>
/// A non-throwing compilation outcome. Exactly one outcome-specific property
/// is populated according to <see cref="Kind"/>.
/// </summary>
public sealed class LuauCompileResult
{
    LuauCompileResult(
        LuauCompileResultKind kind,
        LuauCompilerOutput? output,
        LuauCompilationException? diagnostic,
        Exception? infrastructureException)
    {
        Kind = kind;
        Output = output;
        Diagnostic = diagnostic;
        InfrastructureException = infrastructureException;
    }

    /// <summary>Gets the request outcome.</summary>
    public LuauCompileResultKind Kind { get; }

    /// <summary>
    /// Gets compiler-issued, same-process output for a successful request.
    /// Copied bytecode cannot be promoted back into this capability.
    /// </summary>
    public LuauCompilerOutput? Output { get; }

    /// <summary>Gets the typed source diagnostic for a rejected program.</summary>
    public LuauCompilationException? Diagnostic { get; }

    /// <summary>Gets the backend failure for an infrastructure outcome.</summary>
    public Exception? InfrastructureException { get; }

    /// <summary>Gets whether this result contains compiler-issued output.</summary>
    public bool IsSuccess => Kind == LuauCompileResultKind.Success;

    internal static LuauCompileResult FromOutput(LuauCompilerOutput output)
    {
        return new LuauCompileResult(
            LuauCompileResultKind.Success,
            output ?? throw new ArgumentNullException(nameof(output)),
            null,
            null);
    }

    internal static LuauCompileResult FromDiagnostic(LuauCompilationException diagnostic)
    {
        return new LuauCompileResult(
            LuauCompileResultKind.Diagnostic,
            null,
            diagnostic ?? throw new ArgumentNullException(nameof(diagnostic)),
            null);
    }

    internal static LuauCompileResult Canceled()
    {
        return new LuauCompileResult(LuauCompileResultKind.Canceled, null, null, null);
    }

    internal static LuauCompileResult FromInfrastructureFailure(Exception exception)
    {
        return new LuauCompileResult(
            LuauCompileResultKind.InfrastructureFailure,
            null,
            null,
            exception ?? throw new ArgumentNullException(nameof(exception)));
    }
}
