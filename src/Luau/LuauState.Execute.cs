using System.Buffers;
using System.Text;
using Luau.Internal.Interop;
using static Luau.Internal.Interop.NativeMethods;

namespace Luau;

partial class LuauState
{
    /// <summary>
    /// Executes opaque output produced by this process's compiler and returns
    /// all results under the state's normal execution limits.
    /// </summary>
    public LuauValue[] ExecuteCompilerOutput(
        LuauCompilerOutput output,
        ReadOnlySpan<char> chunkName = default,
        LuauExecutionOptions? executionOptions = null) =>
        ExecuteCompilerOutputCore(output, default, chunkName, executionOptions, hasDestination: false).Results!;

    /// <summary>Executes compiler output into a caller-owned result span.</summary>
    public int ExecuteCompilerOutputInto(
        LuauCompilerOutput output,
        Span<LuauValue> destination,
        ReadOnlySpan<char> chunkName = default,
        LuauExecutionOptions? executionOptions = null) =>
        ExecuteCompilerOutputCore(output, destination, chunkName, executionOptions, hasDestination: true).Count;

    /// <summary>Asynchronously executes compiler output and returns all results.</summary>
    public async ValueTask<LuauValue[]> ExecuteCompilerOutputAsync(
        LuauCompilerOutput output,
        ReadOnlyMemory<char> chunkName = default,
        CancellationToken cancellationToken = default,
        LuauExecutionOptions? executionOptions = null)
    {
        ThrowIfAsyncExecutionCannotStart(chunkName.Span, cancellationToken);
        ValidateCompilerOutputArgument(output, chunkName.Span);
        using var operation = BeginOperation(chunkName.Span, executionOptions, cancellationToken, isAsync: true);
        using var runner = ScriptRunner.Rent();
        LoadAcceptedBytecodeInternal(output.Bytecode, chunkName.Span);
        return await runner.RunAsync(operation, this, 0).ConfigureAwait(false);
    }

