using System;
using System.IO;
using System.Text;
using Luau.Unity.Editor;
using NUnit.Framework;
using UnityEditor;

namespace Luau.Unity.Tests
{
    public sealed class LuauImporterPolicyTests
    {
        const string AssetPath = "Assets/__LuauImporterPolicyTests__.luau";

        LuauAssetImportPolicy originalPolicy;
        string originalProvenanceId;

        [SetUp]
        public void SetUp()
        {
            originalPolicy = LuauAssetImportSettings.ImportPolicy;
            originalProvenanceId = LuauAssetImportSettings.FirstPartyProvenanceId;
            AssetDatabase.DeleteAsset(AssetPath);
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(AssetPath);
            LuauAssetImportSettings.SetFirstPartyProvenanceIdForTests(originalProvenanceId);
            LuauAssetImportSettings.SetImportPolicyForTests(originalPolicy);
        }

        [Test]
        public void CheckedInProjectPolicyDefaultsToSourceOnly()
        {
            Assert.That(LuauAssetImportSettings.ImportPolicy,
                Is.EqualTo(LuauAssetImportPolicy.SourceOnly));
        }

        [Test]
        public void CompilerIdentityTracksCoverageInstrumentation()
        {
            var source = Encoding.UTF8.GetBytes("return 42");
            var defaultOutput = LuauCompiler.Compile(source, LuauCompileOptions.Default);
            var coverageOutput = LuauCompiler.Compile(
                source,
                LuauCompileOptions.Default with
                {
                    CoverageLevel = 2,
                });

            Assert.That(defaultOutput.CompileOptions.CoverageLevel, Is.Zero);
            Assert.That(coverageOutput.CompileOptions.CoverageLevel, Is.EqualTo(2));
            Assert.That(
                coverageOutput.BytecodeSha256,
                Is.Not.EqualTo(defaultOutput.BytecodeSha256));
            Assert.That(
                LuauCompilerIdentityDependency.ComputeHash(coverageOutput),
                Is.Not.EqualTo(LuauCompilerIdentityDependency.ComputeHash(defaultOutput)));
        }

        [Test]
        public void SourceOnlyReimportReplacesPrecompiledContentAndValidatorInspectsContent()
        {
            LuauAssetImportSettings.SetFirstPartyProvenanceIdForTests("tests:first-party");
            LuauAssetImportSettings.SetImportPolicyForTests(
                LuauAssetImportPolicy.AllowFirstPartyPrecompile);

            File.WriteAllText(AssetPath, "return 42");
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);

            var importer = (LuauImporter)AssetImporter.GetAtPath(AssetPath);
            var serializedImporter = new SerializedObject(importer);
            serializedImporter.FindProperty("precompile").boolValue = true;
            serializedImporter.ApplyModifiedPropertiesWithoutUndo();
            importer.SaveAndReimport();

            var precompiled = AssetDatabase.LoadAssetAtPath<LuauAsset>(AssetPath);
            Assert.That(precompiled.IsPrecompiled, Is.True);
            Assert.Throws<InvalidOperationException>(() => precompiled.AsMemory());
            Assert.That(
                Encoding.UTF8.GetString(
                    ReadByteArray(new SerializedObject(precompiled), "provenanceData")),
                Is.EqualTo(AssetDatabase.AssetPathToGUID(AssetPath)));

            using (var state = LuauState.Create(new LuauStateOptions
            {
                BytecodePolicy = LuauBytecodePolicy.RequireValidator,
                BytecodeValidator = new AssetGuidValidator(
                    "tests:first-party",
                    AssetDatabase.AssetPathToGUID(AssetPath)),
                MaxBytecodeBytes = LuauStateOptions.Default.MaxBytecodeBytes,
            }))
            {
                var results = state.Execute(precompiled);
                Assert.That(results[0].Read<int>(), Is.EqualTo(42));
            }
            Assert.Throws<InvalidOperationException>(() =>
                LuauSourceOnlyAssetValidator.ValidateSourceOnly(new[] { AssetPath }));

            LuauAssetImportSettings.SetImportPolicyForTests(LuauAssetImportPolicy.SourceOnly);
            importer.SaveAndReimport();

            var sourceOnly = AssetDatabase.LoadAssetAtPath<LuauAsset>(AssetPath);
            Assert.That(sourceOnly.IsPrecompiled, Is.False);
            Assert.That(Encoding.UTF8.GetString(sourceOnly.AsSpan()), Is.EqualTo("return 42"));
            Assert.DoesNotThrow(() =>
                LuauSourceOnlyAssetValidator.ValidateSourceOnly(new[] { AssetPath }));

            var tampered = new SerializedObject(sourceOnly);
            tampered.FindProperty("contentKind").intValue = 99;
            tampered.ApplyModifiedPropertiesWithoutUndo();
            Assert.Throws<InvalidOperationException>(() => sourceOnly.AsMemory());
            Assert.Throws<InvalidOperationException>(() =>
            {
                _ = sourceOnly.IsPrecompiled;
            });
            Assert.Throws<InvalidOperationException>(() =>
                LuauSourceOnlyAssetValidator.ValidateSourceOnly(new[] { AssetPath }));
        }

        static byte[] ReadByteArray(SerializedObject serializedObject, string propertyName)
        {
            var property = serializedObject.FindProperty(propertyName);
            var bytes = new byte[property.arraySize];
            for (var index = 0; index < bytes.Length; index++)
                bytes[index] = (byte)property.GetArrayElementAtIndex(index).intValue;
            return bytes;
        }

        sealed class AssetGuidValidator : ILuauBytecodeValidator
        {
            readonly string provenanceId;
            readonly string assetGuid;

            public AssetGuidValidator(string provenanceId, string assetGuid)
            {
                this.provenanceId = provenanceId;
                this.assetGuid = assetGuid;
            }

            public bool IsValid(
                LuauBytecodeArtifact artifact,
                ReadOnlySpan<byte> bytecode)
            {
                return artifact.ProvenanceId == provenanceId &&
                    bytecode.Length == artifact.BytecodeLength &&
                    Encoding.UTF8.GetString(artifact.GetProvenanceData()) == assetGuid;
            }
        }
    }
}
