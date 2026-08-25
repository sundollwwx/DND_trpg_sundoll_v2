# 桑哆尔的世界 · Unity

> 状态：**M3 功能范围已实现并通过自动化验证；性能/正式 macOS IL2CPP 与 Windows 矩阵仍未关闭**
> 更新日期：2026-08-25

这是“桑哆尔的世界”Unity 原生桌面产品的规划容器。目前包含方案文档、一次性 M0 Spike，以及正在推进的正式 Unity 工程 `SundollWorld`。

## 文档

- [Unity 从零开发工作计划](./Unity从零开发工作计划.md)
- [简明汇报](./简明汇报.md)
- [M0 一次性技术验证](./M0-Spike/README.md)
- [正式工程上下文](./SundollWorld/Docs/AI/UnityProjectContext.md)
- [M1 结果报告](./SundollWorld/Docs/M1-结果报告.md)
- [M2 结果报告](./SundollWorld/Docs/M2-结果报告.md)
- [M3 结果报告](./SundollWorld/Docs/M3-结果报告.md)
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

- 正式产品代码位于 `SundollWorld/Assets/Sundoll/`，当前已包含 M1 最小纵向闭环、M2 存档基础设施和已验证的 M3 地图编辑切片。
- 启动场景为 `Assets/Sundoll/Scenes/M3Workbench.unity`；`M1Bootstrap.unity` 保留为旧诊断入口。
- Git 基线已经建立；首个基线提交为 `b8d704f`，后续实现按小批次提交，Unity 缓存与构建产物保持在本机且不纳入版本控制。
- M2/M3 存档格式仍为 pre-v1；正式 Workbench、选择/复制/剪切/粘贴/旋转、五层状态、门箱对象和 Schema 2 已实现。M3 macOS universal IL2CPP 构建已通过，p95 性能、真实窗口视觉、Windows 与跨平台互开仍未验证。
- 未复制任何旧项目素材。

M0 已正式接受并保留 Windows/Finder 限制；正式工程已完成 M1、M2 核心切片和 M3 功能范围。Unity `6000.3.22f1` 最新 EditMode 为 57/57，PlayMode Workbench 为 1/1，macOS universal IL2CPP 构建与启动 Smoke 通过；测试证据见 [EditMode XML](./SundollWorld/TestResults_EditMode_20260825_m3_final2.xml)、[PlayMode XML](./SundollWorld/TestResults_PlayMode_20260825_m3_final4.xml) 和 [构建摘要](./SundollWorld/Docs/M3-构建摘要.md)。M3 使用新的 `SundollWorld_M3` 本地开发存档根目录，旧 M1/M2 样本原地保留。当前没有 Windows 环境，Windows IL2CPP、原子写盘、跨平台互开和正式性能 p95 继续登记为发布阻塞/未验证项。
