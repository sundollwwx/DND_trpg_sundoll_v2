#!/usr/bin/env bash
set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=unity-common.sh
source "${SCRIPT_DIR}/unity-common.sh"

APP_PATH="${SUNDOLL_PROJECT_ROOT}/Builds/SundollWorld-v03-M7-macOS-universal.app"
STAMP="$(date +%Y%m%d_%H%M%S)"
LOG_PATH="${SUNDOLL_PROJECT_ROOT}/Logs/Soak_M7_macos_${STAMP}.log"
OUTPUT_PATH="${SUNDOLL_PROJECT_ROOT}/Logs/Soak_M7_macos_${STAMP}.json"
SOAK_SECONDS="${MACOS_PLAYER_SOAK_SECONDS:-180}"
if [ -n "${MACOS_PLAYER_SOAK_TIMEOUT_SECONDS:-}" ]; then
  TIMEOUT_SECONDS="${MACOS_PLAYER_SOAK_TIMEOUT_SECONDS}"
else
  TIMEOUT_SECONDS=$((SOAK_SECONDS + 600))
fi

if [ ! -d "${APP_PATH}" ]; then
  printf 'Player bundle is missing: %s\n' "${APP_PATH}" >&2
  exit 5
fi

EXECUTABLE_NAME="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "${APP_PATH}/Contents/Info.plist" 2>/dev/null || true)"
EXECUTABLE_PATH="${APP_PATH}/Contents/MacOS/${EXECUTABLE_NAME}"
if [ -z "${EXECUTABLE_NAME}" ] || [ ! -x "${EXECUTABLE_PATH}" ]; then
  printf 'Player executable is missing from the bundle.\n' >&2
  exit 5
fi

printf 'Starting macOS operation soak capture.\n'
printf 'Duration: %s seconds; timeout: %s seconds.\n' "${SOAK_SECONDS}" "${TIMEOUT_SECONDS}"
printf 'Launch mode: open.\n'
printf 'Executable: %s\n' "${EXECUTABLE_PATH}"
printf 'Log: %s\n' "${LOG_PATH}"
printf 'Result: %s\n' "${OUTPUT_PATH}"

PLAYER_ARGS=(
  -screen-width 2560
  -screen-height 1440
  -screen-fullscreen 0
  -sundoll-m7-soak
  -sundoll-m7-soak-seconds "${SOAK_SECONDS}"
  -sundoll-m7-soak-output "${OUTPUT_PATH}"
  -logFile "${LOG_PATH}"
)

existing_player_pids=""
if command -v pgrep >/dev/null 2>&1; then
  existing_player_pids="$(pgrep -f "${EXECUTABLE_PATH}" 2>/dev/null || true)"
fi
/usr/bin/open -n -W "${APP_PATH}" --args "${PLAYER_ARGS[@]}" &
launcher_pid=$!
opened_player_pid=""
if command -v pgrep >/dev/null 2>&1; then
  player_lookup_attempt=0
  while [ "${player_lookup_attempt}" -lt 50 ]; do
    candidate_player_pid="$(pgrep -n -f "${EXECUTABLE_PATH}" 2>/dev/null || true)"
    candidate_is_existing=0
    for existing_player_pid in ${existing_player_pids}; do
      if [ "${candidate_player_pid}" = "${existing_player_pid}" ]; then
        candidate_is_existing=1
        break
      fi
    done
    if [ -n "${candidate_player_pid}" ] && [ "${candidate_is_existing}" -eq 0 ]; then
      opened_player_pid="${candidate_player_pid}"
      break
    fi
    sleep 0.1
    player_lookup_attempt=$((player_lookup_attempt + 1))
  done
fi

start_seconds="${SECONDS}"
while kill -0 "${launcher_pid}" 2>/dev/null; do
  elapsed_seconds=$((SECONDS - start_seconds))
  if [ "${elapsed_seconds}" -ge "${TIMEOUT_SECONDS}" ]; then
    printf 'Soak capture exceeded timeout after %s seconds.\n' "${elapsed_seconds}" >&2
    kill -TERM "${launcher_pid}" 2>/dev/null || true
    if [ -n "${opened_player_pid}" ]; then
      kill -TERM "${opened_player_pid}" 2>/dev/null || true
    fi
    wait "${launcher_pid}" 2>/dev/null || true
    exit 124
  fi
  sleep 5
done

wait "${launcher_pid}" 2>/dev/null
player_exit=$?
if [ "${player_exit}" -ne 0 ]; then
  printf 'Soak capture Launcher exited with %s.\n' "${player_exit}" >&2
  exit "${player_exit}"
fi

if [ ! -f "${OUTPUT_PATH}" ]; then
  printf 'Soak capture did not create its JSON result.\n' >&2
  exit 6
fi

if ! jq -e '.ok == true' "${OUTPUT_PATH}" >/dev/null 2>&1; then
  printf 'Soak capture reported failure.\n' >&2
  jq . "${OUTPUT_PATH}" >&2 || true
  exit 6
fi

if grep -Eq 'error CS|NullReferenceException|MissingReferenceException|ArgumentException|Fatal Error|Crash!!!|Abort trap' "${LOG_PATH}"; then
  printf 'Soak capture log contains a runtime failure signature.\n' >&2
  rg -n 'error CS|NullReferenceException|MissingReferenceException|ArgumentException|Fatal Error|Crash!!!|Abort trap' "${LOG_PATH}" >&2 || true
  exit 6
fi

printf 'macOS operation soak capture completed.\n'
jq . "${OUTPUT_PATH}"
