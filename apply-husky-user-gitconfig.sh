#!/usr/bin/env bash
# 대상 git 저장소(공통 .git)용 hooksPath 를 OS 사용자 설정(~/.gitconfig + include)으로 옮깁니다.
# - git config --global 이 아니라, ~/.gitconfig 의 조건부 include 만 추가합니다.
# - 동일 저장소 worktree 전부에 적용됩니다.
# - lint-staged 를 git common dir 에 1회 설치 → 모든 worktree 공유.
# - (4) pre-commit / scripts/dotnet-format-staged.sh / package.json 템플릿이 installer 에서 복사.
#
# 사용:
#   bash apply-husky-user-gitconfig.sh [옵션] [<setup-project-dir-path>]
#   bash apply-husky-user-gitconfig.sh          # 인자 없으면 현재 디렉토리 기준
#
# 옵션:
#   -f, --force   템플릿 버전이 installer 와 같아도 [4/5] 파일을 덮어씀
#
# 예시:
#   bash apply-husky-user-gitconfig.sh C:/Users/admin/Documents/Projects/MyProject
#   bash apply-husky-user-gitconfig.sh --force /c/Users/admin/Documents/Projects/MyProject

set -euo pipefail

INSTALLER_VERSION="5"

# npm 이 필요할 때: PATH 앞의 "node"가 Cursor 등 IDE 번들(node만 있고 npm 없음)이면 npm 을 못 찹니다.
# 공식 Node.js 설치 경로를 PATH 앞에 넣어 실제 npm.cmd 를 쓰게 합니다.
prepend_full_nodejs_to_path() {
  if command -v npm >/dev/null 2>&1; then
    return 0
  fi
  local _d _base
  local _candidates=(
    "/c/Program Files/nodejs"
    "/c/Program Files (x86)/nodejs"
    "${HOME}/AppData/Local/Programs/nodejs"
  )
  if [[ -n "${LOCALAPPDATA:-}" ]] && command -v cygpath >/dev/null 2>&1; then
    _base="$(cygpath -u "$LOCALAPPDATA" 2>/dev/null)" && _candidates+=("${_base}/Programs/nodejs")
  fi
  for _d in "${_candidates[@]}"; do
    [[ -z "$_d" ]] && continue
    case "$_d" in *cursor*) continue ;; esac
    if [[ -f "${_d}/npm.cmd" || -x "${_d}/npm" ]] && [[ -x "${_d}/node.exe" || -x "${_d}/node" ]]; then
      export PATH="${_d}:${PATH}"
      return 0
    fi
  done
  # nvm-windows: 활성 버전은 보통 NVM_SYMLINK(기본 Program Files\nodejs)에 있음
  if [[ -n "${NVM_SYMLINK:-}" ]]; then
    if command -v cygpath >/dev/null 2>&1; then
      _d="$(cygpath -u "$NVM_SYMLINK" 2>/dev/null)"
    else
      _d="$NVM_SYMLINK"
    fi
    if [[ -n "$_d" && ( -f "${_d}/npm.cmd" || -x "${_d}/npm" ) ]]; then
      export PATH="${_d}:${PATH}"
      return 0
    fi
  fi
  return 1
}

require_npm() {
  prepend_full_nodejs_to_path || true
  if command -v npm >/dev/null 2>&1; then
    return 0
  fi
  echo "ERROR: npm 을 찾을 수 없습니다. lint-staged 설치에 Node.js(공식 설치본, npm 포함)가 필요합니다." >&2
  echo "  • https://nodejs.org 에서 LTS 설치 후 터미널을 다시 여세요." >&2
  echo "  • Cursor/IDE가 PATH 앞에 두는 node.exe 만으로는 npm 이 없을 수 있습니다." >&2
  exit 1
}

FORCE_INSTALL=0
_arg_repo=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    -f | --force)
      FORCE_INSTALL=1
      shift
      ;;
    -h | --help)
      sed -n '2,17p' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    --)
      shift
      break
      ;;
    -*)
      echo "ERROR: 알 수 없는 옵션: $1 (도움말: --help)" >&2
      exit 1
      ;;
    *)
      if [[ -n "$_arg_repo" ]]; then
        echo "ERROR: 경로는 하나만 지정하세요: $1" >&2
        exit 1
      fi
      _arg_repo="$1"
      shift
      ;;
  esac
