# M7 macOS Universal build evidence

日期：2026-08-28

## 构建

- Git commit：`934231b fix: use board id for topmost piece picking`
- Unity：`6000.3.22f1`
- 构建入口：`scripts/unity-build-macos.sh`（固定版本专用许可证通道）
- 构建目标：macOS Universal，IL2CPP，Metal
- 输出：`SundollWorld/Builds/SundollWorld-v03-M7-macOS-universal.app`
- 构建日志：`SundollWorld/Logs/Build_macOS_20260828_101406.log`
- Unity Console：`Build Finished, Result: Success`
- 构建摘要：`size=2236792606`、`warnings=1`；`M7BuildValidation` 当前本地 Library/Bee 报告字段显示 `errors=27116`
- Player 可执行文件：`arm64 + x86_64` Mach-O Universal
- IL2CPP：`Contents/Frameworks/GameAssembly.dylib` 和 `Contents/Resources/Data/il2cpp_data/Metadata/global-metadata.dat` 存在；无 `MonoBleedingEdge` 或正式产品 DLL
- Player 可执行文件 SHA-256：`fc718f2f758fcb654183583b7cdcd5cbb41e3cc1444b8b0bac0ac8a1db577a91`
- 许可证：日志确认连接 `LicenseClient-sundoll-6000.3.22`，握手通过

## 运行验证

- 最新 Player Smoke：`scripts/macos-player-smoke.sh` 运行 45 秒，日志为 `SundollWorld/Logs/Smoke_M7_macos_20260828_101935.log`，退出码 `0`，未发现常见运行时异常。
- 真实窗口性能 Player：`2560×1440`、1000 个可见棋子、退出码 `0`。
- 渲染承载 p95：`4.5495 ms`；托管分配 p95：`0 B`。
- EditMode：`84/84` 通过。
- PlayMode：`13/13` 通过。

## 限制

本次直接复用正式工程的本地 `Library/Bee`。Editor Console 仍显示遗留 TypeDB 重复注册诊断（999+），这与当前报告字段的异常 `errors=27116` 同时出现；未发现 `error CS`，构建、IL2CPP 产物和 Player Smoke 均成功，但该结果不替代干净临时工程的发布证据。此前干净临时构建已记录 TypeDB `0`、BuildReport `errors=0`、`warnings=1`；正式 Beta 仍需以干净导入/构建和长时 Soak 结果收口。