    /// <summary>Asynchronously executes compiler output into caller-owned memory.</summary>
    public async ValueTask<int> ExecuteCompilerOutputIntoAsync(
        LuauCompilerOutput output,
        Memory<LuauValue> destination,
        ReadOnlyMemory<char> chunkName = default,
        CancellationToken cancellationToken = default,
        LuauExecutionOptions? executionOptions = null)
    {
        ThrowIfAsyncExecutionCannotStart(chunkName.Span, cancellationToken);
        ValidateCompilerOutputArgument(output, chunkName.Span);
        using var operation = BeginOperation(chunkName.Span, executionOptions, cancellationToken, isAsync: true);
        using var runner = ScriptRunner.Rent();
        LoadAcceptedBytecodeInternal(output.Bytecode, chunkName.Span);
        return await runner.RunAsync(operation, this, 0, destination).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates and executes a persistent artifact, returning all results.
    /// The chunk name is diagnostic and is never part of provenance validation.
    /// </summary>
    public LuauValue[] ExecuteVerifiedBytecode(
        LuauBytecodeArtifact artifact,
        ReadOnlySpan<char> chunkName = default,
        LuauExecutionOptions? executionOptions = null) =>
        ExecuteVerifiedBytecodeCore(artifact, default, chunkName, executionOptions, hasDestination: false).Results!;

    /// <summary>Validates and executes an artifact into a caller-owned span.</summary>
    public int ExecuteVerifiedBytecodeInto(
        LuauBytecodeArtifact artifact,
        Span<LuauValue> destination,
        ReadOnlySpan<char> chunkName = default,
        LuauExecutionOptions? executionOptions = null) =>
        ExecuteVerifiedBytecodeCore(artifact, destination, chunkName, executionOptions, hasDestination: true).Count;

    /// <summary>Asynchronously validates and executes a persistent artifact.</summary>
    public async ValueTask<LuauValue[]> ExecuteVerifiedBytecodeAsync(
        LuauBytecodeArtifact artifact,
        ReadOnlyMemory<char> chunkName = default,
        CancellationToken cancellationToken = default,
        LuauExecutionOptions? executionOptions = null)
    {
        ThrowIfAsyncExecutionCannotStart(chunkName.Span, cancellationToken);
        ValidateArtifactArgument(artifact, chunkName.Span);
        using var operation = BeginOperation(chunkName.Span, executionOptions, cancellationToken, isAsync: true);
        using var runner = ScriptRunner.Rent();
        LoadAcceptedBytecodeInternal(artifact.Bytecode, chunkName.Span);
        return await runner.RunAsync(operation, this, 0).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously validates and executes an artifact into caller-owned memory.
    /// </summary>
    public async ValueTask<int> ExecuteVerifiedBytecodeIntoAsync(
        LuauBytecodeArtifact artifact,
        Memory<LuauValue> destination,
        ReadOnlyMemory<char> chunkName = default,
        CancellationToken cancellationToken = default,
        LuauExecutionOptions? executionOptions = null)
    {
        ThrowIfAsyncExecutionCannotStart(chunkName.Span, cancellationToken);
        ValidateArtifactArgument(artifact, chunkName.Span);
        using var operation = BeginOperation(chunkName.Span, executionOptions, cancellationToken, isAsync: true);
        using var runner = ScriptRunner.Rent();
        LoadAcceptedBytecodeInternal(artifact.Bytecode, chunkName.Span);
        return await runner.RunAsync(operation, this, 0, destination).ConfigureAwait(false);
    }

    (LuauValue[]? Results, int Count) ExecuteCompilerOutputCore(
        LuauCompilerOutput output,
        Span<LuauValue> destination,
        ReadOnlySpan<char> chunkName,
        LuauExecutionOptions? executionOptions,
        bool hasDestination)
    {
        ValidateCompilerOutputArgument(output, chunkName);
        using var operation = BeginOperation(chunkName, executionOptions, default, isAsync: false);
        using var runner = ScriptRunner.Rent();
        LoadAcceptedBytecodeInternal(output.Bytecode, chunkName);
        return hasDestination
            ? (null, runner.Run(operation, this, 0, destination))
            : (runner.Run(operation, this, 0), 0);
    }

    (LuauValue[]? Results, int Count) ExecuteVerifiedBytecodeCore(
        LuauBytecodeArtifact artifact,
        Span<LuauValue> destination,
        ReadOnlySpan<char> chunkName,
        LuauExecutionOptions? executionOptions,
        bool hasDestination)
    {
        ValidateArtifactArgument(artifact, chunkName);
        using var operation = BeginOperation(chunkName, executionOptions, default, isAsync: false);
        using var runner = ScriptRunner.Rent();
        LoadAcceptedBytecodeInternal(artifact.Bytecode, chunkName);
        return hasDestination
            ? (null, runner.Run(operation, this, 0, destination))
            : (runner.Run(operation, this, 0), 0);
    }

    void ValidateCompilerOutputArgument(LuauCompilerOutput output, ReadOnlySpan<char> chunkName)
    {
        if (output == null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        ThrowIfDisposed();
        ValidateCompilerOutput(output, DecodeChunkName(chunkName));
    }

    void ValidateArtifactArgument(LuauBytecodeArtifact artifact, ReadOnlySpan<char> chunkName)
    {
        if (artifact == null)
        {
            throw new ArgumentNullException(nameof(artifact));
        }

        ThrowIfDisposed();
        ValidateArtifact(artifact, DecodeChunkName(chunkName));
    }

    void ThrowIfAsyncExecutionCannotStart(
        ReadOnlySpan<char> chunkName,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (cancellationToken.IsCancellationRequested)
        {
            throw new LuauExecutionCanceledException(DecodeChunkName(chunkName), cancellationToken);
        }
    }

    public LuauValue[] DoString(
        ReadOnlySpan<char> source,
        ReadOnlySpan<char> chunkName = default,
        LuauCompileOptions? options = null,
        LuauExecutionOptions? executionOptions = null)
    {
        using var operation = BeginOperation(chunkName, executionOptions, default, isAsync: false);
        using var runner = ScriptRunner.Rent();
        CompileAndLoadString(this, source, chunkName, options);
        return runner.Run(operation, this, 0);
    }

    public int DoStringInto(
        ReadOnlySpan<char> source,
        Span<LuauValue> destination,
        ReadOnlySpan<char> chunkName = default,
        LuauCompileOptions? options = null,
        LuauExecutionOptions? executionOptions = null)
    {
        using var operation = BeginOperation(chunkName, executionOptions, default, isAsync: false);
        using var runner = ScriptRunner.Rent();
        CompileAndLoadString(this, source, chunkName, options);
        return runner.Run(operation, this, 0, destination);
    }

    public ValueTask<int> DoStringIntoAsync(
        string source,
        Memory<LuauValue> destination,
        string chunkName = "",
        LuauCompileOptions? options = null,
        CancellationToken cancellationToken = default,
        LuauExecutionOptions? executionOptions = null)
    {
        return DoStringIntoAsync(
            source.AsMemory(),
            destination,
            chunkName.AsMemory(),
            options,
            cancellationToken,
            executionOptions);
    }

    public async ValueTask<int> DoStringIntoAsync(
        ReadOnlyMemory<char> source,
        Memory<LuauValue> destination,
        ReadOnlyMemory<char> chunkName = default,
        LuauCompileOptions? options = null,
        CancellationToken cancellationToken = default,
        LuauExecutionOptions? executionOptions = null)
    {
        using var operation = BeginOperation(chunkName.Span, executionOptions, cancellationToken, isAsync: true);
        using var runner = ScriptRunner.Rent();
        CompileAndLoadString(this, source.Span, chunkName.Span, options);
        return await runner.RunAsync(operation, this, 0, destination).ConfigureAwait(false);
    }

    public ValueTask<LuauValue[]> DoStringAsync(
        string source,
        string chunkName = "",
        LuauCompileOptions? options = null,
        CancellationToken cancellationToken = default,
        LuauExecutionOptions? executionOptions = null)
    {
        return DoStringAsync(
            source.AsMemory(),
            chunkName.AsMemory(),
            options,
            cancellationToken,
            executionOptions);
    }

    public async ValueTask<LuauValue[]> DoStringAsync(
        ReadOnlyMemory<char> source,
        ReadOnlyMemory<char> chunkName = default,
        LuauCompileOptions? options = null,
        CancellationToken cancellationToken = default,
        LuauExecutionOptions? executionOptions = null)
    {
        using var operation = BeginOperation(chunkName.Span, executionOptions, cancellationToken, isAsync: true);
        using var runner = ScriptRunner.Rent();
        CompileAndLoadString(this, source.Span, chunkName.Span, options);
        return await runner.RunAsync(operation, this, 0).ConfigureAwait(false);
    }

    public LuauValue[] DoString(
        ReadOnlySpan<byte> utf8Source,
        ReadOnlySpan<byte> utf8ChunkName = default,
        LuauCompileOptions? options = null,
        LuauExecutionOptions? executionOptions = null)
    {
        using var operation = BeginOperation(utf8ChunkName, executionOptions, default, isAsync: false);
        using var runner = ScriptRunner.Rent();
        CompileAndLoadString(this, utf8Source, utf8ChunkName, options);
        return runner.Run(operation, this, 0);
    }

    public int DoStringInto(
        ReadOnlySpan<byte> utf8Source,
        Span<LuauValue> destination,
        ReadOnlySpan<byte> utf8ChunkName = default,
        LuauCompileOptions? options = null,
        LuauExecutionOptions? executionOptions = null)
    {
        using var operation = BeginOperation(utf8ChunkName, executionOptions, default, isAsync: false);
        using var runner = ScriptRunner.Rent();
        CompileAndLoadString(this, utf8Source, utf8ChunkName, options);
        return runner.Run(operation, this, 0, destination);
    }

    public async ValueTask<int> DoStringIntoAsync(
        ReadOnlyMemory<byte> utf8Source,
        Memory<LuauValue> destination,
        ReadOnlyMemory<byte> utf8ChunkName = default,
        LuauCompileOptions? options = null,
        CancellationToken cancellationToken = default,
        LuauExecutionOptions? executionOptions = null)
    {
        using var operation = BeginOperation(utf8ChunkName.Span, executionOptions, cancellationToken, isAsync: true);
        using var runner = ScriptRunner.Rent();
        CompileAndLoadString(this, utf8Source.Span, utf8ChunkName.Span, options);
        return await runner.RunAsync(operation, this, 0, destination).ConfigureAwait(false);
    }

    public async ValueTask<LuauValue[]> DoStringAsync(
        ReadOnlyMemory<byte> utf8Source,
        ReadOnlyMemory<byte> utf8ChunkName = default,
        LuauCompileOptions? options = null,
        CancellationToken cancellationToken = default,
        LuauExecutionOptions? executionOptions = null)
    {
        using var operation = BeginOperation(utf8ChunkName.Span, executionOptions, cancellationToken, isAsync: true);
        using var runner = ScriptRunner.Rent();
        CompileAndLoadString(this, utf8Source.Span, utf8ChunkName.Span, options);
        return await runner.RunAsync(operation, this, 0).ConfigureAwait(false);
    }

    internal unsafe LuauValue[] DoStringForRequire(
        ReadOnlySpan<byte> utf8Source,
        ReadOnlySpan<byte> utf8ChunkName,
        LuauCompileOptions? options)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        var baseTop = luau_host_stack_get_top(l);
        CompileAndLoadString(this, utf8Source, utf8ChunkName, options);
        return ExecuteLoadedForRequire(baseTop, DecodeChunkName(utf8ChunkName));
    }

    internal unsafe LuauValue[] DoCompilerOutputForRequire(
        LuauCompilerOutput output,
        ReadOnlySpan<byte> utf8ChunkName)
    {
        if (output == null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        ThrowIfDisposed();
        ValidateCompilerOutput(output, DecodeChunkName(utf8ChunkName));
        using var access = EnterNativeAccess();
        var baseTop = luau_host_stack_get_top(l);
        LoadAcceptedBytecodeInternal(output.Bytecode, utf8ChunkName);
        return ExecuteLoadedForRequire(baseTop, DecodeChunkName(utf8ChunkName));
    }

    internal unsafe LuauValue[] DoVerifiedBytecodeForRequire(
        LuauBytecodeArtifact artifact,
        ReadOnlySpan<byte> utf8ChunkName)
    {
        if (artifact == null)
        {
            throw new ArgumentNullException(nameof(artifact));
        }

        ThrowIfDisposed();
        ValidateArtifact(artifact, DecodeChunkName(utf8ChunkName));
        using var access = EnterNativeAccess();
        var baseTop = luau_host_stack_get_top(l);
        LoadAcceptedBytecodeInternal(artifact.Bytecode, utf8ChunkName);
        return ExecuteLoadedForRequire(baseTop, DecodeChunkName(utf8ChunkName));
    }

    unsafe LuauValue[] ExecuteLoadedForRequire(int baseTop, string? chunkName)
    {
        using var nestedOperation = BeginNestedOperationIfNeeded(chunkName);
        using var stack = new LuauStackBoundary(this, baseTop);

        LuauNativeProtection.Prepare(context);
        var status = luau_host_pcall(l, 0, -1, 0);
        LuauNativeProtection.ThrowIfFailed(
            this,
            l,
            status,
            "execute a module",
            chunkName);

        var resultCount = luau_host_stack_get_top(l) - baseTop;
        var resultLimit = nestedOperation.Operation.Options.MaxResultCount;
        if (resultLimit is { } limit && resultCount > limit)
        {
            throw new LuauResultLimitException(chunkName, resultCount, limit);
        }

        var results = new LuauValue[resultCount];
        for (var i = resultCount - 1; i >= 0; i--)
        {
            results[i] = Pop();
        }

        stack.Complete();
        return results;
    }

    static void CompileAndLoadString(
        LuauState state,
        ReadOnlySpan<byte> utf8Source,
        ReadOnlySpan<byte> utf8ChunkName,
        LuauCompileOptions? options)
    {
        var chunkName = DecodeChunkName(utf8ChunkName);
        state.ValidateSourceSize(utf8Source.Length, chunkName);
        ThrowIfCompilationStopped(state);
        using var writer = new ArrayPoolBufferWriter(512);
        LuauCompiler.Compile(
            writer,
            utf8Source,
            options,
            LuauNativeProtection.AbiVerifier,
            state.Options.MaxBytecodeBytes,
            chunkName);
        ThrowIfCompilationStopped(state);
        state.LoadAcceptedBytecodeInternal(
            writer.WrittenSpan,
            utf8ChunkName,
            chunkName,
            allowOversizedDiagnostic: true);
    }

    static void CompileAndLoadString(
        LuauState state,
        ReadOnlySpan<char> source,
        ReadOnlySpan<char> chunkName,
        LuauCompileOptions? options)
    {
        var sourceByteCount = Encoding.UTF8.GetByteCount(source);
        state.ValidateSourceSize(sourceByteCount, DecodeChunkName(chunkName));

        var sourceBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, sourceByteCount));
        var chunkByteCount = Encoding.UTF8.GetByteCount(chunkName);
        var chunkBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, chunkByteCount));
        try
        {
            var encodedSourceCount = Encoding.UTF8.GetBytes(source, sourceBuffer);
            var encodedChunkCount = Encoding.UTF8.GetBytes(chunkName, chunkBuffer);
            CompileAndLoadString(
                state,
                sourceBuffer.AsSpan(0, encodedSourceCount),
                chunkBuffer.AsSpan(0, encodedChunkCount),
                options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(sourceBuffer);
            ArrayPool<byte>.Shared.Return(chunkBuffer);
        }
    }

