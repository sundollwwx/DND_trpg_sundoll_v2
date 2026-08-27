#!/usr/bin/env bash
set -u

EXPECTED_UNITY_VERSION="6000.3.22f1"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT_ROOT="${REPO_ROOT}/SundollWorld"
UNITY_EDITOR="/Applications/Unity/Hub/Editor/${EXPECTED_UNITY_VERSION}/Unity.app/Contents/MacOS/Unity"
MODE="${1:-playmode}"
STAMP="$(date +%Y%m%d_%H%M%S)"

run_mode() {
  local mode_name="$1"
  local platform_name="$2"
  local result_path="${PROJECT_ROOT}/TestResults_${platform_name}_${STAMP}.xml"
  local log_path="${PROJECT_ROOT}/Logs/Test_${platform_name}_${STAMP}.log"

  mkdir -p "${PROJECT_ROOT}/Logs"
  printf 'Running %s tests with Unity %s\n' "${platform_name}" "${EXPECTED_UNITY_VERSION}"
  printf 'Result: %s\n' "${result_path}"
  printf 'Log:    %s\n' "${log_path}"

  if [ ! -x "${UNITY_EDITOR}" ]; then
    printf 'Missing Unity editor: %s\n' "${UNITY_EDITOR}" >&2
    return 2
  fi

  if [ -f "${PROJECT_ROOT}/Temp/UnityLockfile" ]; then
    printf 'UnityLockfile is present; close the interactive Editor before batch validation.\n' >&2
    return 3
  fi

  "${UNITY_EDITOR}" \
    -batchmode \
    -nographics \
    -projectPath "${PROJECT_ROOT}" \
    -runTests \
    -testPlatform "${mode_name}" \
    -testResults "${result_path}" \
    -logFile "${log_path}"
  local unity_exit=$?

  if [ ! -f "${result_path}" ]; then
    printf 'Unity exited with %s but did not create a test XML. Inspect the log above.\n' "${unity_exit}" >&2
    return 4
  fi

  if command -v grep >/dev/null 2>&1; then
    grep -E '<test-run|result="|failed="|passed="|skipped="' "${result_path}" | head -n 4 || true
  fi

  return "${unity_exit}"
}

case "${MODE}" in
  editmode|EditMode)
    run_mode "EditMode" "EditMode"
    ;;
  playmode|PlayMode)
    run_mode "PlayMode" "PlayMode"
    ;;
  all)
    run_mode "EditMode" "EditMode"
    edit_exit=$?
    run_mode "PlayMode" "PlayMode"
    play_exit=$?
    if [ "${edit_exit}" -ne 0 ]; then
      exit "${edit_exit}"
    fi
    exit "${play_exit}"
    ;;
  *)
    printf 'Usage: %s [editmode|playmode|all]\n' "$0" >&2
    exit 2
    ;;
esac
