using System.Buffers;
using System.Text;
using Luau.Internal.Interop;
using static Luau.Internal.Interop.NativeMethods;

namespace Luau;

public unsafe partial class LuauState
{
    static readonly byte[] defaultChunkName = [.. "main"u8, 0];

    /// <summary>
    /// Loads host-supplied bytecode after applying the configured
    /// <see cref="LuauStateOptions.BytecodePolicy"/> and byte-size limit.
    /// Untrusted content should normally enter as source through
    /// <c>DoString</c> instead.
    /// </summary>
    public LuauFunction Load(ReadOnlySpan<byte> bytecode, ReadOnlySpan<char> chunkName)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        var originalTop = luau_host_stack_get_top(l);

        var chunkByteCount = Encoding.UTF8.GetByteCount(chunkName);
        var chunkBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, chunkByteCount));
        try
        {
            var encodedCount = Encoding.UTF8.GetBytes(chunkName, chunkBuffer);
            LoadInternal(
                bytecode,
                chunkBuffer.AsSpan(0, encodedCount),
                trustedCompilerOutput: false,
                DecodeChunkName(chunkName));
            var function = ToFunction(-1);
            Pop(1);
            return function;
        }
        finally
        {
            SetTop(originalTop);
            ArrayPool<byte>.Shared.Return(chunkBuffer);
        }
    }

    /// <inheritdoc cref="Load(ReadOnlySpan{byte}, ReadOnlySpan{char})"/>
    public LuauFunction Load(ReadOnlySpan<byte> bytecode, ReadOnlySpan<byte> utf8ChunkName = default)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        var originalTop = luau_host_stack_get_top(l);
        try
        {
            LoadInternal(bytecode, utf8ChunkName, trustedCompilerOutput: false);
            return ToFunction(-1);
        }
        finally
        {
            SetTop(originalTop);
        }
    }

    /// <summary>
    /// Loads bytecode whose provenance has already been established by the
    /// host, bypassing <see cref="LuauStateOptions.BytecodePolicy"/> while still
    /// enforcing the configured bytecode-size limit. Invoking the returned
    /// function still observes the state's execution limits. A size limit is
    /// not provenance validation, and untrusted mods must never reach this API.
    /// </summary>
    public LuauFunction LoadTrustedBytecode(
        ReadOnlySpan<byte> bytecode,
        ReadOnlySpan<char> chunkName)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        var originalTop = luau_host_stack_get_top(l);

        var chunkByteCount = Encoding.UTF8.GetByteCount(chunkName);
        var chunkBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, chunkByteCount));
        try
        {
            var encodedCount = Encoding.UTF8.GetBytes(chunkName, chunkBuffer);
            LoadInternal(
                bytecode,
                chunkBuffer.AsSpan(0, encodedCount),
                trustedCompilerOutput: true,
                DecodeChunkName(chunkName));
            var function = ToFunction(-1);
            Pop(1);
            return function;
        }
        finally
        {
            SetTop(originalTop);
            ArrayPool<byte>.Shared.Return(chunkBuffer);
        }
    }

    /// <inheritdoc cref="LoadTrustedBytecode(ReadOnlySpan{byte}, ReadOnlySpan{char})"/>
    public LuauFunction LoadTrustedBytecode(
        ReadOnlySpan<byte> bytecode,
        ReadOnlySpan<byte> utf8ChunkName = default)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        var originalTop = luau_host_stack_get_top(l);
        try
        {
            LoadInternal(bytecode, utf8ChunkName, trustedCompilerOutput: true);
            return ToFunction(-1);
        }
        finally
        {
            SetTop(originalTop);
        }
    }

    unsafe void LoadInternal(
        ReadOnlySpan<byte> bytecode,
        ReadOnlySpan<char> chunkName,
        bool trustedCompilerOutput = false)
    {
        if (chunkName.IsEmpty)
        {
            LoadInternal(bytecode, ReadOnlySpan<byte>.Empty, trustedCompilerOutput);
            return;
        }

        var byteCount = Encoding.UTF8.GetByteCount(chunkName);
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, byteCount));
        try
        {
            var encodedCount = Encoding.UTF8.GetBytes(chunkName, buffer);
            LoadInternal(
                bytecode,
                buffer.AsSpan(0, encodedCount),
                trustedCompilerOutput,
                DecodeChunkName(chunkName));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    unsafe void LoadInternal(
        ReadOnlySpan<byte> bytecode,
        ReadOnlySpan<byte> utf8ChunkName,
        bool trustedCompilerOutput = false,
        string? decodedChunkName = null)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();

        decodedChunkName ??= DecodeChunkName(utf8ChunkName);
        ValidateBytecode(bytecode, utf8ChunkName, decodedChunkName, trustedCompilerOutput);
        LuauNativeProtection.Prepare(context);

        var nameBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, utf8ChunkName.Length + 1));
        try
        {
            ReadOnlySpan<byte> nullTerminatedName;
            if (utf8ChunkName.IsEmpty)
            {
                nullTerminatedName = defaultChunkName;
            }
            else
            {
                utf8ChunkName.CopyTo(nameBuffer);
                nameBuffer[utf8ChunkName.Length] = 0;
                nullTerminatedName = nameBuffer.AsSpan(0, utf8ChunkName.Length + 1);
            }

            LuauHostStatus status;
            fixed (byte* bytecodePointer = bytecode)
            fixed (byte* namePointer = nullTerminatedName)
            {
                var loadStatus = LuauHostStatus.Ok;
                var protectedStatus = luau_host_load(
                    l,
                    namePointer,
                    bytecodePointer,
                    (ulong)bytecode.Length,
                    0,
                    &loadStatus);
                LuauNativeProtection.ThrowIfFailed(
                    this,
                    l,
                    protectedStatus,
                    "load bytecode",
                    decodedChunkName);
                status = loadStatus;
            }

            if (status == LuauHostStatus.Ok)
            {
                return;
            }

            var message = ReadAndPopError();
            if (context.AllocatorFailure == LuauAllocatorFailure.QuotaExceeded)
            {
                throw new LuauMemoryLimitException(
                    decodedChunkName,
                    context.MemoryUsage,
                    context.LastAttemptedAllocationBytes);
            }

            if (context.AllocatorFailure == LuauAllocatorFailure.SystemOutOfMemory)
            {
                throw new OutOfMemoryException(
                    LuauDiagnosticMessages.WithChunk("The Luau VM could not allocate memory while loading bytecode.", decodedChunkName));
            }

            if (!string.IsNullOrEmpty(decodedChunkName) &&
                message.IndexOf(decodedChunkName, StringComparison.Ordinal) < 0)
            {
                message = LuauDiagnosticMessages.WithChunk(message, decodedChunkName);
            }

            throw new LuauException(message, decodedChunkName);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(nameBuffer);
        }
    }

    internal void ValidateSourceSize(int sourceBytes, string? chunkName)
    {
        if (Options.MaxSourceBytes is { } limit && sourceBytes > limit)
        {
            throw new LuauLoadLimitException(
                chunkName,
                LuauLoadInputKind.Source,
                sourceBytes,
                limit);
        }
    }

    void ValidateBytecode(
        ReadOnlySpan<byte> bytecode,
        ReadOnlySpan<byte> utf8ChunkName,
        string? decodedChunkName,
        bool trustedCompilerOutput)
    {
        if (Options.MaxBytecodeBytes is { } limit && bytecode.Length > limit)
        {
            throw new LuauLoadLimitException(
                decodedChunkName,
                LuauLoadInputKind.Bytecode,
                bytecode.Length,
                limit);
        }

        if (trustedCompilerOutput)
        {
            return;
        }

        switch (Options.BytecodePolicy)
        {
            case LuauBytecodePolicy.AllowUnvalidated:
                return;
            case LuauBytecodePolicy.Reject:
                throw new LuauException(
                    LuauDiagnosticMessages.WithChunk(
                        "Host-supplied precompiled bytecode is disabled for this state.",
                        decodedChunkName),
                    decodedChunkName);
            case LuauBytecodePolicy.RequireValidator:
                if (Options.BytecodeValidator!.IsValid(bytecode, utf8ChunkName))
                {
                    return;
                }

                throw new LuauException(
                    LuauDiagnosticMessages.WithChunk(
                        "Host-supplied precompiled bytecode was rejected by the configured validator.",
                        decodedChunkName),
                    decodedChunkName);
            default:
                throw new InvalidOperationException("Unknown bytecode policy.");
        }
    }

    unsafe string ReadAndPopError()
    {
        try
        {
            // Reading an existing string is allocation-free. Do not ask
            // lua_tolstring to coerce an arbitrary error object here: coercion
            // can allocate and must never long-jump across this managed frame.
            if (luau_host_stack_get_top(l) == 0 || (LuauHostType)luau_host_type(l, -1) != LuauHostType.String)
            {
                return "Luau loading failed without a string error message.";
            }

            ulong length = 0;
            var pointer = luau_host_to_string_view(l, -1, &length);
            return pointer == null || length == 0
                ? "Luau loading failed without an error message."
                : length > int.MaxValue
                    ? "Luau loading failed with an oversized error message."
                    : Encoding.UTF8.GetString(new ReadOnlySpan<byte>(pointer, (int)length));
        }
        finally
        {
            if (luau_host_stack_get_top(l) > 0)
            {
                SetTop(-2);
            }
        }
    }
}
