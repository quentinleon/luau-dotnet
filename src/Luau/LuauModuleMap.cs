using System.Text;
using System.Diagnostics.CodeAnalysis;

namespace Luau;

/// <summary>
/// An immutable, in-memory source module namespace. It never performs
/// filesystem or network resolution. One root VM and its module cache form one
/// trust domain; mutually untrusted maps belong on separate roots.
/// </summary>
public sealed class LuauModuleMap : LuauRequirer
{
    readonly Dictionary<string, byte[]> modules;
    readonly Dictionary<string, string> aliases;
    readonly LuauModuleLimits limits;

    /// <summary>Creates a bounded immutable module map.</summary>
    public LuauModuleMap(
        IReadOnlyDictionary<string, byte[]> modules,
        IReadOnlyDictionary<string, string>? aliases = null,
        LuauModuleLimits? limits = null)
    {
        if (modules == null) throw new ArgumentNullException(nameof(modules));
        this.limits = (limits ?? LuauModuleLimits.Default) with { };
        CheckLimit(
            LuauModuleLimitKind.ModuleCount,
            modules.Count,
            this.limits.MaxModuleCount);

        this.modules = new Dictionary<string, byte[]>(modules.Count, StringComparer.Ordinal);
        long totalSourceBytes = 0;
        foreach (var pair in modules)
        {
            CheckLimit(
                LuauModuleLimitKind.ModuleCount,
                (long)this.modules.Count + 1,
                this.limits.MaxModuleCount);
            var moduleId = CanonicalizeAndValidateId(pair.Key, this.limits);
            var source = pair.Value
                ?? throw new ArgumentException($"Module '{pair.Key}' has no source payload.", nameof(modules));
            totalSourceBytes = AddBytes(
                totalSourceBytes,
                source.Length,
                LuauModuleLimitKind.SourceBytes,
                this.limits.MaxTotalSourceBytes);
            CheckLimit(
                LuauModuleLimitKind.SourceBytes,
                totalSourceBytes,
                this.limits.MaxTotalSourceBytes);
            if (!this.modules.TryAdd(moduleId, (byte[])source.Clone()))
            {
                throw new ArgumentException(
                    $"More than one module maps to canonical module ID '{moduleId}'.",
                    nameof(modules));
            }
        }

        TotalSourceBytes = totalSourceBytes;
        this.aliases = CopyAliases(aliases, this.limits);
    }

    /// <summary>Gets the immutable module count.</summary>
    public int Count => modules.Count;

    /// <summary>Gets the aggregate admitted source bytes.</summary>
    public long TotalSourceBytes { get; }

    /// <summary>Gets the limits captured by this map.</summary>
    public LuauModuleLimits Limits => limits with { };

    /// <summary>
    /// Canonicalizes a module ID by normalizing separators, removing leading
    /// and dot segments and one terminal <c>.luau</c>, and rejecting parent
    /// traversal and alias syntax.
    /// </summary>
    public static string CanonicalizeModuleId(string moduleId) =>
        CanonicalizePath(moduleId, allowEmpty: false);

