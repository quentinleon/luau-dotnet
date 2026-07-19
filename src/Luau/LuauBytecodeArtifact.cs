using System.Buffers;
using System.Text;
using Luau.Internal;

namespace Luau;

/// <summary>
/// Persistent precompiled bytecode plus build identity and provenance claims.
/// Metadata is not proof of trust; a state validator must establish provenance.
/// </summary>
public sealed class LuauBytecodeArtifact
{
    readonly byte[] bytecode;
    readonly byte[] provenanceData;

    /// <summary>Gets the persistent envelope schema supported by this build.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Rehydrates a persistent artifact from serialized data. All buffers are
    /// copied and schema/hash integrity is checked, but provenance remains an
    /// untrusted claim until an <see cref="ILuauBytecodeValidator"/> accepts it.
    /// </summary>
    public LuauBytecodeArtifact(
        int schemaVersion,
        ReadOnlySpan<byte> bytecode,
        LuauCompileOptions compileOptions,
        ulong upstreamRevisionHash,
        ulong hostBuildFingerprint,
        string sourceIdentity,
        string sourceSha256,
        string bytecodeSha256,
        string provenanceId,
        ReadOnlySpan<byte> provenanceData = default)
        : this(
            schemaVersion,
            bytecode,
            compileOptions,
            upstreamRevisionHash,
            hostBuildFingerprint,
            sourceIdentity,
            sourceSha256,
            bytecodeSha256,
            provenanceId,
            provenanceData,
            LuauArtifactLimits.Default)
    {
    }

