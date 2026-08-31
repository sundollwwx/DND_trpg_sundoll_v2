# UX5 主控台页面验证证据

日期：2026-08-31

## 范围

本批次完成主控台页面的第一批情境布局，不升级 World Save 或 Schema，也不改变主持命令、Audience Projection 或多地图切换逻辑。

## 已完成

- 主控台左侧工具区按“地图管理、当前主持状态、迷雾笔刷、动态标注、交互对象”分成独立视觉区块。
- 主控台增加页面标题和操作提示；层级/地图区标题统一为“地图与层级”。
- 迷雾坐标操作保留在迷雾区，标注拖动和标注编辑保留在标注区，交互对象动作保留在交互区。
- 继续使用既有三工作区互斥容器：主控台不显示棋子库筛选，棋子库不显示主控台工具。
- 新增 `sw-host-tool-section` USS 样式，沿用 Workbench 深色外壳和暖金层级，不再让所有主持控件堆在一条无层次的长列表中。
- 移除 Unity 6 不支持的 `line-height` USS 声明，避免导入时产生无效样式警告。

## 自动化验证

执行命令：

```text
bash scripts/unity-run-tests.sh all
```

固定 Unity：`6000.3.22f1`

许可证通道：`LicenseClient-sundoll-6000.3.22`

结果：

- EditMode：`93/93`，0 failed，0 skipped
- PlayMode：`16/16`，0 failed，0 skipped
- PlayMode 覆盖主控台分区节点、样式类、主控台/棋子库/地图制作工作区隔离和既有主控台启动、切图、预览、迷雾、标注、交互流程。
- 两次 Unity 日志均确认版本专用许可证通道握手成功。
- 未发现编译错误、USS 无效属性警告或测试失败。日志中的 `Access token is unavailable` 是可选的远程刷新提示；本地 entitlement 已成功解析，不影响本次测试。

## 结果文件

- [EditMode XML](TestResults/TestResults_EditMode_20260831_112726.xml)
- [PlayMode XML](TestResults/TestResults_PlayMode_20260831_112726.xml)
- `SundollWorld/Logs/Test_EditMode_20260831_112726.log`（本地生成日志，按 `.gitignore` 忽略）
- `SundollWorld/Logs/Test_PlayMode_20260831_112726.log`（本地生成日志，按 `.gitignore` 忽略）

## 未覆盖范围

- 本轮没有重复运行 60 分钟 Soak，遵循此前已确认的跳过决定。
- 主控台真实鼠标手感、1440×900 与 2560×1440 视觉复验仍需手工/Player 证据。
- Windows IL2CPP、Windows 持久化写盘、跨平台存档互开和真实网络仍需 Windows/目标环境验证。
