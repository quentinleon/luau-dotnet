namespace Luau.ConsumerContract;

/// <summary>
/// Compiles as an ordinary consumer assembly with no InternalsVisibleTo grant.
/// These methods intentionally exist to make preview overload and extension
/// contracts fail the repository build if they stop being publicly usable.
/// </summary>
public static class PublicConsumerProbe
{
    public static (LuauValue[] Synchronous, ValueTask<LuauValue[]> Asynchronous) BindResumeArrays(
        LuauState coroutine,
        LuauValue[] arguments)
    {
        LuauValue[] synchronous = coroutine.Resume(arguments);
        ValueTask<LuauValue[]> asynchronous = coroutine.ResumeAsync(arguments);
        return (synchronous, asynchronous);
    }

    public static LuauCompileResult[] CreateEveryCompilationResult(
        LuauCompilerOutput output,
        LuauCompilationException diagnostic,
        Exception infrastructureFailure)
    {
        return
        [
            LuauCompileResult.Success(output),
            LuauCompileResult.Diagnostic(diagnostic),
            LuauCompileResult.Canceled(),
            LuauCompileResult.InfrastructureFailure(infrastructureFailure),
        ];
    }

    public static LuauObjectHandle CreateGeneratedCapability<T>(LuauState state, T target)
        where T : class, ILuauObjectCapability
    {
        return state.CreateHandle(target);
    }
}

public sealed class PublicCompilationService : ILuauCompilationService
{
    public ValueTask<LuauCompileResult> CompileAsync(
        ReadOnlyMemory<byte> utf8Source,
        LuauCompileOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(LuauCompileResult.Canceled());
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class ExternalAssemblyContractTests
{
    [Fact]
    public void PublicContractProbeCompilesWithoutFriendAssemblyAccess()
    {
        Assert.NotNull(typeof(PublicConsumerProbe));
        Assert.NotNull(typeof(PublicCompilationService));
    }
}
