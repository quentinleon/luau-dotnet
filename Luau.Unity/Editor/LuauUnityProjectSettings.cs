using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Luau.Unity.Editor
{
    public enum LuauAssetImportPolicy
    {
        SourceOnly = 0,
        AllowFirstPartyPrecompile = 1,
    }

    [FilePath("ProjectSettings/LuauUnitySettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class LuauUnityProjectSettingsData
        : ScriptableSingleton<LuauUnityProjectSettingsData>
    {
        [SerializeField]
        LuauAssetImportPolicy importPolicy = LuauAssetImportPolicy.SourceOnly;

        [SerializeField]
        string firstPartyProvenanceId = string.Empty;

        internal LuauAssetImportPolicy ImportPolicy =>
            importPolicy == LuauAssetImportPolicy.AllowFirstPartyPrecompile
                ? LuauAssetImportPolicy.AllowFirstPartyPrecompile
                : LuauAssetImportPolicy.SourceOnly;
        internal string FirstPartyProvenanceId =>
            (firstPartyProvenanceId ?? string.Empty).Trim();

        internal bool SetImportPolicy(LuauAssetImportPolicy value, bool save)
        {
            if (!Enum.IsDefined(typeof(LuauAssetImportPolicy), value))
                throw new ArgumentOutOfRangeException(nameof(value));

            if (importPolicy == value)
                return false;

            importPolicy = value;
            if (save)
                Save(true);
            return true;
        }

        internal bool SetFirstPartyProvenanceId(string value, bool save)
        {
            value = (value ?? string.Empty).Trim();
            if (string.Equals(firstPartyProvenanceId, value, StringComparison.Ordinal))
                return false;

            firstPartyProvenanceId = value;
            if (save)
                Save(true);
            return true;
        }
    }

    /// <summary>Project-wide policy for importing <c>.luau</c> assets.</summary>
    public static class LuauAssetImportSettings
    {
        public static LuauAssetImportPolicy ImportPolicy =>
            LuauUnityProjectSettingsData.instance.ImportPolicy;

        /// <summary>
        /// Gets the public provenance scheme or publisher label embedded in
        /// first-party artifacts. The runtime validator must authenticate it.
        /// </summary>
        public static string FirstPartyProvenanceId =>
            LuauUnityProjectSettingsData.instance.FirstPartyProvenanceId;

        public static void SetImportPolicy(LuauAssetImportPolicy importPolicy)
        {
            if (!LuauUnityProjectSettingsData.instance.SetImportPolicy(importPolicy, save: true))
                return;

            ReimportLuauAssets();
        }

        public static void SetFirstPartyProvenanceId(string provenanceId)
        {
            if (string.IsNullOrWhiteSpace(provenanceId))
                throw new ArgumentException("A provenance ID is required.", nameof(provenanceId));
            provenanceId = provenanceId.Trim();
            if (LuauUnityProjectSettingsData.instance.SetFirstPartyProvenanceId(provenanceId, save: true))
                ReimportLuauAssets();
        }

        internal static void SetImportPolicyForTests(LuauAssetImportPolicy importPolicy)
        {
            LuauUnityProjectSettingsData.instance.SetImportPolicy(importPolicy, save: false);
        }

        internal static void SetFirstPartyProvenanceIdForTests(string provenanceId)
        {
            LuauUnityProjectSettingsData.instance.SetFirstPartyProvenanceId(provenanceId, save: false);
        }

        static void ReimportLuauAssets()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:LuauAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
        }

        [SettingsProvider]
        static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider("Project/Luau.Unity", SettingsScope.Project)
            {
                label = "Luau.Unity",
                keywords = new HashSet<string>
                {
                    "Luau", "source", "bytecode", "precompile", "mods",
                },
                guiHandler = _ => DrawSettings(),
            };
        }

        static void DrawSettings()
        {
            var current = ImportPolicy;
            var next = (LuauAssetImportPolicy)EditorGUILayout.EnumPopup("Asset import policy", current);
            if (next != current)
                SetImportPolicy(next);

            if (next == LuauAssetImportPolicy.AllowFirstPartyPrecompile)
            {
                var provenanceId = EditorGUILayout.DelayedTextField(
                    new GUIContent("First-party provenance ID", "Public label only; runtime validation establishes trust."),
                    FirstPartyProvenanceId);
                if (!string.Equals(provenanceId, FirstPartyProvenanceId, StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(provenanceId))
                    SetFirstPartyProvenanceId(provenanceId);
            }

            var sourceOnly = next == LuauAssetImportPolicy.SourceOnly;
            var missingProvenanceId = !sourceOnly &&
                string.IsNullOrWhiteSpace(FirstPartyProvenanceId);
            EditorGUILayout.HelpBox(
                sourceOnly
                    ? "SourceOnly is the safe default. Build validation rejects precompiled assets."
                    : missingProvenanceId
                        ? "A nonempty first-party provenance ID is required before any asset can be precompiled."
                        : "First-party bytecode still requires runtime artifact validation.",
                sourceOnly
                    ? MessageType.Info
                    : missingProvenanceId ? MessageType.Error : MessageType.Warning);
        }
    }
}
