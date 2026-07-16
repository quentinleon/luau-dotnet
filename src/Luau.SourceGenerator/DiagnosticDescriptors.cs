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
        "LuauLibrary type must be partial.");

    public static readonly DiagnosticDescriptor NestedNotAllowed = Create(
        2,
        "LuauLibrary type must not be nested");

    public static readonly DiagnosticDescriptor AbstractNotAllowed = Create(
        3,
        "LuauLibrary type must not be abstract");

    public static DiagnosticDescriptor DuplicateMemberName { get; } = Create(
        4,
        "Duplicate Luau member name",
        "Luau member name '{0}' is used more than once in the same host library.");

    public static DiagnosticDescriptor UnsupportedSignature { get; } = Create(
        5,
        "Unsupported Luau host member signature",
        "Luau host member '{0}' is unsupported: {1}");

    public static DiagnosticDescriptor InvalidName { get; } = Create(
        6,
        "Invalid Luau host name",
        "Luau {0} name must not be null or contain a NUL character.");
}
