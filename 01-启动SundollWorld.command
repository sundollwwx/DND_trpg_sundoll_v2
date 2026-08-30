#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_PATH="${SCRIPT_DIR}/SundollWorld/Builds/SundollWorld-v03-M7-macOS-universal.app"

if [ ! -d "${APP_PATH}" ]; then
  printf '找不到已构建的 SundollWorld 应用：%s\n' "${APP_PATH}" >&2
  printf '请先在 Unity 中完成 macOS 构建，或使用 02-在Unity中编辑SundollWorld.command 打开工程。\n' >&2
  exit 1
fi

exec open "${APP_PATH}"
