# M7 Unity 连接诊断

日期：2026-08-27

## 结论

当前项目没有安装或配置 Unity MCP bridge，也没有官方 Unity relay 目录。因此 Codex 不能使用实时的 Unity Editor MCP 工具去读 Console、查层级或直接操作场景；后续验证默认使用仓库文件和 Unity batchmode。

本机 Unity 许可证通信之前确实不稳定。2026-08-26 的 Editor 日志显示，Editor 曾先连接到通用 `LicenseClient-sundoll`，随后因协议版本不匹配失败；再启动版本专用 `LicenseClient-sundoll-6000.3.22` 时，许可证客户端出现 `ObjectDisposedException` 和 60 秒超时，之后才重连成功。2026-08-27 的新 batchmode 探针已经能在约 1 秒内连接 Licensing Client，但仍没有 MCP bridge。

换成通俗说法：现在主要有两个问题混在一起了。

- Unity 实时连接：项目没有桥，所以 Codex 本来就没有“直接连进 Editor”的通道。
- Unity 许可证通信：Hub/Editor/许可证客户端曾经通道混用和崩溃，导致启动时偶发卡住或报连接丢失。

## 已加入的稳定流程

- `scripts/unity-doctor.sh`：检查 Unity 版本、Editor 路径、Git 状态、活跃 Unity 进程、项目锁、MCP/relay 痕迹，以及最近 Licensing/Editor/UPM 日志。
- `scripts/unity-run-tests.sh`：固定使用 `/Applications/Unity/Hub/Editor/6000.3.22f1/Unity.app/Contents/MacOS/Unity` 启动 batchmode，生成带时间戳的测试 XML 和日志。

## 日常使用

```bash
scripts/unity-doctor.sh
scripts/unity-run-tests.sh editmode
scripts/unity-run-tests.sh playmode
scripts/unity-run-tests.sh all
```

## 操作原则

- 自动化验证优先使用精确的 Unity `6000.3.22f1` 路径，不依赖 Hub 当前选中的项目行。
- 交互式 Editor 打开时不要并行跑 batchmode 测试；若 `Temp/UnityLockfile` 存在，先关闭 Editor。
- 不在项目中同时安装多个 Unity MCP provider，避免工具重复和连接状态混乱。
- 如果后续需要 Codex 直接读 Unity Console、查场景层级或操作 GameObject，需要单独选择一个 Unity MCP provider；当前不在未经确认的情况下修改 Unity 包配置。

## 当前风险

- Unity Licensing Client 的崩溃属于 Unity/Hub 本机链路问题，项目脚本只能诊断并固定验证入口，不能从项目源码里彻底修复 Unity 自身进程崩溃。
- 没有 MCP bridge 时，Codex 无法保证实时 Editor 状态，只能通过 batchmode 日志、XML 证据、文件和必要时的本机 UI 操作来验证。
