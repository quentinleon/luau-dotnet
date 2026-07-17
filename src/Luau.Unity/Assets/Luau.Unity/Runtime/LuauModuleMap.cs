using System;
using System.Collections.Generic;
using System.Text;

namespace Luau.Unity
{
    /// <summary>
    /// Provides one immutable, source-only module namespace to one Luau VM.
    /// Hosts should load and validate a mod package before constructing this
    /// map; module execution never performs filesystem, Resources, or
    /// Addressables I/O.
    /// </summary>
    public sealed class LuauModuleMap : LuauRequirer
    {
        readonly Dictionary<string, byte[]> modules;
        readonly Dictionary<string, string> aliases;

        public LuauModuleMap(
            IReadOnlyDictionary<string, byte[]> modules,
            IReadOnlyDictionary<string, string> aliases = null)
        {
            if (modules == null)
            {
                throw new ArgumentNullException(nameof(modules));
            }

            this.modules = new Dictionary<string, byte[]>(
                modules.Count,
                StringComparer.Ordinal);
            foreach (var pair in modules)
            {
                var moduleId = CanonicalizeModuleId(pair.Key);
                var source = pair.Value
                    ?? throw new ArgumentException(
                        $"Module '{pair.Key}' has no source payload.",
                        nameof(modules));

                if (!this.modules.TryAdd(moduleId, (byte[])source.Clone()))
                {
                    throw new ArgumentException(
                        $"More than one module maps to canonical module ID '{moduleId}'.",
                        nameof(modules));
                }
            }

            this.aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            if (aliases == null)
            {
                return;
            }

            foreach (var pair in aliases)
            {
                ValidateAlias(pair.Key);
                if (pair.Value == null)
                {
                    throw new ArgumentException(
                        $"Module alias '{pair.Key}' has no target path.",
                        nameof(aliases));
                }

                this.aliases.Add(
                    pair.Key,
                    CanonicalizePath(pair.Value, allowEmpty: true));
            }
        }

        /// <summary>
        /// Converts equivalent require paths to one module identity. Leading
        /// slashes and <c>./</c> segments are removed, separators are
        /// normalized, and one terminal <c>.luau</c> extension is stripped.
        /// Parent traversal is rejected.
        /// </summary>
        public static string CanonicalizeModuleId(string moduleId)
        {
            return CanonicalizePath(moduleId, allowEmpty: false);
        }

        protected override string GetCacheKey(string path)
        {
            return CanonicalizeModuleId(path);
        }

        protected override bool TryLoadModule(
            LuauState state,
            string fullPath,
            string requireArgument,
            out LuauValue result)
        {
            var moduleId = CanonicalizeModuleId(fullPath);
            if (!modules.TryGetValue(moduleId, out var source))
            {
                result = default;
                return false;
            }

            var chunkName = Encoding.UTF8.GetBytes($"@modules/{moduleId}.luau");
            result = ExecuteModuleSource(state, requireArgument, source, chunkName);
            return true;
        }

        protected override bool TryGetAliasPath(string alias, out string path)
        {
            return aliases.TryGetValue(alias, out path);
        }

        static string CanonicalizePath(string path, bool allowEmpty)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (path.IndexOf('\0') >= 0)
            {
                throw new ArgumentException(
                    "A module ID cannot contain a NUL character.",
                    nameof(path));
            }

            var normalized = path.Replace('\\', '/');
            var segments = normalized.Split('/');
            var canonicalSegments = new List<string>(segments.Length);
            foreach (var segment in segments)
            {
                if (segment.Length == 0 || segment == ".")
                {
                    continue;
                }

                if (segment == "..")
                {
                    throw new ArgumentException(
                        "A module ID cannot traverse to a parent namespace.",
                        nameof(path));
                }

                canonicalSegments.Add(segment);
            }

            var result = string.Join("/", canonicalSegments);
            const string extension = ".luau";
            if (result.EndsWith(extension, StringComparison.Ordinal))
            {
                result = result.Substring(0, result.Length - extension.Length);
            }

            if (result.Length > 0 && result[0] == '@')
            {
                throw new ArgumentException(
                    "A module ID cannot use alias syntax unless that alias is explicitly configured.",
                    nameof(path));
            }

            if (!allowEmpty && result.Length == 0)
            {
                throw new ArgumentException(
                    "A module ID must contain at least one path segment.",
                    nameof(path));
            }

            return result;
        }

        static void ValidateAlias(string alias)
        {
            if (string.IsNullOrEmpty(alias) ||
                alias.IndexOf('/') >= 0 ||
                alias.IndexOf('\\') >= 0 ||
                alias.IndexOf('\0') >= 0 ||
                alias[0] == '@')
            {
                throw new ArgumentException(
                    "A module alias must be a non-empty name without separators, NUL characters, or a leading '@'.",
                    nameof(alias));
            }
        }
    }
}
