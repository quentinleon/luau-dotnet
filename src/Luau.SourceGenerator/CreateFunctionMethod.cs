using Microsoft.CodeAnalysis;

namespace Luau.SourceGenerator;

internal record CreateFunctionContext
{
    public CreateFunctionMethod? Method { get; set; }
    public required DiagnosticReporter DiagnosticReporter { get; init; }
    public required SemanticModel Model { get; init; }
}

internal class CreateFunctionMethod : IEquatable<CreateFunctionMethod>
{
    public required CreateFunctionMethodParameter[] Parameters { get; init; }
    public required string ReturnTypeName { get; init; }
    public required bool HasReturnValue { get; init; }
    public required bool IsAsync { get; init; }
    public required string FilePath { get; init; }
    public required int LineNumber { get; init; }

    public bool Equals(CreateFunctionMethod other)
    {
        return Parameters.SequenceEqual(other.Parameters) && ReturnTypeName == other.ReturnTypeName;
    }

    public override bool Equals(object obj)
    {
        return obj is CreateFunctionMethod other && Equals(other);
    }

    public override int GetHashCode()
    {
        if (Parameters.Length == 0) return 0;
        var hashCode = Parameters[0].GetHashCode();
        for (int i = 1; i < Parameters.Length; i++)
        {
            hashCode ^= Parameters[i].GetHashCode();
        }
        hashCode ^= ReturnTypeName.GetHashCode();
        return hashCode;
    }

    public static CreateFunctionMethod Create(Location location, bool isAsync, ITypeSymbol? returnType, CreateFunctionMethodParameter[] parameters)
    {
        var returnTypeName = returnType == null ? "void" : returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var lineSpan = location.GetLineSpan();
        var filePath = lineSpan.Path;
        var lineNumber = lineSpan.StartLinePosition.Line + 1; // 1-based

        return new CreateFunctionMethod
        {
            Parameters = parameters!,
            ReturnTypeName = returnTypeName,
            HasReturnValue = returnType != null && returnTypeName != "void" && !(isAsync && returnType is INamedTypeSymbol n && !n.IsGenericType),
            IsAsync = isAsync,
            FilePath = filePath,
            LineNumber = lineNumber,
        };
    }
}

internal record CreateFunctionMethodParameter
{
    public required string FullTypeName { get; init; }
}