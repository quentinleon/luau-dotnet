using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Luau;

public sealed class FileSystemLuauRequirer : LuauRequirer
{
    public static readonly FileSystemLuauRequirer Default = new();

    public LuauCompileOptions? CompileOptions { get; init; }
    public string? WorkingDirectory { get; init; }
    public string? ConfigFilePath { get; init; }

    Dictionary<string, string>? aliases;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    string GetWorkingDirectoryOrDefault()
    {
        return WorkingDirectory ?? Directory.GetCurrentDirectory();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    string GetConfigFilePathOrDefault()
    {
        return ConfigFilePath ?? Path.Combine(GetWorkingDirectoryOrDefault(), ".luaurc");
    }

    protected override bool TryLoadModule(LuauState state, string fullPath, string requireArgument)
    {
        var targetPath = Path.IsPathRooted(fullPath)
            ? fullPath
            : Path.GetRelativePath(GetWorkingDirectoryOrDefault(), fullPath);

        if (!Path.HasExtension(targetPath)) targetPath += ".luau";

        if (!File.Exists(targetPath))
        {
            return false;
        }

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

        var targetFileName = Path.GetFileNameWithoutExtension(targetPath);
        Span<byte> utf8ChunkName = stackalloc byte[targetFileName.Length * 3];
        var utf8ChunkNameCount = Encoding.UTF8.GetBytes(targetFileName, utf8ChunkName);

        var results = state.DoString(writer.WrittenSpan, utf8ChunkName[..utf8ChunkNameCount], CompileOptions);
        if (results.Length != 1)
        {
            throw new LuauException($"Module '{requireArgument}' does not return exactly 1 value. It cannot be required.");
        }
        state.Push(results[0]);

        return true;
    }

    protected override string GetCacheKey(string path)
    {
        var targetPath = Path.IsPathRooted(path)
            ? path
            : Path.GetRelativePath(GetWorkingDirectoryOrDefault(), path);

        return Path.GetFullPath(targetPath);
    }

    protected override bool TryGetAliasPath(string alias, [NotNullWhen(true)] out string? path)
    {
        if (aliases == null)
        {
            var stream = File.Open(GetConfigFilePathOrDefault(), FileMode.Open, FileAccess.Read, FileShare.Read);
            var json = JsonDocument.Parse(stream).RootElement;

            if (json.TryGetProperty("aliases", out var aliasesElement))
            {
                aliases = aliasesElement.Deserialize(DictionaryJsonSerializeContext.Default.DictionaryStringString)!;
            }
            else
            {
                aliases = [];
            }
        }

        return aliases.TryGetValue(alias, out path);
    }
}