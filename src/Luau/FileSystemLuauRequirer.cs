namespace Luau;

public sealed class FileSystemLuauRequirer : LuauRequirer
{
    public static readonly FileSystemLuauRequirer Default = new();

    public LuauCompileOptions? CompileOptions { get; init; }
    public string WorkingDirectory { get; init; } = Directory.GetCurrentDirectory();

    protected override void LoadModule(LuauState state, string path)
    {
        var targetPath = Path.IsPathRooted(path)
            ? path
            : Path.GetRelativePath(WorkingDirectory, path);

        targetPath = Path.GetFileNameWithoutExtension(targetPath) + ".luau";

        using var writer = new ArrayPoolBufferWriter(8192);
        using (var stream = File.Open(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read))
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
        state.Push(results[0]);
    }

    protected override string GetCacheKey(string path)
    {
        var targetPath = Path.IsPathRooted(path)
            ? path
            : Path.GetRelativePath(WorkingDirectory, path);

        return Path.GetFullPath(targetPath);
    }
}