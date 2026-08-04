#!/usr/bin/env bash
# @pre-commit-format v7
# Staged *.cs → dotnet format (repo 루트의 첫 번째 .sln 사용; Unity 가 생성한 솔루션)
set -euo pipefail

ROOT="$(git rev-parse --show-toplevel)"
cd "$ROOT"

shopt -s nullglob
slns=( *.sln )
if (( ${#slns[@]} == 0 )); then
  echo "dotnet-format-staged: 루트에 .sln 이 없습니다. Unity 로 솔루션을 생성한 뒤 다시 시도하세요." >&2
  exit 1
fi

sln="$(printf '%s\n' "${slns[@]}" | sort | head -n1)"
if (( ${#slns[@]} > 1 )); then
  echo "dotnet-format-staged: 여러 .sln 발견 — ${sln} 사용" >&2
fi

# lint-staged 는 절대경로를 넘기지만 dotnet format --include 는 프로젝트 루트 기준 상대경로를 요구함.
# 절대경로·MSYS 경로 모두 상대경로로 정규화한다.
rel_files=()
for f in "$@"; do
  # Windows 절대경로(C:/...) 또는 MSYS 절대경로(/c/...) → 상대경로로 변환
  rel="$(realpath --relative-to="$ROOT" "$f" 2>/dev/null || python3 -c "
import os, sys
print(os.path.relpath(sys.argv[1], '$ROOT'))
" "$f")"
  rel_files+=("$rel")
done

# 서브커맨드(whitespace/style/analyzers) 없이 호출 → 셋을 한 번의 dotnet 실행으로 처리.
# --severity info 는 info(suggestion) 이상 전부 포함하므로 IDE1006(네이밍 규칙) 등
# suggestion 수준 규칙도 warning/error 규칙과 함께 적용됨.
# https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-format
dotnet format "$sln" --no-restore --severity info --include "${rel_files[@]}"
