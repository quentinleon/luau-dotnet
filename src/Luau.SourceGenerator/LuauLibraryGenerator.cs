using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Luau.SourceGenerator;

internal enum LuauLibraryMemberKind
{
    Field,
    Property,
    Method,
}

internal enum LuauLibraryReturnKind
{
    None,
    Value,
    Async,
    AsyncValue,
}

internal enum LuauLibraryParameterKind
{
    Argument,
    CallContext,
    CancellationToken,
    State,
}

internal sealed record LuauLibraryParameter
{
    public required string TypeName { get; init; }
    public required LuauLibraryParameterKind Kind { get; init; }
}

internal sealed record LuauLibraryMember
{
    public required LuauLibraryMemberKind Kind { get; init; }
    public required string LuauName { get; init; }
    public required string ManagedName { get; init; }
    public required string TypeName { get; init; }
    public required bool IsStatic { get; init; }
    public required bool CanRead { get; init; }
    public required bool CanWrite { get; init; }
    public required LuauLibraryReturnKind ReturnKind { get; init; }
    public required EquatableArray<LuauLibraryParameter> Parameters { get; init; }
}

internal sealed record LuauLibraryContext
{
    public required IgnoreEquality<DiagnosticReporter> Diagnostics { get; init; }
    public required string LibraryName { get; init; }
    public required string TypeName { get; init; }
    public required string FullTypeName { get; init; }
    public required string? Namespace { get; init; }
    public required string DeclarationKeyword { get; init; }
    public required EquatableArray<LuauLibraryMember> Members { get; init; }
}

[Generator(LanguageNames.CSharp)]
public sealed class LuauLibraryGenerator : IIncrementalGenerator
{
    const string LibraryAttributeName = "Luau.LuauLibraryAttribute";
    const string MemberAttributeName = "Luau.LuauMemberAttribute";
    const string FromStateAttributeName = "Luau.FromLuauStateAttribute";
    const string CallContextTypeName = "Luau.LuauCallContext";
    const string StateTypeName = "Luau.LuauState";
    const string CancellationTokenTypeName = "System.Threading.CancellationToken";
    const string TaskTypeName = "System.Threading.Tasks.Task";
    const string TaskOfTTypeName = "System.Threading.Tasks.Task`1";
    const string ValueTaskTypeName = "System.Threading.Tasks.ValueTask";
    const string ValueTaskOfTTypeName = "System.Threading.Tasks.ValueTask`1";

    static readonly SymbolDisplayFormat FullyQualifiedTypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(
                SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
                SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var libraries = context.SyntaxProvider.ForAttributeWithMetadataName(
            LibraryAttributeName,
            static (node, _) => node is TypeDeclarationSyntax,
            static (attributeContext, cancellationToken) =>
                Transform(attributeContext, cancellationToken));

        context.RegisterSourceOutput(libraries, Emit);
    }

    static LuauLibraryContext Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var syntax = (TypeDeclarationSyntax)context.TargetNode;
        var diagnostics = new DiagnosticReporter();
        var location = syntax.Identifier.GetLocation();

