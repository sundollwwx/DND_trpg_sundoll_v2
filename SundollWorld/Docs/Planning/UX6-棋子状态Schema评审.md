# UX6 棋子状态 Schema 评审

日期：2026-08-31

## 决策

UX6 不升级 `M1WorldState.schemaVersion`，也不修改现有 Save v1 外壳。`M4PieceInstance.runtimeState` 是 Schema 2 的向后兼容增量字段：旧 JSON 缺少该字段时，在 `EnsureSchema2Defaults()` 中补成空状态。

原因：这组字段是棋子实例的通用标注与主持状态，不改变地图、棋盘、位置关系或既有字段语义；Unity `JsonUtility` 对缺失字段会使用默认值，补默认后可保持 Canonical Hash、重放和旧开发档读取路径稳定。正式 v1 冻结前仍不承诺跨版本开发档兼容，也不迁移旧 Journal。

## 数据边界

`M4PieceRuntimeState` 只保存规则无关数据：

- `resourceBars`：稳定 ID、显示名称、当前值、最大值和玩家可见标记。
- `statuses`：稳定 ID、显示名称、说明和玩家可见标记。
- `customFields`：稳定键、字符串值和玩家可见标记。
- `hostNote`：仅主持人可见的备注。
- `audienceVisible`：整枚棋子是否进入玩家投影。

Core 不解释“生命”“护甲”或状态效果；未来规则层可读取这些通用值并决定其玩法含义。编辑器 `visible` 仍表示本机/主持投影中的显示状态，与 `audienceVisible` 分离。

## 完整性与投影

- 每类资源条、状态和自定义字段分别要求稳定 ID/键唯一且非空。
- 资源条要求 `0 <= current <= maximum`。
- 状态更新使用一个完整的 `M4.SetPieceRuntimeState` 命令，经过 Facade、Command Bus、Undo/Redo、Journal 和异步保存。
- Canonical Hash 包含上述权威字段，并按稳定 ID/键排序。
- Audience Projection 会移除 `audienceVisible=false` 的棋子；对保留棋子只复制玩家可见条目并清空主持备注。
- 玩家预览仍是只读，不能通过 Inspector 修改权威状态。

## UI 约束

右侧 Inspector 采用多行编辑器，使用固定格式承载可变数量条目：

```text
资源条：ID|名称|当前|最大|玩家可见
状态：ID|名称|说明|玩家可见
自定义：键|值|玩家可见
```

保存前执行格式解析和 Core 校验；任何一行非法都整体拒绝，不产生部分命令。`|` 是当前编辑格式的保留分隔符，未来若需要富文本或本地化编辑器，再替换为结构化行控件，不改变 Core DTO。

## 后续门槛

UX7 继续验证视觉布局、键盘焦点、窗口缩放和 Player 行为；M7 Beta 前仍需完成 macOS 性能/Soak 证据、Windows IL2CPP、写盘故障和跨平台互开矩阵。UX6 本身不引入网络框架或新的 Unity 包。
