# CLAUDE.md

이 파일은 이 저장소에서 작업할 때 Claude Code(claude.ai/code)에게 제공되는 안내 문서입니다.

## 목적

Unity/C# 프로젝트에 pre-commit 자동 포매팅을 설정하는 유틸리티 모음입니다. `lint-staged` + `dotnet format`을 husky pre-commit 훅으로 설치하며, 대상 프로젝트의 모든 git worktree에서 공유됩니다.

## 파일 구조

```
apply-husky-user-gitconfig.sh   # 메인 설정 스크립트 (개발자 머신당 1회 실행)
installer/
  husky-pre-commit.sh           # → 대상 프로젝트 .husky/pre-commit 으로 복사
  dotnet-format-staged.sh       # → 대상 프로젝트 scripts/dotnet-format-staged.sh 으로 복사
  root-package.json             # → 대상 프로젝트 package.json 으로 복사
utils/
  fixup-by-lastcommit.sh        # 독립 유틸리티 (husky 설정과 무관)
```

## 스크립트

| 스크립트 | 역할 |
|---|---|
| `apply-husky-user-gitconfig.sh` | **메인 설정 스크립트.** 개발자 머신당 1회 실행하여 전체 설정을 완료합니다. |
| `installer/husky-pre-commit.sh` | `.husky/pre-commit` 템플릿 — 대상 프로젝트에 복사됩니다. |
| `installer/root-package.json` | `package.json` 템플릿 — 대상 프로젝트에 없을 경우 복사됩니다. |
| `installer/dotnet-format-staged.sh` | `scripts/dotnet-format-staged.sh` 템플릿 — 스테이징된 `*.cs` 파일에 `dotnet format`을 실행합니다. |
| `utils/fixup-by-lastcommit.sh` | 독립 유틸리티: 변경된 각 파일에 대해 해당 파일의 마지막 커밋을 대상으로 `--fixup` 커밋을 생성합니다. |

## 설정 방법

```bash
bash apply-husky-user-gitconfig.sh <대상-저장소-경로>
```

스크립트 동작 순서 (4단계):
1. `~/.gitconfig`에 `[includeIf "gitdir/i:<저장소-루트>/"]` 항목을 추가하여 해당 저장소 안에서만 `core.hooksPath = .husky`가 적용되도록 합니다.
2. 저장소 자체 git config에 설정된 `core.hooksPath`를 제거합니다(사용자 include로 위임).
3. `<git-common-dir>/husky-deps/`에 `lint-staged`를 설치합니다 — 모든 worktree가 공유하며 커밋되지 않습니다.
4. 3개의 템플릿 파일을 대상 저장소에 복사합니다(이미 존재하면 건너뜁니다).

## 아키텍처

- **패키지 매니저 / 빌드 단계 없음** — 순수 bash 스크립트입니다.
- `core.hooksPath`는 저장소 config가 아닌 `~/.gitconfig` `includeIf`로 설정되므로, 새 worktree를 추가해도 설정 스크립트를 재실행할 필요가 없습니다.
- `lint-staged`는 git common dir(`.git/husky-deps/`) 안에 설치되어 모든 worktree가 단일 설치본을 공유합니다.
- `dotnet-format-staged.sh`는 저장소 루트의 `.sln` 파일 중 알파벳 순으로 첫 번째 것을 사용하며, Unity가 생성한 솔루션이 미리 존재해야 합니다.
- `dotnet format whitespace`와 `dotnet format style --diagnostics IDE0005 --severity info`를 모두 실행합니다(후자는 불필요한 `using` 지시문을 제거하며, `warn` 수준에서는 적용되지 않는 규칙입니다).
- 모든 스크립트는 `set -euo pipefail`을 사용합니다.

## 의존성 (대상 머신)

- Git for Windows (또는 Git Bash / WSL)
- Node.js + npm (lint-staged 설치용)
- .NET SDK (`dotnet format` 포함, .NET 6 이상)
