#!/usr/bin/env bash
set -u

EXPECTED_UNITY_VERSION="6000.3.22f1"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT_ROOT="${REPO_ROOT}/SundollWorld"
UNITY_EDITOR="/Applications/Unity/Hub/Editor/${EXPECTED_UNITY_VERSION}/Unity.app/Contents/MacOS/Unity"
MODE="${1:-playmode}"
STAMP="$(date +%Y%m%d_%H%M%S)"
UNITY_TEST_TIMEOUT_SECONDS="${UNITY_TEST_TIMEOUT_SECONDS:-900}"
UNITY_LICENSE_STALL_SECONDS="${UNITY_LICENSE_STALL_SECONDS:-180}"

print_connection_diagnostics() {
  local log_path="$1"

  printf '\n== Unity connection diagnostics ==\n' >&2
  if [ -f "${log_path}" ]; then
    printf 'Recent signals from %s:\n' "${log_path}" >&2
    grep -E 'Licensing|LicenseClient|Entitlement|Token|Package Manager|UPM|IPC|Timed-out|ObjectDisposedException|Curl error|No ULF|Access token|Aborting batchmode|Fatal|Error' "${log_path}" | tail -n 40 >&2 || true
  else
    printf 'Unity log was not created: %s\n' "${log_path}" >&2
  fi

  if [ -x "${SCRIPT_DIR}/unity-doctor.sh" ]; then
    "${SCRIPT_DIR}/unity-doctor.sh" >&2 || true
  else
    printf 'unity-doctor.sh is unavailable.\n' >&2
  fi
}

run_unity_with_watchdog() {
  local log_path="$1"
  shift
  local start_seconds="${SECONDS}"
  local elapsed_seconds=0

  "$@" &
  local unity_pid=$!

  while kill -0 "${unity_pid}" 2>/dev/null; do
    elapsed_seconds=$((SECONDS - start_seconds))

    if [ "${elapsed_seconds}" -ge "${UNITY_TEST_TIMEOUT_SECONDS}" ]; then
      printf 'Unity batchmode exceeded timeout after %s seconds; stopping process %s.\n' "${elapsed_seconds}" "${unity_pid}" >&2
      kill "${unity_pid}" 2>/dev/null || true
      wait "${unity_pid}" 2>/dev/null || true
      return 124
    fi

    if [ "${elapsed_seconds}" -ge "${UNITY_LICENSE_STALL_SECONDS}" ] && [ -f "${log_path}" ]; then
      if grep -q 'Licensing initialization failed' "${log_path}" \
        && grep -q 'ObjectDisposedException' "${log_path}" \
        && grep -q 'The re-connection attempt was UN-successful' "${log_path}"; then
        printf 'Unity Licensing Client appears stuck after %s seconds; stopping batchmode process %s.\n' "${elapsed_seconds}" "${unity_pid}" >&2
        kill "${unity_pid}" 2>/dev/null || true
        wait "${unity_pid}" 2>/dev/null || true
        return 125
      fi
    fi

    sleep 5
  done

  wait "${unity_pid}"
  return "$?"
}

run_mode() {
  local mode_name="$1"
  local platform_name="$2"
  local result_path="${PROJECT_ROOT}/TestResults_${platform_name}_${STAMP}.xml"
  local log_path="${PROJECT_ROOT}/Logs/Test_${platform_name}_${STAMP}.log"

  mkdir -p "${PROJECT_ROOT}/Logs"
  printf 'Running %s tests with Unity %s\n' "${platform_name}" "${EXPECTED_UNITY_VERSION}"
  printf 'Result: %s\n' "${result_path}"
  printf 'Log:    %s\n' "${log_path}"
  printf 'Timeout: %ss; license stall guard: %ss\n' "${UNITY_TEST_TIMEOUT_SECONDS}" "${UNITY_LICENSE_STALL_SECONDS}"

  if [ ! -x "${UNITY_EDITOR}" ]; then
    printf 'Missing Unity editor: %s\n' "${UNITY_EDITOR}" >&2
    return 2
  fi

  if [ -f "${PROJECT_ROOT}/Temp/UnityLockfile" ]; then
    local editor_processes=""
    if command -v pgrep >/dev/null 2>&1; then
      editor_processes="$(pgrep -f '/Unity\.app/Contents/MacOS/Unity( |$)' 2>/dev/null || true)"
    fi

    if [ -n "${editor_processes}" ]; then
      printf 'UnityLockfile is present and an interactive Editor is running; close it before batch validation.\n' >&2
      if command -v pgrep >/dev/null 2>&1; then
        pgrep -fl '/Unity\.app/Contents/MacOS/Unity( |$)' >&2 || true
      fi
      return 3
    fi

    local stale_lock_path="/private/tmp/SundollWorld_UnityLockfile_${STAMP}.stale"
    if mv "${PROJECT_ROOT}/Temp/UnityLockfile" "${stale_lock_path}" 2>/dev/null; then
      printf 'Moved stale UnityLockfile to %s; no Unity Editor process was detected.\n' "${stale_lock_path}" >&2
    else
      printf 'UnityLockfile is present and could not be moved safely; inspect the Editor state before retrying.\n' >&2
      return 3
    fi
  fi

  run_unity_with_watchdog "${log_path}" \
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
    print_connection_diagnostics "${log_path}"
    if [ "${unity_exit}" -ne 0 ]; then
      return "${unity_exit}"
    fi
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
