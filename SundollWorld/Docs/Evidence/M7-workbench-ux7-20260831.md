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
| EditMode | 97/97 passed，0 failed，0 skipped | `TestResults/Local/TestResults_EditMode_20260831_125749.xml` |
| PlayMode | 16/16 passed，0 failed，0 skipped | `TestResults/Local/TestResults_PlayMode_20260831_125749.xml` |
| License handshake | Passed | `Logs/Test_EditMode_20260831_125749.log`、`Logs/Test_PlayMode_20260831_125749.log` |

## 日志说明

验证迭代中第一次运行在编译阶段发现 `M3WorkbenchRoot.cs` 对 `Focusable` 直接访问 `ClassListContains` 和 `parent` 的 `CS1061` 错误；已改为安全转换为 `VisualElement` 后重跑。第二次运行确认以原样式 GUID 导入 `WorkbenchStyles.uss`，无 C# 编译错误、USS 无效属性、重连失败或测试失败。日志中的 `Access token is unavailable; failed to update` 属于可选远程刷新提示，不影响本地 entitlement。

本批次新增的 PlayMode 断言确认顶部工作区按钮可聚焦且 Tab 顺序固定为 1/2/3；在棋子库搜索框取得焦点时，Workbench 能识别文本输入焦点，随后失焦状态也能恢复。全局快捷键已让文本框优先处理 Cmd/Ctrl 操作，Escape 仍保留为全局取消手势。

## 最新 macOS Player 构建

焦点修复提交 `a461527` 使用一次性干净工作树完成 macOS Universal IL2CPP 构建：

- BuildResult：`Succeeded`
- Backend：`IL2CPP`
- 架构：`x86_64 + arm64`
- C# 编译错误：`0`
- TypeDB 诊断：`0`
- 产物大小：`2243173485` bytes
- 可执行文件 SHA-256：`f3c31feb2641cb4c6967e812f7ad0fd2bc323d8fc8a8e5cbf6474ecaf23cc3e8`
- 构建日志：`Logs/Build_macOS_clean_20260831_130035.log`

构建唯一 warning 是 Unity Cloud Diagnostics 原生 symbols 上传 token 未配置；不影响 IL2CPP 产物生成。该临时构建未再次执行 Player Smoke，真实 Player 键盘手感和视觉截图仍需后续复验。

## 未关闭风险

- UX7 还需要真实 Player 窗口的键盘焦点、中文布局、缩放和视觉截图复验。
- 60 分钟 Soak 未重跑；严格生产 60 FPS pacing 仍保留此前的未通过记录。
- Windows IL2CPP、Windows 原子写盘、跨平台 `.sundollpkg` 互开和双平台强制退出仍需 Windows 环境。
