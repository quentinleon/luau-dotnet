using System.Diagnostics.CodeAnalysis;

namespace Luau;

public abstract class LuauRequirer
{
    internal bool TryLoad(LuauState state, string argument, out LuauValue result)
    {
        var fullPath = AliasToPath(argument);
        var cacheKey = GetCacheKey(fullPath);

        if (state.Context.TryGetCachedModule(cacheKey, out result))
        {
            return true;
        }

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

        var rootTop = root.GetTop();
        try
        {
            // Normalize registry-backed values onto the VM root before the
            // short-lived module thread is disposed. Primitive values take
            // the same path so resolvers never manage stack ownership.
            thread.Push(moduleResult);
            thread.XMove(root, 1);
            result = root.Pop();
            state.Context.CacheModule(cacheKey, result);
            return true;
        }
        finally
        {
            root.SetTop(rootTop);
        }
    }

    protected abstract bool TryLoadModule(
        LuauState state,
        string fullPath,
        string requireArgument,
        out LuauValue result);

    protected abstract bool TryGetAliasPath(string alias, [NotNullWhen(true)] out string? path);

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

    protected static LuauValue ExecuteTrustedModuleBytecode(
        LuauState state,
        string requireArgument,
        ReadOnlySpan<byte> bytecode,
        ReadOnlySpan<byte> utf8ChunkName = default)
    {
        return GetModuleResult(
            requireArgument,
            state.DoBytecodeForRequire(bytecode, utf8ChunkName, trustedCompilerOutput: true));
    }

    protected static LuauValue ExecuteModuleBytecode(
        LuauState state,
        string requireArgument,
        ReadOnlySpan<byte> bytecode,
        ReadOnlySpan<byte> utf8ChunkName = default)
    {
        return GetModuleResult(
            requireArgument,
            state.DoBytecodeForRequire(bytecode, utf8ChunkName, trustedCompilerOutput: false));
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

        return index == -1
            ? path
            : $"{path}{alias[index..]}";
    }

    static LuauValue GetModuleResult(string requireArgument, LuauValue[] results)
    {
        if (results.Length != 1)
        {
            throw new LuauException(
                $"Module '{requireArgument}' does not return exactly 1 value. It cannot be required.");
        }

        return results[0];
    }
}
