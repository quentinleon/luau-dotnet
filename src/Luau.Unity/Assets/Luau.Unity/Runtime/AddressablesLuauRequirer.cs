#if LUAU_UNITY_ADDRESSABLES

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using UnityEngine.AddressableAssets;

namespace Luau.Unity
{
    public sealed class AddressablesLuauRequirer : LuauRequirer
    {
        public static readonly AddressablesLuauRequirer Default = new();

        public IDictionary<string, string> Aliases { get; init; } = new Dictionary<string, string>();

        protected override bool TryLoadModule(
            LuauState state,
            string fullPath,
            string requireArgument,
            out LuauValue result)
        {
            if (fullPath.StartsWith('/')) fullPath = fullPath[1..];
            else if (fullPath.StartsWith("./")) fullPath = fullPath[2..];

            var asset = Addressables.LoadAssetAsync<LuauAsset>(fullPath)
                .WaitForCompletion();

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

        protected override bool TryGetAliasPath(string alias, [NotNullWhen(true)] out string path)
        {
            return Aliases.TryGetValue(alias, out path);
        }
    }
}

#endif
