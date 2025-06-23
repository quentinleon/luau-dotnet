using UnityEngine;

namespace Luau.Unity
{
    public sealed class ResourcesLuauRequirer : LuauRequirer
    {
        public static readonly ResourcesLuauRequirer Default = new();

        protected override bool TryLoadModule(LuauState state, string fullPath, string requireArgument)
        {
            fullPath = fullPath.Replace(".luau", "");
            if (fullPath.StartsWith('/')) fullPath = fullPath[1..];
            else if (fullPath.StartsWith("./")) fullPath = fullPath[2..];

            var asset = Resources.Load<LuauAsset>(fullPath);
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

        protected override bool TryGetAliasPath(string alias, out string path)
        {
            path = "";
            return true;
        }
    }
}
