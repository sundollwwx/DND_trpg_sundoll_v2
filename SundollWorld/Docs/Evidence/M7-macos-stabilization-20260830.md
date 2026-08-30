# M7 macOS 稳定化证据

日期：2026-08-30
Unity：`6000.3.22f1`
平台：macOS Apple Silicon，正式 Player 为 Universal（arm64+x86_64）

## 自动化回归

- EditMode：`87/87`，0 failed，0 skipped。
- PlayMode：`13/13`，0 failed，0 skipped。
- 结果文件：`TestResults_EditMode_20260830_133200.xml`、`TestResults_PlayMode_20260830_133200.xml`。
- 两次运行均通过版本专用许可证通道 `LicenseClient-sundoll-6000.3.22`。

## 正式构建

- 入口：`scripts/unity-build-macos.sh`。
- 结果：Success，IL2CPP，输出 `Builds/SundollWorld-v03-M7-macOS-universal.app`。
- 主可执行文件包含 `x86_64` 和 `arm64`；`global-metadata.dat` 存在；无 Mono 残留。
- Player 可执行文件 SHA-256：`9491c33b9d9b00092c5ba89f43c6245449455aa5653a2a500e614558bf5aee36`。
- 构建日志：`Logs/Build_macOS_20260829_212811.log`。

## 操作 Soak

- 10 分钟 Soak：通过。600 秒、537 周期、7,668 条命令、69/69 次保存、44 次 View 重建；最终保存状态 `Safe`；重开前后 Canonical Hash 一致。
- 60 分钟 Soak：本轮运行约 50 分钟后终端/Player 会话中断，未生成结果 JSON；无崩溃堆栈或保存失败记录。按未验证处理，不重复运行。
- 未生成结果的日志保留为 `Logs/Soak_M7_macos_20260829_214023.log`，用于审计，不作为通过证据。

## 当前交接点

macOS 侧可交付为 Release Candidate（严格生产 60 FPS pacing 仍未通过）；下一项发布阻塞工作是 Windows x64 IL2CPP 构建、Windows 写盘故障注入、macOS↔Windows 包互开和双平台强制退出恢复。执行方法见 `Docs/M7-Windows验证交接.md`。
