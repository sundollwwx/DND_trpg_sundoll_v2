# M7 Unity 连接诊断

日期：2026-08-28

## 结论

当前项目没有安装或配置 Unity MCP bridge，也没有官方 Unity relay 目录。因此 Codex 不能使用实时的 Unity Editor MCP 工具去读 Console、查层级或直接操作场景；后续验证默认使用仓库文件和 Unity batchmode。

本机 Unity 许可证通信之前确实不稳定。2026-08-26 的 Editor 日志显示，Editor 曾先连接到通用 `LicenseClient-sundoll`，随后因协议版本不匹配失败；再启动版本专用 `LicenseClient-sundoll-6000.3.22` 时，许可证客户端出现 `ObjectDisposedException` 和 60 秒超时，之后才重连成功。2026-08-27 的新 batchmode 探针已经能在约 1 秒内连接 Licensing Client，但仍没有 MCP bridge。

换成通俗说法：之前主要有两个问题混在一起了。许可证问题已经在项目入口层面关闭；MCP 是另一件独立的事。

- Unity 实时连接：项目没有桥，所以 Codex 本来就没有“直接连进 Editor”的通道。
- Unity 许可证通信：Hub/Editor/许可证客户端曾经通道混用和崩溃，导致启动时偶发卡住或报连接丢失；项目现在固定使用 Unity 版本专用通道。

## 已加入的稳定流程

- `scripts/unity-doctor.sh`：检查 Unity 版本、Editor 路径、Git 状态、活跃 Unity 进程、项目锁、MCP/relay 痕迹，以及最近 Licensing/Editor/UPM 日志。
- `scripts/unity-common.sh`：唯一的 Unity 启动策略，固定 Unity `6000.3.22f1` 和版本专用 `LicenseClient-sundoll-6000.3.22` 通道；同时安全处理项目锁、孤立旧通道客户端和超时后的本次客户端。
- `scripts/unity-run-tests.sh`：复用公共启动策略，生成带时间戳的测试 XML 和日志。
- `scripts/unity-license-check.sh`：无界面快速验证本机许可证握手和本地 entitlement，不修改许可证文件。
- `scripts/unity-build-macos.sh`：复用相同通道完成 macOS Universal IL2CPP 构建，并检查双架构、IL2CPP metadata 和 Mono 残留。
- `scripts/macos-player-smoke.sh`：构建后运行 macOS Player 45 秒，检查播放器运行态和常见运行时异常。
- `scripts/unity-open.sh` / `02-在Unity中编辑SundollWorld.command`：交互式打开正式工程时也强制传入版本专用通道，避免从 Hub 项目行启动造成通道漂移。
- `scripts/unity-run-tests.sh` 在 Unity 未产出 XML 时会自动打印最近 License/UPM/IPC 线索并调用 Doctor，避免“只知道失败、不知道卡在哪里”。
- `scripts/unity-run-tests.sh` 增加 watchdog：默认 900 秒整体超时，180 秒 License 崩溃/重连循环保护；触发时停止本次 batchmode 子进程并保留日志。退出码 `124` 表示整体超时，`125` 表示 License 重连循环。
- `scripts/unity-run-tests.sh` 在发现 `UnityLockfile` 时先检查精确的 Unity Editor 进程：确认没有 Editor 才把明确的临时锁移动到 `/private/tmp/SundollWorld_UnityLockfile_<时间>.stale` 备份；若 Editor 正在运行，仍然停止并要求先关闭。

## 2026-08-28 许可证通道修复闭环

- 根因确认：Hub 仍管理着通用 `LicenseClient-sundoll`，该通道对 Unity `6000.3.22f1` 返回 `Unsupported protocol version '1.18.1'`；这不是账号失效，也不是项目代码错误。
- 处理：结束孤立的通用 Licensing Client，并将遗留空锁移动到 `/private/tmp/SundollWorld_UnityLockfile_license_recovery_20260828.stale`；没有删除许可证、缓存、项目或存档。
- 持久化修复：所有正式测试、许可证自检、macOS 构建和交互式打开入口都传入 `-licensingIpc LicenseClient-sundoll-6000.3.22`。脚本还会在没有活动 Editor 时识别并停止孤立的通用客户端。
- 独立探针：`scripts/unity-license-check.sh` 于 2026-08-28 02:00 通过，日志为 `SundollWorld/Logs/LicenseCheck_20260828_020052.log`；握手成功，Unity 初始化许可证成功，本地 entitlement 可用，无需重新登录。
- 回归证据：最新完整 XML 为 EditMode `87/87`、PlayMode `13/13`，日志中均确认 `LicenseClient-sundoll-6000.3.22`，未出现协议不匹配、60 秒超时、`ObjectDisposedException` 或失败重连。

