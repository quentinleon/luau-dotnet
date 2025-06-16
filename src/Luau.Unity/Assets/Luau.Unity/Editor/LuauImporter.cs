using System.IO;
using System.Text;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Luau.Unity.Editor
{
    [ScriptedImporter(1, "luau")]
    public sealed class LuauImporter : ScriptedImporter
    {
        [SerializeField] bool precompile = true;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var text = File.ReadAllText(ctx.assetPath);
            var asset = ScriptableObject.CreateInstance<LuauAsset>();
            asset.text = text;
            asset.isPrecompiled = precompile;

            if (precompile)
            {
                asset.bytes = LuauCompiler.Compile(Encoding.UTF8.GetBytes(text));
            }
            else
            {
                asset.bytes = Encoding.UTF8.GetBytes(text);
            }

            ctx.AddObjectToAsset("Main", asset);
            ctx.SetMainObject(asset);
        }
    }
}