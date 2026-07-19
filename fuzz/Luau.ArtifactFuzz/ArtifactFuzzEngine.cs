using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Luau.ArtifactFuzz;

sealed class ArtifactFuzzEngine(FuzzOptions options)
{
    const int MaximumInputBytes = 1024 * 1024;
    const int MaximumCorpusTextBytes = (MaximumInputBytes * 2) + (64 * 1024);
    const long MaximumCorpusBytes = 16L * 1024 * 1024;

    static readonly int[] InterestingInt32 =
    [
        int.MinValue,
        -1,
        0,
        1,
        2,
        31,
        32,
        127,
        128,
        159,
        160,
        255,
        256,
        1024 * 1024,
        int.MaxValue,
    ];

    static readonly int[] HeaderOffsets = [8, 12, 16, 20, 24, 28, 112, 116, 120, 124];

    static readonly LuauArtifactLimits TargetLimits = new()
    {
        MaxEnvelopeBytes = MaximumInputBytes,
        MaxBytecodeBytes = 768 * 1024,
        MaxProvenanceBytes = 128 * 1024,
        MaxProvenanceIdBytes = 8 * 1024,
        MaxSourceIdentityBytes = 8 * 1024,
    };

    readonly FuzzStatistics statistics = new();

    public int Run()
    {
        if (options.InputPath != null)
        {
            var input = CorpusLoader.LoadInput(options.InputPath, MaximumInputBytes);
            if (!RunOne(input, $"replay/{Path.GetFileName(options.InputPath)}", iteration: null))
            {
                return 1;
            }
            ReproducerStore.ClearCheckpoint(options.ReproducerDirectory);
            return ReportSuccess("replay", seedCount: 1);
        }

        var seeds = LoadCorpus();
        if (seeds.Count == 0)
        {
            throw new FuzzConfigurationException("The hostile corpus is empty.");
        }

        Console.WriteLine(
            $"artifact-fuzz smoke: seeds={seeds.Count}, iterations={options.Iterations}, " +
            $"seed=0x{options.Seed:x16}, maxInput={MaximumInputBytes}");

        foreach (var seed in seeds)
        {
            if (!RunOne(seed.Data, seed.Name, iteration: null, seed.RequiresSuccessfulParse))
            {
                return 1;
            }
        }

        var mutationSeeds = seeds.Where(static seed => seed.ParticipatesInMutation).ToArray();
        if (mutationSeeds.Length == 0)
        {
            throw new FuzzConfigurationException("The hostile corpus has no mutation seeds.");
        }

        var random = new DeterministicRandom(options.Seed);
        var rollingInput = mutationSeeds[0].Data;
        for (var iteration = 0; iteration < options.Iterations; iteration++)
        {
            var useRollingInput = iteration != 0 && random.NextInt(3) == 0;
            var baseSeed = mutationSeeds[random.NextInt(mutationSeeds.Length)];
            var baseInput = useRollingInput ? rollingInput : baseSeed.Data;
            var input = Mutate(baseInput, mutationSeeds, ref random);
            if (!RunOne(input, useRollingInput ? "rolling-mutation" : baseSeed.Name, iteration))
            {
                return 1;
            }
            rollingInput = input;
        }

        if (statistics.MaximumInputLength != MaximumInputBytes)
        {
            throw new InvalidOperationException(
                $"The smoke corpus did not exercise the exact {MaximumInputBytes}-byte envelope limit.");
        }

        ReproducerStore.ClearCheckpoint(options.ReproducerDirectory);
        return ReportSuccess("smoke", seeds.Count);
    }

    IReadOnlyList<ArtifactSeed> LoadCorpus()
    {
        var seeds = new List<ArtifactSeed>(ArtifactSeedCorpus.CreateStructuralSeeds());
        foreach (var seed in CorpusLoader.LoadDirectory(
                     options.CorpusDirectory,
                     MaximumInputBytes,
                     MaximumCorpusTextBytes,
                     MaximumCorpusBytes))
        {
            seeds.Add(seed);
        }

        return seeds;
    }

    bool RunOne(
        byte[] input,
        string seedName,
        int? iteration,
        bool requiresSuccessfulParse = false)
    {
        var context = new ReproducerContext(options.Seed, seedName, iteration);
        try
        {
            ReproducerStore.Checkpoint(options.ReproducerDirectory, input, context);
            var outcome = Evaluate(input);
            if (requiresSuccessfulParse && !outcome.Success)
            {
                throw new InvalidOperationException(
                    $"The canonical valid seed was rejected as {outcome.FailureKind}. " +
                    "Update the reviewed artifact corpus after an intentional codec/ABI identity change.");
            }

            statistics.Record(input.Length, outcome);
            return true;
        }
        catch (Exception exception)
        {
            string? reproducer = null;
            try
            {
                reproducer = ReproducerStore.Save(
                    options.ReproducerDirectory,
                    input,
                    context,
                    exception);
            }
            catch (Exception saveException)
            {
                Console.Error.WriteLine(
                    $"artifact-fuzz could not preserve the reproducer: {saveException}");
            }

            Console.Error.WriteLine(
                $"artifact-fuzz unexpected failure for seed '{seedName}'" +
                (iteration.HasValue ? $" at iteration {iteration.Value}" : string.Empty) +
                ":");
            Console.Error.WriteLine(exception);
            if (reproducer != null)
            {
                Console.Error.WriteLine($"reproducer: {reproducer}");
            }

            return false;
        }
    }

