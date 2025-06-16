using System.Runtime.CompilerServices;
using System.Text;

namespace Luau;

public sealed class FileSystemLuauRequirer : LuauRequirer
{
    readonly string initDirectory;
    string currentDirectory;
    static ReadOnlySpan<byte> ModuleCacheKey => "_MODULES"u8;

    public LuauCompileOptions? CompileOptions { get; init; }

    public FileSystemLuauRequirer([CallerFilePath] string filePath = "")
    {
        currentDirectory = filePath;
        initDirectory = currentDirectory;
    }

    public override bool IsConfigPresent(LuauState state)
    {
        return File.Exists(currentDirectory) && Path.GetFileName(currentDirectory) is ".luaurc";
    }

    public override bool IsModulePresent(LuauState state)
    {
        return File.Exists(currentDirectory) && Path.GetExtension(currentDirectory) is ".luau";
    }

    public override bool IsRequireAllowed(LuauState state, string chunkName)
    {
        return true;
    }

    public override LuauRequirerNavigateResult JumpToAlias(LuauState state, string path)
    {
        currentDirectory = Path.GetFullPath(path);
        return LuauRequirerNavigateResult.Success;
    }

    public override LuauRequirerNavigateResult MoveToChild(LuauState state, string name)
    {
        var files = Directory.GetFiles(currentDirectory);

        foreach (var file in files)
        {
            if (Path.GetFileNameWithoutExtension(file) == name)
            {
                currentDirectory = Path.GetFullPath(file)!;
                return LuauRequirerNavigateResult.Success;
            }
        }

        return LuauRequirerNavigateResult.NotFound;
    }

    public override LuauRequirerNavigateResult MoveToParent(LuauState state)
    {
        var info = Directory.GetParent(currentDirectory);
        if (info != null)
        {
            currentDirectory = info.FullName;
            return LuauRequirerNavigateResult.Success;
        }

        return LuauRequirerNavigateResult.NotFound;
    }

    public override LuauRequirerNavigateResult Reset(LuauState state, string chunkName)
    {
        currentDirectory = initDirectory;
        return LuauRequirerNavigateResult.Success;
    }

    public override bool TryGetCacheKey(LuauState state, Span<byte> destination, out int bytesWritten)
    {
        var cacheKey = currentDirectory;

        var count = Encoding.UTF8.GetByteCount(cacheKey);
        if (count > destination.Length)
        {
            bytesWritten = 0;
            return false;
        }

        bytesWritten = Encoding.UTF8.GetBytes(cacheKey, destination);
        return true;
    }

    public override bool TryGetChunkName(LuauState state, Span<byte> destination, out int bytesWritten)
    {
        var chunkName = Path.GetFileNameWithoutExtension(currentDirectory);

        var count = Encoding.UTF8.GetByteCount(chunkName);
        if (count > destination.Length)
        {
            bytesWritten = 0;
            return false;
        }

        bytesWritten = Encoding.UTF8.GetBytes(chunkName, destination);
        return true;
    }

    public override bool TryGetConfig(LuauState state, Span<byte> destination, out int bytesWritten)
    {
        var fileInfo = new FileInfo(currentDirectory);
        var length = fileInfo.Length;
        bytesWritten = 0;

        if (length > destination.Length)
        {
            return false;
        }

        using (var stream = File.Open(currentDirectory, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            int bytesRead;
            while ((bytesRead = stream.Read(destination)) > 0)
            {
                bytesWritten += bytesRead;
            }
        }

        return true;
    }

    public override bool TryGetLoadName(LuauState state, Span<byte> destination, out int bytesWritten)
    {
        return TryGetCacheKey(state, destination, out bytesWritten);
    }

    public override int Load(LuauState state, string path, string chunkName, string loadName)
    {
        var cacheTable = state[ModuleCacheKey];
        if (cacheTable.IsNil)
        {
            cacheTable = state[ModuleCacheKey] = state.CreateTable();
        }
        else
        {
            var cachedResult = cacheTable.Read<LuauTable>()[loadName];
            if (!cachedResult.IsNil)
            {
                state.Push(cachedResult);
                return 1;
            }
        }

        using var writer = new ArrayPoolBufferWriter(8192);
        using (var stream = File.Open(currentDirectory, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var span = writer.GetSpan(8192);
            int bytesRead;
            while (true)
            {
                bytesRead = stream.Read(span);
                if (bytesRead == 0) break;
                writer.Advance(bytesRead);
            }
        }

        var results = state.DoString(writer.WrittenSpan, CompileOptions);
        var loaded = results.Length == 0 ? default : results[0];
        cacheTable.Read<LuauTable>()[loadName] = loaded;
        state.Push(loaded);
        return results.Length;
    }
}