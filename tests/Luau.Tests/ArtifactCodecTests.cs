using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Luau.Tests;

public sealed class ArtifactCodecTests
{
    const int HeaderLength = 128;
    const int IntegrityLength = 32;

    [Fact]
    public void SpanStreamAndBufferWriterRoundTripsPreserveArtifactMetadata()
    {
        var artifact = CreateArtifact();
        var encoded = LuauBytecodeArtifactCodec.Write(artifact);

        var fromSpan = LuauBytecodeArtifactCodec.Parse(encoded);
        using var stream = new MemoryStream(encoded, writable: false);
        var fromStream = LuauBytecodeArtifactCodec.Parse(stream);
        var writer = new ArrayBufferWriter<byte>();
        LuauBytecodeArtifactCodec.Write(artifact, writer);

        Assert.Equal(encoded, writer.WrittenSpan.ToArray());
        AssertArtifactsEqual(artifact, fromSpan);
        AssertArtifactsEqual(artifact, fromStream);
    }

    [Fact]
    public void TruncationAndTrailingBytesAreTypedMalformedFailures()
    {
        var encoded = LuauBytecodeArtifactCodec.Write(CreateArtifact());
        for (var length = 0; length < encoded.Length; length++)
        {
            var exception = Assert.Throws<LuauArtifactException>(
                () => LuauBytecodeArtifactCodec.Parse(encoded.AsSpan(0, length)));
            Assert.True(
                exception.FailureKind is LuauArtifactFailureKind.Malformed or
                    LuauArtifactFailureKind.UnsupportedVersion,
                $"Unexpected truncation failure {exception.FailureKind} at length {length}.");
        }

        var trailing = new byte[encoded.Length + 1];
        encoded.CopyTo(trailing, 0);
        var trailingException = Assert.Throws<LuauArtifactException>(
            () => LuauBytecodeArtifactCodec.Parse(trailing));
        Assert.Equal(LuauArtifactFailureKind.Malformed, trailingException.FailureKind);
    }

    [Fact]
    public void DeclaredLengthOverflowIsRejectedBeforePayloadAllocation()
    {
        var encoded = LuauBytecodeArtifactCodec.Write(CreateArtifact());
        BinaryPrimitives.WriteInt32LittleEndian(encoded.AsSpan(124, sizeof(int)), int.MaxValue);

        var exception = Assert.Throws<LuauArtifactException>(
            () => LuauBytecodeArtifactCodec.Parse(encoded, LuauArtifactLimits.UnsafeUnbounded));

        Assert.Equal(LuauArtifactFailureKind.Malformed, exception.FailureKind);
    }

    [Fact]
    public void EveryEnvelopeFieldLimitIsCheckedAtExactAndOneOverBoundaries()
    {
        var artifact = CreateArtifact();
        var encoded = LuauBytecodeArtifactCodec.Write(artifact);
        var provenanceBytes = artifact.GetProvenanceData().Length;
        var provenanceIdBytes = Encoding.UTF8.GetByteCount(artifact.ProvenanceId);
        var sourceIdentityBytes = Encoding.UTF8.GetByteCount(artifact.SourceIdentity);

        var exact = new LuauArtifactLimits
        {
            MaxEnvelopeBytes = encoded.Length,
            MaxBytecodeBytes = artifact.BytecodeLength,
            MaxProvenanceBytes = provenanceBytes,
            MaxProvenanceIdBytes = provenanceIdBytes,
            MaxSourceIdentityBytes = sourceIdentityBytes,
        };
        AssertArtifactsEqual(artifact, LuauBytecodeArtifactCodec.Parse(encoded, exact));

        AssertLimit(encoded, exact with { MaxEnvelopeBytes = encoded.Length - 1 }, "envelope");
        AssertLimit(encoded, exact with { MaxBytecodeBytes = artifact.BytecodeLength - 1 }, "bytecode");
        AssertLimit(encoded, exact with { MaxProvenanceBytes = provenanceBytes - 1 }, "provenanceData");
        AssertLimit(encoded, exact with { MaxProvenanceIdBytes = provenanceIdBytes - 1 }, "provenanceId");
        AssertLimit(encoded, exact with { MaxSourceIdentityBytes = sourceIdentityBytes - 1 }, "sourceIdentity");
    }

