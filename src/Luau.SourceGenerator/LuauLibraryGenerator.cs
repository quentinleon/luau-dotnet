using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Luau.SourceGenerator;

internal record LuauLibraryContext
{
    public required IgnoreEquality<DiagnosticReporter> DiagnosticReporter { get; init; }
    public required string LibraryName { get; init; }
    public required string TypeName { get; init; }
    public required string FullTypeName { get; init; }
    public required string? Namespace { get; init; }
    public required string DeclarationKeyword { get; init; }
    public required EquatableArray<LuauLibraryProperty> Properties { get; init; }
    public required EquatableArray<LuauLibraryMethod> Methods { get; init; }
}

internal record LuauLibraryProperty
{
    public required string LuauMemberName { get; init; }
    public required string Name { get; init; }
    public required string FullTypeName { get; init; }
    public required bool HasGetter { get; init; }
    public required bool HasSetter { get; init; }
    public required bool IsStatic { get; init; }
}

internal record LuauLibraryMethod
{
    public required string LuauMemberName { get; init; }
    public required string Name { get; init; }
    public required EquatableArray<LuauLibraryMethodParameter> Parameters { get; init; }
    public required string ReturnTypeName { get; init; }
    public required bool HasReturnValue { get; init; }
    public required bool IsAsync { get; init; }
    public required bool IsStatic { get; init; }
}

internal record LuauLibraryMethodParameter
{
    public required string FullTypeName { get; init; }
    public required bool FromLuauState { get; init; }
}