    internal LuauBytecodeArtifact(
        int schemaVersion,
        ReadOnlySpan<byte> bytecode,
        LuauCompileOptions compileOptions,
        ulong upstreamRevisionHash,
        ulong hostBuildFingerprint,
        string sourceIdentity,
        string sourceSha256,
        string bytecodeSha256,
        string provenanceId,
        ReadOnlySpan<byte> provenanceData,
        LuauArtifactLimits limits)
    {
        if (schemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), schemaVersion, "Unsupported artifact schema.");
        }
        if (bytecode.IsEmpty)
        {
            throw new ArgumentException("A bytecode artifact cannot contain an empty payload.", nameof(bytecode));
        }
        if (bytecode[0] == 0)
        {
            throw new ArgumentException("A compiler diagnostic payload is not loadable bytecode.", nameof(bytecode));
        }
        if (compileOptions == null)
        {
            throw new ArgumentNullException(nameof(compileOptions));
        }
        if (string.IsNullOrWhiteSpace(sourceIdentity))
        {
            throw new ArgumentException("A source identity is required.", nameof(sourceIdentity));
        }
        if (!LuauBytecodeHash.IsSha256(sourceSha256))
        {
            throw new ArgumentException("The source hash must be a 64-character SHA-256 value.", nameof(sourceSha256));
        }
        if (!LuauBytecodeHash.IsSha256(bytecodeSha256))
        {
            throw new ArgumentException("The bytecode hash must be a 64-character SHA-256 value.", nameof(bytecodeSha256));
        }
        if (string.IsNullOrWhiteSpace(provenanceId))
        {
            throw new ArgumentException("A provenance identifier is required.", nameof(provenanceId));
        }

        var sourceIdentityBytes = GetStrictUtf8ByteCount(sourceIdentity, nameof(sourceIdentity));
        var provenanceIdBytes = GetStrictUtf8ByteCount(provenanceId, nameof(provenanceId));

        ValidateAdmission(
            bytecode.Length,
            sourceIdentityBytes,
            provenanceIdBytes,
            provenanceData.Length,
            limits);

        this.bytecode = bytecode.ToArray();
        var actualBytecodeHash = LuauBytecodeHash.Sha256(this.bytecode);
        if (!LuauBytecodeHash.EqualsSha256(actualBytecodeHash, bytecodeSha256))
        {
            throw new ArgumentException("The bytecode payload does not match its SHA-256 hash.", nameof(bytecodeSha256));
        }

        this.provenanceData = provenanceData.ToArray();
        SchemaVersion = schemaVersion;
        CompileOptions = compileOptions with { };
        UpstreamRevisionHash = upstreamRevisionHash;
        HostBuildFingerprint = hostBuildFingerprint;
        SourceIdentity = sourceIdentity;
        SourceSha256 = sourceSha256.ToLowerInvariant();
        BytecodeSha256 = actualBytecodeHash;
        ProvenanceId = provenanceId;
    }

    /// <summary>Gets the persistent envelope schema.</summary>
    public int SchemaVersion { get; }

    /// <summary>Gets the bytecode payload length.</summary>
    public int BytecodeLength => bytecode.Length;

    /// <summary>Gets the immutable compiler-option snapshot.</summary>
    public LuauCompileOptions CompileOptions { get; }

    /// <summary>Gets the claimed upstream Luau revision identity.</summary>
    public ulong UpstreamRevisionHash { get; }

    /// <summary>Gets the claimed exact native host build identity.</summary>
    public ulong HostBuildFingerprint { get; }

    /// <summary>Gets the host-defined stable identity of the admitted source.</summary>
    public string SourceIdentity { get; }

    /// <summary>Gets the claimed lowercase source SHA-256 hash.</summary>
    public string SourceSha256 { get; }

    /// <summary>Gets the verified lowercase bytecode SHA-256 hash.</summary>
    public string BytecodeSha256 { get; }

    /// <summary>Gets the host-defined provenance identifier.</summary>
    public string ProvenanceId { get; }

    /// <summary>
    /// Creates a persistent envelope from opaque compiler output. The supplied
    /// provenance still requires validation when loaded.
    /// </summary>
    public static LuauBytecodeArtifact Create(
        LuauCompilerOutput output,
        string sourceIdentity,
        string provenanceId,
        ReadOnlySpan<byte> provenanceData = default)
    {
        if (output == null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        return Create(
            output,
            sourceIdentity,
            provenanceId,
            provenanceData,
            LuauArtifactLimits.Default);
    }

    /// <summary>
    /// Creates a persistent envelope after applying explicit construction
    /// limits before any payload is cloned.
    /// </summary>
    public static LuauBytecodeArtifact Create(
        LuauCompilerOutput output,
        string sourceIdentity,
        string provenanceId,
        ReadOnlySpan<byte> provenanceData,
        LuauArtifactLimits limits)
    {
        if (output == null)
        {
            throw new ArgumentNullException(nameof(output));
        }
        if (limits == null)
        {
            throw new ArgumentNullException(nameof(limits));
        }

        return new LuauBytecodeArtifact(
            CurrentSchemaVersion,
            output.Bytecode,
            output.CompileOptions,
            output.UpstreamRevisionHash,
            output.HostBuildFingerprint,
            sourceIdentity,
            output.SourceSha256,
            output.BytecodeSha256,
            provenanceId,
            provenanceData,
            limits);
    }

    /// <summary>Returns a defensive copy of the bytecode.</summary>
    public byte[] ToBytecodeArray() => (byte[])bytecode.Clone();

    /// <summary>Returns a defensive copy of opaque provenance data.</summary>
    public byte[] GetProvenanceData() => (byte[])provenanceData.Clone();

    /// <summary>Copies the bytecode to a caller-owned writer.</summary>
    public void CopyBytecodeTo(IBufferWriter<byte> writer)
    {
        if (writer == null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        var destination = writer.GetSpan(bytecode.Length);
        bytecode.CopyTo(destination);
        writer.Advance(bytecode.Length);
    }

    internal ReadOnlySpan<byte> Bytecode => bytecode;
    internal ReadOnlySpan<byte> ProvenanceData => provenanceData;

    internal static void ValidateAdmission(
        int bytecodeBytes,
        int sourceIdentityBytes,
        int provenanceIdBytes,
        int provenanceBytes,
        LuauArtifactLimits limits)
    {
        if (limits == null) throw new ArgumentNullException(nameof(limits));
        LuauBytecodeArtifactCodec.CheckLimit(
            "bytecode",
            bytecodeBytes,
            limits.MaxBytecodeBytes);
        LuauBytecodeArtifactCodec.CheckLimit(
            "sourceIdentity",
            sourceIdentityBytes,
            limits.MaxSourceIdentityBytes);
        LuauBytecodeArtifactCodec.CheckLimit(
            "provenanceId",
            provenanceIdBytes,
            limits.MaxProvenanceIdBytes);
        LuauBytecodeArtifactCodec.CheckLimit(
            "provenanceData",
            provenanceBytes,
            limits.MaxProvenanceBytes);
        LuauBytecodeArtifactCodec.CheckEnvelopeLimit(
            sourceIdentityBytes,
            provenanceIdBytes,
            provenanceBytes,
            bytecodeBytes,
            limits.MaxEnvelopeBytes);
    }

    static int GetStrictUtf8ByteCount(string value, string parameterName)
    {
        try
        {
            return new UTF8Encoding(false, true).GetByteCount(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "The identity must contain valid Unicode text.",
                parameterName,
                exception);
        }
    }
}
