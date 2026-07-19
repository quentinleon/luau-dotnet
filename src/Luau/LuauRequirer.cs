using System.Diagnostics.CodeAnalysis;

namespace Luau;

/// <summary>
/// Defines an opt-in managed <c>require</c> resolver. Each instance has an
/// isolated VM cache namespace; implementations must resolve only explicitly
/// authorized modules and must not use ambient filesystem or network access.
/// </summary>
public abstract class LuauRequirer
{
    static long nextResolverIdentity;
    readonly long resolverIdentity = Interlocked.Increment(ref nextResolverIdentity);

    internal bool TryLoad(LuauState state, string argument, out LuauValue result)
    {
        var fullPath = AliasToPath(argument);
        var cacheKey = $"{resolverIdentity:x16}:{GetCacheKey(fullPath)}";

        if (state.Context.TryGetCachedModule(cacheKey, out result))
        {
            return true;
        }

        state.Context.BeginModuleLoad(cacheKey, argument);
        try
        {
            var root = state.GetMainThread();
            // Module closures are cached for the entire VM. Never compile them in
            // the first requesting sandbox's global proxy, or cached closures can
            // retain that caller's private globals and leak them to siblings.
            using var thread = root.IsRootSandboxed
                ? root.CreateSandboxedThread()
                : root.CreateThread();
            if (!TryLoadModule(thread, fullPath, argument, out var moduleResult))
            {
                result = default;
                return false;
            }
            if (moduleResult.Type == LuauType.Thread)
            {
                moduleResult.DisposeUnpublishedReference();
                throw CreateThreadModuleResultException(argument);
            }

            var rootTop = root.GetTop();
            try
            {
                // Normalize registry-backed values onto the VM root before the
                // short-lived module thread is disposed. Primitive values take
                // the same path so resolvers never manage stack ownership.
                thread.Push(moduleResult);
                thread.XMove(root, 1);
                var normalized = root.Pop();
                try
                {
                    state.Context.CacheModule(cacheKey, normalized);
                }
                catch
                {
                    normalized.DisposeUnpublishedReference();
                    throw;
                }

                result = normalized;
                return true;
            }
            finally
            {
                root.SetTop(rootTop);
                moduleResult.DisposeOwnedReference();
            }
        }
        finally
        {
            state.Context.EndModuleLoad(cacheKey);
        }
    }

    /// <summary>
    /// Attempts to load one resolved module on a temporary module thread.
    /// Return exactly one owned value on success; the base resolver normalizes
    /// and caches it before releasing both the value and temporary thread.
    /// </summary>
    /// <param name="state">The temporary module thread; do not retain it.</param>
    /// <param name="fullPath">The alias-resolved logical module path.</param>
    /// <param name="requireArgument">The original script argument for diagnostics.</param>
    /// <param name="result">Receives the owned module result on success.</param>
    /// <returns><see langword="true"/> when the module was found and evaluated.</returns>
    protected abstract bool TryLoadModule(
        LuauState state,
        string fullPath,
        string requireArgument,
        out LuauValue result);

    /// <summary>Attempts to map an explicit <c>@alias</c> prefix to a logical module path.</summary>
    /// <param name="alias">The alias name without the leading <c>@</c>.</param>
    /// <param name="path">Receives the mapped logical prefix when found.</param>
    /// <returns><see langword="true"/> when the alias is configured.</returns>
    protected abstract bool TryGetAliasPath(string alias, [NotNullWhen(true)] out string? path);

    /// <summary>
    /// Compiles and evaluates already-admitted strict UTF-8 module source and
    /// returns its single owned result. Implementations accepting untrusted
    /// source must enforce finite source and compiler-output limits first.
    /// </summary>
    protected static LuauValue ExecuteModuleSource(
        LuauState state,
        string requireArgument,
        ReadOnlySpan<byte> utf8Source,
        ReadOnlySpan<byte> utf8ChunkName = default,
        LuauCompileOptions? options = null)
    {
        return GetModuleResult(
            requireArgument,
            state.DoStringForRequire(utf8Source, utf8ChunkName, options));
    }

    /// <summary>
    /// Evaluates same-process compiler output and returns its single owned
    /// result. Compiler output is capability-bearing and cannot be persisted or
    /// reconstructed by untrusted callers.
    /// </summary>
    protected static LuauValue ExecuteModuleCompilerOutput(
        LuauState state,
        string requireArgument,
        LuauCompilerOutput output,
        ReadOnlySpan<byte> utf8ChunkName = default)
    {
        return GetModuleResult(
            requireArgument,
            state.DoCompilerOutputForRequire(output, utf8ChunkName));
    }

    /// <summary>
    /// Validates and evaluates a persistent bytecode artifact and returns its
    /// single owned result. Artifact parsing alone does not grant trust; the
    /// state's configured provenance validator must accept it.
    /// </summary>
    protected static LuauValue ExecuteVerifiedModuleBytecode(
        LuauState state,
        string requireArgument,
        LuauBytecodeArtifact artifact,
        ReadOnlySpan<byte> utf8ChunkName = default)
    {
        return GetModuleResult(
            requireArgument,
            state.DoVerifiedBytecodeForRequire(artifact, utf8ChunkName));
    }

    /// <summary>
    /// Gets the deterministic cache key within this resolver instance. Override
    /// when one logical path can denote distinct immutable revisions.
    /// </summary>
    /// <param name="path">The alias-resolved logical module path.</param>
    protected virtual string GetCacheKey(string path) => path;

    string AliasToPath(string alias)
    {
        if (alias.Length <= 1 || alias[0] is not '@')
        {
            return alias;
        }

        var index = alias.IndexOf('/');

        var key = index == -1
            ? alias[1..]
            : alias[1..index];

        if (!TryGetAliasPath(key, out var path))
        {
            return alias;
        }

        return index == -1
            ? path
            : $"{path}{alias[index..]}";
    }

    static LuauValue GetModuleResult(string requireArgument, LuauResultScope results)
    {
        using (results)
        {
            if (results.Length != 1)
            {
                throw new LuauException(
                    $"Module '{requireArgument}' does not return exactly 1 value. It cannot be required.");
            }

            var result = results.Detach(0);
            if (result.Type == LuauType.Thread)
            {
                result.DisposeUnpublishedReference();
                throw CreateThreadModuleResultException(requireArgument);
            }
            return result;
        }
    }

    static LuauException CreateThreadModuleResultException(string requireArgument) =>
        new(
            $"Module '{requireArgument}' cannot return a Luau thread because thread wrappers are shared per VM and cannot be independently owned by the module cache.");
}
