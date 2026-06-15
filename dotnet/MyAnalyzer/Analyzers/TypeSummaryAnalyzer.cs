#nullable enable

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MyAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class TypeSummaryAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "UNT201";

    const string Title = "타입에 <summary> 문서화 주석이 없습니다";
    const string MessageFormat = "'{0}'에 <summary> XML 문서화 주석을 추가하세요";
    const string Description = "모든 타입 선언(class·struct·interface·enum·record·delegate)에는 " +
                               "<summary> 태그가 있는 XML 문서화 주석이 있어야 합니다 (SA1600 참조).";
    const string Category = "Documentation";

    static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeNode,
            SyntaxKind.ClassDeclaration,
            SyntaxKind.StructDeclaration,
            SyntaxKind.InterfaceDeclaration,
            SyntaxKind.EnumDeclaration,
            SyntaxKind.RecordDeclaration,
            SyntaxKind.RecordStructDeclaration,
            SyntaxKind.DelegateDeclaration);
    }

    static void AnalyzeNode(SyntaxNodeAnalysisContext context)
    {
        SyntaxToken identifier;
        SyntaxTokenList modifiers;

        switch (context.Node)
        {
            case BaseTypeDeclarationSyntax typeDecl:
                identifier = typeDecl.Identifier;
                modifiers = typeDecl.Modifiers;
                break;
            case DelegateDeclarationSyntax delegateDecl:
                identifier = delegateDecl.Identifier;
                modifiers = delegateDecl.Modifiers;
                break;
            default:
                return;
        }

        // private 타입은 제외
        if (modifiers.Any(SyntaxKind.PrivateKeyword))
            return;

        if (HasSummaryOrInheritdoc(context.Node))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            identifier.GetLocation(),
            identifier.Text));
    }

    // code fix 에서도 사용
    internal static bool HasSummaryOrInheritdoc(SyntaxNode node)
    {
        foreach (var trivia in node.GetLeadingTrivia())
        {
            if (trivia.Kind() is not SyntaxKind.SingleLineDocumentationCommentTrivia
                              and not SyntaxKind.MultiLineDocumentationCommentTrivia)
                continue;

            if (trivia.GetStructure() is not DocumentationCommentTriviaSyntax docComment)
                continue;

            if (docComment.Content.OfType<XmlElementSyntax>()
                    .Any(static e => e.StartTag.Name.LocalName.Text == "summary"))
                return true;

            // <inheritdoc/> 도 문서화 의도로 간주
            if (docComment.Content.OfType<XmlEmptyElementSyntax>()
                    .Any(static e => e.Name.LocalName.Text == "inheritdoc"))
                return true;
        }

        return false;
    }
}
