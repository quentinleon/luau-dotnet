using System.Text;

namespace Luau.ArtifactFuzz;

static class CorpusLoader
{
    public static IReadOnlyList<ArtifactSeed> LoadDirectory(
        string directory,
        int maximumInputBytes,
        int maximumCorpusTextBytes,
        long maximumCorpusBytes)
    {
        if (!Directory.Exists(directory))
        {
            throw new FuzzConfigurationException($"Corpus directory does not exist: {directory}");
        }

        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path =>
                path.EndsWith(".hex", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        long totalBytes = 0;
        var seeds = new List<ArtifactSeed>(files.Length);
        foreach (var path in files)
        {
            var fileLength = new FileInfo(path).Length;
            try
            {
                totalBytes = checked(totalBytes + fileLength);
            }
            catch (OverflowException exception)
            {
                throw new FuzzConfigurationException("Corpus size overflowed Int64.", exception);
            }

            if (totalBytes > maximumCorpusBytes)
            {
                throw new FuzzConfigurationException(
                    $"Corpus exceeds the {maximumCorpusBytes}-byte aggregate cap.");
            }

            var data = Load(path, maximumInputBytes, maximumCorpusTextBytes);
            var name = Path.GetRelativePath(directory, path).Replace('\\', '/');
            seeds.Add(new($"corpus/{name}", data, RequiresSuccessfulParse: false));
        }

        return seeds;
    }

    public static byte[] LoadInput(string path, int maximumInputBytes)
    {
        if (!File.Exists(path))
        {
            throw new FuzzConfigurationException($"Input file does not exist: {path}");
        }

        return Load(path, maximumInputBytes, (maximumInputBytes * 2) + (64 * 1024));
    }

    static byte[] Load(string path, int maximumInputBytes, int maximumCorpusTextBytes)
    {
        try
        {
            return path.EndsWith(".hex", StringComparison.OrdinalIgnoreCase)
                ? LoadHex(path, maximumInputBytes, maximumCorpusTextBytes)
                : LoadBinary(path, maximumInputBytes);
        }
        catch (FuzzConfigurationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new FuzzConfigurationException($"Could not load corpus input '{path}'.", exception);
        }
    }

    static byte[] LoadBinary(string path, int maximumInputBytes)
    {
        var length = new FileInfo(path).Length;
        if (length > maximumInputBytes)
        {
            throw new FuzzConfigurationException(
                $"Corpus input '{path}' is {length} bytes; the cap is {maximumInputBytes}.");
        }

        return File.ReadAllBytes(path);
    }

    static byte[] LoadHex(string path, int maximumInputBytes, int maximumCorpusTextBytes)
    {
        var fileLength = new FileInfo(path).Length;
        if (fileLength > maximumCorpusTextBytes)
        {
            throw new FuzzConfigurationException(
                $"Hex corpus input '{path}' exceeds the {maximumCorpusTextBytes}-byte text cap.");
        }

        var builder = new StringBuilder((int)fileLength);
        foreach (var line in File.ReadLines(path))
        {
            var comment = line.IndexOf('#');
            var content = comment < 0 ? line : line[..comment];
            foreach (var character in content)
            {
                if (!char.IsWhiteSpace(character))
                {
                    builder.Append(character);
                }
            }
        }

        if ((builder.Length & 1) != 0)
        {
            throw new FuzzConfigurationException($"Hex corpus input '{path}' has an odd digit count.");
        }
        if (builder.Length / 2 > maximumInputBytes)
        {
            throw new FuzzConfigurationException(
                $"Decoded hex corpus input '{path}' exceeds the {maximumInputBytes}-byte cap.");
        }

        try
        {
            return Convert.FromHexString(builder.ToString());
        }
        catch (FormatException exception)
        {
            throw new FuzzConfigurationException(
                $"Hex corpus input '{path}' contains a non-hex character.",
                exception);
        }
    }
}
