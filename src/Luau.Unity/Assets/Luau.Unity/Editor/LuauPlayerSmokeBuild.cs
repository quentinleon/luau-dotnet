using System;
using System.IO;
using Luau.Unity.Verification;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Luau.Unity.Editor
{
    /// <summary>
    /// Builds a disposable smoke scene without changing PlayerSettings or the
    /// project's configured scene list. The requested target must already be
    /// active and configured for IL2CPP.
    /// </summary>
    public static class LuauPlayerSmokeBuild
    {
        const string OutputArgument = "-luauSmokeOutput";

        [MenuItem("Luau/Verification/Build Windows x64 IL2CPP Smoke Player")]
        public static void BuildWindows64Il2Cpp()
        {
            Build(
                BuildTarget.StandaloneWindows64,
                Path.Combine("Builds", "LuauSmoke", "Windows", "LuauSmoke.exe"));
        }

        [MenuItem("Luau/Verification/Build Android ARM64 IL2CPP Smoke Player")]
        public static void BuildAndroidArm64Il2Cpp()
        {
            BuildAndroid(
                AndroidArchitecture.ARM64,
                Path.Combine("Builds", "LuauSmoke", "Android-arm64", "LuauSmoke.apk"));
        }

        [MenuItem("Luau/Verification/Build Android x64 IL2CPP Smoke Player")]
        public static void BuildAndroidX64Il2Cpp()
        {
            BuildAndroid(
                AndroidArchitecture.X86_64,
                Path.Combine("Builds", "LuauSmoke", "Android-x64", "LuauSmoke.apk"));
        }

        static void BuildAndroid(AndroidArchitecture architecture, string defaultOutput)
        {
            var previousArchitecture = PlayerSettings.Android.targetArchitectures;
            try
            {
                PlayerSettings.Android.targetArchitectures = architecture;
                Build(BuildTarget.Android, defaultOutput, architecture);
            }
            finally
            {
                PlayerSettings.Android.targetArchitectures = previousArchitecture;
            }
        }

        static void Build(
            BuildTarget target,
            string defaultOutput,
            AndroidArchitecture? requiredAndroidArchitecture = null)
        {
            ValidateTarget(target, requiredAndroidArchitecture);

            var output = GetOutputPath(defaultOutput);
            var outputDirectory = Path.GetDirectoryName(output);
            if (string.IsNullOrEmpty(outputDirectory))
            {
                throw new BuildFailedException("The Luau smoke output path has no parent directory.");
            }

            Directory.CreateDirectory(outputDirectory);

            var temporaryFolderName = "__LuauPlayerSmoke_" + Guid.NewGuid().ToString("N");
            var temporaryFolderGuid = AssetDatabase.CreateFolder("Assets", temporaryFolderName);
            var temporaryFolder = AssetDatabase.GUIDToAssetPath(temporaryFolderGuid);
            if (string.IsNullOrEmpty(temporaryFolder))
            {
                throw new BuildFailedException("Unable to create a temporary Luau smoke scene folder.");
            }

            Scene smokeScene = default;
            try
            {
                var activeScene = SceneManager.GetActiveScene();
                var sceneMode = activeScene.IsValid() &&
                                string.IsNullOrEmpty(activeScene.path) &&
                                !activeScene.isDirty
                    ? NewSceneMode.Single
                    : NewSceneMode.Additive;
                smokeScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, sceneMode);
                var smokeObject = new GameObject("Luau Player Smoke");
                SceneManager.MoveGameObjectToScene(smokeObject, smokeScene);
                smokeObject.AddComponent<LuauPlayerSmoke>().QuitOnCompletion = true;

                var scenePath = temporaryFolder + "/LuauPlayerSmoke.unity";
                if (!EditorSceneManager.SaveScene(smokeScene, scenePath, false))
                {
                    throw new BuildFailedException("Unable to save the temporary Luau smoke scene.");
                }

                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { scenePath },
                    locationPathName = output,
                    target = target,
                    options = BuildOptions.Development,
                });

                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new BuildFailedException(
                        "Luau smoke build failed with " + report.summary.totalErrors + " error(s).");
                }

                Debug.Log("Luau smoke player built at " + report.summary.outputPath);
            }
            finally
            {
                if (smokeScene.IsValid() && smokeScene.isLoaded)
                {
                    if (SceneManager.sceneCount == 1)
                    {
                        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    }
                    else
                    {
                        EditorSceneManager.CloseScene(smokeScene, true);
                    }
                }

                AssetDatabase.DeleteAsset(temporaryFolder);
            }
        }

        static void ValidateTarget(
            BuildTarget target,
            AndroidArchitecture? requiredAndroidArchitecture)
        {
            if (EditorUserBuildSettings.activeBuildTarget != target)
            {
                throw new BuildFailedException(
                    "The active build target is " + EditorUserBuildSettings.activeBuildTarget +
                    ", but the Luau smoke build requested " + target +
                    ". Start Unity with the matching -buildTarget argument first.");
            }

            var group = BuildPipeline.GetBuildTargetGroup(target);
            var namedTarget = NamedBuildTarget.FromBuildTargetGroup(group);
            if (PlayerSettings.GetScriptingBackend(namedTarget) != ScriptingImplementation.IL2CPP)
            {
                throw new BuildFailedException(
                    "The " + target + " player must already be configured to use IL2CPP.");
            }

            if (target == BuildTarget.Android && requiredAndroidArchitecture.HasValue &&
                PlayerSettings.Android.targetArchitectures != requiredAndroidArchitecture.Value)
            {
                throw new BuildFailedException(
                    "The Android smoke player must target exactly " + requiredAndroidArchitecture.Value + ".");
            }
        }

        static string GetOutputPath(string defaultOutput)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var i = 0; i < arguments.Length; i++)
            {
                if (string.Equals(arguments[i], OutputArgument, StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= arguments.Length || string.IsNullOrWhiteSpace(arguments[i + 1]))
                    {
                        throw new BuildFailedException(OutputArgument + " requires a path value.");
                    }

                    return Path.GetFullPath(arguments[i + 1]);
                }

                var prefix = OutputArgument + "=";
                if (arguments[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var value = arguments[i].Substring(prefix.Length);
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        throw new BuildFailedException(OutputArgument + " requires a path value.");
                    }

                    return Path.GetFullPath(value);
                }
            }

            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", defaultOutput));
        }
    }
}
