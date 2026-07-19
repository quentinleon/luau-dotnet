using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Luau.ArtifactFuzz;

static class ReproducerStore
{
    const int AtomicReplaceAttempts = 8;
    const string CheckpointBinaryName = "current-input.bin";
    const string CheckpointReportName = "current-input.txt";

    public static void Checkpoint(
        string directory,
        byte[] input,
        ReproducerContext context)
    {
        Directory.CreateDirectory(directory);
        WriteAtomically(Path.Combine(directory, CheckpointBinaryName), input);
        var report = new StringBuilder()
            .AppendLine("Luau artifact parser fuzz in-flight checkpoint")
            .Append("length: ").AppendLine(input.Length.ToString(CultureInfo.InvariantCulture))
            .Append("runSeed: 0x").AppendLine(context.RunSeed.ToString("x16", CultureInfo.InvariantCulture))
            .Append("baseSeed: ").AppendLine(context.BaseSeed)
            .Append("iteration: ").AppendLine(
                context.Iteration?.ToString(CultureInfo.InvariantCulture) ?? "corpus/replay")
            .AppendLine()
            .AppendLine("This input is replaced before each evaluation and retained if the process hangs, crashes, or is killed.")
            .AppendLine("Replay from the repository root:")
            .Append("dotnet run --project fuzz/Luau.ArtifactFuzz -c Release -- --input \"")
            .Append(Path.Combine(directory, CheckpointBinaryName))
            .AppendLine("\"")
            .ToString();
        WriteAtomically(
            Path.Combine(directory, CheckpointReportName),
            Encoding.UTF8.GetBytes(report));
    }

    public static void ClearCheckpoint(string directory)
    {
        foreach (var name in new[] { CheckpointBinaryName, CheckpointReportName })
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    public static string Save(
        string directory,
        byte[] input,
        ReproducerContext context,
        Exception exception)
    {
        Directory.CreateDirectory(directory);
        var digest = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        var basePath = Path.Combine(directory, $"artifact-{digest}");
        var binaryPath = basePath + ".bin";
        var reportPath = basePath + ".txt";

        WriteAtomically(binaryPath, input);
        var report = new StringBuilder()
            .AppendLine("Luau artifact parser fuzz reproducer")
            .Append("sha256: ").AppendLine(digest)
            .Append("length: ").AppendLine(input.Length.ToString(CultureInfo.InvariantCulture))
            .Append("runSeed: 0x").AppendLine(context.RunSeed.ToString("x16", CultureInfo.InvariantCulture))
            .Append("baseSeed: ").AppendLine(context.BaseSeed)
            .Append("iteration: ").AppendLine(
                context.Iteration?.ToString(CultureInfo.InvariantCulture) ?? "corpus/replay")
            .AppendLine()
            .AppendLine("Replay from the repository root:")
            .Append("dotnet run --project fuzz/Luau.ArtifactFuzz -c Release -- --input \"")
            .Append(binaryPath)
            .AppendLine("\"")
            .AppendLine()
            .AppendLine("Unexpected exception:")
            .AppendLine(exception.ToString())
            .ToString();
        WriteAtomically(reportPath, Encoding.UTF8.GetBytes(report));
        return binaryPath;
    }

    static void WriteAtomically(string destination, byte[] content)
    {
        var temporary = destination + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, content);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(temporary, destination, overwrite: true);
                    break;
                }
                catch (Exception exception) when (
                    attempt < AtomicReplaceAttempts - 1 &&
                    exception is IOException or UnauthorizedAccessException)
                {
                    // Windows scanners can briefly hold the destination without
                    // delete sharing. Keep the already-written temporary file and
                    // retry the same atomic replacement instead of losing the
                    // in-flight input to a transient sharing violation.
                    Thread.Sleep(Math.Min(1 << attempt, 64));
                }
            }
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (IOException)
            {
                // Do not mask the replacement failure with best-effort cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // A scanner may still hold the temporary file momentarily.
            }
        }
    }
}

readonly record struct ReproducerContext(ulong RunSeed, string BaseSeed, int? Iteration);
