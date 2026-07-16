using System.Buffers;
using System.Text;
using Luau.Internal.Interop;
using static Luau.Internal.Interop.NativeMethods;

namespace Luau;

partial class LuauState
{
    public int Execute(
        ReadOnlySpan<byte> bytecode,
        Span<LuauValue> destination,
        ReadOnlySpan<char> chunkName,
        LuauExecutionOptions? executionOptions = null)
    {
        using var operation = BeginOperation(chunkName, executionOptions, default, isAsync: false);
        using var runner = ScriptRunner.Rent();
        LoadInternal(bytecode, chunkName);
        return runner.Run(operation, this, 0, destination);
    }

    public int Execute(
        ReadOnlySpan<byte> bytecode,
        Span<LuauValue> destination,
        ReadOnlySpan<byte> utf8ChunkName = default,
        LuauExecutionOptions? executionOptions = null)
    {
        using var operation = BeginOperation(utf8ChunkName, executionOptions, default, isAsync: false);
        using var runner = ScriptRunner.Rent();
        LoadInternal(bytecode, utf8ChunkName);
        return runner.Run(operation, this, 0, destination);
    }

    public LuauValue[] Execute(
        ReadOnlySpan<byte> bytecode,
        ReadOnlySpan<char> chunkName,
        LuauExecutionOptions? executionOptions = null)
    {
        using var operation = BeginOperation(chunkName, executionOptions, default, isAsync: false);
        using var runner = ScriptRunner.Rent();
        LoadInternal(bytecode, chunkName);
        return runner.Run(operation, this, 0);
    }

    public LuauValue[] Execute(
        ReadOnlySpan<byte> bytecode,
        ReadOnlySpan<byte> utf8ChunkName = default,
        LuauExecutionOptions? executionOptions = null)
    {
        using var operation = BeginOperation(utf8ChunkName, executionOptions, default, isAsync: false);
        using var runner = ScriptRunner.Rent();
        LoadInternal(bytecode, utf8ChunkName);
        return runner.Run(operation, this, 0);
    }

    /// <summary>
    /// Executes bytecode whose provenance has already been established by the
    /// host, bypassing <see cref="LuauStateOptions.BytecodePolicy"/> while still
    /// enforcing the configured bytecode-size and execution limits.
    /// A size limit is not provenance validation. Never use this API for bytes
    /// supplied directly by an untrusted mod.
    /// </summary>
    public LuauValue[] ExecuteTrustedBytecode(
        ReadOnlySpan<byte> bytecode,
        ReadOnlySpan<char> chunkName,
        LuauExecutionOptions? executionOptions = null)
    {
        using var operation = BeginOperation(chunkName, executionOptions, default, isAsync: false);
        using var runner = ScriptRunner.Rent();
        LoadInternal(bytecode, chunkName, trustedCompilerOutput: true);
        return runner.Run(operation, this, 0);
    }

    /// <inheritdoc cref="ExecuteTrustedBytecode(ReadOnlySpan{byte}, ReadOnlySpan{char}, LuauExecutionOptions?)"/>
    public int ExecuteTrustedBytecode(
        ReadOnlySpan<byte> bytecode,
        Span<LuauValue> destination,
        ReadOnlySpan<char> chunkName,
        LuauExecutionOptions? executionOptions = null)
    {
        using var operation = BeginOperation(chunkName, executionOptions, default, isAsync: false);
        using var runner = ScriptRunner.Rent();
        LoadInternal(bytecode, chunkName, trustedCompilerOutput: true);
        return runner.Run(operation, this, 0, destination);
    }

    /// <inheritdoc cref="ExecuteTrustedBytecode(ReadOnlySpan{byte}, ReadOnlySpan{char}, LuauExecutionOptions?)"/>
    public async ValueTask<LuauValue[]> ExecuteTrustedBytecodeAsync(
        ReadOnlyMemory<byte> bytecode,
        ReadOnlyMemory<char> chunkName,
        CancellationToken cancellationToken = default,
        LuauExecutionOptions? executionOptions = null)
    {
        using var operation = BeginOperation(chunkName.Span, executionOptions, cancellationToken, isAsync: true);
        using var runner = ScriptRunner.Rent();
        LoadInternal(bytecode.Span, chunkName.Span, trustedCompilerOutput: true);
        return await runner.RunAsync(operation, this, 0).ConfigureAwait(false);
    }

    /// <inheritdoc cref="ExecuteTrustedBytecode(ReadOnlySpan{byte}, ReadOnlySpan{char}, LuauExecutionOptions?)"/>
    public async ValueTask<int> ExecuteTrustedBytecodeAsync(
        ReadOnlyMemory<byte> bytecode,
        Memory<LuauValue> destination,
        ReadOnlyMemory<char> chunkName,
        CancellationToken cancellationToken = default,
        LuauExecutionOptions? executionOptions = null)
    {
        using var operation = BeginOperation(chunkName.Span, executionOptions, cancellationToken, isAsync: true);
        using var runner = ScriptRunner.Rent();
        LoadInternal(bytecode.Span, chunkName.Span, trustedCompilerOutput: true);
        return await runner.RunAsync(operation, this, 0, destination).ConfigureAwait(false);
    }

    public ValueTask<int> ExecuteAsync(
        ReadOnlyMemory<byte> bytecode,
        Memory<LuauValue> destination,
        CancellationToken cancellationToken = default,
        LuauExecutionOptions? executionOptions = null)
    {
        return ExecuteAsync(
            bytecode,
            destination,
            ReadOnlyMemory<byte>.Empty,
            cancellationToken,
            executionOptions);
    }

