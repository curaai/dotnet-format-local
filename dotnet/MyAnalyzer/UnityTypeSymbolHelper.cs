#nullable enable

using Microsoft.CodeAnalysis;

namespace Analyzer;

public static class UnityTypeSymbolHelper
{
    public static bool InheritsFromUnityEngineObject(Compilation compilation, ITypeSymbol? type)
    {
        if (type == null)
            return false;

        var unityObjectType = compilation.GetTypeByMetadataName("UnityEngine.Object");
        if (unityObjectType == null)
            return false;

        type = UnwrapArraysAndNullableT(type);

        for (var t = type; t != null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t, unityObjectType))
                return true;
        }

        return false;
    }

    static ITypeSymbol UnwrapArraysAndNullableT(ITypeSymbol type)
    {
        while (true)
        {
            if (type is IArrayTypeSymbol arr)
            {
                type = arr.ElementType;
                continue;
            }

            if (type is INamedTypeSymbol named
                && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                && named.TypeArguments.Length == 1)
            {
                type = named.TypeArguments[0];
                continue;
            }

            break;
        }

        return type;
    }
}