    /// <summary>
    /// Compiles every source through the supplied bounded shared service. The
    /// bundle is published only after every output and aggregate quota passes;
    /// failures never expose a partially installable resolver.
    /// </summary>
    public async ValueTask<LuauModuleBundle> CompileModuleBundleAsync(
        ILuauCompilationService compilationService,
        LuauCompileOptions? compileOptions = null,
        CancellationToken cancellationToken = default)
    {
        if (compilationService == null) throw new ArgumentNullException(nameof(compilationService));
        cancellationToken.ThrowIfCancellationRequested();
        var ordered = modules.OrderBy(static pair => pair.Key, StringComparer.Ordinal).ToArray();
        var outputs = new Dictionary<string, LuauCompilerOutput>(ordered.Length, StringComparer.Ordinal);
        long totalBytecodeBytes = 0;
        for (var index = 0; index < ordered.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var moduleId = ordered[index].Key;
            var result = await CompileOneAsync(
                    compilationService,
                    ordered[index].Value,
                    compileOptions,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? LuauCompileResult.InfrastructureFailure(
                    new InvalidOperationException("The compilation service returned a null result."));
            if (result.Kind == LuauCompileResultKind.Canceled)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            if (result.Kind == LuauCompileResultKind.Diagnostic)
            {
                throw new LuauModuleBundleCompilationException(
                    moduleId,
                    result.CompilationDiagnostic!);
            }
            if (result.Kind == LuauCompileResultKind.InfrastructureFailure)
            {
                throw new LuauModuleBundleCompilationException(
                    moduleId,
                    result.InfrastructureException!);
            }

            var output = result.Output
                ?? throw new LuauModuleBundleCompilationException(
                    moduleId,
                    new InvalidOperationException("The compiler returned no output."));
            CheckLimit(
                LuauModuleLimitKind.BytecodeBytesPerModule,
                output.BytecodeLength,
                limits.MaxBytecodeBytesPerModule);
            totalBytecodeBytes = AddBytes(
                totalBytecodeBytes,
                output.BytecodeLength,
                LuauModuleLimitKind.BundleBytecodeBytes,
                limits.MaxTotalBytecodeBytes);
            CheckLimit(
                LuauModuleLimitKind.BundleBytecodeBytes,
                totalBytecodeBytes,
                limits.MaxTotalBytecodeBytes);
            outputs.Add(moduleId, output);
        }

        return new LuauModuleBundle(outputs, aliases, limits, totalBytecodeBytes);
    }

    /// <inheritdoc/>
    protected override string GetCacheKey(string path) => CanonicalizeModuleId(path);

    /// <inheritdoc/>
    protected override bool TryLoadModule(
        LuauState state,
        string fullPath,
        string requireArgument,
        out LuauValue result)
    {
        var moduleId = CanonicalizeModuleId(fullPath);
        if (!modules.TryGetValue(moduleId, out var source))
        {
            result = default;
            return false;
        }

        using var chunkName = new Utf8BufferScope($"@modules/{moduleId}.luau".AsSpan());
        result = ExecuteModuleSource(
            state,
            requireArgument,
            source,
            chunkName.Bytes);
        return true;
    }

    /// <inheritdoc/>
    protected override bool TryGetAliasPath(
        string alias,
        [NotNullWhen(true)] out string? path) =>
        aliases.TryGetValue(alias, out path);

    internal static Dictionary<string, string> CopyAliases(
        IReadOnlyDictionary<string, string>? aliases,
        LuauModuleLimits limits)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (aliases == null) return result;
        CheckLimit(
            LuauModuleLimitKind.ModuleCount,
            aliases.Count,
            limits.MaxModuleCount);
        foreach (var pair in aliases)
        {
            CheckLimit(
                LuauModuleLimitKind.ModuleCount,
                (long)result.Count + 1,
                limits.MaxModuleCount);
            ValidateAlias(pair.Key);
            CheckLimit(
                LuauModuleLimitKind.ModuleIdBytes,
                Encoding.UTF8.GetByteCount(pair.Key),
                limits.MaxModuleIdUtf8Bytes);
            if (pair.Value == null)
            {
                throw new ArgumentException($"Module alias '{pair.Key}' has no target path.", nameof(aliases));
            }
            result.Add(pair.Key, CanonicalizeAndValidatePath(pair.Value, allowEmpty: true, limits));
        }
        return result;
    }

    internal static string CanonicalizeAndValidateId(string value, LuauModuleLimits limits) =>
        CanonicalizeAndValidatePath(value, allowEmpty: false, limits);

    static string CanonicalizeAndValidatePath(
        string value,
        bool allowEmpty,
        LuauModuleLimits limits)
    {
        var result = CanonicalizePath(value, allowEmpty);
        CheckLimit(
            LuauModuleLimitKind.ModuleIdBytes,
            Encoding.UTF8.GetByteCount(result),
            limits.MaxModuleIdUtf8Bytes);
        return result;
    }

    static string CanonicalizePath(string path, bool allowEmpty)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        if (path.IndexOf('\0') >= 0)
        {
            throw new ArgumentException("A module ID cannot contain a NUL character.", nameof(path));
        }

