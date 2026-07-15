namespace Luau;

/// <summary>
/// The base exception for failures reported by the managed Luau runtime.
/// </summary>
public class LuauException : Exception
{
    /// <summary>
    /// Initializes an exception without chunk context.
    /// </summary>
    /// <param name="message">The error message.</param>
    public LuauException(string? message) : this(message, null, null)
    {
    }

    /// <summary>
    /// Initializes an exception associated with a source or bytecode chunk.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="chunkName">The host-provided chunk name, when available.</param>
    public LuauException(string? message, string? chunkName) : this(message, chunkName, null)
    {
    }

    /// <summary>
    /// Initializes an exception associated with a chunk and an underlying failure.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="chunkName">The host-provided chunk name, when available.</param>
    /// <param name="innerException">The exception that caused this failure.</param>
    public LuauException(string? message, string? chunkName, Exception? innerException)
        : base(message, innerException)
    {
        ChunkName = chunkName;
    }

    /// <summary>
    /// Gets the exact host-provided chunk name associated with the failure, or
    /// <see langword="null"/> when no chunk name was supplied.
    /// </summary>
    public string? ChunkName { get; }
}