done
if [[ -n "$_arg_repo" ]]; then
  TARGET_REPO_ROOT="$_arg_repo"
else
  TARGET_REPO_ROOT="${TARGET_REPO_ROOT:-$(pwd)}"
fi

# 경로 존재 여부 확인
if [[ ! -d "$TARGET_REPO_ROOT" ]]; then
  echo "ERROR: 디렉토리를 찾을 수 없습니다: $TARGET_REPO_ROOT" >&2
  exit 1
fi

# git 저장소인지 확인
if ! git -C "$TARGET_REPO_ROOT" rev-parse --git-dir >/dev/null 2>&1; then
  echo "ERROR: git 저장소가 아닙니다: $TARGET_REPO_ROOT" >&2
  exit 1
fi

echo "프로젝트 경로: $TARGET_REPO_ROOT"
INC_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/dotnet-format-git"
INC_FILE="$INC_DIR/hooks-path.inc"
# Git for Windows 는 include path 에 MSYS 경로(/c/Users/...)를 잘 못 쓰므로 ~/.config/... 형태로 기록합니다.
INC_GITCONFIG_PATH="~/.config/dotnet-format-git/hooks-path.inc"
GITCONFIG="${HOME}/.gitconfig"

# includeIf 의 gitdir 패턴은 git 이 내부적으로 사용하는 절대 경로와 일치해야 합니다.
# Git for Windows 는 C:/Users/... 형식을 사용하므로, git rev-parse 로 정규화합니다.
REPO_GITDIR="$(git -C "$TARGET_REPO_ROOT" rev-parse --absolute-git-dir)"
# .git 접미사 제거 → 저장소 루트 경로 (C:/Users/.../Star/ 형식)
REPO_ROOT_NORMALIZED="${REPO_GITDIR%.git}"
MARKER="# --- husky hooksPath: ${REPO_ROOT_NORMALIZED} ---"

# ── 1. ~/.gitconfig includeIf 설정 ──────────────────────────────────────────
mkdir -p "$INC_DIR"
printf '%s\n' '[core]' '	hooksPath = .husky' >"$INC_FILE"
echo "[1/5] Wrote $INC_FILE"

touch "$GITCONFIG"
if grep -qF "$MARKER" "$GITCONFIG" 2>/dev/null; then
  echo "[1/5] Marker already present in $GITCONFIG — skip append."
else
  {
    echo ""
    echo "$MARKER"
    echo "[includeIf \"gitdir/i:${REPO_ROOT_NORMALIZED}\"]"
    echo "	path = $INC_GITCONFIG_PATH"
    echo "$MARKER"
  } >>"$GITCONFIG"
  echo "[1/5] Appended includeIf to $GITCONFIG"
fi

# ── 2. 저장소 git config 에서 core.hooksPath 제거 + .git/info/exclude 설정 ───
# .husky/ / package.json / scripts/dotnet-format-staged.sh 는 모두 로컬 전용 파일로,
# 이름이 일반적이거나 프로젝트에 따라 다르게 쓰일 수 있으므로 global gitignore 대신
# .git/info/exclude (저장소 로컬, 커밋 안 됨, worktree 전체 공유) 에 등록한다.
common_git="$(git -C "$TARGET_REPO_ROOT" rev-parse --path-format=absolute --git-common-dir 2>/dev/null || true)"
if [[ -n "$common_git" && -f "$common_git/config" ]]; then
  if git config -f "$common_git/config" --get core.hooksPath >/dev/null 2>&1; then
    git config -f "$common_git/config" --unset core.hooksPath
    echo "[2/5] Removed core.hooksPath from $common_git/config (now from user include)."
  else
    echo "[2/5] No core.hooksPath in $common_git/config (nothing to unset)."
  fi
else
  echo "WARN: could not resolve git-common-dir for $TARGET_REPO_ROOT — unset hooksPath manually if needed." >&2
fi