    [Fact]
    public void ArtifactConstructionPreflightsEnvelopeLimitBeforePublication()
    {
        var output = LuauCompiler.Compile("return 42"u8);
        const string sourceIdentity = "assets/answer.luau";
        const string provenanceId = "tests/stage-6";
        byte[] provenance = [0x10, 0x20, 0x30];
        var baseline = LuauBytecodeArtifact.Create(
            output,
            sourceIdentity,
            provenanceId,
            provenance);
        var encodedLength = LuauBytecodeArtifactCodec.Write(baseline).Length;
        var exact = new LuauArtifactLimits
        {
            MaxEnvelopeBytes = encodedLength,
            MaxBytecodeBytes = output.BytecodeLength,
            MaxSourceIdentityBytes = Encoding.UTF8.GetByteCount(sourceIdentity),
            MaxProvenanceIdBytes = Encoding.UTF8.GetByteCount(provenanceId),
            MaxProvenanceBytes = provenance.Length,
        };

        var constructed = LuauBytecodeArtifact.Create(
            output,
            sourceIdentity,
            provenanceId,
            provenance,
            exact);
        Assert.Equal(encodedLength, LuauBytecodeArtifactCodec.Write(constructed, exact).Length);

        var exception = Assert.Throws<LuauArtifactException>(() =>
            LuauBytecodeArtifact.Create(
                output,
                sourceIdentity,
                provenanceId,
                provenance,
                exact with { MaxEnvelopeBytes = encodedLength - 1 }));
        Assert.Equal(LuauArtifactFailureKind.LimitExceeded, exception.FailureKind);
        Assert.Equal("envelope", exception.FieldName);
        Assert.Equal(encodedLength, exception.Actual);
        Assert.Equal(encodedLength - 1, exception.Limit);
    }

    [Fact]
    public void IdentityCorruptionAndInvalidUtf8HaveTypedDiagnostics()
    {
        var artifact = CreateArtifact();
        var encoded = LuauBytecodeArtifactCodec.Write(artifact);

        var identity = (byte[])encoded.Clone();
        identity[32] ^= 0x01;
        Assert.Equal(
            LuauArtifactFailureKind.IntegrityMismatch,
            Assert.Throws<LuauArtifactException>(
                () => LuauBytecodeArtifactCodec.Parse(identity)).FailureKind);

        RefreshIntegrity(identity);
        Assert.Equal(
            LuauArtifactFailureKind.RuntimeIdentityMismatch,
            Assert.Throws<LuauArtifactException>(
                () => LuauBytecodeArtifactCodec.Parse(identity)).FailureKind);

        var corruption = (byte[])encoded.Clone();
        corruption[HeaderLength + Encoding.UTF8.GetByteCount(artifact.SourceIdentity) +
            Encoding.UTF8.GetByteCount(artifact.ProvenanceId) +
            artifact.GetProvenanceData().Length] ^= 0x20;
        Assert.Equal(
            LuauArtifactFailureKind.IntegrityMismatch,
            Assert.Throws<LuauArtifactException>(
                () => LuauBytecodeArtifactCodec.Parse(corruption)).FailureKind);

        var invalidSourceIdentity = (byte[])encoded.Clone();
        invalidSourceIdentity[HeaderLength] = 0xff;
        RefreshIntegrity(invalidSourceIdentity);
        var invalidSourceIdentityException = Assert.Throws<LuauArtifactException>(
            () => LuauBytecodeArtifactCodec.Parse(invalidSourceIdentity));
        Assert.Equal(LuauArtifactFailureKind.Malformed, invalidSourceIdentityException.FailureKind);
        Assert.Equal("sourceIdentity", invalidSourceIdentityException.FieldName);

        var invalidProvenanceId = (byte[])encoded.Clone();
        invalidProvenanceId[
            HeaderLength + Encoding.UTF8.GetByteCount(artifact.SourceIdentity)] = 0xff;
        RefreshIntegrity(invalidProvenanceId);
        var invalidProvenanceIdException = Assert.Throws<LuauArtifactException>(
            () => LuauBytecodeArtifactCodec.Parse(invalidProvenanceId));
        Assert.Equal(LuauArtifactFailureKind.Malformed, invalidProvenanceIdException.FailureKind);
        Assert.Equal("provenanceId", invalidProvenanceIdException.FieldName);
    }