    static void ThrowIfCompilationStopped(LuauState state)
    {
        var operation = state.Context.GetActiveOperation();
        if (operation?.GetHardStopException() is { } exception)
        {
            throw exception;
        }
    }

    /// <summary>
    /// Resumes this child coroutine with caller-owned arguments and returns a
    /// newly allocated result array. Argument arrays are never result destinations.
    /// </summary>
    public LuauValue[] Resume(
        ReadOnlySpan<LuauValue> arguments = default,
        LuauExecutionOptions? executionOptions = null)
    {
        EnsureCoroutine();
        var hasFunction = HasInitialCoroutineFunction();
        using var operation = BeginOperation((string?)null, executionOptions, default, isAsync: false);
        using var runner = ScriptRunner.Rent();
        PushArguments(arguments);
        return runner.Run(operation, this, arguments.Length, hasFunction);
    }

    /// <summary>Resumes this child coroutine into a caller-owned result span.</summary>
    public int ResumeInto(
        Span<LuauValue> destination,
        LuauExecutionOptions? executionOptions = null)
    {
        return ResumeInto(default, destination, executionOptions);
    }

    /// <summary>
    /// Resumes this child coroutine with arguments and writes results into a
    /// distinct caller-owned span.
    /// </summary>
    public int ResumeInto(
        ReadOnlySpan<LuauValue> arguments,
        Span<LuauValue> destination,
        LuauExecutionOptions? executionOptions = null)
    {
        EnsureCoroutine();
        var hasFunction = HasInitialCoroutineFunction();
        using var operation = BeginOperation((string?)null, executionOptions, default, isAsync: false);
        using var runner = ScriptRunner.Rent();
        PushArguments(arguments);
        return runner.Run(operation, this, arguments.Length, destination, hasFunction);
    }