    static ParseOutcome Evaluate(byte[] input)
    {
        var spanOutcome = Parse(() => LuauBytecodeArtifactCodec.Parse(input, TargetLimits));
        using var stream = new FragmentedReadStream(input);
        var streamOutcome = Parse(() => LuauBytecodeArtifactCodec.Parse(stream, TargetLimits));

        if (spanOutcome != streamOutcome)
        {
            throw new InvalidOperationException(
                $"Span/stream parser divergence: span={spanOutcome}, stream={streamOutcome}.");
        }

        return spanOutcome;
    }

    static ParseOutcome Parse(Func<LuauBytecodeArtifact> parse)
    {
        try
        {
            var artifact = parse();
            if (artifact.BytecodeLength <= 0 || artifact.BytecodeLength > MaximumInputBytes)
            {
                throw new InvalidOperationException(
                    $"A successful parse returned invalid bytecode length {artifact.BytecodeLength}.");
            }

            var canonical = LuauBytecodeArtifactCodec.Write(artifact, TargetLimits);
            return ParseOutcome.Accepted(LowerHex(SHA256.HashData(canonical)));
        }
        catch (LuauArtifactException exception)
        {
            if (!Enum.IsDefined(exception.FailureKind))
            {
                throw new InvalidOperationException(
                    $"The parser returned unknown typed failure {exception.FailureKind}.",
                    exception);
            }

            return ParseOutcome.Rejected(exception.FailureKind, exception.FieldName);
        }
    }

