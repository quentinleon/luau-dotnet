using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace Luau;

partial class LuauState
{
    public int Execute(ReadOnlySpan<byte> bytecode, Span<LuauValue> destination, ReadOnlySpan<char> chunkName = default)
    {
        using var runner = ScriptRunner.Rent();
        LoadInternal(bytecode, chunkName);
        return runner.Run(this, 0, destination);
    }

    [OverloadResolutionPriority(1)]
    public int Execute(ReadOnlySpan<byte> bytecode, Span<LuauValue> destination, ReadOnlySpan<byte> utf8ChunkName = default)
    {
        using var runner = ScriptRunner.Rent();
        LoadInternal(bytecode, utf8ChunkName);
        return runner.Run(this, 0, destination);
    }

    public LuauValue[] Execute(ReadOnlySpan<byte> bytecode, ReadOnlySpan<char> chunkName = default)
    {
        using var runner = ScriptRunner.Rent();
        LoadInternal(bytecode, chunkName);
        return runner.Run(this, 0);
    }

    [OverloadResolutionPriority(1)]
    public LuauValue[] Execute(ReadOnlySpan<byte> bytecode, ReadOnlySpan<byte> utf8ChunkName = default)
    {
        using var runner = ScriptRunner.Rent();
        LoadInternal(bytecode, utf8ChunkName);
        return runner.Run(this, 0);
    }

    public ValueTask<int> ExecuteAsync(ReadOnlyMemory<byte> bytecode, Memory<LuauValue> destination, CancellationToken cancellationToken = default)
    {
        using var runner = ScriptRunner.Rent();
        LoadInternal(bytecode.Span, default);
        return runner.RunAsync(this, 0, destination, cancellationToken);
    }

    public ValueTask<int> ExecuteAsync(ReadOnlyMemory<byte> bytecode, Memory<LuauValue> destination, ReadOnlyMemory<char> chunkName = default, CancellationToken cancellationToken = default)
    {
        using var runner = ScriptRunner.Rent();
        LoadInternal(bytecode.Span, chunkName.Span);
        return runner.RunAsync(this, 0, destination, cancellationToken);
    }

    [OverloadResolutionPriority(1)]
    public ValueTask<int> ExecuteAsync(ReadOnlyMemory<byte> bytecode, Memory<LuauValue> destination, ReadOnlyMemory<byte> utf8ChunkName = default, CancellationToken cancellationToken = default)
    {
        using var runner = ScriptRunner.Rent();
        LoadInternal(bytecode.Span, utf8ChunkName.Span);
        return runner.RunAsync(this, 0, destination, cancellationToken);
    }

    public ValueTask<LuauValue[]> ExecuteAsync(ReadOnlyMemory<byte> bytecode, CancellationToken cancellationToken = default)
    {
        using var runner = ScriptRunner.Rent();
        LoadInternal(bytecode.Span);
        return runner.RunAsync(this, 0, cancellationToken);
    }

    public ValueTask<LuauValue[]> ExecuteAsync(ReadOnlyMemory<byte> bytecode, ReadOnlyMemory<char> chunkName = default, CancellationToken cancellationToken = default)
    {
        using var runner = ScriptRunner.Rent();
        LoadInternal(bytecode.Span, chunkName.Span);
        return runner.RunAsync(this, 0, cancellationToken);
    }

    [OverloadResolutionPriority(1)]
    public ValueTask<LuauValue[]> ExecuteAsync(ReadOnlyMemory<byte> bytecode, ReadOnlyMemory<byte> utf8ChunkName = default, CancellationToken cancellationToken = default)
    {
        using var runner = ScriptRunner.Rent();
        LoadInternal(bytecode.Span, utf8ChunkName.Span);
        return runner.RunAsync(this, 0, cancellationToken);
    }

    public LuauValue[] DoString(ReadOnlySpan<char> source, ReadOnlySpan<char> chunkName = default, LuauCompileOptions? options = null)
    {
        using var runner = ScriptRunner.Rent();
        CompileAndLoadString(this, source, chunkName, options);
        return runner.Run(this, 0);
    }

    public int DoString(ReadOnlySpan<char> source, Span<LuauValue> destination, ReadOnlySpan<char> chunkName = default, LuauCompileOptions? options = null)
    {
        using var runner = ScriptRunner.Rent();
        CompileAndLoadString(this, source, chunkName, options);
        return runner.Run(this, 0, destination);
    }

    public ValueTask<int> DoStringAsync(string source, Memory<LuauValue> destination, string chunkName = "", LuauCompileOptions? options = null, CancellationToken cancellationToken = default)
    {
        return DoStringAsync(source.AsMemory(), destination, chunkName.AsMemory(), options, cancellationToken);
    }

    public ValueTask<int> DoStringAsync(ReadOnlyMemory<char> source, Memory<LuauValue> destination, ReadOnlyMemory<char> chunkName = default, LuauCompileOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var runner = ScriptRunner.Rent();
        CompileAndLoadString(this, source.Span, chunkName.Span, options);
        return runner.RunAsync(this, 0, destination, cancellationToken);
    }

    public ValueTask<LuauValue[]> DoStringAsync(string source, string chunkName = "", LuauCompileOptions? options = null, CancellationToken cancellationToken = default)
    {
        return DoStringAsync(source.AsMemory(), chunkName.AsMemory(), options, cancellationToken);
    }

