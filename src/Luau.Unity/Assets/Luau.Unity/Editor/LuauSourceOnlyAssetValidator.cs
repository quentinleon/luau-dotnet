using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Luau.Unity.Editor
{
    /// <summary>
    /// Enforces the source-only package boundary by inspecting imported asset
    /// content, independent of serialized importer options.
    /// </summary>
    public static class LuauSourceOnlyAssetValidator
    {
        public static IReadOnlyList<string> FindNonSourceAssets(
            IEnumerable<string> assetPaths)
        {
            if (assetPaths == null)
                throw new ArgumentNullException(nameof(assetPaths));

            return assetPaths
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct(StringComparer.Ordinal)
                .Where(path => AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<LuauAsset>()
                    .Any(asset => !asset.IsSource))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        public static void ValidateSourceOnly(IEnumerable<string> assetPaths)
        {
            var invalid = FindNonSourceAssets(assetPaths);
            if (invalid.Count != 0)
            {
                throw new InvalidOperationException(
                    "Source-only Luau validation rejected non-source assets: " +
                    string.Join(", ", invalid));
            }
        }

        public static void ValidateProject()
        {
            ValidateSourceOnly(FindAllLuauAssetPaths());
        }

        internal static IEnumerable<string> FindAllLuauAssetPaths()
        {
            return AssetDatabase.FindAssets("t:LuauAsset")
                .Select(AssetDatabase.GUIDToAssetPath);
        }
    }

    internal sealed class LuauSourceOnlyBuildPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (LuauAssetImportSettings.ImportPolicy ==
                LuauAssetImportPolicy.AllowFirstPartyPrecompile)
                return;

            try
            {
                LuauSourceOnlyAssetValidator.ValidateProject();
            }
            catch (InvalidOperationException exception)
            {
                throw new BuildFailedException(exception.Message);
            }
        }
    }
}