if [[ -n "$common_git" ]]; then
  LOCAL_EXCLUDE="${common_git}/info/exclude"
  mkdir -p "${common_git}/info"
  touch "$LOCAL_EXCLUDE"
  for _entry in '.husky/' 'package.json' 'scripts/dotnet-format-staged.sh'; do
    if grep -qxF "$_entry" "$LOCAL_EXCLUDE" 2>/dev/null; then
      echo "[2/5] $_entry already in .git/info/exclude — skip."
    else
      printf '\n%s\n' "$_entry" >> "$LOCAL_EXCLUDE"
      echo "[2/5] Added $_entry to .git/info/exclude"
    fi
  done
fi

# ── 3. lint-staged 를 git common dir 에 설치 (모든 worktree 공유) ───────────
# node_modules 는 .git/ 안에 두므로 repo에 커밋되지 않으며, npm install 없이도
# 복제 worktree 등 모든 worktree에서 즉시 사용 가능합니다.
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
  _pkg_json="${TARGET_REPO_ROOT}/package.json"
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
    echo "[3/5] Installing lint-staged ${_ls_ver} → ${HUSKY_DEPS_DIR}"
    require_npm
    ( cd "$HUSKY_DEPS_DIR" && npm install --no-fund --no-audit --loglevel=error )
  else
    echo "[3/5] lint-staged already installed at ${HUSKY_DEPS_DIR} — skip."
  fi

  if [[ -x "$LINT_STAGED_BIN" ]]; then
    echo "      ✓ $(${LINT_STAGED_BIN} --version 2>/dev/null || echo 'lint-staged OK')"
  else
    echo "WARN: lint-staged 바이너리를 찾을 수 없습니다: ${LINT_STAGED_BIN}" >&2
  fi
fi

# ── 4. pre-commit + dotnet format (lint-staged) 템플릿 설치/업데이트 ──────────
# (4단계: husky 훅 파일 복사 → 5단계: Analyzer DLL 빌드·배포)
# 설치된 파일의 버전이 INSTALLER_VERSION 과 다르면 덮어씌운다.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
_tpl_pre="${SCRIPT_DIR}/installer/husky-pre-commit.sh"
_tpl_fmt="${SCRIPT_DIR}/installer/dotnet-format-staged.sh"
_tpl_pkg="${SCRIPT_DIR}/installer/root-package.json"

# 셸 스크립트에서 "# @pre-commit-format v<N>" 마커를 읽는다. 없으면 빈 문자열.
_read_sh_version() {
  if [[ -f "$1" ]]; then
    sed -n 's/^# @pre-commit-format v\([^ ]*\).*/\1/p' "$1" | head -n1
  fi
}
# package.json 에서 "_pre_commit_format_version" 필드를 읽는다.
_read_pkg_version() {
  if [[ -f "$1" ]] && command -v node >/dev/null 2>&1; then
    node -e "try{const d=JSON.parse(require('fs').readFileSync('$1','utf8'));process.stdout.write(d._pre_commit_format_version||'')}catch(e){}" 2>/dev/null || true
  fi
}

_install_tpl() {
  local _src="$1" _dst="$2" _label="$3" _cur_ver="$4"
  if [[ ! -f "$_src" ]]; then
    echo "WARN: 템플릿 없음: $_src — $_label 을 수동으로 추가하세요." >&2
    return
  fi
  mkdir -p "$(dirname "$_dst")"
  if [[ ! -f "$_dst" ]]; then
    cp "$_src" "$_dst"
    chmod +x "$_dst" 2>/dev/null || true
    echo "[4/5] Installed $_label (v${INSTALLER_VERSION})"
  elif [[ "$_cur_ver" != "$INSTALLER_VERSION" ]] || [[ "$FORCE_INSTALL" -eq 1 ]]; then
    cp "$_src" "$_dst"
    chmod +x "$_dst" 2>/dev/null || true
    if [[ "$FORCE_INSTALL" -eq 1 && "$_cur_ver" == "$INSTALLER_VERSION" ]]; then
      echo "[4/5] Force-updated $_label (v${INSTALLER_VERSION})"
    else
      echo "[4/5] Updated $_label (v${_cur_ver:-?} → v${INSTALLER_VERSION})"
    fi
  else
    echo "[4/5] $_label already up to date (v${INSTALLER_VERSION}) — skip."
  fi
}

