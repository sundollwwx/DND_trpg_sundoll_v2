#!/usr/bin/env bash
set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=unity-common.sh
source "${SCRIPT_DIR}/unity-common.sh"

STAMP="$(date +%Y%m%d_%H%M%S)"
LOG_PATH="${SUNDOLL_PROJECT_ROOT}/Logs/Build_macOS_${STAMP}.log"
APP_PATH="${SUNDOLL_PROJECT_ROOT}/Builds/SundollWorld-v03-M7-macOS-universal.app"
TIMEOUT_SECONDS="${UNITY_BUILD_TIMEOUT_SECONDS:-3600}"
STALL_SECONDS="${UNITY_LICENSE_STALL_SECONDS:-60}"

sundoll_prepare_unity_launch "${STAMP}" || exit $?

printf 'Building macOS Universal IL2CPP player.\n'
printf 'Output: %s\n' "${APP_PATH}"
sundoll_run_unity_with_watchdog \
  "${LOG_PATH}" \
  "${TIMEOUT_SECONDS}" \
  "${STALL_SECONDS}" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "${SUNDOLL_PROJECT_ROOT}" \
  -executeMethod Sundoll.EditorTools.M7BuildValidation.BuildMacOSUniversal
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

if [ ! -d "${APP_PATH}" ]; then
  printf 'Build reported success but the app bundle is missing: %s\n' "${APP_PATH}" >&2
  exit 5
fi

EXECUTABLE_NAME="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "${APP_PATH}/Contents/Info.plist" 2>/dev/null || true)"
EXECUTABLE_PATH="${APP_PATH}/Contents/MacOS/${EXECUTABLE_NAME}"
if [ -z "${EXECUTABLE_NAME}" ] || [ ! -x "${EXECUTABLE_PATH}" ]; then
  printf 'The app bundle has no executable declared by CFBundleExecutable.\n' >&2
  exit 5
fi

FILE_SUMMARY="$(file "${EXECUTABLE_PATH}")"
if [[ "${FILE_SUMMARY}" != *x86_64* ]] || [[ "${FILE_SUMMARY}" != *arm64* ]]; then
  printf 'The player executable is not macOS universal (x86_64 + arm64): %s\n' "${FILE_SUMMARY}" >&2
  exit 5
fi

METADATA_PATH="$(find "${APP_PATH}/Contents" -type f -name global-metadata.dat -print -quit 2>/dev/null)"
if [ -z "${METADATA_PATH}" ]; then
  printf 'IL2CPP global-metadata.dat is missing from the app bundle.\n' >&2
  exit 5
fi
if [ -d "${APP_PATH}/Contents/MonoBleedingEdge" ]; then
  printf 'MonoBleedingEdge exists in the formal M7 build; expected IL2CPP.\n' >&2
  exit 5
fi
if find "${APP_PATH}/Contents" -type f \( -name 'Sundoll*.dll' -o -name 'Assembly-CSharp.dll' \) -print -quit 2>/dev/null | grep -q .; then
  printf 'Managed Sundoll product DLLs remain in the formal M7 build; expected IL2CPP output.\n' >&2
  exit 5
fi

EXECUTABLE_SHA256="$(shasum -a 256 "${EXECUTABLE_PATH}" | awk '{print $1}')"
BUILD_SUMMARY="$(grep -F 'M7 macOS universal build result:' "${LOG_PATH}" | tail -n 1)"

printf '%s\n' "${BUILD_SUMMARY}"
printf 'Architecture: %s\n' "${FILE_SUMMARY}"
printf 'IL2CPP metadata: %s\n' "${METADATA_PATH}"
printf 'Executable SHA-256: %s\n' "${EXECUTABLE_SHA256}"
printf 'macOS Universal IL2CPP build verification passed.\n'