    public async ValueTask<int> ExecuteAsync(
        ReadOnlyMemory<byte> bytecode,
        Memory<LuauValue> destination,
        ReadOnlyMemory<char> chunkName,
        CancellationToken cancellationToken = default,
        LuauExecutionOptions? executionOptions = null)
    {
        using var operation = BeginOperation(chunkName.Span, executionOptions, cancellationToken, isAsync: true);
        using var runner = ScriptRunner.Rent();
        LoadInternal(bytecode.Span, chunkName.Span);
        return await runner.RunAsync(operation, this, 0, destination).ConfigureAwait(false);
    }

    public async ValueTask<int> ExecuteAsync(
        ReadOnlyMemory<byte> bytecode,
        Memory<LuauValue> destination,
        ReadOnlyMemory<byte> utf8ChunkName,
        CancellationToken cancellationToken = default,
        LuauExecutionOptions? executionOptions = null)
    {
        using var operation = BeginOperation(utf8ChunkName.Span, executionOptions, cancellationToken, isAsync: true);
        using var runner = ScriptRunner.Rent();
        LoadInternal(bytecode.Span, utf8ChunkName.Span);
        return await runner.RunAsync(operation, this, 0, destination).ConfigureAwait(false);
    }

    public ValueTask<LuauValue[]> ExecuteAsync(
        ReadOnlyMemory<byte> bytecode,
        CancellationToken cancellationToken = default,
        LuauExecutionOptions? executionOptions = null)
    {
        return ExecuteAsync(
            bytecode,
            ReadOnlyMemory<byte>.Empty,
            cancellationToken,
            executionOptions);
    }

    public async ValueTask<LuauValue[]> ExecuteAsync(
        ReadOnlyMemory<byte> bytecode,
        ReadOnlyMemory<char> chunkName,
        CancellationToken cancellationToken = default,
        LuauExecutionOptions? executionOptions = null)
    {
        using var operation = BeginOperation(chunkName.Span, executionOptions, cancellationToken, isAsync: true);
        using var runner = ScriptRunner.Rent();
        LoadInternal(bytecode.Span, chunkName.Span);
        return await runner.RunAsync(operation, this, 0).ConfigureAwait(false);
    }

    public async ValueTask<LuauValue[]> ExecuteAsync(
        ReadOnlyMemory<byte> bytecode,
        ReadOnlyMemory<byte> utf8ChunkName,
        CancellationToken cancellationToken = default,
        LuauExecutionOptions? executionOptions = null)
    {
        using var operation = BeginOperation(utf8ChunkName.Span, executionOptions, cancellationToken, isAsync: true);
        using var runner = ScriptRunner.Rent();
        LoadInternal(bytecode.Span, utf8ChunkName.Span);
        return await runner.RunAsync(operation, this, 0).ConfigureAwait(false);
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

    public int DoString(
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

    public ValueTask<int> DoStringAsync(
        string source,
        Memory<LuauValue> destination,
        string chunkName = "",
        LuauCompileOptions? options = null,
        CancellationToken cancellationToken = default,
        LuauExecutionOptions? executionOptions = null)
    {
        return DoStringAsync(
            source.AsMemory(),
            destination,
            chunkName.AsMemory(),
            options,
            cancellationToken,
            executionOptions);
    }

    public async ValueTask<int> DoStringAsync(
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

    public int DoString(
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

    public async ValueTask<int> DoStringAsync(
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

    internal unsafe LuauValue[] DoBytecodeForRequire(
        ReadOnlySpan<byte> bytecode,
        ReadOnlySpan<byte> utf8ChunkName,
        bool trustedCompilerOutput)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        var baseTop = luau_host_stack_get_top(l);
        LoadInternal(bytecode, utf8ChunkName, trustedCompilerOutput);
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
        state.ValidateSourceSize(utf8Source.Length, DecodeChunkName(utf8ChunkName));
        using var writer = new ArrayPoolBufferWriter(512);
        LuauCompiler.Compile(writer, utf8Source, options);
        state.LoadInternal(writer.WrittenSpan, utf8ChunkName, trustedCompilerOutput: true);
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

    public LuauValue[] Resume(LuauExecutionOptions? executionOptions = null)
    {
        return Resume([], executionOptions);
    }

    public LuauValue[] Resume(
        ReadOnlySpan<LuauValue> arguments,
        LuauExecutionOptions? executionOptions = null)
    {
        EnsureCoroutine();
        var hasFunction = HasInitialCoroutineFunction();
        using var operation = BeginOperation((string?)null, executionOptions, default, isAsync: false);
        using var runner = ScriptRunner.Rent();
        PushArguments(arguments);
        return runner.Run(operation, this, arguments.Length, hasFunction);
    }

    public int Resume(
        Span<LuauValue> destination,
        LuauExecutionOptions? executionOptions = null)
    {
        return Resume([], destination, executionOptions);
    }

    public int Resume(
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

    public ValueTask<LuauValue[]> ResumeAsync(
        CancellationToken cancellationToken = default,
        LuauExecutionOptions? executionOptions = null)
    {
        return ResumeAsync(ReadOnlyMemory<LuauValue>.Empty, cancellationToken, executionOptions);
    }

    public async ValueTask<LuauValue[]> ResumeAsync(
        ReadOnlyMemory<LuauValue> arguments,
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

    public ValueTask<int> ResumeAsync(
        Memory<LuauValue> destination,
        CancellationToken cancellationToken = default,
        LuauExecutionOptions? executionOptions = null)
    {
        return ResumeAsync(
            ReadOnlyMemory<LuauValue>.Empty,
            destination,
            cancellationToken,
            executionOptions);
    }

    public async ValueTask<int> ResumeAsync(
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
            ThrowHelper.ThrowInvalidOperationException("attempt to yield from outside a coroutine");
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