        var segments = path.Replace('\\', '/').Split('/');
        var canonical = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment == ".") continue;
            if (segment == "..")
            {
                throw new ArgumentException("A module ID cannot traverse to a parent namespace.", nameof(path));
            }
            canonical.Add(segment);
        }

        var result = string.Join("/", canonical);
        if (result.EndsWith(".luau", StringComparison.Ordinal))
        {
            result = result[..^5];
        }
        if (result.Length > 0 && result[0] == '@')
        {
            throw new ArgumentException("A module ID cannot contain unresolved alias syntax.", nameof(path));
        }
        if (!allowEmpty && result.Length == 0)
        {
            throw new ArgumentException("A module ID must contain at least one path segment.", nameof(path));
        }
        return result;
    }

    static void ValidateAlias(string alias)
    {
        if (string.IsNullOrEmpty(alias) || alias[0] == '@' ||
            alias.IndexOf('/') >= 0 || alias.IndexOf('\\') >= 0 || alias.IndexOf('\0') >= 0)
        {
            throw new ArgumentException(
                "A module alias must be a non-empty name without separators, NUL, or a leading '@'.",
                nameof(alias));
        }
    }

    internal static void CheckLimit(LuauModuleLimitKind kind, long actual, long? limit)
    {
        if (limit.HasValue && actual > limit.Value)
        {
            throw new LuauModuleLimitException(kind, actual, limit.Value);
        }
    }

    static async Task<LuauCompileResult> CompileOneAsync(
        ILuauCompilationService compilationService,
        byte[] source,
        LuauCompileOptions? compileOptions,
        CancellationToken cancellationToken)
    {
        try
        {
            return await compilationService.CompileAsync(
                source,
                compileOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return LuauCompileResult.Canceled();
        }
        catch (Exception exception)
        {
            return LuauCompileResult.InfrastructureFailure(exception);
        }
    }

    static long AddBytes(
        long current,
        int increment,
        LuauModuleLimitKind kind,
        long? configuredLimit)
    {
        if (current > long.MaxValue - increment)
        {
            throw new LuauModuleLimitException(
                kind,
                long.MaxValue,
                configuredLimit ?? long.MaxValue - 1);
        }

        return current + increment;
    }
}

/// <summary>
/// An immutable, same-process compiler-output module bundle. Construction is
/// internal to the all-or-nothing bounded compilation phase.
/// </summary>
public sealed class LuauModuleBundle : LuauRequirer
{
    readonly Dictionary<string, LuauCompilerOutput> modules;
    readonly Dictionary<string, string> aliases;

    internal LuauModuleBundle(
        Dictionary<string, LuauCompilerOutput> modules,
        Dictionary<string, string> aliases,
        LuauModuleLimits limits,
        long totalBytecodeBytes)
    {
        this.modules = modules;
        this.aliases = new Dictionary<string, string>(aliases, StringComparer.Ordinal);
        Limits = limits with { };
        TotalBytecodeBytes = totalBytecodeBytes;
    }

    /// <summary>Gets the compiled module count.</summary>
    public int Count => modules.Count;
    /// <summary>Gets the aggregate compiled bytecode bytes.</summary>
    public long TotalBytecodeBytes { get; }
    /// <summary>Gets the limits captured during compilation.</summary>
    public LuauModuleLimits Limits { get; }

    /// <inheritdoc/>
    protected override string GetCacheKey(string path) => LuauModuleMap.CanonicalizeModuleId(path);

    /// <inheritdoc/>
    protected override bool TryLoadModule(
        LuauState state,
        string fullPath,
        string requireArgument,
        out LuauValue result)
    {
        var moduleId = LuauModuleMap.CanonicalizeModuleId(fullPath);
        if (!modules.TryGetValue(moduleId, out var output))
        {
            result = default;
            return false;
        }

        using var chunkName = new Utf8BufferScope($"@modules/{moduleId}.luau".AsSpan());
        result = ExecuteModuleCompilerOutput(
            state,
            requireArgument,
            output,
            chunkName.Bytes);
        return true;
    }

    /// <inheritdoc/>
    protected override bool TryGetAliasPath(
        string alias,
        [NotNullWhen(true)] out string? path) =>
        aliases.TryGetValue(alias, out path);
}
