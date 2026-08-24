# M0 一次性技术验证 Spike

本目录是 `Unity从零开发工作计划.md` 中 M0 的可丢弃验证产物，不是正式 Unity 工程。
它不包含历史项目代码、数据、地图、图片或音乐，也不承担旧格式兼容。

## 当前状态

- Unity Hub：已安装。
- Unity Editor：Unity 6.3 LTS `6000.3.22f1` 已安装并完成首次导入。
- Unity Spike 工程：`UnityProjectFresh` 已由 Hub 正式创建，`ProjectVersion.txt`、URP 2D、Tilemap、UI Toolkit 模块均已验证。
- M0 的独立数据层验证：5/5 通过。
- M0 的 Unity Editor/Batch 验证：5/5 通过；macOS IL2CPP 与 Windows Mono Smoke Build 通过。
- 解锁桌面后的 Unity Editor 复核已完成：`M0Smoke` 场景可加载并进入/退出 Play Mode，Game 视图正常，Console 无红色错误。
- 临时可见工作台探针已打开并手动复核：中文 UI、Inspector `PropertyField` 绑定、名称/Revision 编辑、`Texture2D ObjectField` 选择均通过。

## 运行独立验证

```bash
python3 M0-Spike/scripts/verify_m0.py
```

脚本只使用 Python 标准库，会在临时目录中创建中文路径、不可变 Revision、原子 `HEAD.json`、内容寻址资产和分段 Journal 样本；结果写入 `results/m0-verification.json`。

## 真实 Unity Editor 复核清单

在一次性新工程中已补做：

1. UI Toolkit 工作台面板、中文文本/路径、Inspector 绑定、图片对象选择和无界面拖放 payload adapter。
2. Tilemap 与可见区自绘网格的同场景基准。
3. 运行时图片导入、缩略图、纹理释放、SHA-256 和尺寸限制。
4. macOS IL2CPP Smoke Build、Windows Mono Smoke Build 与 `JsonUtility` 序列化。
5. macOS `Flush(true)`、Revision replace 和中断临时文件恢复。

仍需外部环境补做：Windows IL2CPP（当前 Editor 未安装 Windows IL2CPP 模块）与 Windows 原子写盘实测。Finder 到 Unity 的真实跨窗口鼠标拖放在本次 Computer Use 中未触发，不能计为通过；Inspector 手动编辑和 Project 面板选择图片已计为通过。该面板仍是一次性 Editor-only 探针，不是正式产品工作台。

本次 Editor 复核还观察到两类非阻塞提示：macOS Metal 不支持部分 URP 光线追踪 Shader 编译（黄色警告）；`com.unity.purchasing@4.15.1` 已被 Unity 标记为弃用。M0 不使用 IAP，因此本轮不升级或引入该包。

M0 复核已完成，正式 `Assets/Sundoll/` 工程现已在 `SundollWorld` 中初始化；后续产品开发不应把 M0 Spike 当作正式运行时代码来源。

## 文档

- [M0 结果报告](docs/M0-结果报告.md)
- [ADR-0001 技术基线](docs/ADR-0001-M0-技术基线.md)
- [术语表](docs/术语表.md)
- [项目格式 v0](docs/项目格式-v0.md)
- [范围冻结 v0](docs/范围冻结-v0.md)
- [性能基线 v0](docs/性能基线-v0.md)
