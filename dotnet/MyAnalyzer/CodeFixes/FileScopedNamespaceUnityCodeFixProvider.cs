#nullable enable

using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
namespace MyAnalyzer;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(FileScopedNamespaceUnityCodeFixProvider))]
[Shared]
public sealed class FileScopedNamespaceUnityCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(
        FileScopedNamespaceUnityAnalyzer.NoFileScopedWhenUnityDiagnosticId,
        FileScopedNamespaceUnityAnalyzer.PreferFileScopedDiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!TryGetBaseNamespace(root, diagnostic.Location.SourceSpan, out var baseNs))
                continue;

            if (diagnostic.Id == FileScopedNamespaceUnityAnalyzer.NoFileScopedWhenUnityDiagnosticId
                && baseNs is FileScopedNamespaceDeclarationSyntax fileScoped)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "블록 namespace로 변환",
                        createChangedDocument: ct => ConvertToBlockScopedAsync(context.Document, fileScoped, ct),
                        equivalenceKey: nameof(ConvertToBlockScopedAsync)),
                    diagnostic);
            }
            else if (diagnostic.Id == FileScopedNamespaceUnityAnalyzer.PreferFileScopedDiagnosticId
                     && baseNs is NamespaceDeclarationSyntax blockScoped)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "file-scoped namespace로 변환",
                        createChangedDocument: ct => ConvertToFileScopedAsync(context.Document, blockScoped, ct),
                        equivalenceKey: nameof(ConvertToFileScopedAsync)),
                    diagnostic);
            }
        }
    }

    static bool TryGetBaseNamespace(SyntaxNode root, TextSpan span, out BaseNamespaceDeclarationSyntax? baseNs)
    {
        var node = root.FindNode(span, getInnermostNodeForTie: true);
        baseNs = node as BaseNamespaceDeclarationSyntax
                 ?? node.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>();
        return baseNs != null;
    }

    static async Task<Document> ConvertToFileScopedAsync(
        Document document,
        NamespaceDeclarationSyntax blockScoped,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false)
                   ?? throw new InvalidOperationException("Syntax root missing.");
        var newRoot = NamespaceSyntaxConversion.ReplaceBlockWithFileScoped(root, blockScoped);
        var newDoc = document.WithSyntaxRoot(newRoot);
        return await Formatter.FormatAsync(newDoc, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    static async Task<Document> ConvertToBlockScopedAsync(
        Document document,
        FileScopedNamespaceDeclarationSyntax fileScoped,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false)
                   ?? throw new InvalidOperationException("Syntax root missing.");

        var newRoot = NamespaceSyntaxConversion.ReplaceFileScopedWithBlock(
            root,
            fileScoped,
            Environment.NewLine,
            newLineBeforeOpenBrace: true);

        var newDoc = document.WithSyntaxRoot(newRoot);
        return await Formatter.FormatAsync(newDoc, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
