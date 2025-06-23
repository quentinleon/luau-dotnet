#if LUAU_UNITY_ADDRESSABLES

using System.Diagnostics.CodeAnalysis;
using UnityEngine.AddressableAssets;

namespace Luau.Unity
{
    public sealed class AddressablesLuauRequirer : LuauRequirer
    {
        public static readonly AddressablesLuauRequirer Default = new();

        protected override bool TryLoadModule(LuauState state, string fullPath, string requireArgument)
        {
            if (fullPath.StartsWith('/')) fullPath = fullPath[1..];
            else if (fullPath.StartsWith("./")) fullPath = fullPath[2..];

            var asset = Addressables.LoadAssetAsync<LuauAsset>(fullPath)
                .WaitForCompletion();

            if (asset == null)
            {
                return false;
            }

            var results = state.Execute(asset);

            if (results.Length != 1)
            {
                throw new LuauException($"Module '{requireArgument}' does not return exactly 1 value. It cannot be required.");
            }

            state.Push(results[0]);
            return true;
        }

        protected override bool TryGetAliasPath(string alias, [NotNullWhen(true)] out string path)
        {
            path = "";
            return true;
        }
    }
}

#endif