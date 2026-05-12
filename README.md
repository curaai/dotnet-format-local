# pre-commit-format

Unity/C# 프로젝트에 **커밋 시 자동 포매팅**을 설정하는 유틸리티 모음입니다.  
`dotnet format`을 husky pre-commit 훅으로 연결하며, 설정 파일은 remote에 커밋되지 않습니다.

## 동작 방식

`git commit` 시 staged된 `*.cs` 파일에 대해 아래 포매팅이 자동 실행됩니다.

- `dotnet format style` — 불필요한 using 제거(IDE0005), file-scoped namespace 변환(IDE0161), Unity 성능 진단(UNT102, UNT103)

## 사전 조건

- Git for Windows (또는 Git Bash / WSL)
- Node.js + npm
- .NET SDK 6 이상 (`dotnet format` 내장)
- 대상 프로젝트 루트에 Unity가 생성한 `.sln` 파일

## 설치

```bash
bash apply-husky-user-gitconfig.sh <프로젝트-경로>
```

개발자 머신당 **1회만 실행**하면 됩니다. 이후 worktree를 추가해도 재실행 불필요.

```bash
# 예시
bash apply-husky-user-gitconfig.sh C:/Users/me/Projects/MyGame
bash apply-husky-user-gitconfig.sh /c/Users/me/Projects/MyGame
```

구버전 설정을 썼다면 `~/.gitconfig`의 `include` 경로가 `~/.config/dotnet-format-git/hooks-path.inc`를 가리키는지 확인하세요. 다르면 위 스크립트를 한 번 더 실행하거나 해당 줄을 고치면 됩니다.

### 설치 후 구성

| 위치 | 내용 | 커밋 여부 |
|---|---|---|
| `~/.gitconfig` | `includeIf`로 대상 프로젝트에만 `core.hooksPath = .husky` 적용 | X |
| `.git/husky-deps/` | lint-staged 바이너리 (git common dir, worktree 전체 공유) | X |
| `.git/info/exclude` | `.husky/`, `package.json`, `scripts/dotnet-format-staged.sh` 제외 | X |
| `<프로젝트>/.husky/pre-commit` | lint-staged 실행 훅 | X (로컬 전용) |
| `<프로젝트>/scripts/dotnet-format-staged.sh` | dotnet format 실행 스크립트 | X (로컬 전용) |
| `<프로젝트>/package.json` | lint-staged 설정 (`*.cs` → dotnet-format-staged.sh) | X (로컬 전용) |

설치 파일의 버전(`@pre-commit-format v<N>` 마커)이 installer와 같으면 건너뛰고, 다르면 자동으로 덮어씌웁니다.

## file-scoped namespace 적용

IDE0161(file-scoped namespace 변환)이 동작하려면 대상 프로젝트의 `.editorconfig`에 아래 설정이 있어야 합니다.

```ini
csharp_style_namespace_declarations = file_scoped:warning
dotnet_diagnostic.IDE0160.severity = none
```

## 유틸리티

### fixup-by-lastcommit.sh

변경된 파일 각각에 대해 해당 파일의 마지막 커밋을 타깃으로 `--fixup` 커밋을 생성합니다.  
이후 `git rebase -i --autosquash <base>` 로 정리할 때 사용합니다.

```bash
bash fixup-by-lastcommit.sh
```
