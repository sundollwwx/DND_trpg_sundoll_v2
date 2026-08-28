#!/usr/bin/env bash
set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=unity-common.sh
source "${SCRIPT_DIR}/unity-common.sh"

STAMP="$(date +%Y%m%d_%H%M%S)"
LOG_PATH="${SUNDOLL_PROJECT_ROOT}/Logs/LicenseCheck_${STAMP}.log"
TIMEOUT_SECONDS="${UNITY_LICENSE_CHECK_TIMEOUT_SECONDS:-300}"
STALL_SECONDS="${UNITY_LICENSE_STALL_SECONDS:-30}"

sundoll_prepare_unity_launch "${STAMP}" || exit $?

printf 'Checking the SundollWorld Unity license path.\n'
sundoll_run_unity_with_watchdog \
  "${LOG_PATH}" \
  "${TIMEOUT_SECONDS}" \
  "${STALL_SECONDS}" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "${SUNDOLL_PROJECT_ROOT}"
unity_exit=$?

if [ "${unity_exit}" -ne 0 ]; then
  sundoll_print_connection_diagnostics "${LOG_PATH}"
  exit "${unity_exit}"
fi

sundoll_assert_license_log "${LOG_PATH}" || {
  check_exit=$?
  sundoll_print_connection_diagnostics "${LOG_PATH}"
  exit "${check_exit}"
}

printf 'License check passed. Local entitlement is usable; no account action is required.\n'
