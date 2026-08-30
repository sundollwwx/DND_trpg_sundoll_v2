# M7 macOS Universal IL2CPP 验证：2026-08-31

范围：验证 `87bd5a1` 的 M5 多地图工作区修复能够编译并进入 macOS Player。本记录不替代 Windows 或跨平台验证。

## 基线

- Unity：`6000.3.22f1`
- 启动场景：`Assets/Sundoll/Scenes/M3Workbench.unity`
- 后端：IL2CPP
- 目标架构：`x86_64 + arm64`
- 许可证：`LicenseClient-sundoll-6000.3.22` 握手成功。
- 自动化回归：EditMode `93/93`、PlayMode `15/15`，均 0 失败、0 跳过（`TestResults_*_20260830_214327.xml`）。

## 构建与 Smoke

- 构建命令：`scripts/unity-build-macos.sh`
- 构建日志：`Logs/Build_macOS_20260831_010000.log`
- Unity 结果：`Build Finished, Result: Success`，`M7 macOS universal build result: Succeeded`。
- 产物：`Builds/SundollWorld-v03-M7-macOS-universal.app`
- 主可执行文件：Universal Mach-O，包含 `x86_64` 与 `arm64`。
- IL2CPP：`global-metadata.dat` 存在；未发现 `MonoBleedingEdge` 或产品托管 DLL。
- 可执行文件 SHA-256：`6a5931b1977d63c12db0f970729e8104f8b17c770f015a939b34d66e4ad1c258`。
- Smoke 命令：`scripts/macos-player-smoke.sh`
- Smoke 日志：`Logs/Smoke_M7_macos_20260831_010418.log`
- Smoke 结果：Player 运行至脚本结束；未出现 `error CS`、`NullReferenceException`、`MissingReferenceException`、`ArgumentException`、`Fatal Error` 或 `Crash!!!`。

## TypeDB 诊断分类

本机构建缓存仍记录 `34819` 条 TypeDB 重复登记诊断，其中 `34789` 条为 NUnit `Class`、`30` 条为 NUnit `Assembly`，均指向 `Packages/com.unity.ext.nunit/net40/unity-custom/nunit.framework.dll` 的同一登记来源。它们令 Unity 的 BuildReport `totalErrors` 计数失真，但没有 C# 编译错误、没有 Build Failure，且 Player 成功生成和启动。

这与先前的干净临时导入证据一致：问题属于当前本机旧 `Library/Bee` 缓存，尚未在不清理用户约 11 GB 缓存的前提下从本机构建日志消除。发布结论仍保留为 macOS **Ready with limitations**，不能将该本机日志描述为“无未解释 TypeDB 诊断”。
