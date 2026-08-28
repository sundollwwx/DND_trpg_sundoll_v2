#!/usr/bin/env bash

# Shared Unity 6000.3.22f1 launch policy for SundollWorld.
# Source this file from launch/test/build scripts; do not invoke Unity directly.

if [ -n "${SUNDOLL_UNITY_COMMON_LOADED:-}" ]; then
  return 0
fi
SUNDOLL_UNITY_COMMON_LOADED=1

SUNDOLL_EXPECTED_UNITY_VERSION="6000.3.22f1"
SUNDOLL_UNITY_VERSION_CHANNEL="${SUNDOLL_EXPECTED_UNITY_VERSION%f*}"
SUNDOLL_UNITY_SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SUNDOLL_REPO_ROOT="$(cd "${SUNDOLL_UNITY_SCRIPT_DIR}/.." && pwd)"
SUNDOLL_PROJECT_ROOT="${SUNDOLL_REPO_ROOT}/SundollWorld"
SUNDOLL_UNITY_EDITOR="/Applications/Unity/Hub/Editor/${SUNDOLL_EXPECTED_UNITY_VERSION}/Unity.app/Contents/MacOS/Unity"
SUNDOLL_UNITY_USER_NAME="$(id -un)"
SUNDOLL_UNITY_LICENSE_CHANNEL="LicenseClient-${SUNDOLL_UNITY_USER_NAME}-${SUNDOLL_UNITY_VERSION_CHANNEL}"
SUNDOLL_UNITY_LICENSE_PIPE="Unity-${SUNDOLL_UNITY_LICENSE_CHANNEL}"
SUNDOLL_UNITY_GENERIC_LICENSE_PIPE="Unity-LicenseClient-${SUNDOLL_UNITY_USER_NAME}"

sundoll_require_unity_editor() {
  if [ ! -x "${SUNDOLL_UNITY_EDITOR}" ]; then
    printf 'Missing Unity editor: %s\n' "${SUNDOLL_UNITY_EDITOR}" >&2
    return 2
  fi
}

sundoll_editor_processes() {
  if ! command -v pgrep >/dev/null 2>&1; then
    return 0
  fi

  pgrep -f '/Unity\.app/Contents/MacOS/Unity( |$)' 2>/dev/null || true
}

sundoll_prepare_project_lock() {
  local stamp="$1"
  local lock_path="${SUNDOLL_PROJECT_ROOT}/Temp/UnityLockfile"
  local editor_processes=""
  local stale_lock_path=""

  if [ ! -f "${lock_path}" ]; then
    return 0
  fi

  editor_processes="$(sundoll_editor_processes)"
  if [ -n "${editor_processes}" ]; then
    printf 'UnityLockfile is present and an Editor is running; close it before this operation.\n' >&2
    printf 'Unity Editor PID(s): %s\n' "$(printf '%s' "${editor_processes}" | tr '\n' ' ')" >&2
    return 3
  fi

  stale_lock_path="/private/tmp/SundollWorld_UnityLockfile_${stamp}.stale"
  if mv "${lock_path}" "${stale_lock_path}" 2>/dev/null; then
    printf 'Moved stale UnityLockfile to %s; no Unity Editor process was detected.\n' "${stale_lock_path}" >&2
    return 0
  fi

  printf 'UnityLockfile is present and could not be moved safely; inspect the Editor state before retrying.\n' >&2
  return 3
}

sundoll_stop_orphaned_generic_licensing_clients() {
  local client_pid=""
  local client_ppid=""
  local client_pids=""
  local generic_socket="/tmp/${SUNDOLL_UNITY_GENERIC_LICENSE_PIPE}.sock"

  if ! command -v pgrep >/dev/null 2>&1 \
    || ! command -v ps >/dev/null 2>&1 \
    || ! command -v lsof >/dev/null 2>&1; then
    return 0
  fi

  client_pids="$(pgrep -x 'Unity.Licensing.Client' 2>/dev/null || true)"
  for client_pid in ${client_pids}; do
    client_ppid="$(ps -p "${client_pid}" -o ppid= 2>/dev/null | tr -d '[:space:]')"
    if [ "${client_ppid}" != "1" ]; then
      continue
    fi
    if ! lsof -a -p "${client_pid}" -U -Fn 2>/dev/null | grep -Fqx "n${generic_socket}"; then
      continue
    fi

    printf 'Stopping orphaned generic Unity Licensing Client process %s before launch.\n' "${client_pid}" >&2
    kill -TERM "${client_pid}" 2>/dev/null || true
    local attempt=0
    while kill -0 "${client_pid}" 2>/dev/null && [ "${attempt}" -lt 20 ]; do
      sleep 0.1
      attempt=$((attempt + 1))
    done
    if kill -0 "${client_pid}" 2>/dev/null; then
      printf 'Orphaned Unity Licensing Client process %s did not exit; stop it before retrying.\n' "${client_pid}" >&2
      return 126
    fi
  done
}

sundoll_prepare_unity_launch() {
  local stamp="$1"

  sundoll_require_unity_editor || return $?
  sundoll_prepare_project_lock "${stamp}" || return $?
  sundoll_stop_orphaned_generic_licensing_clients || return $?
}

