#!/usr/bin/env bash
set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=unity-common.sh
source "${SCRIPT_DIR}/unity-common.sh"

MODE="${1:-playmode}"
STAMP="$(date +%Y%m%d_%H%M%S)"
UNITY_TEST_TIMEOUT_SECONDS="${UNITY_TEST_TIMEOUT_SECONDS:-900}"
UNITY_LICENSE_STALL_SECONDS="${UNITY_LICENSE_STALL_SECONDS:-180}"

run_mode() {
  local mode_name="$1"
  local platform_name="$2"
  local result_directory="${SUNDOLL_PROJECT_ROOT}/TestResults/Local"
  local result_path="${result_directory}/TestResults_${platform_name}_${STAMP}.xml"
  local log_path="${SUNDOLL_PROJECT_ROOT}/Logs/Test_${platform_name}_${STAMP}.log"

  mkdir -p "${SUNDOLL_PROJECT_ROOT}/Logs" "${result_directory}"
  printf 'Running %s tests with Unity %s\n' "${platform_name}" "${SUNDOLL_EXPECTED_UNITY_VERSION}"
  printf 'Result: %s\n' "${result_path}"
  printf 'Log:    %s\n' "${log_path}"
  printf 'Timeout: %ss; license stall guard: %ss\n' "${UNITY_TEST_TIMEOUT_SECONDS}" "${UNITY_LICENSE_STALL_SECONDS}"

  sundoll_prepare_unity_launch "${STAMP}" || return $?

  sundoll_run_unity_with_watchdog \
    "${log_path}" \
    "${UNITY_TEST_TIMEOUT_SECONDS}" \
    "${UNITY_LICENSE_STALL_SECONDS}" \
    -batchmode \
    -nographics \
    -projectPath "${SUNDOLL_PROJECT_ROOT}" \
    -runTests \
    -testPlatform "${mode_name}" \
    -testResults "${result_path}"
  local unity_exit=$?

  if [ ! -f "${result_path}" ]; then
    printf 'Unity exited with %s but did not create a test XML. Inspect the log above.\n' "${unity_exit}" >&2
    sundoll_print_connection_diagnostics "${log_path}"
    if [ "${unity_exit}" -ne 0 ]; then
      return "${unity_exit}"
    fi
    return 4
  fi

  sundoll_assert_license_log "${log_path}" || {
    local license_exit=$?
    sundoll_print_connection_diagnostics "${log_path}"
    return "${license_exit}"
  }

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
