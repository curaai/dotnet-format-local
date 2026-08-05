#nullable enable

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MyAnalyzer;

/// <summary>
/// private 필드에 '_' 접두사가 없으면 UNT206을 보고한다.
/// IDE1006은 내장 NamingStyleCodeFixProvider가 FixAll을 지원하지 않아
/// dotnet format 이 고치지 못하므로, format 가능한 별도 진단으로 둔다.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PrivateFieldUnderscoreAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "UNT206";

    const string Title = "private 필드에 '_' 접두사가 없습니다";
    const string MessageFormat = "private 필드 '{0}' 이름을 '_{0}'(으)로 바꾸세요";
    const string Description =
        "private 필드는 _camelCase 네이밍을 사용합니다. IDE1006과 동일한 규칙이며 " +
        "dotnet format 이 자동 수정할 수 있도록 별도 진단 ID를 사용합니다.";
    const string Category = "Naming";

    static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeField, SymbolKind.Field);
    }

    static void AnalyzeField(SymbolAnalysisContext context)
    {
        var field = (IFieldSymbol)context.Symbol;
        if (field.IsImplicitlyDeclared || field.IsConst) return;
        if (field.DeclaredAccessibility != Accessibility.Private) return;
        if (field.Name.Length == 0 || field.Name[0] == '_') return;

        var location = field.Locations.Length > 0 ? field.Locations[0] : null;
        if (location is null) return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, location, field.Name));
    }
}