sundoll_cleanup_launched_licensing_client() {
  local log_path="$1"
  local client_pid=""
  local client_name=""

  if [ ! -f "${log_path}" ] || ! command -v ps >/dev/null 2>&1; then
    return 0
  fi

  client_pid="$(sed -nE 's/.*Successfully launched the LicensingClient \(PId: ([0-9]+)\).*/\1/p' "${log_path}" | tail -n 1)"
  if [ -z "${client_pid}" ]; then
    return 0
  fi

  client_name="$(ps -p "${client_pid}" -o comm= 2>/dev/null)"
  if [[ "${client_name}" != *Unity.Licensing.Client ]]; then
    return 0
  fi

  printf 'Stopping Licensing Client %s launched by the failed Unity run.\n' "${client_pid}" >&2
  kill -TERM "${client_pid}" 2>/dev/null || true
}

sundoll_run_unity_with_watchdog() {
  local log_path="$1"
  local timeout_seconds="$2"
  local license_stall_seconds="$3"
  shift 3
  local start_seconds="${SECONDS}"
  local elapsed_seconds=0
  local unity_pid=""

  mkdir -p "$(dirname "${log_path}")"
  printf 'Unity: %s\n' "${SUNDOLL_EXPECTED_UNITY_VERSION}"
  printf 'License channel: %s\n' "${SUNDOLL_UNITY_LICENSE_CHANNEL}"
  printf 'Log: %s\n' "${log_path}"

  "${SUNDOLL_UNITY_EDITOR}" \
    -licensingIpc "${SUNDOLL_UNITY_LICENSE_CHANNEL}" \
    "$@" \
    -logFile "${log_path}" &
  unity_pid=$!

  while kill -0 "${unity_pid}" 2>/dev/null; do
    elapsed_seconds=$((SECONDS - start_seconds))

    if [ "${elapsed_seconds}" -ge "${timeout_seconds}" ]; then
      printf 'Unity exceeded timeout after %s seconds; stopping process %s.\n' "${elapsed_seconds}" "${unity_pid}" >&2
      kill -TERM "${unity_pid}" 2>/dev/null || true
      wait "${unity_pid}" 2>/dev/null || true
      sundoll_cleanup_launched_licensing_client "${log_path}"
      return 124
    fi

    if [ "${elapsed_seconds}" -ge "${license_stall_seconds}" ] && [ -f "${log_path}" ]; then
      if grep -q 'Unsupported protocol version' "${log_path}" \
        || { grep -q 'Licensing initialization failed' "${log_path}" \
          && grep -q 'ObjectDisposedException' "${log_path}" \
          && grep -q 'The re-connection attempt was UN-successful' "${log_path}"; }; then
        printf 'Unity Licensing Client is on an invalid or stalled channel after %s seconds; stopping process %s.\n' "${elapsed_seconds}" "${unity_pid}" >&2
        kill -TERM "${unity_pid}" 2>/dev/null || true
        wait "${unity_pid}" 2>/dev/null || true
        sundoll_cleanup_launched_licensing_client "${log_path}"
        return 125
      fi
    fi

    sleep 5
  done

  wait "${unity_pid}"
}

sundoll_assert_license_log() {
  local log_path="$1"
  local expected_signal="Successfully connected to LicensingClient on channel: \"${SUNDOLL_UNITY_LICENSE_CHANNEL}\""

  if [ ! -f "${log_path}" ]; then
    printf 'Unity log was not created: %s\n' "${log_path}" >&2
    return 4
  fi
  if ! grep -Fq "${expected_signal}" "${log_path}"; then
    printf 'Unity did not confirm the required license channel %s.\n' "${SUNDOLL_UNITY_LICENSE_CHANNEL}" >&2
    return 127
  fi
  if grep -Eq 'Unsupported protocol version|Timed-out after 60\.00s|ObjectDisposedException|The re-connection attempt was UN-successful|Licensing initialization failed' "${log_path}"; then
    printf 'Unity log contains a licensing protocol, timeout, or reconnect failure.\n' >&2
    return 125
  fi

  printf 'License handshake passed on %s.\n' "${SUNDOLL_UNITY_LICENSE_CHANNEL}"
}

sundoll_print_connection_diagnostics() {
  local log_path="$1"

  printf '\n== Unity connection diagnostics ==\n' >&2
  if [ -f "${log_path}" ]; then
    printf 'Recent signals from %s:\n' "${log_path}" >&2
    grep -E 'Licensing|LicenseClient|Entitlement|Token|Package Manager|UPM|IPC|Timed-out|ObjectDisposedException|Curl error|No ULF|Access token|Aborting batchmode|Fatal|Error' "${log_path}" | tail -n 40 >&2 || true
  else
    printf 'Unity log was not created: %s\n' "${log_path}" >&2
  fi

  if [ -x "${SUNDOLL_UNITY_SCRIPT_DIR}/unity-doctor.sh" ]; then
    "${SUNDOLL_UNITY_SCRIPT_DIR}/unity-doctor.sh" >&2 || true
  else
    printf 'unity-doctor.sh is unavailable.\n' >&2
  fi
}
