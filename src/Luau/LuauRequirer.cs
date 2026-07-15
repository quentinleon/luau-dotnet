using System.Diagnostics.CodeAnalysis;

namespace Luau;

public abstract class LuauRequirer
{
    public bool TryLoad(LuauState state, string argument)
    {
        var fullPath = AliasToPath(argument);
        var cacheKey = GetCacheKey(fullPath);

        if (state.Context.TryGetCachedModule(cacheKey, out var result))
        {
            state.Push(result);
            return true;
        }

        var root = state.GetMainThread();
        // Module closures are cached for the entire VM. Never compile them in
        // the first requesting sandbox's global proxy, or cached closures can
        // retain that caller's private globals and leak them to siblings.
        using var thread = root.IsRootSandboxed
            ? root.CreateSandboxedThread()
            : root.CreateThread();
        if (!TryLoadModule(thread, fullPath, argument))
        {
            return false;
        }

        thread.XMove(root, 1);
        var cachedResult = root.ToValue(-1);
        state.Context.CacheModule(cacheKey, cachedResult);

        if (!ReferenceEquals(root, state))
        {
            root.XMove(state, 1);
        }

        return true;
    }

    protected abstract bool TryLoadModule(LuauState state, string fullPath, string requireArgument);
    protected abstract bool TryGetAliasPath(string alias, [NotNullWhen(true)] out string? path);

    protected static LuauValue[] ExecuteModuleSource(
        LuauState state,
        ReadOnlySpan<byte> utf8Source,
        ReadOnlySpan<byte> utf8ChunkName = default,
        LuauCompileOptions? options = null)
    {
        return state.DoStringForRequire(utf8Source, utf8ChunkName, options);
    }

    protected static LuauValue[] ExecuteTrustedModuleBytecode(
        LuauState state,
        ReadOnlySpan<byte> bytecode,
        ReadOnlySpan<byte> utf8ChunkName = default)
    {
        return state.DoBytecodeForRequire(bytecode, utf8ChunkName, trustedCompilerOutput: true);
    }

    protected static LuauValue[] ExecuteModuleBytecode(
        LuauState state,
        ReadOnlySpan<byte> bytecode,
        ReadOnlySpan<byte> utf8ChunkName = default)
    {
        return state.DoBytecodeForRequire(bytecode, utf8ChunkName, trustedCompilerOutput: false);
    }

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

        return $"{path}{alias[index..]}";
    }
}
