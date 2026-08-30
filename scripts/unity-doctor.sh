#!/usr/bin/env bash
set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=unity-common.sh
source "${SCRIPT_DIR}/unity-common.sh"

USER_DIR="/Users/${SUNDOLL_UNITY_USER_NAME}"
UNITY_LOG_DIR="${USER_DIR}/Library/Logs/Unity"
LICENSING_LOG="${UNITY_LOG_DIR}/Unity.Licensing.Client.log"
EDITOR_LOG="${UNITY_LOG_DIR}/Editor.log"
UPM_LOG="${UNITY_LOG_DIR}/upm.log"

print_status() {
  local label="$1"
  local value="$2"
  printf '%-28s %s\n' "${label}" "${value}"
}

print_section() {
  printf '\n== %s ==\n' "$1"
}

print_section "Sundoll Unity Doctor"
print_status "Repo root" "${SUNDOLL_REPO_ROOT}"
print_status "Project root" "${SUNDOLL_PROJECT_ROOT}"
print_status "Pinned license channel" "${SUNDOLL_UNITY_LICENSE_CHANNEL}"

if [ -f "${SUNDOLL_PROJECT_ROOT}/ProjectSettings/ProjectVersion.txt" ]; then
  PROJECT_VERSION="$(awk -F': ' '/m_EditorVersion:/ { print $2; exit }' "${SUNDOLL_PROJECT_ROOT}/ProjectSettings/ProjectVersion.txt")"
  print_status "Project Unity version" "${PROJECT_VERSION}"
else
  print_status "Project Unity version" "missing ProjectVersion.txt"
fi

if [ -x "${SUNDOLL_UNITY_EDITOR}" ]; then
  print_status "Expected Unity editor" "found"
else
  print_status "Expected Unity editor" "missing: ${SUNDOLL_UNITY_EDITOR}"
fi

print_section "Git"
if command -v git >/dev/null 2>&1 && git -C "${SUNDOLL_REPO_ROOT}" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  print_status "Branch" "$(git -C "${SUNDOLL_REPO_ROOT}" branch --show-current 2>/dev/null)"
  print_status "HEAD" "$(git -C "${SUNDOLL_REPO_ROOT}" rev-parse --short HEAD 2>/dev/null)"
  if [ -z "$(git -C "${SUNDOLL_REPO_ROOT}" status --short 2>/dev/null)" ]; then
    print_status "Worktree" "clean"
  else
    print_status "Worktree" "has local changes"
  fi
else
  print_status "Git" "not available"
fi

print_section "Live Processes"
if command -v ps >/dev/null 2>&1; then
  if PROCESS_LIST="$(ps -axo pid=,ppid=,comm= 2>/dev/null)"; then
    MATCHES="$(printf '%s\n' "${PROCESS_LIST}" | grep -Ei 'Unity|Unity Hub|Unity\.Licensing\.Client|UnityPackageManager|UnityPackage' || true)"
    if [ -n "${MATCHES}" ]; then
      printf '%s\n' "${MATCHES}"
    else
      print_status "Unity processes" "none"
    fi
  else
    print_status "Unity processes" "unavailable (process inspection denied)"
  fi
else
  print_status "ps" "not available"
fi

print_section "Project Locks"
LOCKS="$(find "${SUNDOLL_PROJECT_ROOT}" -maxdepth 3 \( -name 'UnityLockfile' -o -name '*.lock' \) 2>/dev/null || true)"
if [ -n "${LOCKS}" ]; then
  printf '%s\n' "${LOCKS}"
else
  print_status "Locks" "none found under project"
fi

print_section "Unity MCP Bridge"
MCP_HITS="$(grep -RIEin 'mcp|unity-mcp|coplay|ivanmurzak|codergamester|com\.unity\.ai\.assistant|Unity MCP|AI Assistant' \
  "${SUNDOLL_PROJECT_ROOT}/Packages" "${SUNDOLL_REPO_ROOT}/.mcp.json" "${SUNDOLL_REPO_ROOT}/.cursor" "${SUNDOLL_REPO_ROOT}/.vscode" 2>/dev/null || true)"
if [ -n "${MCP_HITS}" ]; then
  printf '%s\n' "${MCP_HITS}"
else
  print_status "Detected bridge" "none"
fi

if [ -d "${USER_DIR}/.unity/relay" ]; then
  print_status "Official relay" "found: ${USER_DIR}/.unity/relay"
else
  print_status "Official relay" "not found"
fi

print_section "Recent Licensing Signals"
if [ -f "${LICENSING_LOG}" ]; then
  print_status "Licensing log" "${LICENSING_LOG}"
  tail -n 600 "${LICENSING_LOG}" | grep -E 'Unsupported protocol|Timed-out|ObjectDisposedException|Access token is unavailable|Successfully connected|License group|Curl error 35|No ULF license found|Token not found' | tail -n 30 || true
else
  print_status "Licensing log" "missing"
fi

print_section "Recent Editor Signals"
if [ -f "${EDITOR_LOG}" ]; then
  print_status "Editor log" "${EDITOR_LOG}"
  tail -n 500 "${EDITOR_LOG}" | grep -v '^##utp:' | grep -E 'Licensing|Unsupported protocol|Timed-out|ObjectDisposedException|Package Manager|Curl error|LogAssemblyErrors|Script compilation' | tail -n 25 || true
else
  print_status "Editor log" "missing"
fi

print_section "Recent UPM Signals"
if [ -f "${UPM_LOG}" ]; then
  print_status "UPM log" "${UPM_LOG}"
  tail -n 80 "${UPM_LOG}"
else
  print_status "UPM log" "missing"
fi

print_section "Interpretation"
if [ ! -d "${USER_DIR}/.unity/relay" ] && [ -z "${MCP_HITS}" ]; then
  printf '%s\n' "No Unity MCP bridge is configured, so live Codex-to-Editor tools are unavailable."
  printf '%s\n' "Use repository tools and Unity batchmode scripts unless one Unity MCP provider is deliberately installed."
fi
printf '%s\n' "For validation, prefer scripts/unity-run-tests.sh so the exact Unity 6000.3.22f1 binary is used."
printf '%s\n' "For interactive work, use 02-在Unity中编辑SundollWorld.command; for a quick license probe, use scripts/unity-license-check.sh."
printf '%s\n' "An 'Access token is unavailable' refresh line is non-blocking when the same run resolves local entitlements and initializes licensing."
