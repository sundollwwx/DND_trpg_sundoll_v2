# M7 macOS Universal build evidence

日期：2026-08-28

## 构建

- Unity：`6000.3.22f1`
- 构建入口：Unity Editor 菜单 `Sundoll → M7 → Build macOS Universal`
- 构建目标：macOS Universal，IL2CPP，Metal
- 输出：`SundollWorld/Builds/SundollWorld-v03-M7-macOS-universal.app`
- Unity Console：`Build Finished, Result: Success`
- 构建摘要：`size=119990810`、`warnings=1`；`M7BuildValidation` 当前本地 Library/Bee 报告字段显示 `errors=27116`
- Player 可执行文件：`arm64 + x86_64` Mach-O Universal
- Player 可执行文件 SHA-256：`9569c6f96d08a855d0c6abe9d8b9241fe24f82333f2b842d8c6f55ef84dceb87`

## 运行验证

- 最新真实窗口性能 Player：`2560×1440`、1000 个可见棋子、退出码 `0`。
- 渲染承载 p95：`4.5495 ms`；托管分配 p95：`0 B`。
- EditMode：`84/84` 通过。
- PlayMode：`13/13` 通过。

## 限制

本次直接复用正式工程的本地 `Library/Bee`。Editor Console 仍显示遗留 TypeDB 重复注册诊断（999+），这与当前报告字段的异常 `errors=27116` 同时出现；未发现 `error CS`，构建和 Player 均成功，但该结果不替代干净临时工程的发布证据。此前干净临时构建已记录 TypeDB `0`、BuildReport `errors=0`、`warnings=1`；正式 Beta 仍需以干净导入/构建和长时 Soak 结果收口。
