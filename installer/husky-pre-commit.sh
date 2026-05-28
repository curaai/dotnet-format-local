#!/usr/bin/env sh
# @pre-commit-format v3
# lint-staged (공통 git dir 의 husky-deps) — package.json 의 lint-staged 설정 실행
set -e
ROOT="$(git rev-parse --show-toplevel)"
cd "$ROOT" || exit 1
COMMON_GIT="$(git rev-parse --path-format=absolute --git-common-dir)"

# rebase 중이면 포매팅 스킵
if [ -d "${COMMON_GIT}/rebase-merge" ] || [ -d "${COMMON_GIT}/rebase-apply" ]; then
  exit 0
fi
LINT_STAGED="${COMMON_GIT}/husky-deps/node_modules/.bin/lint-staged"
if [ ! -f "$LINT_STAGED" ]; then
  echo "lint-staged 를 찾을 수 없습니다. 다음을 실행하세요:" >&2
  echo "  bash <저장소>/path/to/apply-husky-user-gitconfig.sh \"$ROOT\"" >&2
  exit 1
fi
PKG_JSON="$ROOT/package.json"
if [ ! -f "$PKG_JSON" ]; then
  echo "package.json 이 없습니다. 다음을 실행하세요:" >&2
  echo "  bash <저장소>/path/to/apply-husky-user-gitconfig.sh \"$ROOT\"" >&2
  exit 1
fi
# package.json 은 .git/info/exclude 에 있어 git 이 추적하지 않음.
# lint-staged 15+ 는 git ls-files 로 설정 파일을 찾으므로, 경로를 명시해야 함.
exec "$LINT_STAGED" --config "$PKG_JSON"
