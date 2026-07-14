using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

static class LuauTypeHelper
{
    public static string GetLuauType(TypeSyntax typeSyntax)
    {
        if (typeSyntax is PredefinedTypeSyntax predefinedType)
        {
            return ConvertPredefinedType(predefinedType);
        }
        else if (typeSyntax is NullableTypeSyntax nullableType)
        {
            var innerType = GetLuauType(nullableType.ElementType);
            return $"{innerType}?";
        }
        else if (typeSyntax is ArrayTypeSyntax arrayType)
        {
            var elementType = GetLuauType(arrayType.ElementType);
            return $"{{ [number]: {elementType} }}";
        }
        else if (typeSyntax is GenericNameSyntax genericName)
        {
            return ConvertGenericType(genericName);
        }
        else if (typeSyntax is IdentifierNameSyntax identifierName)
        {
            string name = identifierName.Identifier.Text;
            return name switch
            {
                "String" => "string",
                "Object" => "any",
                "Boolean" => "boolean",
                "Int32" or "Int64" or "Double" or "Single" or "Decimal" => "number",
                "Void" => "()",
                _ => "any",
            };
        }
        else
        {
            return "any";
        }
    }

    static string ConvertPredefinedType(PredefinedTypeSyntax predefinedType)
    {
        return predefinedType.Keyword.Kind() switch
        {
            SyntaxKind.BoolKeyword => "boolean",
            SyntaxKind.ByteKeyword or SyntaxKind.SByteKeyword or SyntaxKind.ShortKeyword or SyntaxKind.UShortKeyword or SyntaxKind.IntKeyword or SyntaxKind.UIntKeyword or SyntaxKind.LongKeyword or SyntaxKind.ULongKeyword or SyntaxKind.FloatKeyword or SyntaxKind.DoubleKeyword or SyntaxKind.DecimalKeyword => "number",
            SyntaxKind.StringKeyword or SyntaxKind.CharKeyword => "string",
            SyntaxKind.ObjectKeyword => "any",
            SyntaxKind.VoidKeyword => "()",
            _ => "any",
        };
    }

    static string ConvertGenericType(GenericNameSyntax genericName)
    {
        string baseTypeName = genericName.Identifier.Text;
        string luauTypeParameters = string.Join(", ", genericName.TypeArgumentList.Arguments.Select(GetLuauType));

        switch (baseTypeName)
        {
            case "List":
            case "IList":
            case "IEnumerable":
            case "ICollection":
            case "ArraySegment":
                return $"{{ [number]: {luauTypeParameters} }}"; // { [number]: T }
            case "Dictionary":
            case "IDictionary":
                var args = genericName.TypeArgumentList.Arguments.ToList();
                if (args.Count == 2)
                {
                    string keyType = GetLuauType(args[0]);
                    string valueType = GetLuauType(args[1]);
                    return $"{{ [{keyType}]: {valueType} }}"; // { [TKey]: TValue }
                }
                break;
            case "Nullable":
                return $"{luauTypeParameters}?";
            case "Task":
                if (genericName.TypeArgumentList.Arguments.Any())
                {
                    return GetLuauType(genericName.TypeArgumentList.Arguments.First());
                }
                else
                {
                    return "()";
                }
        }

        return "any";
    }
}