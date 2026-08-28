#!/usr/bin/env bash
set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=unity-common.sh
source "${SCRIPT_DIR}/unity-common.sh"

STAMP="$(date +%Y%m%d_%H%M%S)"

sundoll_prepare_unity_launch "${STAMP}" || exit $?

printf 'Opening SundollWorld with Unity %s.\n' "${SUNDOLL_EXPECTED_UNITY_VERSION}"
printf 'Pinned license channel: %s\n' "${SUNDOLL_UNITY_LICENSE_CHANNEL}"
exec "${SUNDOLL_UNITY_EDITOR}" \
  -licensingIpc "${SUNDOLL_UNITY_LICENSE_CHANNEL}" \
  -projectPath "${SUNDOLL_PROJECT_ROOT}"
