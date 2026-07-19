using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Luau.SourceGenerator;

internal sealed record LuauGeneratorLocation
{
    public required string SourcePath { get; init; }
    public required int SpanStart { get; init; }
    public required int SpanLength { get; init; }
    public required int StartLine { get; init; }
    public required int StartCharacter { get; init; }
    public required int EndLine { get; init; }
    public required int EndCharacter { get; init; }

    public static LuauGeneratorLocation? FromLocation(Location location)
    {
        if (!location.IsInSource)
        {
            return null;
        }

        var lineSpan = location.GetLineSpan();
        return new LuauGeneratorLocation
        {
            SourcePath = lineSpan.Path,
            SpanStart = location.SourceSpan.Start,
            SpanLength = location.SourceSpan.Length,
            StartLine = lineSpan.StartLinePosition.Line,
            StartCharacter = lineSpan.StartLinePosition.Character,
            EndLine = lineSpan.EndLinePosition.Line,
            EndCharacter = lineSpan.EndLinePosition.Character,
        };
    }

    public Location ToLocation() => Location.Create(
        SourcePath,
        new TextSpan(SpanStart, SpanLength),
        new LinePositionSpan(
            new LinePosition(StartLine, StartCharacter),
            new LinePosition(EndLine, EndCharacter)));
}

internal sealed record LuauGeneratorDiagnostic
{
    public required string DescriptorId { get; init; }
    public required LuauGeneratorLocation? Location { get; init; }
    public required EquatableArray<string> MessageArguments { get; init; }

    public Diagnostic ToDiagnostic() => Diagnostic.Create(
        DiagnosticDescriptors.GetDescriptor(DescriptorId),
        Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None,
        MessageArguments.Select(static argument => (object?)argument).ToArray());
}

public static class DiagnosticDescriptors
{
    const string Category = "LuauSourceGeneration";

    internal static void ReportDiagnostic(
        this List<LuauGeneratorDiagnostic> diagnostics,
        DiagnosticDescriptor descriptor,
        Location location,
        params string[] messageArguments)
    {
        diagnostics.Add(new LuauGeneratorDiagnostic
        {
            DescriptorId = descriptor.Id,
            Location = LuauGeneratorLocation.FromLocation(location),
            MessageArguments = messageArguments,
        });
    }

    internal static DiagnosticDescriptor GetDescriptor(string id) => id switch
    {
        "LUAU001" => MustBePartial,
        "LUAU002" => NestedNotAllowed,
        "LUAU003" => AbstractNotAllowed,
        "LUAU004" => DuplicateMemberName,
        "LUAU005" => UnsupportedSignature,
        "LUAU006" => InvalidName,
        "LUAU007" => InvalidExposure,
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown Luau generator diagnostic."),
    };

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

    public static DiagnosticDescriptor InvalidExposure { get; } = Create(
        7,
        "Invalid Luau library exposure",
        "Luau library exposure value '{0}' is not supported.");
}
