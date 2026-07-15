using Luau;
using Luau.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Reflection.PortableExecutable;

namespace Luau.SourceGenerator.Tests;

public class CreateFunctionGeneratorTests
{
    const string ConsumerPath = @"C:\probe\Consumer.cs";

    static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);

    static readonly MetadataReference[] PlatformReferences =
        Directory.EnumerateFiles(
                System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
                "*.dll")
        .Where(IsManagedAssembly)
        .Select(path => MetadataReference.CreateFromFile(path))
        .ToArray();

    [Fact]
    public void LuauConsumer_GeneratesAndCompilesTypedLambdaAndMethodGroupOverloads()
    {
        const string source = """
using System.Threading;
using System.Threading.Tasks;
using Luau;

public sealed class Consumer
{
    public LuauFunction CreateLambda(LuauState state)
    {
        return state.CreateFunction(
            (double left, [FromLuauState] LuauState callbackState, double right) =>
                left + right);
    }

    public (LuauFunction, LuauFunction) CreateDuplicates(LuauState state) => (state.CreateFunction((double value) => value), state.CreateFunction((double value) => value));

    public LuauFunction CreateMethodGroup(LuauState state)
    {
        return state.CreateFunction(Add);
    }

    public LuauFunction CreateAsync(LuauState state)
    {
        return state.CreateFunction(async (double seconds, CancellationToken cancellationToken) =>
        {
            await Task.Yield();
        });
    }

    public LuauFunction CreateRuntimeOverload(LuauState state)
    {
        return state.CreateFunction("runtime", callbackState => 0);
    }

    private static double Add(
        double left,
        [FromLuauState] LuauState callbackState,
        double right) => left + right;
}
""";

        var compilation = CreateCompilation(
            source,
            "LuauConsumer",
            MetadataReference.CreateFromFile(typeof(LuauState).Assembly.Location));
        var tree = compilation.SyntaxTrees.Single();
        var invocations = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(IsCreateFunctionInvocation)
            .ToArray();

        var result = RunGenerator(compilation, out var outputCompilation);

        Assert.Equal(2, result.GeneratedSources.Length);
        Assert.Empty(result.Diagnostics);
        AssertNoErrors(outputCompilation);

        var implementation = result.GeneratedSources.Single(
                sourceResult => sourceResult.HintName == "GeneratedLuauStateExtensions.RegisterFunction.Impl.g.cs")
            .SourceText
            .ToString();

        var lambdaInvocation = invocations.Single(invocation =>
            invocation.Ancestors().OfType<MethodDeclarationSyntax>().First().Identifier.Text == "CreateLambda");
        var methodGroupInvocation = invocations.Single(invocation =>
            invocation.Ancestors().OfType<MethodDeclarationSyntax>().First().Identifier.Text == "CreateMethodGroup");
        var runtimeInvocation = invocations.Single(invocation =>
            invocation.Ancestors().OfType<MethodDeclarationSyntax>().First().Identifier.Text == "CreateRuntimeOverload");

        Assert.Contains(CaseLabel(lambdaInvocation), implementation);
        Assert.Contains(CaseLabel(methodGroupInvocation), implementation);
        Assert.DoesNotContain(CaseLabel(runtimeInvocation), implementation);

        Assert.Contains("var arg0 = state.ToValue(-2).Read<double>();", implementation);
        Assert.Contains("var arg1 = state;", implementation);
        Assert.Contains("var arg2 = state.ToValue(-1).Read<double>();", implementation);
        Assert.Contains("var arg1 = ct;", implementation);
    }

    static bool IsCreateFunctionInvocation(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
               memberAccess.Name.Identifier.Text == "CreateFunction";
    }

    static string CaseLabel(InvocationExpressionSyntax invocation)
    {
        var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        return $"case (\"C:/probe/Consumer.cs\", {line}):";
    }

    static CSharpCompilation CreateCompilation(
        string source,
        string assemblyName,
        params MetadataReference[] additionalReferences)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions, ConsumerPath);
        return CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            PlatformReferences.Concat(additionalReferences),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    static bool IsManagedAssembly(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        return reader.HasMetadata;
    }

    static GeneratorRunResult RunGenerator(
        CSharpCompilation compilation,
        out Compilation outputCompilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new CreateFunctionGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out outputCompilation,
            out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        return driver.GetRunResult().Results.Single();
    }

    static void AssertNoErrors(Compilation compilation)
    {
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();

        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
    }
}
