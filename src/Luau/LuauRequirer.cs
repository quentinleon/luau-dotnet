namespace Luau;

public enum LuauRequirerNavigateResult
{
    Success,
    Ambiguous,
    NotFound,
};

public abstract class LuauRequirer
{
    public abstract bool IsRequireAllowed(LuauState state, string chunkName);
    public abstract LuauRequirerNavigateResult Reset(LuauState state, string chunkName);
    public abstract LuauRequirerNavigateResult JumpToAlias(LuauState state, string path);
    public abstract LuauRequirerNavigateResult MoveToParent(LuauState state);
    public abstract LuauRequirerNavigateResult MoveToChild(LuauState state, string name);
    public abstract bool IsModulePresent(LuauState state);
    public abstract bool IsConfigPresent(LuauState state);
    public abstract bool TryGetChunkName(LuauState state, Span<byte> destination, out int bytesWritten);
    public abstract bool TryGetLoadName(LuauState state, Span<byte> destination, out int bytesWritten);
    public abstract bool TryGetCacheKey(LuauState state, Span<byte> destination, out int bytesWritten);
    public abstract bool TryGetConfig(LuauState state, Span<byte> destination, out int bytesWritten);
    public abstract int Load(LuauState state, string path, string chunkName, string loadName);

    public virtual void OnError(Exception exception)
    {
        Console.WriteLine(exception);
    }
}