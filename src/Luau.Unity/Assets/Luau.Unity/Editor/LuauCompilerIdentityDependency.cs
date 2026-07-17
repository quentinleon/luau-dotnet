using System;
using System.Globalization;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Luau.Unity.Editor
{
    internal static class LuauCompilerIdentityDependency
    {
        internal const string Name = "Luau.Unity.CompilerIdentity";
        static Hash128? pendingHash;

        [InitializeOnLoadMethod]
        static void ScheduleRegistration()
        {
            EditorApplication.delayCall -= RegisterAtEditorReady;
            EditorApplication.delayCall += RegisterAtEditorReady;
        }

        internal static void DependsOn(AssetImportContext context)
        {
            context.DependsOnCustomDependency(Name);
        }

        internal static void ScheduleRegistration(LuauCompilerOutput output)
        {
            pendingHash = ComputeHash(output);
            EditorApplication.delayCall -= RegisterPending;
            EditorApplication.delayCall += RegisterPending;
        }

        static Hash128 ComputeHash(LuauCompilerOutput output)
        {
            var options = output.CompileOptions;
            var identity = string.Join(
                "|",
                LuauBytecodeArtifact.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture),
                options.OptimizationLevel.ToString(CultureInfo.InvariantCulture),
                options.DebugLevel.ToString(CultureInfo.InvariantCulture),
                options.TypeInfoLevel.ToString(CultureInfo.InvariantCulture),
                options.CoverageLevel.ToString(CultureInfo.InvariantCulture),
                output.UpstreamRevisionHash.ToString("x16", CultureInfo.InvariantCulture),
                output.HostBuildFingerprint.ToString("x16", CultureInfo.InvariantCulture));
            return Hash128.Compute(identity);
        }

        static void RegisterPending()
        {
            if (pendingHash is not { } hash)
                return;

            pendingHash = null;
            AssetDatabase.RegisterCustomDependency(Name, hash);
        }

        static void RegisterAtEditorReady()
        {
            try
            {
                AssetDatabase.RegisterCustomDependency(
                    Name,
                    ComputeHash(LuauCompiler.Compile(ReadOnlySpan<byte>.Empty)));
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Luau.Unity could not register compiler artifact identity. " +
                    $"Normal asset import will report the underlying failure: {exception.Message}");
            }
        }
    }
}
