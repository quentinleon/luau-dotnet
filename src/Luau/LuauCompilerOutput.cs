using System.Buffers;

namespace Luau;

/// <summary>
/// Opaque, success-only output produced by this process's Luau compiler.
/// It can be loaded directly for development and streamed-source execution,
/// but arbitrary bytes cannot be promoted into this capability.
/// </summary>
public sealed class LuauCompilerOutput
{
    readonly byte[] bytecode;

    internal LuauCompilerOutput(
        byte[] bytecode,
        LuauCompileOptions compileOptions,
        string sourceSha256,
        ulong upstreamRevisionHash,
        ulong hostBuildFingerprint)
    {
        if (bytecode == null)
        {
            throw new ArgumentNullException(nameof(bytecode));
        }
        if (bytecode.Length == 0 || bytecode[0] == 0)
        {
            throw new ArgumentException("Compiler output must contain loadable bytecode.", nameof(bytecode));
        }
        if (!Internal.LuauBytecodeHash.IsSha256(sourceSha256))
        {
            throw new ArgumentException("The source hash must be a SHA-256 value.", nameof(sourceSha256));
        }
        this.bytecode = bytecode;
        CompileOptions = compileOptions == null
            ? throw new ArgumentNullException(nameof(compileOptions))
            : compileOptions with { };
        SourceSha256 = sourceSha256.ToLowerInvariant();
        BytecodeSha256 = Internal.LuauBytecodeHash.Sha256(bytecode);
        UpstreamRevisionHash = upstreamRevisionHash;
        HostBuildFingerprint = hostBuildFingerprint;
    }

    /// <summary>Gets the compiled bytecode length in bytes.</summary>
    public int BytecodeLength => bytecode.Length;

    /// <summary>Gets the immutable compiler-option snapshot.</summary>
    public LuauCompileOptions CompileOptions { get; }

    /// <summary>Gets the lowercase SHA-256 hash of the compiled source.</summary>
    public string SourceSha256 { get; }

    /// <summary>Gets the lowercase SHA-256 hash of the bytecode.</summary>
    public string BytecodeSha256 { get; }

    /// <summary>Gets the pinned upstream Luau revision identity.</summary>
    public ulong UpstreamRevisionHash { get; }

    /// <summary>Gets the exact native host build identity.</summary>
    public ulong HostBuildFingerprint { get; }

    /// <summary>Returns a defensive copy for inspection or persistence.</summary>
    public byte[] ToBytecodeArray() => (byte[])bytecode.Clone();

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
}
