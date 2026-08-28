#!/usr/bin/env bash
set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=unity-common.sh
source "${SCRIPT_DIR}/unity-common.sh"

STAMP="$(date +%Y%m%d_%H%M%S)"
APP_PATH="${SUNDOLL_PROJECT_ROOT}/Builds/SundollWorld-v03-M7-macOS-universal.app"
LOG_PATH="${SUNDOLL_PROJECT_ROOT}/Logs/Smoke_M7_macos_${STAMP}.log"
SMOKE_SECONDS="${MACOS_PLAYER_SMOKE_SECONDS:-45}"

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

printf 'Starting macOS Player Smoke for %s seconds.\n' "${SMOKE_SECONDS}"
printf 'Executable: %s\n' "${EXECUTABLE_PATH}"
printf 'Log: %s\n' "${LOG_PATH}"

"${EXECUTABLE_PATH}" -batchmode -nographics -logFile "${LOG_PATH}" &
player_pid=$!
start_seconds="${SECONDS}"
elapsed_seconds=0

while kill -0 "${player_pid}" 2>/dev/null; do
  elapsed_seconds=$((SECONDS - start_seconds))
  if [ "${elapsed_seconds}" -ge "${SMOKE_SECONDS}" ]; then
    kill -TERM "${player_pid}" 2>/dev/null || true
    break
  fi
  sleep 5
done

wait "${player_pid}" 2>/dev/null
player_exit=$?
if [ "${player_exit}" -ne 0 ] && [ "${player_exit}" -ne 143 ]; then
  printf 'Player Smoke exited with %s.\n' "${player_exit}" >&2
  exit "${player_exit}"
fi

if grep -Eq 'error CS|NullReferenceException|MissingReferenceException|ArgumentException|Fatal Error|Crash!!!' "${LOG_PATH}"; then
  printf 'Player Smoke log contains a runtime failure signature.\n' >&2
  rg -n 'error CS|NullReferenceException|MissingReferenceException|ArgumentException|Fatal Error|Crash!!!' "${LOG_PATH}" >&2 || true
  exit 6
fi

printf 'macOS Player Smoke passed; process was alive for at least %s seconds and no runtime failure signature was found.\n' "${SMOKE_SECONDS}"