    static string LowerHex(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(bytes).ToLowerInvariant();

    static byte[] Mutate(
        byte[] seed,
        IReadOnlyList<ArtifactSeed> corpus,
        ref DeterministicRandom random)
    {
        return random.NextInt(10) switch
        {
            0 => FlipBit(seed, ref random),
            1 => ReplaceByte(seed, ref random),
            2 => Truncate(seed, ref random),
            3 => AppendBytes(seed, ref random),
            4 => InsertBytes(seed, ref random),
            5 => DeleteRange(seed, ref random),
            6 => OverwriteInt32(seed, ref random),
            7 => ArbitraryBytes(ref random),
            8 => Splice(seed, corpus[random.NextInt(corpus.Count)].Data, ref random),
            _ => RepeatRange(seed, ref random),
        };
    }

    static byte[] FlipBit(byte[] seed, ref DeterministicRandom random)
    {
        if (seed.Length == 0)
        {
            return [(byte)(1 << random.NextInt(8))];
        }

        var result = (byte[])seed.Clone();
        result[random.NextInt(result.Length)] ^= (byte)(1 << random.NextInt(8));
        return result;
    }

    static byte[] ReplaceByte(byte[] seed, ref DeterministicRandom random)
    {
        if (seed.Length == 0)
        {
            return [(byte)random.NextInt(256)];
        }

        var result = (byte[])seed.Clone();
        result[random.NextInt(result.Length)] = (byte)random.NextInt(256);
        return result;
    }

    static byte[] Truncate(byte[] seed, ref DeterministicRandom random)
    {
        if (seed.Length == 0)
        {
            return [];
        }

        return seed[..random.NextInt(seed.Length + 1)];
    }

    static byte[] AppendBytes(byte[] seed, ref DeterministicRandom random)
    {
        var count = Math.Min(random.NextInt(65), MaximumInputBytes - seed.Length);
        var result = new byte[seed.Length + count];
        seed.CopyTo(result, 0);
        random.Fill(result.AsSpan(seed.Length));
        return result;
    }

    static byte[] InsertBytes(byte[] seed, ref DeterministicRandom random)
    {
        var count = Math.Min(1 + random.NextInt(32), MaximumInputBytes - seed.Length);
        if (count == 0)
        {
            return (byte[])seed.Clone();
        }

        var offset = random.NextInt(seed.Length + 1);
        var result = new byte[seed.Length + count];
        seed.AsSpan(0, offset).CopyTo(result);
        random.Fill(result.AsSpan(offset, count));
        seed.AsSpan(offset).CopyTo(result.AsSpan(offset + count));
        return result;
    }

    static byte[] DeleteRange(byte[] seed, ref DeterministicRandom random)
    {
        if (seed.Length == 0)
        {
            return [];
        }

        var offset = random.NextInt(seed.Length);
        var count = 1 + random.NextInt(seed.Length - offset);
        var result = new byte[seed.Length - count];
        seed.AsSpan(0, offset).CopyTo(result);
        seed.AsSpan(offset + count).CopyTo(result.AsSpan(offset));
        return result;
    }

    static byte[] OverwriteInt32(byte[] seed, ref DeterministicRandom random)
    {
        var minimumLength = sizeof(int);
        var result = seed.Length < minimumLength ? new byte[minimumLength] : (byte[])seed.Clone();
        seed.CopyTo(result, 0);

        int offset;
        var headerOffset = HeaderOffsets[random.NextInt(HeaderOffsets.Length)];
        if (headerOffset <= result.Length - sizeof(int) && random.NextInt(4) != 0)
        {
            offset = headerOffset;
        }
        else
        {
            offset = random.NextInt(result.Length - sizeof(int) + 1);
        }

        BinaryPrimitives.WriteInt32LittleEndian(
            result.AsSpan(offset, sizeof(int)),
            InterestingInt32[random.NextInt(InterestingInt32.Length)]);
        return result;
    }

    static byte[] ArbitraryBytes(ref DeterministicRandom random)
    {
        var length = random.NextInt(4097);
        var result = new byte[length];
        random.Fill(result);
        return result;
    }

    static byte[] Splice(byte[] left, byte[] right, ref DeterministicRandom random)
    {
        var leftLength = left.Length == 0 ? 0 : random.NextInt(left.Length + 1);
        var remaining = MaximumInputBytes - leftLength;
        var rightOffset = right.Length == 0 ? 0 : random.NextInt(right.Length + 1);
        var available = right.Length - rightOffset;
        var rightLength = Math.Min(available, remaining);
        if (rightLength > 0)
        {
            rightLength = random.NextInt(rightLength + 1);
        }

        var result = new byte[leftLength + rightLength];
        left.AsSpan(0, leftLength).CopyTo(result);
        right.AsSpan(rightOffset, rightLength).CopyTo(result.AsSpan(leftLength));
        return result;
    }

    static byte[] RepeatRange(byte[] seed, ref DeterministicRandom random)
    {
        if (seed.Length == 0 || seed.Length == MaximumInputBytes)
        {
            return (byte[])seed.Clone();
        }

        var offset = random.NextInt(seed.Length);
        var count = 1 + random.NextInt(Math.Min(64, seed.Length - offset));
        count = Math.Min(count, MaximumInputBytes - seed.Length);
        var destination = random.NextInt(seed.Length + 1);
        var result = new byte[seed.Length + count];
        seed.AsSpan(0, destination).CopyTo(result);
        seed.AsSpan(offset, count).CopyTo(result.AsSpan(destination));
        seed.AsSpan(destination).CopyTo(result.AsSpan(destination + count));
        return result;
    }

    int ReportSuccess(string mode, int seedCount)
    {
        Console.WriteLine(
            $"artifact-fuzz {mode} passed: inputs={statistics.Total}, seeds={seedCount}, " +
            $"accepted={statistics.Accepted}, rejected={statistics.Rejected}, " +
            $"maxObservedInput={statistics.MaximumInputLength}");
        Console.WriteLine($"typed rejections: {statistics.FormatRejections()}");
        return 0;
    }
}

readonly record struct ParseOutcome(
    bool Success,
    LuauArtifactFailureKind? FailureKind,
    string? FieldName,
    string? CanonicalSha256)
{
    public static ParseOutcome Accepted(string canonicalSha256) =>
        new(true, null, null, canonicalSha256);

    public static ParseOutcome Rejected(LuauArtifactFailureKind kind, string? fieldName) =>
        new(false, kind, fieldName, null);
}

sealed class FuzzStatistics
{
    readonly int[] rejectionCounts = new int[Enum.GetValues<LuauArtifactFailureKind>().Length];

    public int Total { get; private set; }

    public int Accepted { get; private set; }

    public int Rejected => Total - Accepted;

    public int MaximumInputLength { get; private set; }

    public void Record(int inputLength, ParseOutcome outcome)
    {
        Total++;
        MaximumInputLength = Math.Max(MaximumInputLength, inputLength);
        if (outcome.Success)
        {
            Accepted++;
        }
        else
        {
            rejectionCounts[(int)outcome.FailureKind!.Value]++;
        }
    }

    public string FormatRejections()
    {
        return string.Join(
            ", ",
            Enum.GetValues<LuauArtifactFailureKind>().Select(
                kind => $"{kind}={rejectionCounts[(int)kind]}"));
    }
}

struct DeterministicRandom(ulong seed)
{
    ulong state = seed;

    public int NextInt(int exclusiveMaximum)
    {
        if (exclusiveMaximum <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
        }

        return (int)(NextUInt64() % (uint)exclusiveMaximum);
    }

    public void Fill(Span<byte> destination)
    {
        var offset = 0;
        while (offset < destination.Length)
        {
            var value = NextUInt64();
            for (var index = 0; index < sizeof(ulong) && offset < destination.Length; index++)
            {
                destination[offset++] = (byte)value;
                value >>= 8;
            }
        }
    }

    ulong NextUInt64()
    {
        state += 0x9e3779b97f4a7c15UL;
        var value = state;
        value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL;
        value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
        return value ^ (value >> 31);
    }
}
