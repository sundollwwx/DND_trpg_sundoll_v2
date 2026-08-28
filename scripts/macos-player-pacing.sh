#!/usr/bin/env bash
set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=unity-common.sh
source "${SCRIPT_DIR}/unity-common.sh"

APP_PATH="${SUNDOLL_PROJECT_ROOT}/Builds/SundollWorld-v03-M7-macOS-universal.app"
STAMP="$(date +%Y%m%d_%H%M%S)"
LOG_PATH="${SUNDOLL_PROJECT_ROOT}/Logs/Pacing_M7_macos_${STAMP}.log"
OUTPUT_PATH="${SUNDOLL_PROJECT_ROOT}/Logs/Pacing_M7_macos_${STAMP}.json"
TARGET_FPS="${MACOS_PLAYER_PACING_FPS:-60}"
WARMUP_FRAMES="${MACOS_PLAYER_PACING_WARMUP_FRAMES:-120}"
SAMPLE_FRAMES="${MACOS_PLAYER_PACING_SAMPLE_FRAMES:-3600}"
if [ -n "${MACOS_PLAYER_PACING_TIMEOUT_SECONDS:-}" ]; then
  TIMEOUT_SECONDS="${MACOS_PLAYER_PACING_TIMEOUT_SECONDS}"
elif [ "${TARGET_FPS}" -gt 0 ] 2>/dev/null; then
  EXPECTED_SECONDS=$(( (SAMPLE_FRAMES + WARMUP_FRAMES + TARGET_FPS - 1) / TARGET_FPS ))
  TIMEOUT_SECONDS=$(( EXPECTED_SECONDS + EXPECTED_SECONDS / 4 + 120 ))
else
  TIMEOUT_SECONDS=120
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

printf 'Starting macOS production pacing capture.\n'
printf 'Target: %s FPS; warmup: %s frames; sample: %s frames.\n' "${TARGET_FPS}" "${WARMUP_FRAMES}" "${SAMPLE_FRAMES}"
printf 'Timeout: %s seconds.\n' "${TIMEOUT_SECONDS}"
printf 'Executable: %s\n' "${EXECUTABLE_PATH}"
printf 'Log: %s\n' "${LOG_PATH}"
printf 'Result: %s\n' "${OUTPUT_PATH}"

"${EXECUTABLE_PATH}" \
  -screen-width 2560 \
  -screen-height 1440 \
  -screen-fullscreen 0 \
  -sundoll-m7-perf \
  -sundoll-m7-perf-target-fps "${TARGET_FPS}" \
  -sundoll-m7-perf-warmup "${WARMUP_FRAMES}" \
  -sundoll-m7-perf-frames "${SAMPLE_FRAMES}" \
  -sundoll-m7-perf-output "${OUTPUT_PATH}" \
  -logFile "${LOG_PATH}" &
player_pid=$!
start_seconds="${SECONDS}"

while kill -0 "${player_pid}" 2>/dev/null; do
  elapsed_seconds=$((SECONDS - start_seconds))
  if [ "${elapsed_seconds}" -ge "${TIMEOUT_SECONDS}" ]; then
    printf 'Pacing capture exceeded timeout after %s seconds.\n' "${elapsed_seconds}" >&2
    kill -TERM "${player_pid}" 2>/dev/null || true
    wait "${player_pid}" 2>/dev/null || true
    exit 124
  fi
  sleep 5
done

wait "${player_pid}" 2>/dev/null
player_exit=$?
if [ "${player_exit}" -ne 0 ]; then
  printf 'Pacing capture Player exited with %s.\n' "${player_exit}" >&2
  exit "${player_exit}"
fi

if [ ! -f "${OUTPUT_PATH}" ]; then
  printf 'Pacing capture did not create its JSON result.\n' >&2
  exit 6
fi

if grep -Eq 'error CS|NullReferenceException|MissingReferenceException|ArgumentException|Fatal Error|Crash!!!' "${LOG_PATH}"; then
  printf 'Pacing capture log contains a runtime failure signature.\n' >&2
  rg -n 'error CS|NullReferenceException|MissingReferenceException|ArgumentException|Fatal Error|Crash!!!' "${LOG_PATH}" >&2 || true
  exit 6
fi

printf 'macOS production pacing capture completed.\n'
sed -n '1,220p' "${OUTPUT_PATH}"