    /// <summary>
    /// Asynchronously resumes this child coroutine and returns a newly
    /// allocated result array.
    /// </summary>
    public async ValueTask<LuauValue[]> ResumeAsync(
        ReadOnlyMemory<LuauValue> arguments = default,
        CancellationToken cancellationToken = default,
        LuauExecutionOptions? executionOptions = null)
    {
        EnsureCoroutine();
        var hasFunction = HasInitialCoroutineFunction();
        using var operation = BeginOperation((string?)null, executionOptions, cancellationToken, isAsync: true);
        using var runner = ScriptRunner.Rent();
        for (var i = 0; i < arguments.Length; i++)
        {
            Push(arguments.Span[i]);
        }

        return await runner.RunAsync(operation, this, arguments.Length, hasFunction).ConfigureAwait(false);
    }

    /// <summary>Asynchronously resumes into caller-owned result memory.</summary>
    public ValueTask<int> ResumeIntoAsync(
        Memory<LuauValue> destination,
        CancellationToken cancellationToken = default,
        LuauExecutionOptions? executionOptions = null)
    {
        return ResumeIntoAsync(
            ReadOnlyMemory<LuauValue>.Empty,
            destination,
            cancellationToken,
            executionOptions);
    }

    /// <summary>
    /// Asynchronously resumes with arguments and writes results into distinct
    /// caller-owned memory.
    /// </summary>
    public async ValueTask<int> ResumeIntoAsync(
        ReadOnlyMemory<LuauValue> arguments,
        Memory<LuauValue> destination,
        CancellationToken cancellationToken = default,
        LuauExecutionOptions? executionOptions = null)
    {
        EnsureCoroutine();
        var hasFunction = HasInitialCoroutineFunction();
        using var operation = BeginOperation((string?)null, executionOptions, cancellationToken, isAsync: true);
        using var runner = ScriptRunner.Rent();
        for (var i = 0; i < arguments.Length; i++)
        {
            Push(arguments.Span[i]);
        }

        return await runner.RunAsync(
            operation,
            this,
            arguments.Length,
            destination,
            hasFunction).ConfigureAwait(false);
    }

