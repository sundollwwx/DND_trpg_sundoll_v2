# 桑哆尔的世界 · Unity

> 状态：**M3 最小切片已验证，完整退出条件未关闭；M2 Windows/跨平台验收阻塞**
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
- 启动场景为 `Assets/Sundoll/Scenes/M1Bootstrap.unity`；当前运行时面板仍是诊断用 UI。
- Git 基线已经建立；首个基线提交为 `b8d704f`，后续实现按小批次提交，Unity 缓存与构建产物保持在本机且不纳入版本控制。
- M2 存档格式为 pre-v1；Windows、跨平台互开、正式 Workbench、选择/复制旋转、门箱对象和性能门槛尚未完成。
- 未复制任何旧项目素材。

M0 已正式接受并保留 Windows/Finder 限制；正式工程已完成 M1、M2 核心切片和 M3 的已列明切片。2026-08-25 重启失效的 Unity 版本专用 Licensing Client 后，Unity `6000.3.22f1` 完整 EditMode 已刷新为 52/52 通过（M1 4、M2 26、M3 22），Console 为 0 error / 0 warning；本轮新增 M2 OS 文件写锁、锁超时和写盘故障保护。当前没有 Windows 环境，M3 继续在 macOS 上推进。