    public ValueTask<LuauValue[]> DoStringAsync(ReadOnlyMemory<char> source, ReadOnlyMemory<char> chunkName = default, LuauCompileOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var runner = ScriptRunner.Rent();
        CompileAndLoadString(this, source.Span, chunkName.Span, options);
        return runner.RunAsync(this, 0, cancellationToken);
    }

    public LuauValue[] DoString(ReadOnlySpan<byte> utf8Source, ReadOnlySpan<byte> utf8ChunkName = default, LuauCompileOptions? options = null)
    {
        using var runner = ScriptRunner.Rent();
        CompileAndLoadString(this, utf8Source, utf8ChunkName, options);
        return runner.Run(this, 0);
    }

    public int DoString(ReadOnlySpan<byte> utf8Source, Span<LuauValue> destination, ReadOnlySpan<byte> utf8ChunkName = default, LuauCompileOptions? options = null)
    {
        using var runner = ScriptRunner.Rent();
        CompileAndLoadString(this, utf8Source, utf8ChunkName, options);
        return runner.Run(this, 0, destination);
    }

    public ValueTask<int> DoStringAsync(ReadOnlyMemory<byte> utf8Source, Memory<LuauValue> destination, ReadOnlyMemory<byte> utf8ChunkName = default, LuauCompileOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var runner = ScriptRunner.Rent();
        CompileAndLoadString(this, utf8Source.Span, utf8ChunkName.Span, options);
        return runner.RunAsync(this, 0, destination, cancellationToken);
    }

    public ValueTask<LuauValue[]> DoStringAsync(ReadOnlyMemory<byte> utf8Source, ReadOnlyMemory<byte> utf8ChunkName = default, LuauCompileOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var runner = ScriptRunner.Rent();
        CompileAndLoadString(this, utf8Source.Span, utf8ChunkName.Span, options);
        return runner.RunAsync(this, 0, cancellationToken);
    }

    static void CompileAndLoadString(LuauState state, ReadOnlySpan<byte> utf8Source, ReadOnlySpan<byte> utf8ChunkName, LuauCompileOptions? options)
    {
        using var writer = new ArrayPoolBufferWriter(512);
        LuauCompiler.Compile(writer, utf8Source, options);
        state.LoadInternal(writer.WrittenSpan, utf8ChunkName);
    }

    static void CompileAndLoadString(LuauState state, ReadOnlySpan<char> source, ReadOnlySpan<char> chunkName, LuauCompileOptions? options)
    {
        var buffer1 = ArrayPool<byte>.Shared.Rent(source.Length * 3);
        var buffer2 = ArrayPool<byte>.Shared.Rent(chunkName.Length * 3);
        try
        {
            var utf8Count1 = Encoding.UTF8.GetBytes(source, buffer1);
            var utf8Count2 = Encoding.UTF8.GetBytes(chunkName, buffer2);
            CompileAndLoadString(state, buffer1.AsSpan(0, utf8Count1), buffer2.AsSpan(0, utf8Count2), options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer1);
            ArrayPool<byte>.Shared.Return(buffer2);
        }
    }

    public LuauValue[] Resume() => Resume([]);
    public LuauValue[] Resume(ReadOnlySpan<LuauValue> arguments)
    {
        if (IsMainThread)
        {
            ThrowHelper.ThrowInvalidOperationException("attempt to yield from outside a coroutine");
        }

        for (int i = 0; i < arguments.Length; i++)
        {
            Push(arguments[i]);
        }

        using var runner = ScriptRunner.Rent();
        return runner.Run(this, arguments.Length);
    }

    public int Resume(Span<LuauValue> destination) => Resume([], destination);
    public int Resume(ReadOnlySpan<LuauValue> arguments, Span<LuauValue> destination)
    {
        if (IsMainThread)
        {
            ThrowHelper.ThrowInvalidOperationException("attempt to yield from outside a coroutine");
        }

        for (int i = 0; i < arguments.Length; i++)
        {
            Push(arguments[i]);
        }

        using var runner = ScriptRunner.Rent();
        return runner.Run(this, arguments.Length, destination);
    }

    public ValueTask<LuauValue[]> ResumeAsync(CancellationToken cancellationToken = default) => ResumeAsync(ReadOnlyMemory<LuauValue>.Empty, cancellationToken);
    public async ValueTask<LuauValue[]> ResumeAsync(ReadOnlyMemory<LuauValue> arguments, CancellationToken cancellationToken = default)
    {
        if (IsMainThread)
        {
            ThrowHelper.ThrowInvalidOperationException("attempt to yield from outside a coroutine");
        }

        var span = arguments.Span;
        for (int i = 0; i < span.Length; i++)
        {
            Push(span[i]);
        }

        using var runner = ScriptRunner.Rent();
        return await runner.RunAsync(this, arguments.Length, cancellationToken);
    }

    public ValueTask<int> ResumeAsync(Memory<LuauValue> destination, CancellationToken cancellationToken = default) => ResumeAsync(ReadOnlyMemory<LuauValue>.Empty, destination, cancellationToken);
    public async ValueTask<int> ResumeAsync(ReadOnlyMemory<LuauValue> arguments, Memory<LuauValue> destination, CancellationToken cancellationToken = default)
    {
        if (IsMainThread)
        {
            ThrowHelper.ThrowInvalidOperationException("attempt to yield from outside a coroutine");
        }

        var span = arguments.Span;
        for (int i = 0; i < span.Length; i++)
        {
            Push(span[i]);
        }

        using var runner = ScriptRunner.Rent();
        return await runner.RunAsync(this, arguments.Length, destination, cancellationToken);
    }
}