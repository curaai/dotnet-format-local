#nullable enable

using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MyAnalyzer;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TypeSummaryCodeFixProvider))]
[Shared]
public sealed class TypeSummaryCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(TypeSummaryAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var node = root.FindNode(context.Diagnostics[0].Location.SourceSpan);
        var typeDecl = node.FirstAncestorOrSelf<MemberDeclarationSyntax>(
            static n => n is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax);
        if (typeDecl is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "<summary> 주석 추가",
                createChangedDocument: ct => AddSummaryAsync(context.Document, typeDecl, ct),
                equivalenceKey: nameof(TypeSummaryCodeFixProvider)),
            context.Diagnostics[0]);
    }

    static async Task<Document> AddSummaryAsync(
        Document document,
        MemberDeclarationSyntax typeDecl,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document;

        var indent = GetIndentation(typeDecl);

        // 올바른 들여쓰기가 포함된 doc comment trivia 를 파싱으로 생성
        var templateSrc = $"{indent}/// <summary>\n{indent}/// \n{indent}/// </summary>\n{indent}class D {{}}";
        var dummyRoot = (CompilationUnitSyntax)SyntaxFactory.ParseSyntaxTree(templateSrc).GetRoot();
        var docTrivia = dummyRoot.Members[0].GetFirstToken().LeadingTrivia;

        var firstToken = typeDecl.GetFirstToken();
        var originalLeading = firstToken.LeadingTrivia;

        // 마지막 WhitespaceTrivia(키워드 직전 들여쓰기) 이전 trivia 를 유지하고 docTrivia 를 이어붙임
        int lastWsIdx = -1;
        for (int i = originalLeading.Count - 1; i >= 0; i--)
        {
            if (originalLeading[i].IsKind(SyntaxKind.WhitespaceTrivia))
            {
                lastWsIdx = i;
                break;
            }
        }

        var newLeading = SyntaxTriviaList.Empty;
        for (int i = 0; i < (lastWsIdx < 0 ? 0 : lastWsIdx); i++)
            newLeading = newLeading.Add(originalLeading[i]);
        newLeading = newLeading.AddRange(docTrivia);

        var newRoot = root.ReplaceToken(firstToken, firstToken.WithLeadingTrivia(newLeading));
        return document.WithSyntaxRoot(newRoot);
    }

    static string GetIndentation(SyntaxNode node)
    {
        var leading = node.GetFirstToken().LeadingTrivia;
        for (int i = leading.Count - 1; i >= 0; i--)
        {
            if (leading[i].IsKind(SyntaxKind.WhitespaceTrivia))
                return leading[i].ToString();
        }
        return string.Empty;
    }
}
