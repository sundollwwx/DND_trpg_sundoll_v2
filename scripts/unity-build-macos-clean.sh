#!/usr/bin/env bash
set -euo pipefail

# Produce a clean, disposable macOS IL2CPP build without touching the primary
# Unity Library or its existing diagnostics. This isolates cache-originated
# TypeDB noise from product compilation evidence.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
STAMP="$(date +%Y%m%d_%H%M%S)"
TEMP_ROOT="$(mktemp -d /private/tmp/SundollWorld-clean-build-XXXXXX)"
WORKTREE_PATH="${TEMP_ROOT}/source"
PRESERVED_LOG="${REPO_ROOT}/SundollWorld/Logs/Build_macOS_clean_${STAMP}.log"
KEEP_CLEAN_WORKTREE="${KEEP_CLEAN_WORKTREE:-0}"

cleanup() {
  local exit_code=$?
  trap - EXIT
  if [ "${KEEP_CLEAN_WORKTREE}" = "1" ]; then
    printf 'Preserving clean worktree for inspection: %s\n' "${WORKTREE_PATH}" >&2
  else
    if [ -d "${WORKTREE_PATH}" ]; then
      git -C "${REPO_ROOT}" worktree remove --force "${WORKTREE_PATH}" || true
    fi
    rmdir "${TEMP_ROOT}" 2>/dev/null || true
  fi
  exit "${exit_code}"
}
trap cleanup EXIT

if [ -n "$(git -C "${REPO_ROOT}" status --porcelain)" ]; then
  printf 'Refusing clean build from a dirty worktree; commit or stash source changes first.\n' >&2
  exit 3
fi

printf 'Creating disposable clean worktree: %s\n' "${WORKTREE_PATH}"
git -C "${REPO_ROOT}" worktree add --detach "${WORKTREE_PATH}" HEAD

"${WORKTREE_PATH}/scripts/unity-build-macos.sh"

BUILD_LOG="$(find "${WORKTREE_PATH}/SundollWorld/Logs" -maxdepth 1 -type f -name 'Build_macOS_*.log' -print | sort | tail -n 1)"
if [ -z "${BUILD_LOG}" ]; then
  printf 'Clean build completed but no build log was found.\n' >&2
  exit 4
fi

mkdir -p "$(dirname "${PRESERVED_LOG}")"
cp "${BUILD_LOG}" "${PRESERVED_LOG}"

TYPEDB_COUNT="$(rg -c '^TypeDB:' "${PRESERVED_LOG}" || true)"
CSHARP_ERROR_COUNT="$(rg -c 'error CS[0-9]+' "${PRESERVED_LOG}" || true)"
printf 'Preserved clean-build log: %s\n' "${PRESERVED_LOG}"
printf 'Clean-build TypeDB diagnostics: %s\n' "${TYPEDB_COUNT:-0}"
printf 'Clean-build C# errors: %s\n' "${CSHARP_ERROR_COUNT:-0}"
