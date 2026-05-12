#!/usr/bin/env sh
# @pre-commit-format v1
# lint-staged (공통 git dir 의 husky-deps) — package.json 의 lint-staged 설정 실행
set -e
ROOT="$(git rev-parse --show-toplevel)"
cd "$ROOT" || exit 1
COMMON_GIT="$(git rev-parse --path-format=absolute --git-common-dir)"
LINT_STAGED="${COMMON_GIT}/husky-deps/node_modules/.bin/lint-staged"
if [ ! -f "$LINT_STAGED" ]; then
  echo "lint-staged 를 찾을 수 없습니다. 다음을 실행하세요:" >&2
  echo "  bash <저장소>/path/to/apply-husky-user-gitconfig.sh \"$ROOT\"" >&2
  exit 1
fi
exec "$LINT_STAGED"
