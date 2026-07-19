using System;
using System.Text;
using UnityEditor;

namespace Luau.Unity.Editor
{
    [CustomEditor(typeof(LuauAsset))]
    public sealed class LuauAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var asset = (LuauAsset)target;

            var isKnown = asset.IsSource ||
                asset.contentKind == LuauAssetContentKind.VerifiedBytecode;
            EditorGUILayout.HelpBox(
                asset.IsSource
                    ? "This asset stores UTF-8 source."
                    : isKnown
                        ? "This asset stores first-party bytecode. Runtime execution still requires a configured provenance validator."
                        : "This asset has an unknown serialized content kind and cannot be executed or packaged.",
                isKnown ? MessageType.Info : MessageType.Error);

            using (new EditorGUI.IndentLevelScope(-1))
            {
                EditorGUILayout.TextArea(asset.text);
            }
        }
    }
}
