#!/usr/bin/env bash
set -u

EXPECTED_UNITY_VERSION="6000.3.22f1"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT_ROOT="${REPO_ROOT}/SundollWorld"
UNITY_EDITOR="/Applications/Unity/Hub/Editor/${EXPECTED_UNITY_VERSION}/Unity.app/Contents/MacOS/Unity"
USER_NAME="$(id -un)"
USER_DIR="/Users/${USER_NAME}"
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
print_status "Repo root" "${REPO_ROOT}"
print_status "Project root" "${PROJECT_ROOT}"

if [ -f "${PROJECT_ROOT}/ProjectSettings/ProjectVersion.txt" ]; then
  PROJECT_VERSION="$(awk -F': ' '/m_EditorVersion:/ { print $2; exit }' "${PROJECT_ROOT}/ProjectSettings/ProjectVersion.txt")"
  print_status "Project Unity version" "${PROJECT_VERSION}"
else
  print_status "Project Unity version" "missing ProjectVersion.txt"
fi

if [ -x "${UNITY_EDITOR}" ]; then
  print_status "Expected Unity editor" "found"
else
  print_status "Expected Unity editor" "missing: ${UNITY_EDITOR}"
fi

print_section "Git"
if command -v git >/dev/null 2>&1 && git -C "${REPO_ROOT}" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  print_status "Branch" "$(git -C "${REPO_ROOT}" branch --show-current 2>/dev/null)"
  print_status "HEAD" "$(git -C "${REPO_ROOT}" rev-parse --short HEAD 2>/dev/null)"
  if [ -z "$(git -C "${REPO_ROOT}" status --short 2>/dev/null)" ]; then
    print_status "Worktree" "clean"
  else
    print_status "Worktree" "has local changes"
  fi
else
  print_status "Git" "not available"
fi

print_section "Live Processes"
if command -v ps >/dev/null 2>&1; then
  MATCHES="$(ps aux 2>/dev/null | grep -Ei 'Unity|Unity Hub|UnityLicensing|UnityPackageManager|UnityPackage' | grep -v grep || true)"
  if [ -n "${MATCHES}" ]; then
    printf '%s\n' "${MATCHES}"
  else
    print_status "Unity processes" "none"
  fi
else
  print_status "ps" "not available"
fi

print_section "Project Locks"
LOCKS="$(find "${PROJECT_ROOT}" -maxdepth 3 \( -name 'UnityLockfile' -o -name '*.lock' \) 2>/dev/null || true)"
if [ -n "${LOCKS}" ]; then
  printf '%s\n' "${LOCKS}"
else
  print_status "Locks" "none found under project"
fi

print_section "Unity MCP Bridge"
MCP_HITS="$(grep -RIEin 'mcp|unity-mcp|coplay|ivanmurzak|codergamester|com\.unity\.ai\.assistant|Unity MCP|AI Assistant' \
  "${PROJECT_ROOT}/Packages" "${REPO_ROOT}/.mcp.json" "${REPO_ROOT}/.cursor" "${REPO_ROOT}/.vscode" 2>/dev/null || true)"
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
