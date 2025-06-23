using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Luau;

public abstract class LuauRequirer
{
    static ReadOnlySpan<byte> Key => "_MODULES"u8;

    public bool TryLoad(LuauState state, string argument)
    {
        try
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
            LoadModule(thread, fullPath, argument);
            thread.XMove(state, 1);
            cacheTable.Add(cacheKey, state.ToValue(-1));
            return true;
        }
        catch (Exception ex)
        {
            OnError(ex);
        }

        return false;
    }

    protected abstract void LoadModule(LuauState state, string fullPath, string requireArgument);
    protected abstract bool TryGetAliasPath(string alias, [NotNullWhen(true)] out string? path);

    protected virtual string GetCacheKey(string path) => path;
    protected virtual void OnError(Exception ex)
    {
        Console.WriteLine(ex);
    }

    string AliasToPath(string alias)
    {
        Debug.Assert(alias.Length > 1 && alias[0] is '@');
        var index = alias.IndexOf('/');

        var key = index == -1
            ? alias[1..]
            : alias[1..index];

        if (!TryGetAliasPath(key, out var path))
        {
            return alias;
        }

        return $"{path}/{alias[index..]}";
    }
}