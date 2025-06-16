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

            using (new EditorGUI.IndentLevelScope(-1))
            {
                EditorGUILayout.TextArea(asset.text);
            }
        }
    }
}