[Generator(LanguageNames.CSharp)]
public class LuauLibraryGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var provider = context.SyntaxProvider
            .ForAttributeWithMetadataName("Luau.LuauLibraryAttribute",
                (node, ct) => true,
                static (context, ct) =>
                {
                    var symbol = (INamedTypeSymbol)context.TargetSymbol;
                    var syntax = (TypeDeclarationSyntax)context.TargetNode;
                    var members = symbol.GetAllMembers(false);
                    var reporter = new DiagnosticReporter();

                    if (!syntax.Modifiers.Any(x => x.IsKind(SyntaxKind.PartialKeyword)))
                    {
                        reporter.ReportDiagnostic(DiagnosticDescriptors.MustBePartial, syntax.Identifier.GetLocation());
                        goto ERROR;
                    }

                    if (syntax.Parent is TypeDeclarationSyntax)
                    {
                        reporter.ReportDiagnostic(DiagnosticDescriptors.NestedNotAllowed, syntax.Identifier.GetLocation());
                        goto ERROR;
                    }

                    if (symbol.IsAbstract)
                    {
                        reporter.ReportDiagnostic(DiagnosticDescriptors.AbstractNotAllowed, syntax.Identifier.GetLocation());
                        goto ERROR;
                    }

                    var luauMemberAttribute = context.SemanticModel.Compilation.GetTypeByMetadataName("Luau.LuauMemberAttribute")!;
                    var fromLuauStateAttribute = context.SemanticModel.Compilation.GetTypeByMetadataName("Luau.FromLuauStateAttribute")!;

                    var fields = members
                        .OfType<IFieldSymbol>()
                        .Select(x =>
                        {
                            var tryResult = x.TryGetAttribute(luauMemberAttribute, out var attribute);
                            return (symbol: x, tryResult, attribute);
                        })
                        .Where(x => x.tryResult)
                        .Select(static x =>
                        {
                            var memberName = x.attribute?.ConstructorArguments.FirstOrDefault().Value?.ToString();
                            return new LuauLibraryProperty
                            {
                                LuauMemberName = memberName ?? x.symbol.Name,
                                Name = x.symbol.Name,
                                FullTypeName = x.symbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                HasGetter = true,
                                HasSetter = !x.symbol.IsReadOnly,
                                IsStatic = x.symbol.IsStatic,
                            };
                        });
                    var properties = members
                        .OfType<IPropertySymbol>()
                        .Select(x =>
                        {
                            var tryResult = x.TryGetAttribute(luauMemberAttribute, out var attribute);
                            return (symbol: x, tryResult, attribute);
                        })
                        .Where(x => x.tryResult)
                        .Select(static x =>
                        {
                            var memberName = x.attribute?.ConstructorArguments.FirstOrDefault().Value?.ToString();
                            return new LuauLibraryProperty
                            {
                                LuauMemberName = memberName ?? x.symbol.Name,
                                Name = x.symbol.Name,
                                FullTypeName = x.symbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                HasGetter = x.symbol.GetMethod != null,
                                HasSetter = x.symbol.SetMethod != null,
                                IsStatic = x.symbol.IsStatic,
                            };
                        });
                    var methods = members
                        .OfType<IMethodSymbol>()
                        .Select(x =>
                        {
                            var tryResult = x.TryGetAttribute(luauMemberAttribute, out var attribute);
                            return (symbol: x, tryResult, attribute);
                        })
                        .Where(x => x.tryResult)
                        .Select(x =>
                        {
                            var returnTypeName = x.symbol.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                            var isAsync = x.symbol.ReturnType.IsTaskType();
                            var memberName = x.attribute?.ConstructorArguments.FirstOrDefault().Value?.ToString();
                            return new LuauLibraryMethod
                            {
                                LuauMemberName = memberName ?? x.symbol.Name,
                                Name = x.symbol.Name,
                                ReturnTypeName = returnTypeName,
                                HasReturnValue = returnTypeName != "void" && !(isAsync && x.symbol.ReturnType is INamedTypeSymbol n && !n.IsGenericType),
                                IsAsync = isAsync,
                                IsStatic = x.symbol.IsStatic,
                                Parameters = x.symbol.Parameters
                                    .Select(x => new LuauLibraryMethodParameter
                                    {
                                        FullTypeName = x.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                        FromLuauState = x.TryGetAttribute(fromLuauStateAttribute, out _)
                                    })
                                    .ToArray()
                            };
                        });

                    return new LuauLibraryContext
                    {
                        DiagnosticReporter = reporter,
                        FullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        LibraryName = context.Attributes[0].ConstructorArguments[0].Value!.ToString(),
                        TypeName = symbol.Name,
                        Namespace = symbol.ContainingNamespace.IsGlobalNamespace ? null : symbol.ContainingNamespace.Name,
                        DeclarationKeyword = syntax.Keyword.ToFullString(),
                        Properties = properties
                            .Concat(fields)
                            .ToArray(),
                        Methods = methods
                            .ToArray(),
                    };

                ERROR:
                    return new LuauLibraryContext
                    {
                        DiagnosticReporter = reporter,
                        LibraryName = "",
                        Namespace = null,
                        FullTypeName = "",
                        TypeName = "",
                        DeclarationKeyword = "",
                        Properties = [],
                        Methods = [],
                    };
                });

        context.RegisterSourceOutput(provider, Emit);
    }

    static void Emit(SourceProductionContext context, LuauLibraryContext library)
    {
        if (library.DiagnosticReporter.Value.HasDiagnostics)
        {
            library.DiagnosticReporter.Value.ReportToContext(context);
            return;
        }

        var builder = new CodeBuilder(0);

        CodeBuilder.Block? namespaceBlock = null;
        if (library.Namespace != null)
        {
            namespaceBlock = builder.BeginBlock($"namespace {library.Namespace}");
        }

        using (builder.BeginBlock($"partial {library.DeclarationKeyword} {library.TypeName} : global::Luau.ILuauLibrary"))
        {
            using (builder.BeginBlock("void global::Luau.ILuauLibrary.RegisterTo(global::Luau.LuauState state)"))
            {
                builder.AppendLine("var table = state.CreateTable();");
                builder.AppendLine("var metatable = state.CreateTable();");

                foreach (var method in library.Methods)
                {
                    const string ctType = "global::System.Threading.CancellationToken";
                    var parameterCount = method.Parameters.Count(x => x.FullTypeName != ctType && !x.FromLuauState);

                    var argsDeclarations = method.Parameters.Select((x, i) =>
                    {
                        if (x.FullTypeName == ctType)
                        {
                            return $"var arg{i} = ct;";
                        }
                        else if (x.FromLuauState)
                        {
                            return $"var arg{i} = state";
                        }
                        else
                        {
                            return $"var arg{i} = state.ToValue({i - parameterCount}).Read<{x.FullTypeName}>();";
                        }
                    });

                    var call = $"{(method.HasReturnValue ? "var result = " : "")} " +
                        (method.IsAsync ? "await " : "") +
                        $"{(method.IsStatic ? library.FullTypeName : "this")}.{method.Name}({string.Join(", ", method.Parameters.Select((x, i) => $"arg{i}"))});";

                    var pushResult = method.HasReturnValue ? "state.Push(state.CreateFrom(result));" : "";

                    builder.AppendLine($"var function_{method.LuauMemberName} = state.CreateFunction({(method.IsAsync ? "async(state, ct)" : "state")} => ");
                    builder.AppendLine("{");
                    using (builder.BeginIndent())
                    {
                        foreach (var d in argsDeclarations)
                        {
                            builder.AppendLine(d);
                        }
                        builder.AppendLine(call);
                        builder.AppendLine(pushResult);
                        builder.AppendLine(method.HasReturnValue ? "return 1;" : "return 0;");
                    }
                    builder.AppendLine("});");
                }

                builder.AppendLine("metatable[\"__index\"] = state.CreateFunction(state =>");
                builder.AppendLine("{");
                using (builder.BeginIndent())
                {
                    builder.AppendLine("var table = state.ToTable(-2);");
                    builder.AppendLine("var key = state.ToString(-1);");
                    builder.AppendLine("var result = table.RawGet(key);");

                    using (builder.BeginBlock("if (!result.IsNil)"))
                    {
                        builder.AppendLine("state.Push(result);");
                        builder.AppendLine("return 1;");
                    }

                    using (builder.BeginBlock("switch (key)"))
                    {
                        foreach (var property in library.Properties)
                        {
                            if (property.HasGetter)
                            {
                                using (builder.BeginIndent($"case \"{property.LuauMemberName}\":"))
                                {
                                    builder.AppendLine($"state.Push(state.CreateFrom({(property.IsStatic ? library.FullTypeName : "this")}.{property.Name}));");
                                    builder.AppendLine("break;");
                                }
                            }
                            else
                            {
                                builder.AppendLine("throw new global::Luau.LuauException($\"cannot get set-only property '{key}'\");");
                            }
                        }

                        foreach (var method in library.Methods)
                        {
                            using (builder.BeginIndent($"case \"{method.LuauMemberName}\":"))
                            {
                                builder.AppendLine($"state.PushFunction(function_{method.LuauMemberName});");
                                builder.AppendLine("break;");
                            }
                        }

                        using (builder.BeginIndent("default:"))
                        {
                            builder.AppendLine("state.PushNil();");
                            builder.AppendLine("break;");
                        }
                    }

                    builder.AppendLine("return 1;");
                }
                builder.AppendLine("});");

                builder.AppendLine("metatable[\"__newindex\"] = state.CreateFunction(state =>");
                builder.AppendLine("{");
                using (builder.BeginIndent())
                {
                    builder.AppendLine("var table = state.ToTable(-3);");
                    builder.AppendLine("var key = state.ToString(-2);");
                    builder.AppendLine("var value = state.ToValue(-1);");

                    using (builder.BeginBlock("switch (key)"))
                    {
                        foreach (var property in library.Properties)
                        {
                            using (builder.BeginIndent($"case \"{property.LuauMemberName}\":"))
                            {
                                if (property.HasSetter)
                                {
                                    builder.AppendLine($"{(property.IsStatic ? library.FullTypeName : "this")}.{property.Name} = value.Read<{property.FullTypeName}>();");
                                    builder.AppendLine("break;");
                                }
                                else
                                {
                                    builder.AppendLine("throw new global::Luau.LuauException($\"cannot set readonly property '{key}'\");");
                                }
                            }
                        }

                        foreach (var method in library.Methods)
                        {
                            using (builder.BeginIndent($"case \"{method.LuauMemberName}\":"))
                            {
                                builder.AppendLine("throw new global::Luau.LuauException($\"cannot set readonly property '{key}'\");");
                            }
                        }

                        using (builder.BeginIndent("default:"))
                        {
                            builder.AppendLine("table.RawSet(key, value);");
                            builder.AppendLine("break;");
                        }
                    }

                    builder.AppendLine("return 0;");
                }
                builder.AppendLine("});");

                builder.AppendLine("state.SetMetatable(table, metatable);");
                builder.AppendLine($"state[\"{library.LibraryName}\"] = table;");
                builder.AppendLine("state.PushTable(table);");
            }
        }

        namespaceBlock?.Dispose();

        var source =
$$"""
// <auto-generated/ >

{{builder}}
""";

        var hintName = library.FullTypeName
            .Replace("global::", "")
            .Replace("<", "_")
            .Replace(">", "_");

        context.AddSource($"LuauLibrary.{hintName}.g.cs", source);
    }
}