这里的“以后不会再出现”边界是：通过本项目提供的入口不会再主动走旧通道。Hub 自己的通用客户端仍可能为 Hub 其他项目保留；打开本项目请使用 `02-在Unity中编辑SundollWorld.command`，不要从 Hub 的项目行直接启动。

## 2026-08-27 15:33 复查

- Unity `6000.3.22f1` 路径存在，项目 `ProjectVersion.txt` 匹配。
- 当前没有 Unity/Hub/PackageManager 活跃进程，项目下没有 `UnityLockfile` 或其他锁文件。
- 当前仍未检测到 MCP bridge 或官方 relay；Codex 不能实时读取 Editor Console/Hierarchy。
- Licensing 日志仍有 `401 Token not found in cache`、`No ULF license found` 和 `assigned update failed` 记录；Editor 日志也有证书校验噪声，但同一轮日志显示 entitlement 能成功解析。
- 本轮复现一次 batchmode 卡在 License Client：日志包含 `Timed-out after 60.00s`、`ObjectDisposedException` 和 `The re-connection attempt was UN-successful`；中止后残留一个空 `Temp/UnityLockfile` 和孤立 Licensing Client。已手动结束孤立客户端，并将空锁移动到 `/private/tmp/SundollWorld_UnityLockfile_20260827_153828.stale`。
- 清理后用授权的系统权限复跑 `scripts/unity-run-tests.sh all`，历史基线 EditMode `83/83`、PlayMode `11/11` 均通过，XML 为 `TestResults_EditMode_20260827_155030.xml` 和 `TestResults_PlayMode_20260827_155030.xml`。
- 22:01 的复跑再次确认：沙箱会阻止 `/tmp/Unity-Upm-*.sock`，导致 Package Manager IPC 失败；获得必要系统权限后，第一次仍因孤立 Licensing Client 进程触发 `ObjectDisposedException` 和 60 秒重连超时。结束该次验证遗留的孤立客户端、移走临时锁后，22:05 的复跑在 0.45 秒内完成 Licensing 握手并通过 EditMode `84/84`、PlayMode `12/12`；新增性能测试后 22:11 再通过 EditMode `84/84`、PlayMode `13/13`。
- 因此本项目侧采用“固定 batchmode + XML/日志证据”的稳定验证链路；若要实时 Editor 控制，需要另行确认并安装一个 Unity MCP provider。

## 日常使用

```bash
scripts/unity-doctor.sh
scripts/unity-license-check.sh
scripts/unity-run-tests.sh editmode
scripts/unity-run-tests.sh playmode
scripts/unity-run-tests.sh all
scripts/unity-build-macos.sh
scripts/macos-player-smoke.sh
```

如需临时调整 watchdog：

```bash
UNITY_LICENSE_STALL_SECONDS=240 UNITY_TEST_TIMEOUT_SECONDS=1200 scripts/unity-run-tests.sh all
```

## 操作原则

- 自动化验证优先使用精确的 Unity `6000.3.22f1` 路径，不依赖 Hub 当前选中的项目行。
- 交互式 Editor 打开时不要并行跑 batchmode 测试；若 `Temp/UnityLockfile` 存在，先关闭 Editor。
- 若上次 Unity 异常退出遗留锁，验证脚本会在没有活动 Editor 时自动备份并移走该临时锁；不要手动删除正在运行 Editor 的锁文件。
- 不在项目中同时安装多个 Unity MCP provider，避免工具重复和连接状态混乱。
- 如果后续需要 Codex 直接读 Unity Console、查场景层级或操作 GameObject，需要单独选择一个 Unity MCP provider；当前不在未经确认的情况下修改 Unity 包配置。

## 当前风险

- Unity Licensing Client 的底层崩溃属于 Unity/Hub 本机链路问题；项目脚本已经固定正确通道，并对旧孤立客户端和本次异常退出做自动保护，但无法修复 Unity/Hub 二进制本身的上游缺陷。
- 没有 MCP bridge 时，Codex 无法保证实时 Editor 状态，只能通过 batchmode 日志、XML 证据、文件和必要时的本机 UI 操作来验证。
