#!/usr/bin/env bash
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

# 기본 --severity 는 warn → IDE0005(불필요 using) 등 suggestion 규칙은 적용되지 않음.
# https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-format
dotnet format "$sln" whitespace --no-restore --include "${rel_files[@]}"
dotnet format "$sln" style --no-restore --diagnostics IDE0005 IDE0161 --severity info --include "${rel_files[@]}"
