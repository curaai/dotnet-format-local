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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NamespaceFolderMatchUnityCodeFixProvider))]
[Shared]
public sealed class NamespaceFolderMatchUnityCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(NamespaceFolderMatchUnityAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(
                    NamespaceFolderMatchUnityAnalyzer.ExpectedNamespaceKey, out var expected)
                || string.IsNullOrEmpty(expected))
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: $"네임스페이스를 '{expected}'으로 변경",
                    createChangedDocument: ct => FixNamespaceAsync(context.Document, diagnostic, expected!, ct),
                    equivalenceKey: nameof(FixNamespaceAsync)),
                diagnostic);
        }

        return Task.CompletedTask;
    }

    static async Task<Document> FixNamespaceAsync(
        Document document,
        Diagnostic diagnostic,
        string expectedNamespace,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document;

        // 진단 위치는 Name 노드 → 부모가 BaseNamespaceDeclarationSyntax
        var nameNode = root.FindNode(diagnostic.Location.SourceSpan);
        var nsDecl = nameNode.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>();
        if (nsDecl is null) return document;

        var newName = SyntaxFactory.ParseName(expectedNamespace)
            .WithLeadingTrivia(nsDecl.Name.GetLeadingTrivia())
            .WithTrailingTrivia(nsDecl.Name.GetTrailingTrivia());

        SyntaxNode newNsDecl = nsDecl switch
        {
            NamespaceDeclarationSyntax ns => ns.WithName(newName),
            FileScopedNamespaceDeclarationSyntax fs => fs.WithName(newName),
            _ => nsDecl
        };

        return document.WithSyntaxRoot(root.ReplaceNode(nsDecl, newNsDecl));
    }
}
