#!/bin/bash
set -euo pipefail

# 변경된 파일 목록 (unstaged + staged)
FILES=$(
  { git diff --name-only; git diff --cached --name-only; } \
  | sort -u
)

if [ -z "${FILES}" ]; then
  echo "No changed files."
  exit 0
fi

for file in ${FILES}; do
  [ -f "${file}" ] || continue

  echo "Processing ${file}"

  # 파일을 마지막으로 수정한 커밋 (가장 최근 1개)
  TARGET_SHA=$(git log -n 1 --format=%H -- "${file}" || true)

  if [ -z "${TARGET_SHA}" ]; then
    echo "  Could not determine last commit for ${file}"
    continue
  fi

  echo "  Target commit: ${TARGET_SHA}"

  # 파일만 스테이징해서 fixup 커밋 생성
  git add "${file}"
  git commit --fixup "${TARGET_SHA}"
done

echo ""
echo "Done. Now run:"
echo "git rebase -i --autosquash <base_commit>"
