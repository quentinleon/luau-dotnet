using System.Diagnostics.CodeAnalysis;

namespace Luau;

public abstract class LuauRequirer
{
    static ReadOnlySpan<byte> Key => "_MODULES"u8;

    public bool TryLoad(LuauState state, string argument)
    {
        var cache = state[Key];

        LuauTable cacheTable;
        if (cache.IsNil)
        {
            cacheTable = state.CreateTable();
            state[Key] = cacheTable;
        }
        else
        {
            cacheTable = cache.Read<LuauTable>();
        }

        var fullPath = AliasToPath(argument);
        var cacheKey = GetCacheKey(fullPath);

        if (cacheTable.TryGetValue(cacheKey, out var result))
        {
            state.Push(result);
            return true;
        }

        var thread = state.CreateThread();
        if (!TryLoadModule(thread, fullPath, argument))
        {
            return false;
        }

        thread.XMove(state, 1);
        cacheTable.Add(cacheKey, state.ToValue(-1));
        return true;
    }

    protected abstract bool TryLoadModule(LuauState state, string fullPath, string requireArgument);
    protected abstract bool TryGetAliasPath(string alias, [NotNullWhen(true)] out string? path);

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