    ScriptOperation BeginOperation(
        ReadOnlySpan<char> chunkName,
        LuauExecutionOptions? options,
        CancellationToken cancellationToken,
        bool isAsync)
    {
        return BeginOperation(DecodeChunkName(chunkName), options, cancellationToken, isAsync);
    }

    ScriptOperation BeginOperation(
        ReadOnlySpan<byte> utf8ChunkName,
        LuauExecutionOptions? options,
        CancellationToken cancellationToken,
        bool isAsync)
    {
        return BeginOperation(DecodeChunkName(utf8ChunkName), options, cancellationToken, isAsync);
    }

    internal ScriptOperation BeginOperation(
        string? chunkName,
        LuauExecutionOptions? options,
        CancellationToken cancellationToken,
        bool isAsync,
        ScriptOperationMode mode = ScriptOperationMode.TopLevelResume)
    {
        ThrowIfDisposed();
        if (cancellationToken.IsCancellationRequested)
        {
            throw new LuauExecutionCanceledException(chunkName, cancellationToken);
        }

        return context.BeginOperation(this, chunkName, options, cancellationToken, isAsync, mode);
    }

    internal static string? DecodeChunkName(ReadOnlySpan<char> chunkName)
    {
        return chunkName.IsEmpty ? null : new string(chunkName);
    }

    internal static string? DecodeChunkName(ReadOnlySpan<byte> utf8ChunkName)
    {
        return utf8ChunkName.IsEmpty ? null : Encoding.UTF8.GetString(utf8ChunkName);
    }

    void EnsureCoroutine()
    {
        ThrowIfDisposed();
        if (IsMainThread)
        {
            ThrowHelper.ThrowInvalidOperationException(
                "Resume requires a child Luau coroutine; the root state cannot be resumed.");
        }
    }

    void PushArguments(ReadOnlySpan<LuauValue> arguments)
    {
        for (var i = 0; i < arguments.Length; i++)
        {
            Push(arguments[i]);
        }
    }

    unsafe bool HasInitialCoroutineFunction()
    {
        using var access = EnterNativeAccess();
        return luau_host_thread_status(l) == LuauHostStatus.Ok && luau_host_stack_get_top(l) > 0;
    }
}
