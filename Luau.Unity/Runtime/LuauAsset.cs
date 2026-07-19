using System;
using UnityEngine;

namespace Luau.Unity
{
    internal enum LuauAssetContentKind
    {
        Source = 0,
        VerifiedBytecode = 1,
    }

    public sealed class LuauAsset : ScriptableObject
    {
        [SerializeField] internal LuauAssetContentKind contentKind;
        [SerializeField] internal byte[] bytes;

        [SerializeField] internal int artifactSchemaVersion;
        [SerializeField] internal int optimizationLevel;
        [SerializeField] internal int debugLevel;
        [SerializeField] internal int typeInfoLevel;
        [SerializeField] internal int coverageLevel;
        [SerializeField] internal ulong upstreamRevisionHash;
        [SerializeField] internal ulong hostBuildFingerprint;
        [SerializeField] internal string sourceIdentity;
        [SerializeField] internal string sourceSha256;
        [SerializeField] internal string bytecodeSha256;
        [SerializeField] internal string provenanceId;
        [SerializeField] internal byte[] provenanceData;

        [NonSerialized] LuauBytecodeArtifact cachedArtifact;

#if UNITY_EDITOR
        [SerializeField] internal string text;
#endif

        /// <summary>
        /// Gets whether this asset stores a bytecode artifact. This marker is
        /// informational and never establishes trust; execution still requires
        /// the state's configured artifact validator.
        /// </summary>
        public bool IsPrecompiled => contentKind switch
        {
            LuauAssetContentKind.Source => false,
            LuauAssetContentKind.VerifiedBytecode => true,
            _ => throw InvalidContentKind(),
        };

        internal bool IsSource => contentKind == LuauAssetContentKind.Source;
        internal int PayloadLength => bytes?.Length ?? 0;

        /// <summary>
        /// Gets the UTF-8 source payload. Precompiled and unknown content kinds
        /// are rejected so source-only exporters cannot silently package bytecode.
        /// </summary>
        public ReadOnlySpan<byte> AsSpan()
        {
            if (!IsSource)
                throw InvalidContentKind();
            return bytes;
        }

        /// <summary>
        /// Gets the UTF-8 source payload as read-only memory.
        /// </summary>
        public ReadOnlyMemory<byte> AsMemory()
        {
            if (!IsSource)
                throw InvalidContentKind();
            return bytes;
        }

        internal void SetSource(string sourceText, byte[] sourceBytes)
        {
            contentKind = LuauAssetContentKind.Source;
            bytes = sourceBytes ?? throw new ArgumentNullException(nameof(sourceBytes));
            cachedArtifact = null;
#if UNITY_EDITOR
            text = sourceText ?? throw new ArgumentNullException(nameof(sourceText));
#endif
            ClearArtifactMetadata();
        }

        internal void SetVerifiedBytecode(
            string sourceText,
            LuauBytecodeArtifact artifact)
        {
            if (artifact == null)
                throw new ArgumentNullException(nameof(artifact));

            contentKind = LuauAssetContentKind.VerifiedBytecode;
            bytes = artifact.ToBytecodeArray();
#if UNITY_EDITOR
            text = sourceText ?? throw new ArgumentNullException(nameof(sourceText));
#endif
            artifactSchemaVersion = artifact.SchemaVersion;
            optimizationLevel = artifact.CompileOptions.OptimizationLevel;
            debugLevel = artifact.CompileOptions.DebugLevel;
            typeInfoLevel = artifact.CompileOptions.TypeInfoLevel;
            coverageLevel = artifact.CompileOptions.CoverageLevel;
            upstreamRevisionHash = artifact.UpstreamRevisionHash;
            hostBuildFingerprint = artifact.HostBuildFingerprint;
            sourceIdentity = artifact.SourceIdentity;
            sourceSha256 = artifact.SourceSha256;
            bytecodeSha256 = artifact.BytecodeSha256;
            provenanceId = artifact.ProvenanceId;
            provenanceData = artifact.GetProvenanceData();
            // Reconstruct from serialized fields on first execution so Editor
            // behavior matches the exact payload that will ship in a player.
            cachedArtifact = null;
        }

        internal LuauBytecodeArtifact GetVerifiedBytecode()
        {
            if (!IsPrecompiled)
                throw new InvalidOperationException("The Luau asset contains source, not bytecode.");

            if (cachedArtifact != null)
                return cachedArtifact;

            if (string.IsNullOrWhiteSpace(sourceIdentity))
            {
                throw new InvalidOperationException(
                    "The serialized bytecode artifact has no source identity. " +
                    "Reimport it with the current Luau.Unity importer.");
            }

            cachedArtifact = new LuauBytecodeArtifact(
                artifactSchemaVersion,
                bytes,
                new LuauCompileOptions
                {
                    OptimizationLevel = optimizationLevel,
                    DebugLevel = debugLevel,
                    TypeInfoLevel = typeInfoLevel,
                    CoverageLevel = coverageLevel,
                },
                upstreamRevisionHash,
                hostBuildFingerprint,
                sourceIdentity,
                sourceSha256,
                bytecodeSha256,
                provenanceId,
                provenanceData);
            return cachedArtifact;
        }

        InvalidOperationException InvalidContentKind()
        {
            return new InvalidOperationException(
                $"Luau asset '{name}' does not contain source (serialized content kind " +
                $"{(int)contentKind}).");
        }

        void ClearArtifactMetadata()
        {
            artifactSchemaVersion = 0;
            optimizationLevel = 0;
            debugLevel = 0;
            typeInfoLevel = 0;
            coverageLevel = 0;
            upstreamRevisionHash = 0;
            hostBuildFingerprint = 0;
            sourceIdentity = string.Empty;
            sourceSha256 = string.Empty;
            bytecodeSha256 = string.Empty;
            provenanceId = string.Empty;
            provenanceData = Array.Empty<byte>();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            cachedArtifact = null;
        }
#endif
    }
}