    [Fact]
    public void BoundedRandomMutationCorpusOnlyProducesArtifactsOrTypedRejections()
    {
        var encoded = LuauBytecodeArtifactCodec.Write(CreateArtifact());
        var random = new Random(0x5a17_0603);
        for (var iteration = 0; iteration < 2_000; iteration++)
        {
            var mutated = (byte[])encoded.Clone();
            var mutationCount = random.Next(1, 9);
            for (var mutation = 0; mutation < mutationCount; mutation++)
            {
                var index = random.Next(mutated.Length);
                mutated[index] ^= (byte)random.Next(1, 256);
            }

            ParseOrTypedFailure(mutated);
        }

        for (var iteration = 0; iteration < 1_000; iteration++)
        {
            var arbitrary = new byte[random.Next(0, 513)];
            random.NextBytes(arbitrary);
            ParseOrTypedFailure(arbitrary);
        }
    }

    [Fact]
    public void BoundedStreamRejectsOneByteOverWithoutReturningAnArtifact()
    {
        var encoded = LuauBytecodeArtifactCodec.Write(CreateArtifact());
        using var stream = new MemoryStream(encoded, writable: false);
        var exception = Assert.Throws<LuauArtifactException>(() =>
            LuauBytecodeArtifactCodec.Parse(stream, new LuauArtifactLimits
            {
                MaxEnvelopeBytes = encoded.Length - 1,
                MaxBytecodeBytes = null,
                MaxProvenanceBytes = null,
                MaxProvenanceIdBytes = null,
                MaxSourceIdentityBytes = null,
            }));

        Assert.Equal(LuauArtifactFailureKind.LimitExceeded, exception.FailureKind);
        Assert.Equal("envelope", exception.FieldName);
    }

    static LuauBytecodeArtifact CreateArtifact()
    {
        var output = LuauCompiler.Compile(
            "local answer: number = 40 + 2; return answer"u8,
            new LuauCompileOptions
            {
                OptimizationLevel = 2,
                DebugLevel = 1,
                TypeInfoLevel = 1,
                CoverageLevel = 1,
            });
        return LuauBytecodeArtifact.Create(
            output,
            "assets/gameplay/answer.luau",
            "tests/stage-6",
            [0x10, 0x20, 0x30, 0x40]);
    }

    static void AssertArtifactsEqual(LuauBytecodeArtifact expected, LuauBytecodeArtifact actual)
    {
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.CompileOptions, actual.CompileOptions);
        Assert.Equal(expected.UpstreamRevisionHash, actual.UpstreamRevisionHash);
        Assert.Equal(expected.HostBuildFingerprint, actual.HostBuildFingerprint);
        Assert.Equal(expected.SourceIdentity, actual.SourceIdentity);
        Assert.Equal(expected.SourceSha256, actual.SourceSha256);
        Assert.Equal(expected.BytecodeSha256, actual.BytecodeSha256);
        Assert.Equal(expected.ProvenanceId, actual.ProvenanceId);
        Assert.Equal(expected.GetProvenanceData(), actual.GetProvenanceData());
        Assert.Equal(expected.ToBytecodeArray(), actual.ToBytecodeArray());
    }

    static void AssertLimit(
        byte[] encoded,
        LuauArtifactLimits limits,
        string field)
    {
        var exception = Assert.Throws<LuauArtifactException>(
            () => LuauBytecodeArtifactCodec.Parse(encoded, limits));
        Assert.Equal(LuauArtifactFailureKind.LimitExceeded, exception.FailureKind);
        Assert.Equal(field, exception.FieldName);
    }

    static void ParseOrTypedFailure(byte[] encoded)
    {
        try
        {
            _ = LuauBytecodeArtifactCodec.Parse(encoded, new LuauArtifactLimits
            {
                MaxEnvelopeBytes = 1024 * 1024,
                MaxBytecodeBytes = 512 * 1024,
                MaxProvenanceBytes = 64 * 1024,
                MaxProvenanceIdBytes = 1024,
                MaxSourceIdentityBytes = 4096,
            });
        }
        catch (LuauArtifactException)
        {
            // The parser contract intentionally has one typed malformed-input family.
        }
        catch (Exception exception)
        {
            Assert.Fail($"Artifact parser leaked {exception.GetType().FullName}: {exception.Message}");
        }
    }

    static void RefreshIntegrity(byte[] encoded)
    {
        var digest = SHA256.HashData(encoded.AsSpan(0, encoded.Length - IntegrityLength));
        digest.CopyTo(encoded, encoded.Length - IntegrityLength);
    }
}
