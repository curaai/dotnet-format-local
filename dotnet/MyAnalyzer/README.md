## Dinkum.CodeAnalyzers

### 배포

릴리즈 빌드를 돌려서 나온 dll은 유니티 작업경로로 복사된다.

`dotnet/Analyzer/` 에서

```sh
dotnet build --configuration Release
```

출력 DLL 이름은 `CustomAnalyzer.dll` 이다. PostBuild로 `Assets/Plugins/Editor/Analyzers/`에 복사된다. `dotnet/UnitGenerator.Custom/` Release 빌드도 동일 경로에 `CustomGenerator.dll`을 복사한다.

진단 ID는 Roslyn 규칙상 **하이픈 없이** C# 식별자로 쓸 수 있는 문자열만 허용된다 (예: `UNT102`, `UNT103`. `UNT-102`는 불가).

- `UNT104` / `UNT105`: `[UnitOf]`와 `[UnitOfCustom(typeof(T), UnitGenerationCustomOptions.CsvMapGen)]` 정합성 (`dotnet/UnitGenerator.Custom/Analyzers/UnitOfRequiresCsvMapGenAnalyzer.cs`, `CustomGenerator.dll`에 포함).

Unity에서 `Microsoft.CodeAnalysis` 참조 오류가 나면 `CustomAnalyzer.dll`이 **일반 플러그인**으로 로드된 것이다. `CustomAnalyzer.dll.meta`에 다음이 있어야 한다.

- 루트에 에셋 레이블 **`RoslynAnalyzer`** (대소문자 동일)
- PluginImporter: **`validateReferences: 0`**
- **Any Platform / Editor / Standalone** 포함은 모두 끔 (컴파일러 전용 분석기로만 쓰임). 자세한 것은 Unity 매뉴얼 [Install and use an existing analyzer](https://docs.unity3d.com/Manual/install-existing-analyzer.html) 참고.

.editorconfig로는 설정할 수 없는 컴파일 warning, error를 처리하기 위한 Roslyn 확장
배포 경로 일원화를 위해 병후님과 이야기하여 CompilerMagic에 기생합니다.

### CodeFix (IDE)

`Analyzer/CodeFixes/` 프로젝트를 빌드하면 `Analyzer.CodeFixes.dll` 이 생성된다. Unity 컴파일러에는 넣지 않고, Rider/Visual Studio용으로 사용한다.

### 한계

#### DiagnosticSuppressor 버그

VS Code - C# 확장의 버그로 (`2.50.27`...`2.59.14 (prerelease)`)에서 `DiagnosticSuppressor`가 동작하지 않음
시험 버전으로 전환하여 `2.60.26` 버전에서는 정상 동작하는 것을 확인함.

#### internal DiagnosticAnalyzer

내장 `DiagnosticAnalyzer`가 죄다 internal이라서 유닛 테스트 같은걸 해볼 수가 없었음

### SerializeFieldReadonlySuppressor

[UNT0013](https://github.com/microsoft/Microsoft.Unity.Analyzers/blob/main/doc/UNT0013.md)과 [IDE0044](https://learn.microsoft.com/ko-kr/dotnet/fundamentals/code-analysis/style-rules/ide0044)가 서로 배타적으로 동작하는 상황에서 IDE0044를 끕니다.

Before:

```C#
string _notAssigned; // Info: IDE0044 triggered
string _assigned; // none
readonly string _readonlyNotAssigned; // none

[SerializeField] string _notAssignedWithSerializeField; // Info: IDE0044 triggereds
[SerializeField] string _assignedWithSerializeField; // none
[SerializeField] readonly string _readonlyNotAssignedWithSerializeField; // none
void AssignTestField()
{
    _assigned = "IDE0044 not triggered";
    _assignedWithSerializeField = "IDE0044 not triggered";
}
```

After:

```C#
string _notAssigned; // Info: IDE0044 triggered
string _assigned; // none
readonly string _readonlyNotAssigned; // none

[SerializeField] string _notAssignedWithSerializeField; // none <- changed
[SerializeField] string _assignedWithSerializeField; // none
[SerializeField] readonly string _readonlyNotAssignedWithSerializeField; // Error: UNT0013 triggered <- changed
void AssignTestField()
{
    _assigned = "IDE0044 not triggered";
    _assignedWithSerializeField = "IDE0044 not triggered";
}
```
