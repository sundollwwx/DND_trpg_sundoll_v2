# M7 current commit macOS build evidence

日期：2026-08-27

## Build

- Git commit：`3424309 perf: add m7 workbench performance baseline`
- Unity：`6000.3.22f1`
- 目标：macOS Standalone Universal IL2CPP
- Build 方法：临时 Git 导出工程执行 `Sundoll.EditorTools.M7BuildValidation.BuildMacOSUniversal`
- 构建日志：`/private/tmp/sundollworld-m7-current-V1VlTo/Build_M7_current.log`
- BuildPipeline：`Build Finished, Result: Success`
- BuildReport：`errors=0`、`warnings=1`
- 警告：Unity Cloud Diagnostics/native symbols 上传 token 为空；不是 C# 编译或项目运行时错误
- 产物：`/private/tmp/sundollworld-m7-current-V1VlTo/SundollWorld/Builds/SundollWorld-v03-M7-macOS-universal.app`
- 产物大小：`119,974,442` bytes（约 114.4 MiB，BuildReport 字节数）
- 主可执行文件：`x86_64 + arm64` universal Mach-O
- 主可执行文件 SHA-256：`e2c948a23ab5b79689b7d80e3971bf50a336af05aeb903bad97d97b5e2f109a8`

## Player Smoke

- 命令：启动主可执行文件，使用 `-batchmode -nographics`，运行 45 秒后结束。
- 日志：`/private/tmp/sundollworld-m7-current-V1VlTo/PlayerSmoke_M7_current.log`
- 退出码：`0`
- 已确认：Unity `6000.3.22f1`、Input System 初始化、Player 正常进入运行态并正常关闭。
- 日志未发现：`error CS`、`NullReferenceException`、`MissingReferenceException`、`ArgumentException`。

## 限制

这是当前提交的 macOS 构建和无图形 Smoke 证据。它不替代真实桌面窗口的 2560×1440 60 FPS/视觉操作验证，也不覆盖 Windows IL2CPP、跨平台存档互开或双平台强制退出恢复。
