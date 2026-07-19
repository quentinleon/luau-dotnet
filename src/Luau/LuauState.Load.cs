using System.Buffers;
using System.Text;
using Luau.Internal.Interop;
using static Luau.Internal.Interop.NativeMethods;

namespace Luau;

public unsafe partial class LuauState
{
    static readonly byte[] defaultChunkName = [.. "main"u8, 0];

    /// <summary>
    /// Loads opaque bytecode returned by this process's compiler as a script
    /// closure that managed code may execute through <see cref="LuauFunction.Invoke"/>.
    /// </summary>
    public LuauFunction LoadCompilerOutput(
        LuauCompilerOutput output,
        ReadOnlySpan<char> chunkName = default)
    {
        if (output == null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        ThrowIfDisposed();
        ValidateCompilerOutput(output, DecodeChunkName(chunkName));
        using var access = EnterNativeAccess();
        var originalTop = luau_host_stack_get_top(l);

        var chunkByteCount = Encoding.UTF8.GetByteCount(chunkName);
        var chunkBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, chunkByteCount));
        try
        {
            var encodedCount = Encoding.UTF8.GetBytes(chunkName, chunkBuffer);
            LoadAcceptedBytecodeInternal(
                output.Bytecode,
                chunkBuffer.AsSpan(0, encodedCount),
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

    /// <summary>
    /// Loads a persistent artifact after build-identity and configured
    /// provenance validation as a host-invokable script closure. Caller-provided
    /// chunk names are diagnostic only.
    /// </summary>
    public LuauFunction LoadVerifiedBytecode(
        LuauBytecodeArtifact artifact,
        ReadOnlySpan<char> chunkName = default)
    {
        if (artifact == null)
        {
            throw new ArgumentNullException(nameof(artifact));
        }

        ThrowIfDisposed();
        ValidateArtifact(artifact, DecodeChunkName(chunkName));
        using var access = EnterNativeAccess();
        var originalTop = luau_host_stack_get_top(l);

        var chunkByteCount = Encoding.UTF8.GetByteCount(chunkName);
        var chunkBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, chunkByteCount));
        try
        {
            var encodedCount = Encoding.UTF8.GetBytes(chunkName, chunkBuffer);
            LoadAcceptedBytecodeInternal(
                artifact.Bytecode,
                chunkBuffer.AsSpan(0, encodedCount),
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

    unsafe void LoadAcceptedBytecodeInternal(
        ReadOnlySpan<byte> bytecode,
        ReadOnlySpan<char> chunkName)
    {
        if (chunkName.IsEmpty)
        {
            LoadAcceptedBytecodeInternal(bytecode, ReadOnlySpan<byte>.Empty);
            return;
        }

        var byteCount = Encoding.UTF8.GetByteCount(chunkName);
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, byteCount));
        try
        {
            var encodedCount = Encoding.UTF8.GetBytes(chunkName, buffer);
            LoadAcceptedBytecodeInternal(
                bytecode,
                buffer.AsSpan(0, encodedCount),
                DecodeChunkName(chunkName));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Raw native loader primitive. Callers must establish compiler-output or
    /// persistent-artifact admission before reaching this method.
    /// </summary>
    unsafe void LoadAcceptedBytecodeInternal(
        ReadOnlySpan<byte> bytecode,
        ReadOnlySpan<byte> utf8ChunkName,
        string? decodedChunkName = null,
        bool allowOversizedDiagnostic = false)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();

        decodedChunkName ??= DecodeChunkName(utf8ChunkName);
        if (!allowOversizedDiagnostic || bytecode.IsEmpty || bytecode[0] != 0)
        {
            ValidateBytecodeSize(bytecode.Length, decodedChunkName);
        }
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
                LuauHostStatus protectedStatus;
                using (context.BeginBytecodeLoad())
                {
                    protectedStatus = luau_host_load(
                        l,
                        namePointer,
                        bytecodePointer,
                        (ulong)bytecode.Length,
                        0,
                        &loadStatus);
                }
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

    void ValidateCompilerOutput(LuauCompilerOutput output, string? chunkName)
    {
        ValidateBytecodeSize(output.BytecodeLength, chunkName);
        ValidateBytecodeIdentity(
            output.UpstreamRevisionHash,
            output.HostBuildFingerprint,
            chunkName);
    }

    void ValidateArtifact(LuauBytecodeArtifact artifact, string? chunkName)
    {
        ValidateBytecodeSize(artifact.BytecodeLength, chunkName);
        ValidateBytecodeIdentity(
            artifact.UpstreamRevisionHash,
            artifact.HostBuildFingerprint,
            chunkName);

        if (Options.BytecodePolicy == LuauBytecodePolicy.Reject)
        {
            throw new LuauException(
                LuauDiagnosticMessages.WithChunk(
                    "Persistent bytecode artifacts are disabled for this state.",
                    chunkName),
                chunkName);
        }
        if (Options.BytecodePolicy != LuauBytecodePolicy.RequireValidator)
        {
            throw new InvalidOperationException("Unknown bytecode policy.");
        }
        if (!Options.BytecodeValidator!.IsValid(artifact, artifact.Bytecode))
        {
            throw new LuauException(
                LuauDiagnosticMessages.WithChunk(
                    "The persistent bytecode artifact was rejected by the configured validator.",
                    chunkName),
                chunkName);
        }
    }

    void ValidateBytecodeSize(int bytecodeLength, string? chunkName)
    {
        if (Options.MaxBytecodeBytes is { } limit && bytecodeLength > limit)
        {
            throw new LuauLoadLimitException(
                chunkName,
                LuauLoadInputKind.Bytecode,
                bytecodeLength,
                limit);
        }
    }

    static void ValidateBytecodeIdentity(
        ulong upstreamRevisionHash,
        ulong hostBuildFingerprint,
        string? chunkName)
    {
        if (upstreamRevisionHash != LuauNativeProtection.ExpectedUpstreamRevisionHash ||
            hostBuildFingerprint != LuauNativeProtection.ExpectedHostBuildFingerprint)
        {
            throw new LuauException(
                LuauDiagnosticMessages.WithChunk(
                    "The bytecode was produced for a different Luau runtime build.",
                    chunkName),
                chunkName);
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
