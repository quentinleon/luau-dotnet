namespace Luau;

public abstract class LuauRequirer
{
    static ReadOnlySpan<byte> Key => "_MODULES"u8;

    public bool TryLoad(LuauState state, string path)
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

            if (cacheTable.TryGetValue(GetCacheKey(path), out var result))
            {
                state.Push(result);
                return true;
            }

            var thread = state.CreateThread();
            LoadModule(thread, path);
            thread.XMove(state, 1);
            cacheTable.Add(GetCacheKey(path), state.ToValue(-1));
            return true;
        }
        catch (Exception ex)
        {
            OnError(ex);
        }

        return false;
    }

    protected abstract void LoadModule(LuauState state, string path);

    protected virtual string GetCacheKey(string path) => path;
    protected virtual void OnError(Exception ex)
    {
        Console.WriteLine(ex);
    }
}