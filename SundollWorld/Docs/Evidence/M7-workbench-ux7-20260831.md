# M7 Workbench UX7 第一批验证记录

日期：2026-08-31

## 本批次

UX7 第一批完成 Workbench 共享 USS 的视觉兼容与布局收尾：

- 保留 `WorkbenchStyles.uss` 原有 GUID，避免 Unity 资源引用断裂。
- 恢复项目中心、旧诊断 Tab、棋子库虚拟化网格、地图工具栏、图层状态、地图边界反馈和 Inspector 所需选择器。
- 统一深色专业外壳、暖色地图画布、按钮焦点/悬停/选中态、素材卡片、棋子卡片和错误提示的样式。
- 保留地图有效区闭合边界、越界提示和动态工作区布局；不改变 World Schema、保存链路或命令入口。

## 验证环境

- Unity：`6000.3.22f1`
- 许可证通道：`LicenseClient-sundoll-6000.3.22`
- 仓库：`main`
- 60 分钟 Soak：遵循此前用户决定，本批次不重复运行，仍标记未验证。

## 结果

| 套件 | 结果 | 证据 |
| --- | --- | --- |
| EditMode | 97/97 passed，0 failed，0 skipped | `TestResults/Local/TestResults_EditMode_20260831_124959.xml` |
| PlayMode | 16/16 passed，0 failed，0 skipped | `TestResults/Local/TestResults_PlayMode_20260831_124959.xml` |
| License handshake | Passed | `Logs/Test_EditMode_20260831_124959.log`、`Logs/Test_PlayMode_20260831_124959.log` |

## 日志说明

两次 Unity 日志均确认以原样式 GUID 导入 `WorkbenchStyles.uss`，并成功解析版本专用许可证。未发现编译错误、USS 无效属性、重连失败或测试失败。日志中的 `Access token is unavailable; failed to update` 属于可选远程刷新提示，不影响本地 entitlement。

## 未关闭风险

- UX7 还需要真实 Player 窗口的键盘焦点、中文布局、缩放和视觉截图复验。
- 60 分钟 Soak 未重跑；严格生产 60 FPS pacing 仍保留此前的未通过记录。
- Windows IL2CPP、Windows 原子写盘、跨平台 `.sundollpkg` 互开和双平台强制退出仍需 Windows 环境。
