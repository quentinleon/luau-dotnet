using System.Buffers;
using System.Text;

namespace Luau;

partial class LuauState
{
    public int Execute(ReadOnlySpan<byte> bytecode, Span<LuauValue> destination)
    {
        using var runner = ScriptRunner.Rent();
        LoadInternal(bytecode);
        return runner.Run(this, 0, destination);
    }

    public LuauValue[] Execute(ReadOnlySpan<byte> bytecode)
    {
        using var runner = ScriptRunner.Rent();
        LoadInternal(bytecode);
        return runner.Run(this, 0);
    }

    public ValueTask<int> ExecuteAsync(ReadOnlyMemory<byte> bytecode, Memory<LuauValue> destination, CancellationToken cancellationToken = default)
    {
        using var runner = ScriptRunner.Rent();
        LoadInternal(bytecode.Span);
        return runner.RunAsync(this, 0, destination, cancellationToken);
    }

    public ValueTask<LuauValue[]> ExecuteAsync(ReadOnlyMemory<byte> bytecode, CancellationToken cancellationToken = default)
    {
        using var runner = ScriptRunner.Rent();
        LoadInternal(bytecode.Span);
        return runner.RunAsync(this, 0, cancellationToken);
    }

    public LuauValue[] DoString(ReadOnlySpan<char> source, LuauCompileOptions? options = null)
    {
        using var runner = ScriptRunner.Rent();
        CompileAndLoadString(this, source, options);
        return runner.Run(this, 0);
    }

    public int DoString(ReadOnlySpan<char> source, Span<LuauValue> destination, LuauCompileOptions? options = null)
    {
        using var runner = ScriptRunner.Rent();
        CompileAndLoadString(this, source, options);
        return runner.Run(this, 0, destination);
    }

    public ValueTask<int> DoStringAsync(string source, Memory<LuauValue> destination, LuauCompileOptions? options = null, CancellationToken cancellationToken = default)
    {
        return DoStringAsync(source.AsMemory(), destination, options, cancellationToken);
    }

    public ValueTask<int> DoStringAsync(ReadOnlyMemory<char> source, Memory<LuauValue> destination, LuauCompileOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var runner = ScriptRunner.Rent();
        CompileAndLoadString(this, source.Span, options);
        return runner.RunAsync(this, 0, destination, cancellationToken);
    }

    public ValueTask<LuauValue[]> DoStringAsync(string source, LuauCompileOptions? options = null, CancellationToken cancellationToken = default)
    {
        return DoStringAsync(source.AsMemory(), options, cancellationToken);
    }

    public ValueTask<LuauValue[]> DoStringAsync(ReadOnlyMemory<char> source, LuauCompileOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var runner = ScriptRunner.Rent();
        CompileAndLoadString(this, source.Span, options);
        return runner.RunAsync(this, 0, cancellationToken);
    }

    public LuauValue[] DoString(ReadOnlySpan<byte> utf8Source, LuauCompileOptions? options = null)
    {
        using var runner = ScriptRunner.Rent();
        CompileAndLoadString(this, utf8Source, options);
        return runner.Run(this, 0);
    }

    public int DoString(ReadOnlySpan<byte> utf8Source, Span<LuauValue> destination, LuauCompileOptions? options = null)
    {
        using var runner = ScriptRunner.Rent();
        CompileAndLoadString(this, utf8Source, options);
        return runner.Run(this, 0, destination);
    }

    public ValueTask<int> DoStringAsync(ReadOnlyMemory<byte> utf8Source, Memory<LuauValue> destination, LuauCompileOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var runner = ScriptRunner.Rent();
        CompileAndLoadString(this, utf8Source.Span, options);
        return runner.RunAsync(this, 0, destination, cancellationToken);
    }

    public ValueTask<LuauValue[]> DoStringAsync(ReadOnlyMemory<byte> utf8Source, LuauCompileOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var runner = ScriptRunner.Rent();
        CompileAndLoadString(this, utf8Source.Span, options);
        return runner.RunAsync(this, 0, cancellationToken);
    }

    static void CompileAndLoadString(LuauState state, ReadOnlySpan<byte> utf8Source, LuauCompileOptions? options)
    {
        using var writer = new ArrayPoolBufferWriter(512);
        LuauCompiler.Compile(writer, utf8Source, options);
        state.LoadInternal(writer.WrittenSpan, default);
    }

    static void CompileAndLoadString(LuauState state, ReadOnlySpan<char> source, LuauCompileOptions? options)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(source.Length * 3);
        try
        {
            var utf8Count = Encoding.UTF8.GetBytes(source, buffer);
            CompileAndLoadString(state, buffer.AsSpan(0, utf8Count), options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
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