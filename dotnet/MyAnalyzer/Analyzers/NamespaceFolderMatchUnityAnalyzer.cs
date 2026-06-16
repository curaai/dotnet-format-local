#nullable enable

using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MyAnalyzer;

/// <summary>
/// Assets/Scripts 경로를 기준으로 네임스페이스가 폴더 구조와 일치하는지 확인합니다.
/// IDE0130의 Unity 프로젝트 버전: Assets/Scripts 이전 경로 세그먼트는 네임스페이스에서 제외합니다.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class NamespaceFolderMatchUnityAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "UNT202";
    public const string ExpectedNamespaceKey = "ExpectedNamespace";

    const string Title = "Unity 폴더 구조와 네임스페이스가 일치하지 않습니다";
    const string MessageFormat = "네임스페이스 '{0}'이(가) Unity 폴더 구조와 일치하지 않습니다. '{1}'이(가) 맞습니다";
    const string Description =
        "Assets/Scripts 경로를 기준으로 계산한 네임스페이스와 실제 선언이 다릅니다. " +
        "IDE0130의 Unity 프로젝트 버전으로, Assets 및 Scripts 세그먼트를 네임스페이스에서 제외합니다.";
    const string Category = "Style";

    static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeNamespace,
            SyntaxKind.NamespaceDeclaration,
            SyntaxKind.FileScopedNamespaceDeclaration);
    }

    static void AnalyzeNamespace(SyntaxNodeAnalysisContext context)
    {
        // 중첩 네임스페이스는 건너뜀 — 최상위만 확인
        if (context.Node.Parent is not CompilationUnitSyntax)
            return;

        var tree = context.Node.SyntaxTree;
        var filePath = tree.FilePath;
        if (string.IsNullOrEmpty(filePath))
            return;

        var configOptions = context.Options.AnalyzerConfigOptionsProvider.GetOptions(tree);
        configOptions.TryGetValue("build_property.RootNamespace", out var rootNamespace);
        if (string.IsNullOrEmpty(rootNamespace))
            return;

        var expected = ComputeExpectedNamespace(filePath, rootNamespace!);
        if (expected is null)
            return;

        var (actualName, nameSyntax) = context.Node switch
        {
            NamespaceDeclarationSyntax ns => (ns.Name.ToString().Trim(), (SyntaxNode)ns.Name),
            FileScopedNamespaceDeclarationSyntax fs => (fs.Name.ToString().Trim(), (SyntaxNode)fs.Name),
            _ => (string.Empty, (SyntaxNode)context.Node)
        };

        if (string.IsNullOrEmpty(actualName) || actualName == expected)
            return;

        var props = ImmutableDictionary<string, string?>.Empty.Add(ExpectedNamespaceKey, expected);
        context.ReportDiagnostic(Diagnostic.Create(Rule, nameSyntax.GetLocation(), props, actualName, expected));
    }

    /// <summary>
    /// filePath 에서 Unity 규칙에 따른 예상 네임스페이스를 계산합니다.
    /// Assets/Scripts/ 앵커를 기준으로 하고, 없으면 Assets/ 를 사용합니다.
    /// Assets 경로가 없으면 null 을 반환합니다.
    /// </summary>
    internal static string? ComputeExpectedNamespace(string filePath, string rootNamespace)
    {
        var path = filePath.Replace('\\', '/');

        int anchorEnd;
        var assetsScriptsIdx = FindPathAnchor(path, "Assets/Scripts/");
        if (assetsScriptsIdx >= 0)
        {
            anchorEnd = assetsScriptsIdx + "Assets/Scripts/".Length;
        }
        else
        {
            var assetsIdx = FindPathAnchor(path, "Assets/");
            if (assetsIdx < 0)
                return null;
            anchorEnd = assetsIdx + "Assets/".Length;
        }

        var relative = path.Substring(anchorEnd);
        var dirPart = Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? string.Empty;

        var segments = dirPart
            .Split('/')
            .Where(static s => !string.IsNullOrEmpty(s))
            .ToArray();

        // Assets/Scripts/GGrid/Runtime/ + RootNamespace=GGrid → 첫 세그먼트가 RootNamespace와
        // 같으면 제거해 GGrid.GGrid.Runtime 중복을 방지한다.
        if (segments.Length > 0 &&
            string.Equals(segments[0], rootNamespace, System.StringComparison.OrdinalIgnoreCase))
        {
            segments = segments.Skip(1).ToArray();
        }

        return segments.Length > 0
            ? rootNamespace + "." + string.Join(".", segments)
            : rootNamespace;
    }

    // path 안에서 anchor 가 경로 경계(앞이 '/' 또는 시작)에서 시작되는 첫 위치를 반환합니다.
    // "MyAssets/" 같은 부분 일치는 무시합니다.
    static int FindPathAnchor(string path, string anchor)
    {
        var start = 0;
        while (true)
        {
            var idx = path.IndexOf(anchor, start, System.StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return -1;
            if (idx == 0 || path[idx - 1] == '/')
                return idx;
            start = idx + 1;
        }
    }
}
