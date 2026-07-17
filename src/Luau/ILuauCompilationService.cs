namespace Luau;

/// <summary>
/// Compiles owned snapshots of UTF-8 Luau source without requiring the caller
/// to run the compiler on its current thread.
/// </summary>
public interface ILuauCompilationService : IAsyncDisposable
{
    /// <summary>
    /// Compiles a snapshot of <paramref name="utf8Source"/> using the supplied
    /// options. Source diagnostics, cancellation, and infrastructure failures
    /// are represented by the returned result.
    /// </summary>
    /// <exception cref="LuauCompilationLimitException">
    /// The request cannot be admitted within a configured source or queue
    /// limit.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// The service has stopped accepting work.
    /// </exception>
    ValueTask<LuauCompileResult> CompileAsync(
        ReadOnlyMemory<byte> utf8Source,
        LuauCompileOptions? options = null,
        CancellationToken cancellationToken = default);
}
