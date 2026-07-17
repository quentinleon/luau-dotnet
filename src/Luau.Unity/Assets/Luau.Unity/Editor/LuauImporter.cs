using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Luau.Unity.Editor
{
    [ScriptedImporter(3, "luau")]
    public sealed class LuauImporter : ScriptedImporter
    {
        [SerializeField]
        bool precompile;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            LuauCompilerIdentityDependency.DependsOn(ctx);
            var text = File.ReadAllText(ctx.assetPath);
            var source = Encoding.UTF8.GetBytes(text);
            var asset = ScriptableObject.CreateInstance<LuauAsset>();

            LuauCompilerOutput compilerOutput;
            try
            {
                compilerOutput = LuauCompiler.Compile(source);
                LuauCompilerIdentityDependency.ScheduleRegistration(compilerOutput);
            }
            catch (LuauCompilationException exception)
            {
                ctx.LogImportError(exception.Message);
                asset.SetSource(text, source);
                AddAsset(ctx, asset);
                return;
            }
            var allowPrecompile =
                LuauAssetImportSettings.ImportPolicy ==
                    LuauAssetImportPolicy.AllowFirstPartyPrecompile &&
                precompile;

            if (allowPrecompile)
            {
                ImportPrecompiled(ctx, asset, text, compilerOutput);
            }
            else
            {
                asset.SetSource(text, source);
            }

            AddAsset(ctx, asset);
        }

        static void AddAsset(AssetImportContext ctx, LuauAsset asset)
        {
            ctx.AddObjectToAsset("Main", asset);
            ctx.SetMainObject(asset);
        }

        static void ImportPrecompiled(
            AssetImportContext ctx,
            LuauAsset asset,
            string sourceText,
            LuauCompilerOutput compilerOutput)
        {
            var provenanceId = LuauAssetImportSettings.FirstPartyProvenanceId;
            var assetGuid = AssetDatabase.AssetPathToGUID(ctx.assetPath);
            if (string.IsNullOrWhiteSpace(provenanceId) || string.IsNullOrEmpty(assetGuid))
            {
                ctx.LogImportError(
                    "First-party precompile requires a project provenance ID and stable asset GUID. " +
                    "The importer stored source instead.");
                asset.SetSource(sourceText, Encoding.UTF8.GetBytes(sourceText));
                return;
            }

            var artifact = LuauBytecodeArtifact.Create(
                compilerOutput,
                provenanceId,
                Encoding.UTF8.GetBytes(assetGuid));
            asset.SetVerifiedBytecode(sourceText, artifact);
        }
    }
}
