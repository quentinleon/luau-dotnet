using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Luau.Unity.Editor
{
    [CustomEditor(typeof(LuauImporter))]
    public sealed class LuauImporterEditor : ScriptedImporterEditor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var policy = LuauAssetImportSettings.ImportPolicy;
            if (policy == LuauAssetImportPolicy.AllowFirstPartyPrecompile)
            {
                var hasProvenanceId =
                    !string.IsNullOrWhiteSpace(LuauAssetImportSettings.FirstPartyProvenanceId);
                if (!hasProvenanceId)
                {
                    EditorGUILayout.HelpBox(
                        "Set a first-party provenance ID in Project Settings > Luau.Unity before precompiling.",
                        MessageType.Error);
                }

                using (new EditorGUI.DisabledScope(!hasProvenanceId))
                {
                    EditorGUILayout.PropertyField(
                        serializedObject.FindProperty("precompile"),
                        new GUIContent("Precompile first-party artifact"));
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "This project stores .luau assets as UTF-8 source. Compilation still runs for import diagnostics.",
                    MessageType.Info);
            }

            serializedObject.ApplyModifiedProperties();
            ApplyRevertGUI();
        }
    }
}
