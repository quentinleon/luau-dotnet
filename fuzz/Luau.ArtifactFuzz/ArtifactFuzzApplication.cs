using System.Globalization;

namespace Luau.ArtifactFuzz;

static class ArtifactFuzzApplication
{
    public static int Run(string[] args)
    {
        FuzzOptions options;
        try
        {
            options = FuzzOptions.Parse(args);
        }
        catch (FuzzUsageException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine();
            WriteUsage(Console.Error);
            return 2;
        }

        if (options.ShowHelp)
        {
            WriteUsage(Console.Out);
            return 0;
        }

        try
        {
            return new ArtifactFuzzEngine(options).Run();
        }
        catch (FuzzConfigurationException exception)
        {
            Console.Error.WriteLine($"artifact-fuzz configuration error: {exception.Message}");
            return 2;
        }
    }

    static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("Bounded hostile-input target for LuauBytecodeArtifactCodec.");
        writer.WriteLine();
        writer.WriteLine("Usage:");
        writer.WriteLine("  dotnet run --project fuzz/Luau.ArtifactFuzz -- --smoke [options]");
        writer.WriteLine("  dotnet run --project fuzz/Luau.ArtifactFuzz -- --input <reproducer.bin> [options]");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  --smoke                 Run the deterministic bounded corpus/mutation pass (default).");
        writer.WriteLine("  --iterations <count>    Mutation count, 0 through 1,000,000 (default: 10,000).");
        writer.WriteLine("  --seed <value>          Decimal or 0x-prefixed 64-bit seed (default: 0x6a09e667f3bcc909).");
        writer.WriteLine("  --corpus <directory>    Additional .hex/.bin seed directory (default: copied Corpus).");
        writer.WriteLine("  --reproducers <dir>     Unexpected-failure output (default: artifacts/artifact-fuzz-reproducers).");
        writer.WriteLine("  --input <path>          Replay exactly one bounded .bin or .hex input.");
        writer.WriteLine("  --help                   Show this help.");
    }
}

sealed record FuzzOptions(
    int Iterations,
    ulong Seed,
    string CorpusDirectory,
    string ReproducerDirectory,
    string? InputPath,
    bool ShowHelp)
{
    const int DefaultIterations = 10_000;
    const int MaximumIterations = 1_000_000;
    const ulong DefaultSeed = 0x6a09e667f3bcc909UL;

    public static FuzzOptions Parse(string[] args)
    {
        var iterations = DefaultIterations;
        var seed = DefaultSeed;
        var corpusDirectory = Path.Combine(AppContext.BaseDirectory, "Corpus");
        var reproducerDirectory = Path.Combine(
            Environment.CurrentDirectory,
            "artifacts",
            "artifact-fuzz-reproducers");
        string? inputPath = null;
        var showHelp = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--smoke":
                    break;
                case "--iterations":
                    iterations = ParseIterations(RequireValue(args, ref index, "--iterations"));
                    break;
                case "--seed":
                    seed = ParseSeed(RequireValue(args, ref index, "--seed"));
                    break;
                case "--corpus":
                    corpusDirectory = RequireValue(args, ref index, "--corpus");
                    break;
                case "--reproducers":
                    reproducerDirectory = RequireValue(args, ref index, "--reproducers");
                    break;
                case "--input":
                    inputPath = RequireValue(args, ref index, "--input");
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                default:
                    throw new FuzzUsageException($"Unknown option '{args[index]}'.");
            }
        }

        return new(
            iterations,
            seed,
            Path.GetFullPath(corpusDirectory),
            Path.GetFullPath(reproducerDirectory),
            inputPath == null ? null : Path.GetFullPath(inputPath),
            showHelp);
    }

    static string RequireValue(string[] args, ref int index, string option)
    {
        index++;
        if (index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
        {
            throw new FuzzUsageException($"{option} requires a value.");
        }

        return args[index];
    }

    static int ParseIterations(string value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) ||
            result < 0 ||
            result > MaximumIterations)
        {
            throw new FuzzUsageException(
                $"--iterations must be between 0 and {MaximumIterations:N0}.");
        }

        return result;
    }

    static ulong ParseSeed(string value)
    {
        var style = NumberStyles.None;
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            value = value[2..];
            style = NumberStyles.AllowHexSpecifier;
        }

        if (!ulong.TryParse(value, style, CultureInfo.InvariantCulture, out var result))
        {
            throw new FuzzUsageException("--seed must be an unsigned decimal or 0x-prefixed value.");
        }

        return result;
    }
}

sealed class FuzzUsageException(string message) : Exception(message);

sealed class FuzzConfigurationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
