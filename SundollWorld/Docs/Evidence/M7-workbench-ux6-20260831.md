# M7 Workbench UX6 验证记录

日期：2026-08-31

## 本批次

UX6 完成规则无关棋子状态闭环：资源条、状态列表、自定义字段、主持备注和玩家可见性。World Schema 保持 2；老 JSON 缺少 `runtimeState` 时补空默认值。

实现覆盖：

- `M4PieceRuntimeState` 及资源条/状态/自定义字段模型。
- `M4.SetPieceRuntimeState` 命令、Facade、Envelope 编解码、Undo/Redo 和 Journal 重放。
- Canonical Hash 纳入状态字段，并保持稳定排序。
- Audience Projection 过滤不可见棋子、私有条目和主持备注。
- 右侧 Inspector 状态编辑区及玩家预览只读状态。

## 验证环境

- Unity：`6000.3.22f1`
- 许可证通道：`LicenseClient-sundoll-6000.3.22`
- 仓库：`main`
- 60 分钟 Soak：按用户决定本轮不重复，仍标记未验证。

## 结果

| 套件 | 结果 | 证据 |
| --- | --- | --- |
| EditMode | 97/97 passed，0 failed，0 skipped | `TestResults/TestResults_EditMode_20260831_123652.xml` |
| PlayMode | 16/16 passed，0 failed，0 skipped | `TestResults/TestResults_PlayMode_20260831_123723.xml` |
| License handshake | Passed | 对应 `Logs/Test_EditMode_20260831_123652.log` 与 `Logs/Test_PlayMode_20260831_123723.log` |

新增断言覆盖：状态保存/重载、Canonical Hash、Undo/Redo、命令 Envelope、Journal Replay、非法资源值整体拒绝、Audience 私有状态过滤以及 Inspector 控件存在性。

## 日志说明

两次 Unity 日志均出现 `Access token is unavailable; failed to update`。本地 entitlement 已解析，版本专用许可证通道握手成功，测试 XML 也完整通过；该信息属于可选远程刷新提示，不是本地许可证通信失败。本批次未观察到 `Unsupported protocol version`、重连超时或编译错误。

## 未关闭风险

- 真实鼠标/视觉审美仍需手工 Player 复验。
- 60 分钟 Soak 未重跑。
- Windows IL2CPP、Windows 原子写盘、跨平台 `.sundollpkg` 互开和双平台强制退出仍需 Windows 环境。
