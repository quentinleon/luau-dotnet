using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Luau.Unity
{
    public sealed class ResourcesLuauRequirer : LuauRequirer
    {
        public static readonly ResourcesLuauRequirer Default = new();

        public IDictionary<string, string> Aliases { get; init; } = new Dictionary<string, string>();

        protected override bool TryLoadModule(
            LuauState state,
            string fullPath,
            string requireArgument,
            out LuauValue result)
        {
            fullPath = fullPath.Replace(".luau", "");
            if (fullPath.StartsWith('/')) fullPath = fullPath[1..];
            else if (fullPath.StartsWith("./")) fullPath = fullPath[2..];

            var asset = Resources.Load<LuauAsset>(fullPath);
            if (asset == null)
            {
                result = default;
                return false;
            }

            var chunkName = Encoding.UTF8.GetBytes(asset.name);
            result = asset.IsPrecompiled
                ? ExecuteModuleBytecode(state, requireArgument, asset.AsSpan(), chunkName)
                : ExecuteModuleSource(state, requireArgument, asset.AsSpan(), chunkName);
            return true;
        }

        protected override bool TryGetAliasPath(string alias, out string path)
        {
            return Aliases.TryGetValue(alias, out path);
        }
    }
}
