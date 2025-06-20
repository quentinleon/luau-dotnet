using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Luau.SourceGenerator;

internal static class RoslynExtensions
{
    public static bool TryGetAttribute(this ISymbol symbol, INamedTypeSymbol attributeType, [NotNullWhen(true)] out AttributeData? result)
    {
        var attributes = symbol.GetAttributes();
        result = symbol.GetAttributes().FirstOrDefault(x => SymbolEqualityComparer.Default.Equals(x.AttributeClass, attributeType));
        return result != null;
    }

    public static AttributeData GetAttribute(this ISymbol symbol, INamedTypeSymbol attributeType)
    {
        var attributes = symbol.GetAttributes();
        return symbol.GetAttributes().First(x => SymbolEqualityComparer.Default.Equals(x.AttributeClass, attributeType));
    }

    public static IEnumerable<ISymbol> GetAllMembers(this INamedTypeSymbol symbol, bool withoutOverride = true)
    {
        // Iterate Parent -> Derived
        if (symbol.BaseType != null)
        {
            foreach (var item in GetAllMembers(symbol.BaseType))
            {
                // override item already iterated in parent type
                if (!withoutOverride || !item.IsOverride)
                {
                    yield return item;
                }
            }
        }

        foreach (var item in symbol.GetMembers())
        {
            if (!withoutOverride || !item.IsOverride)
            {
                yield return item;
            }
        }
    }

    public static bool IsTaskType(this ITypeSymbol? symbol)
    {
        var name = symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return symbol != null && (
            name == "global::System.Threading.Tasks.Task" ||
            name == "global::System.Threading.Tasks.ValueTask" ||
            name == "global::Cysharp.Threading.Tasks.UniTask" ||
            name == "global::UnityEngine.Awaitable"
        );
    }
}