_install_tpl "$_tpl_pre" "$TARGET_REPO_ROOT/.husky/pre-commit" \
  ".husky/pre-commit" "$(_read_sh_version "$TARGET_REPO_ROOT/.husky/pre-commit")"

_install_tpl "$_tpl_fmt" "$TARGET_REPO_ROOT/scripts/dotnet-format-staged.sh" \
  "scripts/dotnet-format-staged.sh" "$(_read_sh_version "$TARGET_REPO_ROOT/scripts/dotnet-format-staged.sh")"

_install_tpl "$_tpl_pkg" "$TARGET_REPO_ROOT/package.json" \
  "package.json" "$(_read_pkg_version "$TARGET_REPO_ROOT/package.json")"

# ── 5. dotnet/MyAnalyzer 소스 복사 + 빌드 → 대상 프로젝트에 배포 ─────────────
_src_analyzer="${SCRIPT_DIR}/dotnet/MyAnalyzer"
_dst_analyzer="${TARGET_REPO_ROOT}/dotnet/MyAnalyzer"

if [[ ! -d "$_src_analyzer" ]]; then
  echo "WARN: dotnet/MyAnalyzer 를 찾을 수 없습니다: $_src_analyzer — 건너뜁니다." >&2
elif ! command -v dotnet >/dev/null 2>&1; then
  echo "WARN: dotnet 를 찾을 수 없습니다. CustomAnalyzer 설치를 건너뜁니다." >&2
else
  # 5a. 소스 복사 (obj/, bin/, tools/ 제외)
  echo "[5/5] Copying dotnet/MyAnalyzer → ${_dst_analyzer}"
  mkdir -p "$_dst_analyzer"
  while IFS= read -r _f; do
    _rel="${_f#${_src_analyzer}/}"
    _dst_f="${_dst_analyzer}/${_rel}"
    mkdir -p "$(dirname "$_dst_f")"
    cp "$_f" "$_dst_f"
  done < <(find "$_src_analyzer" -type f \
    -not -path "*/obj/*" \
    -not -path "*/bin/*" \
    -not -path "*/tools/*")
  echo "      ✓ 소스 복사 완료"

  # 5b. 대상 위치에서 빌드 → PostBuild가 ../../Assets/Plugins/Editor/Analyzers 에 DLL 자동 배포
  echo "[5/5] Building MyAnalyzer → ${TARGET_REPO_ROOT}/Assets/Plugins/Editor/Analyzers"
  dotnet build "${_dst_analyzer}/MyAnalyzer.csproj" \
    --configuration Release \
    --no-incremental \
    --verbosity minimal
  echo "      ✓ MyAnalyzer.dll 배포 완료"

  # .meta 파일이 없으면 템플릿 복사
  _meta_tpl="${SCRIPT_DIR}/installer/MyAnalyzer.dll.meta"
  _meta_dst="${TARGET_REPO_ROOT}/Assets/Plugins/Editor/Analyzers/MyAnalyzer.dll.meta"
  if [[ -f "$_meta_tpl" && ! -f "$_meta_dst" ]]; then
    cp "$_meta_tpl" "$_meta_dst"
    echo "      ✓ MyAnalyzer.dll.meta 설치됨 (RoslynAnalyzer 레이블 포함)"
  fi

fi

echo ""
echo "새 worktree 추가 시 별도 설치 불필요 — 이 스크립트를 다시 실행할 필요 없음."

# ── 자동 검증 ─────────────────────────────────────────────────────────────────
# git config --show-origin 은 대상 저장소 안에서만 includeIf 가 적용되므로
# 이 스크립트 위치(installer 등)가 아닌 TARGET_REPO_ROOT 기준으로 확인합니다.
echo ""
echo "--- 검증 ---"
_verify="$(git -C "$TARGET_REPO_ROOT" config --show-origin --get core.hooksPath 2>/dev/null || true)"
if [[ "$_verify" == *".husky"* ]]; then
  echo "✓ core.hooksPath: $_verify"
else
  echo "✗ core.hooksPath 미확인. 아래 명령으로 직접 확인하세요:" >&2
  echo "    cd \"$TARGET_REPO_ROOT\" && git config --show-origin --get core.hooksPath" >&2
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
