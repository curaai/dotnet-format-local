#nullable enable

using System;
using System.Collections.Generic;
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

namespace MyAnalyzer;

/// <summary>
/// UNT206(private 필드 '_' 접두사 누락)을 문서 내 심볼 참조 치환으로 고친다.
/// IDE1006용 내장 NamingStyleCodeFixProvider는 FixAll을 지원하지 않아 format에 쓰이지 않는다.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingUnderscorePrefixCodeFixProvider))]
[Shared]
public sealed class MissingUnderscorePrefixCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(PrivateFieldUnderscoreAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var token = root.FindToken(diagnostic.Location.SourceSpan.Start);
            if (token.Parent is null) continue;

            var symbol = semanticModel.GetDeclaredSymbol(token.Parent, context.CancellationToken);
            if (symbol is null || symbol.Name.Length == 0) continue;
            if (symbol.Name.StartsWith("_", StringComparison.Ordinal)) continue;

            var newName = "_" + symbol.Name;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: $"이름을 '{newName}'(으)로 변경",
                    createChangedDocument: ct => RenameInDocumentAsync(context.Document, symbol, newName, ct),
                    equivalenceKey: nameof(MissingUnderscorePrefixCodeFixProvider)),
                diagnostic);
        }
    }

    static async Task<Document> RenameInDocumentAsync(
        Document document,
        ISymbol symbol,
        string newName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null) return document;

        var tokens = new HashSet<SyntaxToken>();

        foreach (var declarator in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            var declared = model.GetDeclaredSymbol(declarator, cancellationToken);
            if (SymbolEqualityComparer.Default.Equals(declared, symbol))
                tokens.Add(declarator.Identifier);
        }

        foreach (var name in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var referenced = model.GetSymbolInfo(name, cancellationToken).Symbol;
            if (SymbolEqualityComparer.Default.Equals(referenced, symbol))
                tokens.Add(name.Identifier);
        }

        if (tokens.Count == 0) return document;

        var newRoot = root.ReplaceTokens(
            tokens,
            (original, _) => SyntaxFactory.Identifier(
                original.LeadingTrivia,
                newName,
                original.TrailingTrivia));

        return document.WithSyntaxRoot(newRoot);
    }
}
