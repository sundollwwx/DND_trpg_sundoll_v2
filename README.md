# 桑哆尔的世界 · Unity

> 状态：**M7 macOS Universal IL2CPP、自动化测试和 10 分钟操作 Soak 已通过；Windows/跨平台发布矩阵仍未关闭**
> 更新日期：2026-08-30

这是“桑哆尔的世界”Unity 原生桌面产品的规划容器。目前包含方案文档、一次性 M0 Spike，以及正在推进的正式 Unity 工程 `SundollWorld`。

## 文档

- [Unity 从零开发工作计划](./Unity从零开发工作计划.md)
- [简明汇报](./简明汇报.md)
- [M0 一次性技术验证](./M0-Spike/README.md)
- [正式工程上下文](./SundollWorld/Docs/AI/UnityProjectContext.md)
- [M1 结果报告](./SundollWorld/Docs/M1-结果报告.md)
- [M2 结果报告](./SundollWorld/Docs/M2-结果报告.md)
- [M3 结果报告](./SundollWorld/Docs/M3-结果报告.md)
- [M4 结果报告](./SundollWorld/Docs/M4-结果报告.md)
- [M5 结果报告](./SundollWorld/Docs/M5-结果报告.md)
- [M6 结果报告](./SundollWorld/Docs/M6-结果报告.md)
- [M7 结果报告](./SundollWorld/Docs/M7-结果报告.md)
- [M7 Unity 连接诊断](./SundollWorld/Docs/M7-Unity连接诊断.md)
- [M7 Windows 验证交接](./SundollWorld/Docs/M7-Windows验证交接.md)
- [v0.3 实施状态](./SundollWorld/Docs/v0.3-实施状态.md)

## 产品目标

制作一个本地优先、规则无关的 2D 桌面棋盘工作台，包含：

- 地图制作器
- 主控台
- 项目棋子库
- 棋子和场景的通用交互
- 可靠的新格式存档与恢复
- 后续规则模块和联机系统的清晰接入口

## Greenfield 边界

历史网页项目只作为产品思想和交互经验参考。新 Unity 项目不会读取或复制其代码、地图、棋子、存档、图片或音乐，不开发旧格式导入器，也不承担旧数据备份与兼容任务。

## 正式工程当前状态

- 正式产品代码位于 `SundollWorld/Assets/Sundoll/`，当前已包含 M1 最小纵向闭环、M2 存档基础设施、M3 地图编辑器、M4 棋子库/空间交互、M5 主控台切片、M6A/M6B 证明、M7 v1 加固骨架，以及 M7 棋子库虚拟化缩略图网格。
- 启动场景为 `Assets/Sundoll/Scenes/M3Workbench.unity`；`M1Bootstrap.unity` 保留为旧诊断入口。
- Git 基线已经建立；首个基线提交为 `b8d704f`，后续实现按小批次提交，Unity 缓存与构建产物保持在本机且不纳入版本控制。
- M2/M3/M4/M5 存档仍为 pre-v1；M7 已提供 save v1/schema 2 冻结、Schema 1→2 迁移注册表、Golden Save 校验、可复用 View Pool 和引用计数纹理缓存。M4 当前通过路径导入和 macOS 原生文件选择器完成图片选择/取消接缝，最新 Player 已实际走通选择与 Escape 取消；正式图片视觉、2560×1440 帧率/分配、Windows 与跨平台互开仍未验证。
- 未复制任何旧项目素材。

M0 已正式接受并保留 Windows/Finder 限制；Unity `6000.3.22f1` 最新已存证 EditMode 87/87、PlayMode 13/13，macOS universal IL2CPP Player 成功启动。未使用的 Visual Scripting 包已移除；干净临时构建中 TypeDB 重复类型诊断为 0，当前本地旧 Library/Bee 构建仍会报告历史 TypeDB 诊断，另有 1 条 Unity Cloud 符号上传 token warning，不是 C# 编译错误。最新测试证据见 [EditMode XML](./SundollWorld/TestResults_EditMode_20260830_133200.xml)、[PlayMode XML](./SundollWorld/TestResults_PlayMode_20260830_133200.xml) 和 [M7 结果报告](./SundollWorld/Docs/M7-结果报告.md)。M7 已有 macOS 批量编辑、Snapshot、Revision 保存、10,000 Journal 恢复、64 棋子纹理共享、1000 棋子投影和稳态分配、棋子库缩略图 LRU 基线；1000 棋子 headless 投影刷新 p95 为 12.494 ms、稳态托管分配 p95 为 0 B。真实 2560×1440 Player 窗口解除采样限速后的渲染承载 p95 为 4.5495 ms、托管分配 p95 为 0 B；最新构建已再次通过 Universal IL2CPP 校验。10 分钟操作 Soak 通过，60 分钟操作 Soak 本轮运行约 50 分钟后未生成结果文件，按未验证处理；生产 60 FPS pacing、Windows/跨平台/强退矩阵仍未关闭。M4 使用新的 `SundollWorld_M4` 本地开发存档根目录，旧 M1/M2/M3 样本原地保留。

Unity 连接和许可证链路排查入口见 [M7 Unity 连接诊断](./SundollWorld/Docs/M7-Unity连接诊断.md)。日常验证优先使用 `scripts/unity-doctor.sh`、`scripts/unity-license-check.sh` 和 `scripts/unity-run-tests.sh`；正式工程交互打开使用 [Open-SundollWorld.command](./Open-SundollWorld.command)，所有入口固定走 Unity `6000.3.22f1` 版本专用通道，避免 Hub/许可证客户端状态漂移影响判断。
