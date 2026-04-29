#nullable enable

// dotnet/roslyn ConvertNamespace 변환을 MIT 라이선스에 따라 단순화·이식.
// 복잡한 티비아 이전은 생략하고 Formatter.FormatAsync로 마무리한다.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Analyzer.CodeFixes;

internal static class NamespaceSyntaxConversion
{
    public static SyntaxNode ReplaceBlockWithFileScoped(SyntaxNode root, NamespaceDeclarationSyntax namespaceDeclaration)
    {
        var semi = SyntaxFactory.Token(SyntaxKind.SemicolonToken)
            .WithTrailingTrivia(namespaceDeclaration.OpenBraceToken.LeadingTrivia);

        if (!TriviaIsWhitespaceOnly(namespaceDeclaration.OpenBraceToken.TrailingTrivia))
            semi = semi.WithTrailingTrivia(semi.TrailingTrivia.AddRange(namespaceDeclaration.OpenBraceToken.TrailingTrivia));

        var fileScoped = SyntaxFactory.FileScopedNamespaceDeclaration(
            namespaceDeclaration.AttributeLists,
            namespaceDeclaration.Modifiers,
            namespaceDeclaration.NamespaceKeyword,
            namespaceDeclaration.Name.WithoutTrailingTrivia(),
            semi,
            namespaceDeclaration.Externs,
            namespaceDeclaration.Usings,
            namespaceDeclaration.Members);

        var closeTrivia = namespaceDeclaration.CloseBraceToken.LeadingTrivia.AddRange(
            namespaceDeclaration.CloseBraceToken.TrailingTrivia);

        fileScoped = fileScoped.WithTrailingTrivia(closeTrivia);

        return root.ReplaceNode(namespaceDeclaration, fileScoped);
    }

    public static SyntaxNode ReplaceFileScopedWithBlock(
        SyntaxNode root,
        FileScopedNamespaceDeclarationSyntax fileScoped,
        string lineEnding,
        bool newLineBeforeOpenBrace)
    {
        var nameTrailing = fileScoped.Name.GetTrailingTrivia()
            .AddRange(fileScoped.SemicolonToken.LeadingTrivia);

        if (newLineBeforeOpenBrace)
            nameTrailing = nameTrailing.Add(SyntaxFactory.EndOfLine(lineEnding));
        else
            nameTrailing = nameTrailing.Add(SyntaxFactory.Space);

        var name = fileScoped.Name.WithoutTrailingTrivia().WithTrailingTrivia(nameTrailing);

        var openBraceTrailing = fileScoped.SemicolonToken.TrailingTrivia;
        if (!TrailingTriviaEndsWithEndOfLine(openBraceTrailing))
            openBraceTrailing = openBraceTrailing.Add(SyntaxFactory.EndOfLine(lineEnding));

        var openBrace = SyntaxFactory.Token(SyntaxKind.OpenBraceToken).WithTrailingTrivia(openBraceTrailing);

        var closeBrace = SyntaxFactory.Token(SyntaxKind.CloseBraceToken)
            .WithTrailingTrivia(SyntaxFactory.EndOfLine(lineEnding));

        var block = SyntaxFactory.NamespaceDeclaration(
            fileScoped.AttributeLists,
            fileScoped.Modifiers,
            fileScoped.NamespaceKeyword,
            name,
            openBrace,
            fileScoped.Externs,
            fileScoped.Usings,
            fileScoped.Members,
            closeBrace,
            default);

        return root.ReplaceNode(fileScoped, block);
    }

    static bool TriviaIsWhitespaceOnly(SyntaxTriviaList list)
    {
        foreach (var t in list)
        {
            if (!t.IsKind(SyntaxKind.WhitespaceTrivia) && !t.IsKind(SyntaxKind.EndOfLineTrivia))
                return false;
        }

        return true;
    }

    static bool TrailingTriviaEndsWithEndOfLine(SyntaxTriviaList list)
    {
        for (var i = list.Count - 1; i >= 0; i--)
        {
            var t = list[i];
            if (t.IsKind(SyntaxKind.EndOfLineTrivia))
                return true;
            if (t.IsKind(SyntaxKind.WhitespaceTrivia))
                continue;
            return false;
        }

        return false;
    }
}
