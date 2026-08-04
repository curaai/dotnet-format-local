#nullable enable

using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Rename;

namespace MyAnalyzer;

/// <summary>
/// IDE1006(Naming Rule Violation) 중 "Missing prefix: '_'" 케이스를 고쳐주는 CodeFix.
/// 내장 IDE1006 진단은 여러 네이밍 규칙 위반을 하나의 ID로 묶어 보고하므로,
/// 진단 메시지 문자열로 "밑줄 접두사 누락" 케이스만 걸러서 처리한다.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingUnderscorePrefixCodeFixProvider))]
[Shared]
public sealed class MissingUnderscorePrefixCodeFixProvider : CodeFixProvider
{
    const string DiagnosticId = "IDE1006";
    const string MissingUnderscorePrefixMessage = "Missing prefix: '_'";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.GetMessage().Contains(MissingUnderscorePrefixMessage))
                continue;

            var token = root.FindToken(diagnostic.Location.SourceSpan.Start);
            if (token.Parent is null) continue;

            var symbol = semanticModel.GetDeclaredSymbol(token.Parent, context.CancellationToken);
            if (symbol is null || symbol.Name.Length == 0) continue;

            var newName = "_" + symbol.Name;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: $"이름을 '{newName}'(으)로 변경",
                    createChangedSolution: ct => RenameSymbolAsync(context.Document, symbol, newName, ct),
                    equivalenceKey: nameof(MissingUnderscorePrefixCodeFixProvider)),
                diagnostic);
        }
    }

    static async Task<Solution> RenameSymbolAsync(
        Document document,
        ISymbol symbol,
        string newName,
        CancellationToken cancellationToken)
    {
        return await Renamer.RenameSymbolAsync(
            document.Project.Solution,
            symbol,
            new SymbolRenameOptions(),
            newName,
            cancellationToken).ConfigureAwait(false);
    }
}
