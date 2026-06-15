#nullable enable

using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MyAnalyzer;

/// <summary>
/// UnityEngine.Object 파생 타입이 있는 소스는 file-scoped namespace를 쓰지 않도록 하고,
/// 그렇지 않은 단일 블록 namespace 소스는 file-scoped namespace를 쓰도록 유도한다.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class FileScopedNamespaceUnityAnalyzer : DiagnosticAnalyzer
{
    public const string NoFileScopedWhenUnityDiagnosticId = "UNT102";
    public const string PreferFileScopedDiagnosticId = "UNT103";

    const string NoFileScopedTitle = "UnityEngine.Object 파생 타입이 있을 때는 file-scoped namespace를 사용하지 마세요";
    const string NoFileScopedMessage = "이 파일에 UnityEngine.Object를 상속하는 타입이 있으므로 블록 형태의 namespace를 사용하세요.";
    const string NoFileScopedDescription =
        "Unity 직렬화·도구가 파일 단위 구문(file-scoped namespace)과 함께 쓰일 때 문제가 생기는 경우가 있어, Object 파생 타입이 있는 파일은 블록 namespace로 통일합니다.";

    const string PreferFileScopedTitle = "file-scoped namespace를 사용하세요";
    const string PreferFileScopedMessage = "UnityEngine.Object를 상속하는 타입이 없으므로 `namespace X;` 형태의 file-scoped namespace로 바꿀 수 있습니다.";
    const string PreferFileScopedDescription =
        "단일 블록 namespace만 있는 파일은 중괄호 없이 file-scoped namespace로 정리합니다.";

    const string Category = "Style";

    static readonly DiagnosticDescriptor NoFileScopedWhenUnityRule = new(
        NoFileScopedWhenUnityDiagnosticId,
        NoFileScopedTitle,
        NoFileScopedMessage,
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: NoFileScopedDescription);

    static readonly DiagnosticDescriptor PreferFileScopedRule = new(
        PreferFileScopedDiagnosticId,
        PreferFileScopedTitle,
        PreferFileScopedMessage,
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: PreferFileScopedDescription);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(NoFileScopedWhenUnityRule, PreferFileScopedRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSemanticModelAction(AnalyzeSemanticModel);
    }

    static void AnalyzeSemanticModel(SemanticModelAnalysisContext context)
    {
        if (context.SemanticModel.SyntaxTree.GetRoot(context.CancellationToken) is not CompilationUnitSyntax root)
            return;

        var model = context.SemanticModel;
        bool hasUnitySubtype = FileContainsUnityObjectSubtype(model, root, context.CancellationToken);
        bool hasFileScoped = root.Members.Any(static m => m is FileScopedNamespaceDeclarationSyntax);
        var blockNamespaces = root.Members.OfType<NamespaceDeclarationSyntax>().ToImmutableArray();

        if (hasFileScoped && hasUnitySubtype)
        {
            var fileScoped = root.Members.OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
            if (fileScoped != null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    NoFileScopedWhenUnityRule,
                    fileScoped.NamespaceKeyword.GetLocation()));
            }
        }

        if (hasFileScoped || blockNamespaces.Length != 1 || hasUnitySubtype)
            return;

        if (HasRootLevelTypeOrGlobalStatements(root))
            return;

        var singleNs = blockNamespaces[0];
        if (singleNs.Members.Any(static m => m is NamespaceDeclarationSyntax))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            PreferFileScopedRule,
            singleNs.NamespaceKeyword.GetLocation()));
    }

    static bool HasRootLevelTypeOrGlobalStatements(CompilationUnitSyntax root) =>
        root.Members.Any(static m =>
            m is BaseTypeDeclarationSyntax
            || m is DelegateDeclarationSyntax
            || m is EnumDeclarationSyntax
            || m is GlobalStatementSyntax);

    static bool FileContainsUnityObjectSubtype(
        SemanticModel model,
        CompilationUnitSyntax root,
        CancellationToken cancellationToken)
    {
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case ClassDeclarationSyntax cls:
                    if (model.GetDeclaredSymbol(cls, cancellationToken) is INamedTypeSymbol classSymbol
                        && UnityTypeSymbolHelper.InheritsFromUnityEngineObject(model.Compilation, classSymbol))
                        return true;
                    break;

                case RecordDeclarationSyntax record:
                    if (record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword))
                        break;
                    if (model.GetDeclaredSymbol(record, cancellationToken) is INamedTypeSymbol recordSymbol
                        && UnityTypeSymbolHelper.InheritsFromUnityEngineObject(model.Compilation, recordSymbol))
                        return true;
                    break;
            }
        }

        return false;
    }
}
