#!/usr/bin/env bash
# Canary 저장소(공통 .git)용 hooksPath 를 OS 사용자 설정(~/.gitconfig + include)으로 옮깁니다.
# - git config --global 이 아니라, ~/.gitconfig 의 조건부 include 만 추가합니다.
# - 동일 저장소 worktree 전부에 적용됩니다(gitdir 이 .../canary/.git 아래인 경우).
# - lint-staged 를 git common dir 에 1회 설치 → 모든 worktree 공유.
# - (4) pre-commit / scripts/dotnet-format-staged.sh / package.json 템플릿이 없으면 utils 에서 복사.
#
# 사용:
#   bash apply-canary-husky-user-gitconfig.sh <setup-project-dir-path>
#   bash apply-canary-husky-user-gitconfig.sh          # 인자 없으면 현재 디렉토리 기준
#
# 예시:
#   bash apply-canary-husky-user-gitconfig.sh C:/Users/admin/Documents/Projects/canary
#   bash apply-canary-husky-user-gitconfig.sh /c/Users/admin/Documents/Projects/canary

set -euo pipefail

# 인자가 있으면 그 경로를, 없으면 환경변수 → 현재 디렉토리 순으로 사용
if [[ $# -ge 1 ]]; then
  CANARY_REPO_ROOT="$1"
else
  CANARY_REPO_ROOT="${CANARY_REPO_ROOT:-$(pwd)}"
fi

# 경로 존재 여부 확인
if [[ ! -d "$CANARY_REPO_ROOT" ]]; then
  echo "ERROR: 디렉토리를 찾을 수 없습니다: $CANARY_REPO_ROOT" >&2
  exit 1
fi

# git 저장소인지 확인
if ! git -C "$CANARY_REPO_ROOT" rev-parse --git-dir >/dev/null 2>&1; then
  echo "ERROR: git 저장소가 아닙니다: $CANARY_REPO_ROOT" >&2
  exit 1
fi

echo "프로젝트 경로: $CANARY_REPO_ROOT"
INC_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/canary-git"
INC_FILE="$INC_DIR/hooks-path.inc"
# Git for Windows 는 include path 에 MSYS 경로(/c/Users/...)를 잘 못 쓰므로 ~/.config/... 형태로 기록합니다.
INC_GITCONFIG_PATH="~/.config/canary-git/hooks-path.inc"
GITCONFIG="${HOME}/.gitconfig"

# includeIf 의 gitdir 패턴은 git 이 내부적으로 사용하는 절대 경로와 일치해야 합니다.
# Git for Windows 는 C:/Users/... 형식을 사용하므로, git rev-parse 로 정규화합니다.
REPO_GITDIR="$(git -C "$CANARY_REPO_ROOT" rev-parse --absolute-git-dir)"
# .git 접미사 제거 → 저장소 루트 경로 (C:/Users/.../Star/ 형식)
REPO_ROOT_NORMALIZED="${REPO_GITDIR%.git}"
MARKER="# --- husky hooksPath: ${REPO_ROOT_NORMALIZED} ---"

# ── 1. ~/.gitconfig includeIf 설정 ──────────────────────────────────────────
mkdir -p "$INC_DIR"
printf '%s\n' '[core]' '	hooksPath = .husky' >"$INC_FILE"
echo "[1/4] Wrote $INC_FILE"

touch "$GITCONFIG"
if grep -qF "$MARKER" "$GITCONFIG" 2>/dev/null; then
  echo "[1/4] Marker already present in $GITCONFIG — skip append."
else
  {
    echo ""
    echo "$MARKER"
    echo "[includeIf \"gitdir/i:${REPO_ROOT_NORMALIZED}\"]"
    echo "	path = $INC_GITCONFIG_PATH"
    echo "$MARKER"
  } >>"$GITCONFIG"
  echo "[1/4] Appended includeIf to $GITCONFIG"
fi

# ── 2. 저장소 git config 에서 core.hooksPath 제거 + .git/info/exclude 설정 ───
# .husky/ / package.json / scripts/dotnet-format-staged.sh 는 모두 로컬 전용 파일로,
# 이름이 일반적이거나 프로젝트에 따라 다르게 쓰일 수 있으므로 global gitignore 대신
# .git/info/exclude (저장소 로컬, 커밋 안 됨, worktree 전체 공유) 에 등록한다.
common_git="$(git -C "$CANARY_REPO_ROOT" rev-parse --path-format=absolute --git-common-dir 2>/dev/null || true)"
if [[ -n "$common_git" && -f "$common_git/config" ]]; then
  if git config -f "$common_git/config" --get core.hooksPath >/dev/null 2>&1; then
    git config -f "$common_git/config" --unset core.hooksPath
    echo "[2/4] Removed core.hooksPath from $common_git/config (now from user include)."
  else
    echo "[2/4] No core.hooksPath in $common_git/config (nothing to unset)."
  fi
else
  echo "WARN: could not resolve git-common-dir for $CANARY_REPO_ROOT — unset hooksPath manually if needed." >&2
fi

if [[ -n "$common_git" ]]; then
  LOCAL_EXCLUDE="${common_git}/info/exclude"
  mkdir -p "${common_git}/info"
  touch "$LOCAL_EXCLUDE"
  for _entry in '.husky/' 'package.json' 'scripts/dotnet-format-staged.sh'; do
    if grep -qxF "$_entry" "$LOCAL_EXCLUDE" 2>/dev/null; then
      echo "[2/4] $_entry already in .git/info/exclude — skip."
    else
      printf '\n%s\n' "$_entry" >> "$LOCAL_EXCLUDE"
      echo "[2/4] Added $_entry to .git/info/exclude"
    fi
  done
fi

# ── 3. lint-staged 를 git common dir 에 설치 (모든 worktree 공유) ───────────
# node_modules 는 .git/ 안에 두므로 repo에 커밋되지 않으며, npm install 없이도
# canary / canary_clone / canary_worktree 등 모든 worktree에서 즉시 사용 가능합니다.
if [[ -z "$common_git" ]]; then
  echo "WARN: git-common-dir 를 찾지 못해 lint-staged 설치를 건너뜁니다." >&2
else
  HUSKY_DEPS_DIR="${common_git}/husky-deps"
  HUSKY_PKG="${HUSKY_DEPS_DIR}/package.json"
  LINT_STAGED_BIN="${HUSKY_DEPS_DIR}/node_modules/.bin/lint-staged"

  mkdir -p "$HUSKY_DEPS_DIR"

  # 프로젝트의 package.json 에서 lint-staged 버전을 읽어 공유 package.json 생성
  # (버전이 없으면 latest 사용)
  _ls_ver="latest"
  _pkg_json="${CANARY_REPO_ROOT}/package.json"
  if command -v node >/dev/null 2>&1 && [[ -f "$_pkg_json" ]]; then
    _ls_ver="$(node -e "
      try {
        const d = JSON.parse(require('fs').readFileSync('${_pkg_json}','utf8'));
        const v = (d.devDependencies||{})['lint-staged'] || (d.dependencies||{})['lint-staged'];
        if (v) process.stdout.write(v);
      } catch(e) {}
    " 2>/dev/null || true)"
    [[ -z "$_ls_ver" ]] && _ls_ver="latest"
  fi

  # package.json 없거나, 바이너리 없거나, 버전이 바뀌었을 때 재설치
  _needs_install=0
  if [[ ! -f "$HUSKY_PKG" ]] || [[ ! -x "$LINT_STAGED_BIN" ]]; then
    _needs_install=1
  else
    _cur_ver="$(node -e "try{const d=JSON.parse(require('fs').readFileSync('${HUSKY_PKG}','utf8'));process.stdout.write((d.devDependencies||{})['lint-staged']||'')}catch(e){}" 2>/dev/null || true)"
    [[ "$_cur_ver" != "$_ls_ver" ]] && _needs_install=1
  fi

  if [[ $_needs_install -eq 1 ]]; then
    printf '{\n  "private": true,\n  "devDependencies": {\n    "lint-staged": "%s"\n  }\n}\n' "$_ls_ver" > "$HUSKY_PKG"
    echo "[3/4] Installing lint-staged ${_ls_ver} → ${HUSKY_DEPS_DIR}"
    ( cd "$HUSKY_DEPS_DIR" && npm install --no-fund --no-audit --loglevel=error )
  else
    echo "[3/4] lint-staged already installed at ${HUSKY_DEPS_DIR} — skip."
  fi

  if [[ -x "$LINT_STAGED_BIN" ]]; then
    echo "      ✓ $(${LINT_STAGED_BIN} --version 2>/dev/null || echo 'lint-staged OK')"
  else
    echo "WARN: lint-staged 바이너리를 찾을 수 없습니다: ${LINT_STAGED_BIN}" >&2
  fi
fi

# ── 4. pre-commit + dotnet format (lint-staged) 템플릿이 없으면 utils 에서 복사 ─
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
_tpl_pre="${SCRIPT_DIR}/installer/canary-husky-pre-commit.sh"
_tpl_fmt="${SCRIPT_DIR}/installer/dotnet-format-staged.sh"
_tpl_pkg="${SCRIPT_DIR}/installer/canary-root-package.json"
if [[ -f "$_tpl_pre" ]]; then
  mkdir -p "$CANARY_REPO_ROOT/.husky"
  if [[ ! -f "$CANARY_REPO_ROOT/.husky/pre-commit" ]]; then
    cp "$_tpl_pre" "$CANARY_REPO_ROOT/.husky/pre-commit"
    chmod +x "$CANARY_REPO_ROOT/.husky/pre-commit" 2>/dev/null || true
    echo "[4/4] Installed .husky/pre-commit (lint-staged → dotnet format)"
  else
    echo "[4/4] .husky/pre-commit already exists — skip (delete to reinstall from utils template)."
  fi
else
  echo "WARN: 템플릿 없음: $_tpl_pre — pre-commit 을 수동으로 추가하세요." >&2
fi
if [[ -f "$_tpl_fmt" ]]; then
  mkdir -p "$CANARY_REPO_ROOT/scripts"
  if [[ ! -f "$CANARY_REPO_ROOT/scripts/dotnet-format-staged.sh" ]]; then
    cp "$_tpl_fmt" "$CANARY_REPO_ROOT/scripts/dotnet-format-staged.sh"
    chmod +x "$CANARY_REPO_ROOT/scripts/dotnet-format-staged.sh" 2>/dev/null || true
    echo "[4/4] Installed scripts/dotnet-format-staged.sh"
  else
    echo "[4/4] scripts/dotnet-format-staged.sh already exists — skip."
  fi
fi
if [[ -f "$_tpl_pkg" ]]; then
  if [[ ! -f "$CANARY_REPO_ROOT/package.json" ]]; then
    cp "$_tpl_pkg" "$CANARY_REPO_ROOT/package.json"
    echo "[4/4] Installed package.json (lint-staged + *.cs → dotnet format)"
  else
    echo "[4/4] package.json already exists — skip (merge lint-staged manually if needed)."
  fi
fi

echo ""
echo "새 worktree 추가 시 별도 설치 불필요 — 이 스크립트를 다시 실행할 필요 없음."

# ── 자동 검증 ─────────────────────────────────────────────────────────────────
# git config --show-origin 은 canary 저장소 안에서만 includeIf 가 적용되므로
# 이 스크립트 위치(utils 등)가 아닌 CANARY_REPO_ROOT 기준으로 확인합니다.
echo ""
echo "--- 검증 ---"
_verify="$(git -C "$CANARY_REPO_ROOT" config --show-origin --get core.hooksPath 2>/dev/null || true)"
if [[ "$_verify" == *".husky"* ]]; then
  echo "✓ core.hooksPath: $_verify"
else
  echo "✗ core.hooksPath 미확인. 아래 명령으로 직접 확인하세요:" >&2
  echo "    cd \"$CANARY_REPO_ROOT\" && git config --show-origin --get core.hooksPath" >&2
fi
if [[ -n "$common_git" ]]; then
  _local_excl="${common_git}/info/exclude"
  for _entry in '.husky/' 'package.json' 'scripts/dotnet-format-staged.sh'; do
    if grep -qxF "$_entry" "$_local_excl" 2>/dev/null; then
      echo "✓ .git/info/exclude에 $_entry 등록됨"
    else
      echo "✗ .git/info/exclude에 $_entry 미확인." >&2
    fi
  done
fi
