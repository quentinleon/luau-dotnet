using Microsoft.CodeAnalysis;

namespace Luau.SourceGenerator;

internal sealed class DiagnosticReporter
{
    List<Diagnostic>? diagnostics;

    public bool HasDiagnostics => diagnostics != null && diagnostics.Count != 0;

    public void ReportDiagnostic(DiagnosticDescriptor diagnosticDescriptor, Location location, params object?[]? messageArgs)
    {
        var diagnostic = Diagnostic.Create(diagnosticDescriptor, location, messageArgs);
        diagnostics ??= [];
        diagnostics.Add(diagnostic);
    }

    public void ReportToContext(SourceProductionContext context)
    {
        if (diagnostics != null)
        {
            foreach (var item in diagnostics)
            {
                context.ReportDiagnostic(item);
            }
        }
    }
}

public static class DiagnosticDescriptors
{
    const string Category = "LuauSourceGeneration";

    public static void ReportDiagnostic(this SourceProductionContext context, DiagnosticDescriptor diagnosticDescriptor, Location location, params object?[]? messageArgs)
    {
        var diagnostic = Diagnostic.Create(diagnosticDescriptor, location, messageArgs);
        context.ReportDiagnostic(diagnostic);
    }

    public static DiagnosticDescriptor Create(int id, string message)
    {
        return Create(id, message, message);
    }

    public static DiagnosticDescriptor Create(int id, string title, string messageFormat)
    {
        return new DiagnosticDescriptor(
            id: "LUAU" + id.ToString("000"),
            title: title,
            messageFormat: messageFormat,
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }

    public static DiagnosticDescriptor MustBePartial { get; } = Create(
        1,
        "LuauObject type must be partial.");

    public static DiagnosticDescriptor DefinedInOtherProject { get; } = Create(
        2,
        "LuauSharp cannot register type/method in another project outside the SourceGenerator referenced project.");

    public static DiagnosticDescriptor ArgumentMustBeMethodGroupOrLambda { get; } = Create(
        3,
        "The argument to CreateFunction must be a lambda expression or a method group.");
}