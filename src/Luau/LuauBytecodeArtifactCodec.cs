using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Luau.Internal;

namespace Luau;

/// <summary>
/// Reads and writes the bounded binary envelope for persistent bytecode.
/// Successful parsing verifies integrity but never grants compiler-output trust;
/// the returned artifact still requires the state's provenance validator.
/// </summary>
public static class LuauBytecodeArtifactCodec
{
    static readonly byte[] Magic = [0x4c, 0x55, 0x41, 0x55, 0x41, 0x52, 0x54, 0x00];
    const int HeaderLength = 128;
    const int IntegrityLength = 32;

    /// <summary>Gets the current binary envelope version.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Encodes an artifact into a newly allocated bounded byte array.</summary>
    public static byte[] Write(
        LuauBytecodeArtifact artifact,
        LuauArtifactLimits? limits = null)
    {
        var length = GetEncodedLength(artifact, limits ?? LuauArtifactLimits.Default);
        var result = new byte[length];
        WriteCore(artifact, result);
        return result;
    }

    /// <summary>Encodes an artifact into a caller-owned buffer writer.</summary>
    public static void Write(
        LuauBytecodeArtifact artifact,
        IBufferWriter<byte> destination,
        LuauArtifactLimits? limits = null)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        var length = GetEncodedLength(artifact, limits ?? LuauArtifactLimits.Default);
        var span = destination.GetSpan(length);
        if (span.Length < length)
        {
            throw new InvalidOperationException("The artifact buffer writer returned insufficient space.");
        }
        WriteCore(artifact, span[..length]);
        destination.Advance(length);
    }

    /// <summary>Encodes an artifact to a writable stream.</summary>
    public static void Write(
        LuauBytecodeArtifact artifact,
        Stream destination,
        LuauArtifactLimits? limits = null)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        if (!destination.CanWrite) throw new ArgumentException("The stream is not writable.", nameof(destination));
        var encoded = Write(artifact, limits);
        destination.Write(encoded, 0, encoded.Length);
    }

    /// <summary>Parses and integrity-checks one complete artifact envelope.</summary>
    public static LuauBytecodeArtifact Parse(
        ReadOnlySpan<byte> encoded,
        LuauArtifactLimits? limits = null)
    {
        var effectiveLimits = limits ?? LuauArtifactLimits.Default;
        CheckLimit("envelope", encoded.Length, effectiveLimits.MaxEnvelopeBytes);
        if (encoded.Length < HeaderLength + IntegrityLength)
        {
            throw Failure(LuauArtifactFailureKind.Malformed, "The artifact envelope is truncated.");
        }
        if (!encoded[..Magic.Length].SequenceEqual(Magic))
        {
            throw Failure(LuauArtifactFailureKind.Malformed, "The artifact magic is invalid.", "magic");
        }

        var offset = Magic.Length;
        var formatVersion = ReadInt32(encoded, ref offset);
        var schemaVersion = ReadInt32(encoded, ref offset);
        if (formatVersion != CurrentFormatVersion ||
            schemaVersion != LuauBytecodeArtifact.CurrentSchemaVersion)
        {
            throw Failure(
                LuauArtifactFailureKind.UnsupportedVersion,
                $"Unsupported artifact format/schema {formatVersion}/{schemaVersion}.");
        }

        var optimizationLevel = ReadInt32(encoded, ref offset);
        var debugLevel = ReadInt32(encoded, ref offset);
        var typeInfoLevel = ReadInt32(encoded, ref offset);
        var coverageLevel = ReadInt32(encoded, ref offset);

        var upstreamRevisionHash = ReadUInt64(encoded, ref offset);
        var hostBuildFingerprint = ReadUInt64(encoded, ref offset);

        var sourceHash = LuauBytecodeHash.ToLowerHex(Take(encoded, ref offset, 32));
        var bytecodeHash = LuauBytecodeHash.ToLowerHex(Take(encoded, ref offset, 32));
        var sourceIdentityLength = ReadLength(encoded, ref offset, "sourceIdentity");
        var provenanceIdLength = ReadLength(encoded, ref offset, "provenanceId");
        var provenanceLength = ReadLength(encoded, ref offset, "provenanceData");
        var bytecodeLength = ReadLength(encoded, ref offset, "bytecode");

        CheckLimit("sourceIdentity", sourceIdentityLength, effectiveLimits.MaxSourceIdentityBytes);
        CheckLimit("provenanceId", provenanceIdLength, effectiveLimits.MaxProvenanceIdBytes);
        CheckLimit("provenanceData", provenanceLength, effectiveLimits.MaxProvenanceBytes);
        CheckLimit("bytecode", bytecodeLength, effectiveLimits.MaxBytecodeBytes);

        int expectedLength;
        try
        {
            expectedLength = checked(
                HeaderLength + sourceIdentityLength + provenanceIdLength + provenanceLength +
                bytecodeLength + IntegrityLength);
        }
        catch (OverflowException exception)
        {
            throw Failure(
                LuauArtifactFailureKind.Malformed,
                "Artifact field lengths overflow the managed envelope size.",
                innerException: exception);
        }
        if (encoded.Length != expectedLength)
        {
            throw Failure(
                LuauArtifactFailureKind.Malformed,
                encoded.Length < expectedLength
                    ? "The artifact payload is truncated."
                    : "The artifact envelope contains trailing bytes.");
        }

        var integrityOffset = encoded.Length - IntegrityLength;
        Span<byte> actualIntegrity = stackalloc byte[IntegrityLength];
        using (var sha256 = SHA256.Create())
        {
            if (!sha256.TryComputeHash(encoded[..integrityOffset], actualIntegrity, out var written) ||
                written != IntegrityLength)
            {
                throw new CryptographicException("SHA-256 did not produce a complete artifact digest.");
            }
        }
        if (!CryptographicOperations.FixedTimeEquals(actualIntegrity, encoded[integrityOffset..]))
        {
            throw Failure(
                LuauArtifactFailureKind.IntegrityMismatch,
                "The artifact envelope integrity hash does not match.",
                "integrity");
        }

        LuauCompileOptions compileOptions;
        try
        {
            compileOptions = new LuauCompileOptions
            {
                OptimizationLevel = optimizationLevel,
                DebugLevel = debugLevel,
                TypeInfoLevel = typeInfoLevel,
                CoverageLevel = coverageLevel,
            };
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw Failure(
                LuauArtifactFailureKind.Malformed,
                "The artifact contains invalid compiler options.",
                "compileOptions",
                innerException: exception);
        }

        if (upstreamRevisionHash != LuauNativeProtection.ExpectedUpstreamRevisionHash ||
            hostBuildFingerprint != LuauNativeProtection.ExpectedHostBuildFingerprint)
        {
            throw Failure(
                LuauArtifactFailureKind.RuntimeIdentityMismatch,
                "The artifact was produced for a different compiler or host ABI build.");
        }

        var sourceIdentity = DecodeIdentity(
            Take(encoded, ref offset, sourceIdentityLength),
            "sourceIdentity",
            "source identity");
        var provenanceId = DecodeIdentity(
            Take(encoded, ref offset, provenanceIdLength),
            "provenanceId",
            "provenance identifier");
        var provenanceData = Take(encoded, ref offset, provenanceLength);
        var bytecode = Take(encoded, ref offset, bytecodeLength);

        try
        {
            return new LuauBytecodeArtifact(
                schemaVersion,
                bytecode,
                compileOptions,
                upstreamRevisionHash,
                hostBuildFingerprint,
                sourceIdentity,
                sourceHash,
                bytecodeHash,
                provenanceId,
                provenanceData,
                effectiveLimits);
        }
        catch (LuauArtifactException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            throw Failure(
                exception.ParamName == "bytecodeSha256"
                    ? LuauArtifactFailureKind.IntegrityMismatch
                    : LuauArtifactFailureKind.Malformed,
                exception.ParamName == "bytecodeSha256"
                    ? "The bytecode payload integrity hash does not match."
                    : "The artifact payload metadata is malformed.",
                innerException: exception);
        }
        catch (OverflowException exception)
        {
            throw Failure(
                LuauArtifactFailureKind.Malformed,
                "The artifact payload metadata overflows managed limits.",
                innerException: exception);
        }
    }

    /// <summary>Reads one bounded artifact envelope from a stream.</summary>
    public static LuauBytecodeArtifact Parse(
        Stream source,
        LuauArtifactLimits? limits = null)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (!source.CanRead) throw new ArgumentException("The stream is not readable.", nameof(source));
        var effectiveLimits = limits ?? LuauArtifactLimits.Default;
        using var writer = new ArrayPoolBufferWriter(4096);
        var rented = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            while (true)
            {
                var read = source.Read(rented, 0, rented.Length);
                if (read == 0) break;
                int nextLength;
                try
                {
                    nextLength = checked(writer.WrittenCount + read);
                }
                catch (OverflowException exception)
                {
                    throw Failure(
                        LuauArtifactFailureKind.LimitExceeded,
                        "The artifact envelope exceeds the managed representation limit.",
                        "envelope",
                        (long)writer.WrittenCount + read,
                        int.MaxValue,
                        exception);
                }
                CheckLimit("envelope", nextLength, effectiveLimits.MaxEnvelopeBytes);
                var destination = writer.GetSpan(read);
                rented.AsSpan(0, read).CopyTo(destination);
                writer.Advance(read);
            }

            return Parse(writer.WrittenSpan, effectiveLimits);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    static int GetEncodedLength(LuauBytecodeArtifact artifact, LuauArtifactLimits limits)
    {
        if (artifact == null) throw new ArgumentNullException(nameof(artifact));
        var sourceIdentityLength = Encoding.UTF8.GetByteCount(artifact.SourceIdentity);
        var provenanceIdLength = Encoding.UTF8.GetByteCount(artifact.ProvenanceId);
        LuauBytecodeArtifact.ValidateAdmission(
            artifact.BytecodeLength,
            sourceIdentityLength,
            provenanceIdLength,
            artifact.ProvenanceData.Length,
            limits);
        int length;
        try
        {
            length = checked(
                HeaderLength + sourceIdentityLength + provenanceIdLength +
                artifact.ProvenanceData.Length + artifact.BytecodeLength + IntegrityLength);
        }
        catch (OverflowException exception)
        {
            throw Failure(
                LuauArtifactFailureKind.LimitExceeded,
                "The encoded artifact length exceeds managed limits.",
                "envelope",
                innerException: exception);
        }
        CheckLimit("envelope", length, limits.MaxEnvelopeBytes);
        return length;
    }

    static void WriteCore(LuauBytecodeArtifact artifact, Span<byte> destination)
    {
        Magic.CopyTo(destination);
        var offset = Magic.Length;
        WriteInt32(destination, ref offset, CurrentFormatVersion);
        WriteInt32(destination, ref offset, artifact.SchemaVersion);
        WriteInt32(destination, ref offset, artifact.CompileOptions.OptimizationLevel);
        WriteInt32(destination, ref offset, artifact.CompileOptions.DebugLevel);
        WriteInt32(destination, ref offset, artifact.CompileOptions.TypeInfoLevel);
        WriteInt32(destination, ref offset, artifact.CompileOptions.CoverageLevel);
        WriteUInt64(destination, ref offset, artifact.UpstreamRevisionHash);
        WriteUInt64(destination, ref offset, artifact.HostBuildFingerprint);
        LuauBytecodeHash.WriteHex(artifact.SourceSha256, Take(destination, ref offset, 32));
        LuauBytecodeHash.WriteHex(artifact.BytecodeSha256, Take(destination, ref offset, 32));
        var sourceIdentityLength = Encoding.UTF8.GetByteCount(artifact.SourceIdentity);
        var provenanceIdLength = Encoding.UTF8.GetByteCount(artifact.ProvenanceId);
        WriteInt32(destination, ref offset, sourceIdentityLength);
        WriteInt32(destination, ref offset, provenanceIdLength);
        WriteInt32(destination, ref offset, artifact.ProvenanceData.Length);
        WriteInt32(destination, ref offset, artifact.BytecodeLength);
        offset += Encoding.UTF8.GetBytes(artifact.SourceIdentity, destination[offset..]);
        offset += Encoding.UTF8.GetBytes(artifact.ProvenanceId, destination[offset..]);
        artifact.ProvenanceData.CopyTo(Take(destination, ref offset, artifact.ProvenanceData.Length));
        artifact.Bytecode.CopyTo(Take(destination, ref offset, artifact.BytecodeLength));

        using var sha256 = SHA256.Create();
        if (!sha256.TryComputeHash(destination[..offset], destination[offset..], out var written) ||
            written != IntegrityLength)
        {
            throw new CryptographicException("SHA-256 did not produce a complete artifact digest.");
        }
    }

    static int ReadInt32(ReadOnlySpan<byte> source, ref int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(Take(source, ref offset, sizeof(int)));

    static ulong ReadUInt64(ReadOnlySpan<byte> source, ref int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(Take(source, ref offset, sizeof(ulong)));

    static int ReadLength(ReadOnlySpan<byte> source, ref int offset, string field)
    {
        var length = ReadInt32(source, ref offset);
        if (length < 0)
        {
            throw Failure(LuauArtifactFailureKind.Malformed, $"The {field} length is negative.", field);
        }
        return length;
    }

    static string DecodeIdentity(
        ReadOnlySpan<byte> encoded,
        string field,
        string displayName)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(encoded);
        }
        catch (DecoderFallbackException exception)
        {
            throw Failure(
                LuauArtifactFailureKind.Malformed,
                $"The artifact {displayName} is not valid UTF-8.",
                field,
                innerException: exception);
        }
    }

    static void WriteInt32(Span<byte> destination, ref int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(Take(destination, ref offset, sizeof(int)), value);
    }

    static void WriteUInt64(Span<byte> destination, ref int offset, ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(Take(destination, ref offset, sizeof(ulong)), value);
    }

    static ReadOnlySpan<byte> Take(ReadOnlySpan<byte> source, ref int offset, int length)
    {
        if (length < 0 || offset > source.Length - length)
        {
            throw Failure(LuauArtifactFailureKind.Malformed, "The artifact envelope is truncated.");
        }
        var result = source.Slice(offset, length);
        offset += length;
        return result;
    }

    static Span<byte> Take(Span<byte> source, ref int offset, int length)
    {
        var result = source.Slice(offset, length);
        offset += length;
        return result;
    }

    internal static void CheckLimit(string field, long actual, int? limit)
    {
        if (limit.HasValue && actual > limit.Value)
        {
            throw Failure(
                LuauArtifactFailureKind.LimitExceeded,
                $"Artifact {field} size of {actual} bytes exceeds the configured {limit.Value}-byte limit.",
                field,
                actual,
                limit.Value);
        }
    }

    internal static void CheckEnvelopeLimit(
        int sourceIdentityBytes,
        int provenanceIdBytes,
        int provenanceBytes,
        int bytecodeBytes,
        int? limit)
    {
        var encodedBytes =
            (long)HeaderLength + IntegrityLength + sourceIdentityBytes +
            provenanceIdBytes + provenanceBytes + bytecodeBytes;
        CheckLimit("envelope", encodedBytes, limit);
    }

    static LuauArtifactException Failure(
        LuauArtifactFailureKind kind,
        string message,
        string? field = null,
        long? actual = null,
        long? limit = null,
        Exception? innerException = null) =>
        new(kind, message, field, actual, limit, innerException);
}
