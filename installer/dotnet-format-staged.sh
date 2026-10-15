#!/usr/bin/env bash
# @pre-commit-format v8
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
# Git Bash 의 realpath --relative-to 는 C:/ 와 /c/ 혼용 시 절대경로를 돌려주므로 python 으로 고정한다.
rel_files=()
for f in "$@"; do
  rel="$(python3 -c "
import os, sys
print(os.path.relpath(os.path.abspath(sys.argv[1]), os.path.abspath(sys.argv[2])))
" "$f" "$ROOT")"
  # Windows 경로 구분자를 dotnet format 이 받는 슬래시로 통일
  rel="${rel//\\//}"
  # Assets/Scripts 하위만 포맷한다 (ExternalAssets 등 외부 에셋은 제외)
  [[ "$rel" == Assets/Scripts/* ]] && rel_files+=("$rel")
done
(( ${#rel_files[@]} == 0 )) && exit 0

# 서브커맨드(whitespace/style/analyzers) 없이 호출 → 셋을 한 번의 dotnet 실행으로 처리.
# --severity info 는 info(suggestion) 이상 전부 포함하므로 IDE0005 등도 함께 적용됨.
# private 필드 '_' 접두사는 IDE1006(내장 FixAll 미지원) 대신 UNT206 CodeFix 로 고친다.
# https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-format
dotnet format "$sln" --no-restore --severity info --include "${rel_files[@]}"