        if (!syntax.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PartialKeyword)))
        {
            diagnostics.ReportDiagnostic(DiagnosticDescriptors.MustBePartial, location);
        }

        if (symbol.ContainingType != null)
        {
            diagnostics.ReportDiagnostic(DiagnosticDescriptors.NestedNotAllowed, location);
        }

        if (symbol.IsAbstract)
        {
            diagnostics.ReportDiagnostic(DiagnosticDescriptors.AbstractNotAllowed, location);
        }

        if (symbol.TypeParameters.Length != 0)
        {
            diagnostics.ReportDiagnostic(
                DiagnosticDescriptors.UnsupportedSignature,
                location,
                symbol.Name,
                "generic host-library types are not supported.");
        }

        var libraryName = context.Attributes[0].ConstructorArguments.FirstOrDefault().Value as string;
        if (!IsValidLuauName(libraryName))
        {
            diagnostics.ReportDiagnostic(DiagnosticDescriptors.InvalidName, location, "library");
            libraryName = string.Empty;
        }

        var memberAttribute = context.SemanticModel.Compilation.GetTypeByMetadataName(MemberAttributeName);
        var fromStateAttribute = context.SemanticModel.Compilation.GetTypeByMetadataName(FromStateAttributeName);
        var callContextType = context.SemanticModel.Compilation.GetTypeByMetadataName(CallContextTypeName);
        var stateType = context.SemanticModel.Compilation.GetTypeByMetadataName(StateTypeName);
        var cancellationTokenType = context.SemanticModel.Compilation.GetTypeByMetadataName(CancellationTokenTypeName);
        var taskType = context.SemanticModel.Compilation.GetTypeByMetadataName(TaskTypeName);
        var taskOfTType = context.SemanticModel.Compilation.GetTypeByMetadataName(TaskOfTTypeName);
        var valueTaskType = context.SemanticModel.Compilation.GetTypeByMetadataName(ValueTaskTypeName);
        var valueTaskOfTType = context.SemanticModel.Compilation.GetTypeByMetadataName(ValueTaskOfTTypeName);

        var members = new List<LuauLibraryMember>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        if (memberAttribute == null || fromStateAttribute == null || callContextType == null ||
            stateType == null || cancellationTokenType == null || taskType == null ||
            taskOfTType == null || valueTaskType == null || valueTaskOfTType == null)
        {
            diagnostics.ReportDiagnostic(
                DiagnosticDescriptors.UnsupportedSignature,
                location,
                symbol.Name,
                "the referenced Luau callback API is incomplete.");
        }
        else
        {
            foreach (var memberSymbol in symbol.GetMembers())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!memberSymbol.TryGetAttribute(memberAttribute, out var attribute))
                {
                    continue;
                }

                var memberLocation = memberSymbol.Locations.FirstOrDefault() ?? location;
                var explicitName = attribute.ConstructorArguments.FirstOrDefault().Value as string;
                var luauName = explicitName ?? memberSymbol.Name;
                if (!IsValidLuauName(luauName))
                {
                    diagnostics.ReportDiagnostic(
                        DiagnosticDescriptors.InvalidName,
                        memberLocation,
                        "member");
                    continue;
                }

                if (!names.Add(luauName))
                {
                    diagnostics.ReportDiagnostic(
                        DiagnosticDescriptors.DuplicateMemberName,
                        memberLocation,
                        luauName);
                    continue;
                }

                var member = CreateMember(
                    memberSymbol,
                    luauName,
                    fromStateAttribute,
                    callContextType,
                    stateType,
                    cancellationTokenType,
                    taskType,
                    taskOfTType,
                    valueTaskType,
                    valueTaskOfTType,
                    diagnostics,
                    memberLocation);
                if (member != null)
                {
                    members.Add(member);
                }
            }
        }

        return new LuauLibraryContext
        {
            Diagnostics = diagnostics,
            LibraryName = libraryName!,
            TypeName = EscapeIdentifier(symbol.Name),
            FullTypeName = symbol.ToDisplayString(FullyQualifiedTypeFormat),
            Namespace = symbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : symbol.ContainingNamespace.ToDisplayString(),
            DeclarationKeyword = symbol.IsRecord ? "record class" : "class",
            Members = members.ToArray(),
        };
    }

    static LuauLibraryMember? CreateMember(
        ISymbol symbol,
        string luauName,
        INamedTypeSymbol fromStateAttribute,
        INamedTypeSymbol callContextType,
        INamedTypeSymbol stateType,
        INamedTypeSymbol cancellationTokenType,
        INamedTypeSymbol taskType,
        INamedTypeSymbol taskOfTType,
        INamedTypeSymbol valueTaskType,
        INamedTypeSymbol valueTaskOfTType,
        DiagnosticReporter diagnostics,
        Location location)
    {
        switch (symbol)
        {
            case IFieldSymbol field:
                if (field.IsFixedSizeBuffer || IsUnsupportedType(field.Type))
                {
                    ReportUnsupported(diagnostics, location, field.Name, "the field type is not callback-safe.");
                    return null;
                }

                return new LuauLibraryMember
                {
                    Kind = LuauLibraryMemberKind.Field,
                    LuauName = luauName,
                    ManagedName = EscapeIdentifier(field.Name),
                    TypeName = field.Type.ToDisplayString(FullyQualifiedTypeFormat),
                    IsStatic = field.IsStatic,
                    CanRead = true,
                    CanWrite = !field.IsReadOnly && !field.IsConst,
                    ReturnKind = LuauLibraryReturnKind.None,
                    Parameters = [],
                };

            case IPropertySymbol property:
                if (property.IsIndexer || IsUnsupportedType(property.Type))
                {
                    ReportUnsupported(
                        diagnostics,
                        location,
                        property.Name,
                        property.IsIndexer
                            ? "indexers are not supported."
                            : "the property type is not callback-safe.");
                    return null;
                }

                return new LuauLibraryMember
                {
                    Kind = LuauLibraryMemberKind.Property,
                    LuauName = luauName,
                    ManagedName = EscapeIdentifier(property.Name),
                    TypeName = property.Type.ToDisplayString(FullyQualifiedTypeFormat),
                    IsStatic = property.IsStatic,
                    CanRead = property.GetMethod != null,
                    CanWrite = property.SetMethod is { IsInitOnly: false },
                    ReturnKind = LuauLibraryReturnKind.None,
                    Parameters = [],
                };

            case IMethodSymbol method:
                return CreateMethod(
                    method,
                    luauName,
                    fromStateAttribute,
                    callContextType,
                    stateType,
                    cancellationTokenType,
                    taskType,
                    taskOfTType,
                    valueTaskType,
                    valueTaskOfTType,
                    diagnostics,
                    location);

            default:
                ReportUnsupported(diagnostics, location, symbol.Name, "only fields, properties, and methods are supported.");
                return null;
        }
    }

    static LuauLibraryMember? CreateMethod(
        IMethodSymbol method,
        string luauName,
        INamedTypeSymbol fromStateAttribute,
        INamedTypeSymbol callContextType,
        INamedTypeSymbol stateType,
        INamedTypeSymbol cancellationTokenType,
        INamedTypeSymbol taskType,
        INamedTypeSymbol taskOfTType,
        INamedTypeSymbol valueTaskType,
        INamedTypeSymbol valueTaskOfTType,
        DiagnosticReporter diagnostics,
        Location location)
    {
        if (method.MethodKind != MethodKind.Ordinary || method.IsGenericMethod ||
            method.ReturnsByRef || method.ReturnsByRefReadonly)
        {
            ReportUnsupported(
                diagnostics,
                location,
                method.Name,
                "generic, operator, and by-reference-returning methods are not supported.");
            return null;
        }

        var returnKind = LuauLibraryReturnKind.Value;
        var returnType = method.ReturnType;
        if (method.ReturnsVoid)
        {
            if (method.IsAsync)
            {
                ReportUnsupported(diagnostics, location, method.Name, "async void methods are not supported.");
                return null;
            }

            returnKind = LuauLibraryReturnKind.None;
        }
        else if (SymbolEqualityComparer.Default.Equals(returnType, taskType) ||
                 SymbolEqualityComparer.Default.Equals(returnType, valueTaskType))
        {
            returnKind = LuauLibraryReturnKind.Async;
        }
        else if (returnType is INamedTypeSymbol namedReturn &&
                 namedReturn.TypeArguments.Length == 1 &&
                 (SymbolEqualityComparer.Default.Equals(namedReturn.OriginalDefinition, taskOfTType) ||
                  SymbolEqualityComparer.Default.Equals(namedReturn.OriginalDefinition, valueTaskOfTType)))
        {
            returnKind = LuauLibraryReturnKind.AsyncValue;
            returnType = namedReturn.TypeArguments[0];
        }
        else if (method.IsAsync)
        {
            ReportUnsupported(
                diagnostics,
                location,
                method.Name,
                "async methods must return Task, Task<T>, ValueTask, or ValueTask<T>.");
            return null;
        }

        if (returnKind is LuauLibraryReturnKind.Value or LuauLibraryReturnKind.AsyncValue &&
            IsUnsupportedType(returnType))
        {
            ReportUnsupported(diagnostics, location, method.Name, "the return type is not callback-safe.");
            return null;
        }

        var parameters = new List<LuauLibraryParameter>(method.Parameters.Length);
        foreach (var parameter in method.Parameters)
        {
            if (parameter.RefKind != RefKind.None || IsUnsupportedType(parameter.Type))
            {
                ReportUnsupported(
                    diagnostics,
                    parameter.Locations.FirstOrDefault() ?? location,
                    method.Name,
                    "ref, out, in, pointer, and ref-like parameters are not supported.");
                return null;
            }

            var fromState = parameter.TryGetAttribute(fromStateAttribute, out _);
            LuauLibraryParameterKind kind;
            if (fromState)
            {
                if (!SymbolEqualityComparer.Default.Equals(parameter.Type, stateType))
                {
                    ReportUnsupported(
                        diagnostics,
                        parameter.Locations.FirstOrDefault() ?? location,
                        method.Name,
                        "[FromLuauState] is valid only on a LuauState parameter.");
                    return null;
                }

                kind = LuauLibraryParameterKind.State;
            }
            else if (SymbolEqualityComparer.Default.Equals(parameter.Type, callContextType))
            {
                kind = LuauLibraryParameterKind.CallContext;
            }
            else if (SymbolEqualityComparer.Default.Equals(parameter.Type, cancellationTokenType))
            {
                kind = LuauLibraryParameterKind.CancellationToken;
            }
            else
            {
                kind = LuauLibraryParameterKind.Argument;
            }

            if (kind == LuauLibraryParameterKind.Argument && (parameter.IsParams || parameter.IsOptional))
            {
                ReportUnsupported(
                    diagnostics,
                    parameter.Locations.FirstOrDefault() ?? location,
                    method.Name,
                    "params and optional script arguments are not supported.");
                return null;
            }

            parameters.Add(new LuauLibraryParameter
            {
                TypeName = parameter.Type.ToDisplayString(FullyQualifiedTypeFormat),
                Kind = kind,
            });
        }

        return new LuauLibraryMember
        {
            Kind = LuauLibraryMemberKind.Method,
            LuauName = luauName,
            ManagedName = EscapeIdentifier(method.Name),
            TypeName = returnType.ToDisplayString(FullyQualifiedTypeFormat),
            IsStatic = method.IsStatic,
            CanRead = true,
            CanWrite = false,
            ReturnKind = returnKind,
            Parameters = parameters.ToArray(),
        };
    }

    static void Emit(SourceProductionContext context, LuauLibraryContext library)
    {
        if (library.Diagnostics.Value.HasDiagnostics)
        {
            library.Diagnostics.Value.ReportToContext(context);
            return;
        }

        var builder = new CodeBuilder(0);
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine();

        var namespaceBlock = library.Namespace == null
            ? builder.Nop
            : builder.BeginBlock($"namespace {library.Namespace}");

        using (builder.BeginBlock(
                   $"partial {library.DeclarationKeyword} {library.TypeName} : global::Luau.ILuauLibrary"))
        {
            using (builder.BeginBlock(
                       "void global::Luau.ILuauLibrary.RegisterTo(global::Luau.LuauState state)"))
            {
                builder.AppendLine("using var table = state.CreateTable();");
                if (library.Members.Length != 0)
                {
                    builder.AppendLine("using var metatable = state.CreateTable();");
                    builder.AppendLine();
                }

                var methods = library.Members
                    .Where(static member => member.Kind == LuauLibraryMemberKind.Method)
                    .ToArray();
                for (var index = 0; index < methods.Length; index++)
                {
                    EmitMethod(builder, library, methods[index], index);
                    builder.AppendLine();
                }

                if (library.Members.Length != 0)
                {
                    EmitIndexCallback(builder, library);
                    builder.AppendLine();
                    EmitNewIndexCallback(builder, library);
                    builder.AppendLine();
                    builder.AppendLine("state.SetMetatable(table, metatable);");
                }

                builder.AppendLine($"state[{Literal(library.LibraryName)}] = table;");
            }
        }

        namespaceBlock.Dispose();

        var hintName = new string(
            library.FullTypeName
                .Replace("global::", string.Empty)
                .Select(static character => char.IsLetterOrDigit(character) ? character : '_')
                .ToArray());
        context.AddSource($"LuauLibrary.{hintName}.g.cs", builder.ToString());
    }

    static void EmitMethod(
        CodeBuilder builder,
        LuauLibraryContext library,
        LuauLibraryMember method,
        int methodIndex)
    {
        var callbackName = $"{library.LibraryName}.{method.LuauName}";
        var factory = method.ReturnKind is LuauLibraryReturnKind.Async or LuauLibraryReturnKind.AsyncValue
            ? "CreateAsyncFunction"
            : "CreateFunction";
        var lambda = method.ReturnKind is LuauLibraryReturnKind.Async or LuauLibraryReturnKind.AsyncValue
            ? "async context =>"
            : "context =>";

        builder.AppendLine(
            $"var function{methodIndex} = state.{factory}({Literal(callbackName)}, {lambda}");
        builder.AppendLine("{");
        using (builder.BeginIndent())
        {
            var argumentCount = method.Parameters.Count(
                static parameter => parameter.Kind == LuauLibraryParameterKind.Argument);
            if (argumentCount != 0)
            {
                var message = $"Host function '{callbackName}' expects at least {argumentCount} argument(s).";
                using (builder.BeginBlock($"if (context.ArgumentCount < {argumentCount})"))
                {
                    builder.AppendLine($"throw new global::Luau.LuauException({Literal(message)});");
                }
            }

            var argumentIndex = 0;
            for (var parameterIndex = 0; parameterIndex < method.Parameters.Length; parameterIndex++)
            {
                var parameter = method.Parameters[parameterIndex];
                var expression = parameter.Kind switch
                {
                    LuauLibraryParameterKind.Argument =>
                        $"context.Read<{parameter.TypeName}>({argumentIndex++})",
                    LuauLibraryParameterKind.CallContext => "context",
                    LuauLibraryParameterKind.CancellationToken => "context.CancellationToken",
                    LuauLibraryParameterKind.State => "context.State",
                    _ => throw new ArgumentOutOfRangeException(),
                };
                builder.AppendLine($"var arg{parameterIndex} = {expression};");
            }

            var target = method.IsStatic ? library.FullTypeName : "this";
            var arguments = string.Join(
                ", ",
                Enumerable.Range(0, method.Parameters.Length).Select(static index => $"arg{index}"));
            var invocation = $"{target}.{method.ManagedName}({arguments})";
            switch (method.ReturnKind)
            {
                case LuauLibraryReturnKind.None:
                    builder.AppendLine($"{invocation};");
                    break;
                case LuauLibraryReturnKind.Value:
                    builder.AppendLine($"var result = {invocation};");
                    builder.AppendLine("context.Return(result);");
                    break;
                case LuauLibraryReturnKind.Async:
                    builder.AppendLine($"await {invocation};");
                    break;
                case LuauLibraryReturnKind.AsyncValue:
                    builder.AppendLine($"var result = await {invocation};");
                    builder.AppendLine("context.Return(result);");
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        builder.AppendLine("});");
    }

    static void EmitIndexCallback(
        CodeBuilder builder,
        LuauLibraryContext library)
    {
        builder.AppendLine(
            $"metatable[\"__index\"] = state.CreateFunction({Literal($"{library.LibraryName}.__index")}, context =>");
        builder.AppendLine("{");
        using (builder.BeginIndent())
        {
            builder.AppendLine("var key = context.Read<global::System.String>(1);");
            using (builder.BeginBlock("switch (key)"))
            {
                var methodIndex = 0;
                foreach (var member in library.Members)
                {
                    using (builder.BeginIndent($"case {Literal(member.LuauName)}:"))
                    {
                        if (member.Kind == LuauLibraryMemberKind.Method)
                        {
                            builder.AppendLine($"context.Return(function{methodIndex++});");
                            builder.AppendLine("break;");
                        }
                        else if (member.CanRead)
                        {
                            builder.AppendLine(
                                $"context.Return({MemberTarget(library, member)}.{member.ManagedName});");
                            builder.AppendLine("break;");
                        }
                        else
                        {
                            builder.AppendLine(
                                "throw new global::Luau.LuauException($\"cannot read write-only member '{key}'\");");
                        }
                    }
                }
            }
        }
        builder.AppendLine("});");
    }

    static void EmitNewIndexCallback(CodeBuilder builder, LuauLibraryContext library)
    {
        builder.AppendLine(
            $"metatable[\"__newindex\"] = state.CreateFunction({Literal($"{library.LibraryName}.__newindex")}, context =>");
        builder.AppendLine("{");
        using (builder.BeginIndent())
        {
            builder.AppendLine("var key = context.Read<global::System.String>(1);");
            using (builder.BeginBlock("switch (key)"))
            {
                foreach (var member in library.Members)
                {
                    using (builder.BeginIndent($"case {Literal(member.LuauName)}:"))
                    {
                        if (member.Kind != LuauLibraryMemberKind.Method && member.CanWrite)
                        {
                            builder.AppendLine(
                                $"{MemberTarget(library, member)}.{member.ManagedName} = context.Read<{member.TypeName}>(2);");
                            builder.AppendLine("break;");
                        }
                        else
                        {
                            builder.AppendLine(
                                "throw new global::Luau.LuauException($\"cannot set readonly member '{key}'\");");
                        }
                    }
                }

                using (builder.BeginIndent("default:"))
                {
                    builder.AppendLine(
                        "throw new global::Luau.LuauException($\"cannot set unknown member '{key}'\");");
                }
            }
        }
        builder.AppendLine("});");
    }

    static string MemberTarget(LuauLibraryContext library, LuauLibraryMember member) =>
        member.IsStatic ? library.FullTypeName : "this";

    static bool IsUnsupportedType(ITypeSymbol type) =>
        type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Error or TypeKind.TypeParameter ||
        type.IsRefLikeType;

    static bool IsValidLuauName(string? name) => name != null && name.IndexOf('\0') < 0;

    static void ReportUnsupported(
        DiagnosticReporter diagnostics,
        Location location,
        string memberName,
        string reason) =>
        diagnostics.ReportDiagnostic(
            DiagnosticDescriptors.UnsupportedSignature,
            location,
            memberName,
            reason);

    static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    static string EscapeIdentifier(string identifier) =>
        SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None ||
        SyntaxFacts.GetContextualKeywordKind(identifier) != SyntaxKind.None
            ? "@" + identifier
            : identifier;
}
