# 桑哆尔的世界 · Unity

> 状态：**项目已于 2026-08-31 封存；macOS 验证证据保留，Windows/跨平台发布矩阵未完成。**
> 更新日期：2026-08-31

这是“桑哆尔的世界”Unity 原生桌面产品的封存仓库。它保留方案文档、一次性 M0 Spike，以及正式 Unity 工程 `SundollWorld`，但不再继续开发或作为 Beta 发布。

## 立即开始

项目当前处于封存状态。需要恢复时，先阅读 [封存说明](./ARCHIVED.md) 与 [v0.3 实施状态](./SundollWorld/Docs/Planning/v0.3-实施状态.md)。

- **日常使用成品：双击 [01-启动SundollWorld.command](./01-启动SundollWorld.command)。** 这是已经构建好的 macOS 应用，不需要打开 Unity。
- **继续开发或查看工程：双击 [02-在Unity中编辑SundollWorld.command](./02-在Unity中编辑SundollWorld.command)。** 它会通过固定的 Unity `6000.3.22f1` 通道打开正式工程，避免 Hub 导致的许可证通道漂移。
- 成品应用本体位于 `SundollWorld/Builds/SundollWorld-v03-M7-macOS-universal.app`；`M0-Spike/` 与顶层 `Builds/` 都是历史验证内容，不是日常入口。

## 文档

- [Unity 从零开发工作计划](./Docs/Planning/Unity从零开发工作计划.md)
- [简明汇报](./Docs/Planning/简明汇报.md)
- [M0 一次性技术验证](./M0-Spike/README.md)
- [正式工程上下文](./SundollWorld/Docs/AI/UnityProjectContext.md)
- [M1–M7 阶段报告](./SundollWorld/Docs/Reports/)
- [Unity 与 Windows 操作指南](./SundollWorld/Docs/Guides/)
- [M7 Windows 验证交接](./SundollWorld/Docs/Guides/M7-Windows验证交接.md)
- [v0.3 实施状态](./SundollWorld/Docs/Planning/v0.3-实施状态.md)
- [测试与构建证据](./SundollWorld/Docs/Evidence/)

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
- M2/M3/M4/M5 存档仍为 pre-v1；M7 已提供 save v1/schema 2 冻结、Schema 1→2 迁移注册表、Golden Save 校验、可复用 View Pool 和引用计数纹理缓存。M4 现已完成棋盘棋子的单选/多选、框选、拖动 Ghost、批量旋转/翻面/显隐/删除和显式发布建棋盘入口；图片路径导入与 macOS 原生文件选择器的选择/取消接缝均可用。正式图片视觉、2560×1440 帧率/分配、Windows 与跨平台互开仍未验证。
- 未复制任何旧项目素材。

M0 已正式接受并保留 Windows/Finder 限制；Unity `6000.3.22f1` 最新已存证 EditMode 93/93、PlayMode 15/15，macOS universal IL2CPP Player 成功启动。本机最新测试结果位于 `SundollWorld/Docs/Evidence/TestResults/`。本轮还完成了 M5 多地图工作区隔离：每张地图分别恢复相机、工具、图层状态和素材选择，并兼容已有单文件工作区状态；切图前会将当前草稿加入异步保存队列。M7 稳定化证据仍见 [EditMode XML](./SundollWorld/Docs/Evidence/TestResults/TestResults_EditMode_20260830_133200.xml)、[PlayMode XML](./SundollWorld/Docs/Evidence/TestResults/TestResults_PlayMode_20260830_133200.xml) 和 [M7 结果报告](./SundollWorld/Docs/Reports/M7-结果报告.md)。未使用的 Visual Scripting 包已移除；干净临时构建中 TypeDB 重复类型诊断为 0，当前本地旧 Library/Bee 构建仍会报告历史 TypeDB 诊断，另有 1 条 Unity Cloud 符号上传 token warning，不是 C# 编译错误。M7 已有 macOS 批量编辑、Snapshot、Revision 保存、10,000 Journal 恢复、64 棋子纹理共享、1000 棋子投影和稳态分配、棋子库缩略图 LRU 基线；1000 棋子 headless 投影刷新 p95 为 12.494 ms、稳态托管分配 p95 为 0 B。真实 2560×1440 Player 窗口解除采样限速后的渲染承载 p95 为 4.5495 ms、托管分配 p95 为 0 B；最新构建已再次通过 Universal IL2CPP 校验。10 分钟操作 Soak 通过，60 分钟操作 Soak 本轮运行约 50 分钟后未生成结果文件，按未验证处理；生产 60 FPS pacing、Windows/跨平台/强退矩阵仍未关闭。M4 使用新的 `SundollWorld_M4` 本地开发存档根目录，旧 M1/M2/M3 样本原地保留。

Unity 连接和许可证链路排查入口见 [M7 Unity 连接诊断](./SundollWorld/Docs/Guides/M7-Unity连接诊断.md)。日常验证优先使用 `scripts/unity-doctor.sh`、`scripts/unity-license-check.sh` 和 `scripts/unity-run-tests.sh`；正式工程交互打开使用 [02-在Unity中编辑SundollWorld.command](./02-在Unity中编辑SundollWorld.command)，所有入口固定走 Unity `6000.3.22f1` 版本专用通道，避免 Hub/许可证客户端状态漂移影响判断。

Windows 发布矩阵准备使用 `scripts/windows-m7-validation.ps1`；它会生成统一的测试 XML、Unity 日志和 `validation-summary.json`，并在缺少测试/构建证据、Unity 版本不符、许可证连接失败或 C# 编译错误时返回非零退出码。
