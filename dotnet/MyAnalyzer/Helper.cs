#nullable enable

using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MyAnalyzer;

public class Helper
{
    public static ITypeSymbol GetTypeSymbol(Compilation compilation, TypeSyntax typeSyntax, bool ignoreTask = false)
    {

        var model = compilation.GetSemanticModel(typeSyntax.SyntaxTree);
        if (model.GetSymbolInfo(typeSyntax).Symbol is not ITypeSymbol typeSymbol)
            throw new Exception("can not get typeSymbol.");

        return typeSymbol;
    }

    public static string AsTaskResultFullName(ITypeSymbol typeSymbol)
    {
        if (typeSymbol.Name == "Task" || typeSymbol.Name == "UniTask")
        {
            if (typeSymbol is INamedTypeSymbol namedTypeSymbol && namedTypeSymbol.IsGenericType)
            {
                var innerType = namedTypeSymbol.TypeArguments.First();
                return AsFullName(innerType);
            }
            throw new Exception("unknown error");
        }

        return AsFullName(typeSymbol);
    }


    public static string AsFullName(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is IArrayTypeSymbol arrayTypeSymbol)
        {
            var innerTypeStr = AsFullName(arrayTypeSymbol.ElementType);
            return $"{innerTypeStr}[]";
        }

        if (typeSymbol.ContainingNamespace.IsGlobalNamespace)
        {
            return typeSymbol.Name;
        }

        else if (typeSymbol.ContainingNamespace.ToDisplayString() == "System")
        {
            // bool, int 같은건 Boolean, String, Int32 같은 풀네임으로 바꾸고싶지 않다.
            // 코드에 입력된 타입 그대로 쓰고싶다.
            return typeSymbol.ToString();
        }

        if (typeSymbol is INamedTypeSymbol namedTypeSymbol && namedTypeSymbol.IsGenericType)
        {
            if (typeSymbol.Name == "UnaryResult")
            {
                var innerTypes = namedTypeSymbol.TypeArguments.Select(AsFullName);
                var result = $"Task<{string.Join(", ", innerTypes)}>";
                return result;
            }
        }

        return typeSymbol.ToDisplayString();
    }

    public static string? GetNamespaceText(Compilation compilation, InterfaceDeclarationSyntax syntax)
    {
        var typeSymbol = compilation.GetSemanticModel(syntax.SyntaxTree).GetDeclaredSymbol(syntax);
        if (typeSymbol == null)
            throw new Exception("can not get typeSymbol.");

        var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : typeSymbol.ContainingNamespace.ToDisplayString();
        return ns;
    }
}
