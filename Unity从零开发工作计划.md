# 桑哆尔的世界 · Unity 从零开发工作计划

> 文档状态：v0.3，执行跟踪稿
> 更新日期：2026-08-25
> 当前状态：**M4 第一批棋子库与空间交互底座已实现并通过自动化验证；M4 UI 完整度、性能与 Windows 矩阵仍未关闭**

## 里程碑状态矩阵

| 阶段 | Passed | Partial | Blocked |
| --- | --- | --- | --- |
| M0 | 数据验证、Unity Editor 验证、macOS IL2CPP、Windows Mono 构建 | Finder 到 Unity 的真实跨窗口拖放 | Windows IL2CPP 与 Windows 原子写盘 |
| M1 | 纵向领域闭环、纯数据重建、4/4 测试、macOS universal build | 当前正式构建仍为 Mono | Windows Smoke |
| M2 | Revision、HEAD/Revision 扫描恢复、generation 冲突保护、Journal、blob、便携包、命令契约、Journal v2 Payload 重放、单写入后台保存队列、保存状态、OS 文件写锁与故障注入；完整证据含 M2 26/26 | 真实磁盘满/权限、独立进程强制退出与跨平台验证 | Windows 强制退出、原子写盘与跨平台互开矩阵 |
| M3 | 正式 Workbench、UI Toolkit 固定布局、Tilemap 五层投影、Input System 绘制/视口、选择/复制/剪切/粘贴/旋转、五层 Workspace State 2、门箱对象、Schema 2；57/57 EditMode、1/1 PlayMode、macOS universal IL2CPP 构建与 Smoke | 性能 p95、真实鼠标视觉复验 | Windows 性能与构建、跨平台互开矩阵 |
| M4 | Asset/Definition/Instance 纯数据模型、内容寻址去重、四种位置、堆叠/收纳/附着/解除、旋转/翻面/显隐、关系循环校验、Command/Journal 接入、Workbench 占位投影；EditMode 65/65、PlayMode 2/2 | 正式棋子库 UI、运行时图片导入/缩略图、棋子操作 UI、纹理性能基准 | Windows/跨平台矩阵与后续发布门槛 |

详细执行证据与限制见 [v0.3 实施状态](./SundollWorld/Docs/v0.3-实施状态.md)。状态为 Partial 或 Blocked 的项目不得被解释为对应里程碑已经完整关闭。

## 0. 结论先行

本项目改为真正的 **Unity 原生 Greenfield（从零开发）项目**。历史网页版本只提供产品思想和交互经验，不再承担数据源、兼容对象或迁移基线的角色。

> 历史网页版本仅作为非规范性的需求与交互思想参考。本项目不读取其文件，不迁移或复制其代码、地图、棋子、存档、图片或音乐，不建立旧格式导入器，不承诺数据、素材、行为或功能兼容，也不把旧项目的数量统计作为验收标准。

建议锁定以下总方向：

1. 产品是一款 **Unity Runtime 桌面应用**，不是只能在 Unity Editor 中运行的编辑器插件。
2. 首版聚焦 **2D 顶视方格棋盘/沙盘工作台**，优先 Windows 与 macOS，中文界面、本地优先、离线可用。
3. 一个应用内包含项目中心、地图制作器、棋子库和主控台，四者共享同一套领域模型、命令与存档服务。
4. 纯 C# 的项目/世界状态是唯一事实来源；Scene、GameObject、Transform 和 UI 只负责表现与输入。
5. 正式修改全部经过可序列化命令管线；撤销、崩溃恢复、未来规则检查和未来联机共用同一条状态变更路径。
6. 现在只实现 `NoRules/AllowAll`，不加入 D&D 或其他玩法规则，但用独立接口和测试规则证明后续可以接入。
7. 棋子支持规则无关的通用交互：移动、旋转、翻面、显隐、堆叠、收纳、附着与解除附着。
8. 新项目存档使用不可变 Revision、原子 HEAD 提交、分段恢复日志、自动保存和便携包；不依赖 Scene 序列化或 `PlayerPrefs` 保存世界。
9. 单机阶段也通过 `LocalAuthority` 执行命令；未来联网采用主机/服务器权威，而不是同步 Transform 或 GameObject。
10. 网络框架、正式规则、美术内容包、云存档和账号系统均推迟到核心闭环验证之后。

建议工期口径：

- 内部功能闭环（M0–M5，不对外承诺存档兼容）：约 **16–17 人周**。
- 完成规则与 Loopback 架构验证（M0–M6）：约 **19–20 人周**。
- 冻结 v1 并形成可对外测试的 Beta（M0–M7）：约 **22–24 人周**。
- 加入约 20% 风险缓冲后，架构验证完成约 **23–24 人周**，Beta 约 **27–29 人周**。

以上以一名熟悉 Unity/C# 的全职开发者为基准，是工作量估算，不是日历承诺；不包含正式联网、真实规则、美术量产和商店发布合规。

---

## 1. 产品章程

### 1.1 产品一句话

一个本地优先、规则无关的 2D 桌面棋盘工作台：用户可以从空白开始制作地图、建立棋子库、组织场景并通过主控台主持互动；以后可以接入不同规则模块和在线会话。

### 1.2 核心用户旅程

首个真正可用的闭环必须是：

1. 新建一个空白项目。
2. 在地图制作器中创建地图并加入地形、墙体、物件和交互物。
3. 在项目棋子库中新建棋子定义并导入一张新图片。
4. 把地图发布为不可变内容版本，并在主控台中创建一个场景实例。
5. 放置棋子，执行移动、堆叠、收纳、附着、显隐、迷雾和标注等规则无关操作。
6. 自动保存或手动保存，关闭应用。
7. 再次打开后恢复到相同权威状态；即使最新写盘中断，也能回到完整的旧 Revision 或可验证恢复点。

任何里程碑如果不能让这条旅程更接近可用闭环，就不应优先于闭环功能。

### 1.3 从历史产品中吸收的思想

只吸收以下产品思想，不继承实现：

| 历史经验提供的启发 | Unity 新项目中的重新设计 |
| --- | --- |
| 地图制作、棋子管理和主持场景是连续工作流 | 统一为一个桌面应用中的四个工作区 |
| 制作中的地图与主持时的地图状态不同 | 拆分 `MapDocument`、`MapContentVersion` 与 `BoardInstance` |
| 棋子库条目与棋盘上的棋子不是同一对象 | 拆分 `PieceDefinition`、`PieceInstance` 与视觉资产 |
| 棋子需要骑乘、堆叠、收纳等关系 | 使用互斥 `PieceLocation` 和受约束的通用关系 |
| 主持过程需要迷雾、标注、显隐和多地图 | 作为规则无关的 `ScenarioDocument` 权威状态 |
| 本地使用也需要恢复与未来玩家端 | 从第一天使用命令管线、`LocalAuthority` 和可重放操作 |

明确不继承：

- HTML/JavaScript/Python 代码及其模块边界。
- 旧数据文件、ID、字段、存档格式和浏览器存储方式。
- 旧地图、棋子、立绘、音乐和其他素材。
- 旧界面的像素级布局、隐含行为和历史兼容负担。
- 旧项目备份、归档、导入、对照计数或回归 Fixture。

零旧项目依赖是质量门槛：即使历史项目目录完全不存在，新的 Unity 工程仍必须可以构建、测试和运行。

### 1.4 目标用户与使用方式

- 一名主持人主要在桌面端本地使用。
- 用户不需要安装 Unity Editor。
- 第一阶段围绕鼠标和键盘优化，保留触控与手柄的输入抽象，但不做专门适配。
- 项目文件可离线保存、复制、校验和打包分享。
- UI 以中文为第一语言，文本资源从第一天可本地化，不把中文硬编码到领域数据键中。

### 1.5 成功标准

Alpha 成功不是“功能按钮数量很多”，而是同时满足：

- 能从空白项目完成地图、棋子和主持场景的完整流程。
- 所有权威状态都能在销毁 View 后从纯数据重建。
- 批量修改原子化，Undo/Redo 不产生半完成状态。
- 保存、强制退出、恢复和跨平台互开有自动化验证。
- 缺失图片或未知扩展不会导致项目实体被静默删除。
- 核心不包含具体玩法规则，也不引用具体网络框架。
- 独立测试规则和进程内 Loopback 能证明扩展接缝真实可用。

---

## 2. Alpha 范围与明确延期

### 2.1 Alpha 必须包含

#### 项目中心

- 新建、打开、最近项目、重命名和项目基本信息。
- 脏状态、正在保存、已安全落盘和保存失败的明确提示。
- 手动保存、自动保存、恢复点、last-known-good。
- 导出和打开本产品自己的 `.sundollpkg` 便携包。
- 安全关闭：未持久化变更必须可见并可处理。

#### 地图制作器

- 2D 方格地图；尺寸、网格、平移、缩放和吸附。
- 内容图层：Terrain、Wall、Object、Interaction、StaticAnnotation。
- 画笔、橡皮、拾取、直线、矩形、填充。
- 框选、多选、复制、剪切、粘贴、旋转。
- 图层排序；本机的图层显示、锁定和当前选择状态。
- 事务化多步 Undo/Redo；一次连续笔画只产生一个正式事务。
- 门、箱子等少量规则无关状态对象；更多对象类型通过数据扩展。
- 保存可编辑 `MapDocument`，并显式发布不可变 `MapContentVersion`。

#### 项目棋子库

- 项目内 `PieceDefinition` 与地图上的 `PieceInstance` 完全分离。
- 名称、分类、标签、footprint、视觉资源和通用属性。
- 用户导入新图片、缩略图、搜索、筛选和缺失资源占位。
- 同一二进制内容按 SHA-256 去重；文件改名不破坏引用。
- 一小组全新制作的中性几何/颜色占位棋子，不复制历史素材。

#### 主控台

- `ScenarioDocument`、多个 `BoardInstance` 与地图切换。
- 中央地图视口、Hierarchy、Inspector、工具状态与操作历史。
- 棋子放置、选择、多选、移动、旋转、翻面、删除和显隐。
- 网格吸附、不同 footprint、相机位置和快捷键。
- 规则无关的迷雾、动态标注/涂鸦和 Board Object 状态交互。
- 恢复/保存状态、缺失资产和验证错误的可理解提示。

#### 通用交互

- `Unplaced`、`OnBoard`、`InContainer`、`Attached` 四种互斥位置。
- 堆叠、调整堆叠顺序、收入容器、移出容器、附着与解除附着。
- 骑乘作为 `Attached` 的一种 slot/metadata 表达，不实现玩法判定。
- 门的开关、箱子的开关、对象状态切换等规则无关动作。
- 上下文菜单展示当前可执行的通用交互；执行时仍由权威管线重新验证。

#### 新格式存档

- 稳定 ID、显式 Schema、不可变 Save Revision 和原子 HEAD。
- 完整世界快照为主，有限 `AcceptedOperationBatch` Journal 为崩溃恢复辅助手段。
- 自动保存、手动快照、恢复点、损坏检测和跨平台互开。
- 用户资产内容寻址、缺失资源占位和便携包安全导入/导出。
- 只处理 Unity 新产品格式的未来版本升级，不包含任何历史网页格式兼容。

### 2.2 非功能要求

- Windows 与 macOS 从 M1 起持续构建验证。
- 领域层无需 Scene 或 Unity Runtime 即可运行测试。
- 后台保存只处理不可变纯 C# DTO，不在后台线程访问 Unity API。
- 输入文件、图片、便携包和未来网络消息全部视为不可信。
- 关键操作有结构化错误码，用户可知道“失败在哪里、旧档是否安全”。
- 中文路径、大小写差异、区域设置和跨平台换行不能影响身份或状态 Hash。

### 2.3 明确延期

- 对历史网页项目的读取、复制、导入、备份、兼容和对照验收。
- D&D 或其他玩法规则：属性、HP/AC、职业、法术、先攻、攻击和伤害。
- 正式公网联机、账号、房间、Relay、云存档、专用服务器和主机迁移。
- 六边形、无网格自由画布、3D 地图和 Unity 物理驱动的玩法。
- 随机地图生成、PNG 导出、3D 骰子、BGM、网站面板。
- 完整自由 Docking、移动端和手柄专用 UX。
- 自动跟随、行为型 link、复杂自动化和多人 CRDT 地图编辑。
- 运行时任意 C# DLL、脚本 VM、模组市场和在线内容下载。
- 跨项目全局棋子库、自动差异比较和定义升级；Alpha 先做好项目棋子库。
- JSON 对象级内容寻址、差分快照和大地图逻辑 Chunk；只有真实基准证明需要时再加入。
- 专业美术内容生产和正式内容包；Alpha 使用全新中性占位资源。

---

## 3. 应用形态与用户体验

### 3.1 四个工作区

1. **项目中心**：项目生命周期、最近项目、恢复与便携包。
2. **地图制作器**：编辑 `MapDocument`，显式发布 `MapContentVersion`。
3. **棋子库**：维护当前项目的定义与视觉资源。
4. **主控台**：创建 `ScenarioDocument`，实例化地图并主持互动。

四个工作区共享同一个 Application 层，但不共享混乱的全局 UI 状态。每个文档拥有自己的 Undo Scope；切到另一张地图后，撤销默认不会修改前一张地图。

### 3.2 Workbench 布局

首版采用稳定、可调宽度的工作台布局：

- 顶部：项目、保存状态、工作区与地图/场景标签。
- 左侧：项目树、Hierarchy 或棋子库。
- 中央：2D 地图视口。
- 右侧：Inspector、图层与上下文工具。
- 底部：工具提示、操作历史、验证和恢复状态。

完整 IDE 式自由 Docking 延期。UI Toolkit 负责面板和表单，地图与棋子由 Unity 2D 场景渲染；不把整张地图做成成千上万个 `VisualElement`。

### 3.3 三类用户动作

必须从模型上分开：

| 类型 | 示例 | 是否进入权威状态/存档/未来网络 |
| --- | --- | --- |
| `WorkspaceAction` | 面板宽度、搜索词、个人相机、选择状态 | 否 |
| `PreviewAction` | 拖拽 Ghost、画笔预览、框选矩形 | 否 |
| `DomainCommand` | 落笔、移动棋子、开门、修改迷雾 | 是 |

拖动一百帧也不应产生一百条正式操作；松手时只提交一次 `DomainCommand`。

### 3.4 状态分区

| 分区 | 示例 | 是否存项目 | 未来是否共享 |
| --- | --- | --- | --- |
| 权威项目状态 | 地图、棋子定义、场景、关系 | 是 | 是 |
| `SharedSceneState` | 当前 Board、迷雾、对象状态 | 是 | 是 |
| `SessionPresenceState` | 在线光标、临时拖拽、在线状态 | 否 | 是，未来 |
| 本机工作区偏好 | 面板布局、快捷键、个人相机 | 否，存本机设置 | 否 |
| 临时 UI | hover、selection、弹窗、Preview | 否 | 否 |

---

## 4. 核心架构

### 4.1 唯一事实来源

```text
输入 / UI
  → WorkspaceAction / PreviewAction / DomainCommand
  → CommandEnvelope 解码、版本与大小限制
  → LocalAuthority：身份、幂等与访问上下文
  → Revision / 前置条件与结构校验
  → IRulePolicy（当前 AllowAll）评估 ProposedTransaction
  → 规则替换/追加内容重新校验
  → CommandHandler 原子提交 + 最终不变量检查
  → WorldState + ChangeSet
       ├─ AcceptedOperationBatch（持久、可重放、未来同步）
       ├─ UI Undo Record（按文档分区，不跨重启）
       ├─ DomainNotification（仅进程内）
       └─ Unity View 更新
```

硬性规定：

- `WorldState`/Document 是唯一事实来源。
- Scene、Prefab、GameObject、Transform、Tilemap 和 MonoBehaviour 都不是存档模型。
- UI、碰撞回调、拖拽处理器不能直接修改业务状态。
- 直接改动某个显示 Transform 后，下一次重新投影必须把它恢复到权威位置。
- View 可以被完全销毁，再仅凭文档重建相同棋盘。
- `ChangeSet` 是提交过程的内部结果；`AcceptedOperationBatch` 才是可持久重放记录。
- `DomainNotification` 只用于表现和应用内订阅，不能写盘或承担恢复责任。
- Recovery Journal 与 UI Undo 是两个系统，不能互相代替。

### 4.2 命令设计

`CommandEnvelope` 至少包含：

- `CommandId`
- `CommandTypeId` 与 Payload Version
- `ActorContext`
- 目标 Project/Document ID
- `BaseWorldRevision`
- 需要校验的 Entity/Chunk 版本前置条件
- Correlation/Transaction ID
- 版本化 Payload

命令使用有业务含义的类型，例如：

- `PaintCells`
- `PublishMapContent`
- `CreatePiece`
- `MovePiece`
- `SetPieceVisibility`
- `AttachPiece`
- `PutPieceInContainer`
- `SetBoardObjectState`

不向 UI 暴露任意 JSON Patch 或 DTO 路径修改，避免绕过不变量和未来规则语义。

批量刷图、区域填充、多选移动和批量粘贴都作为一个事务：要么全部提交，要么完全不变。大范围 Undo 使用压缩 Delta，并设置内存预算。

### 4.3 工程目录与程序集

正式工程开始时建议保持可理解的单仓库结构，不先拆本地 UPM 包：

```text
Assets/Sundoll/
  Core/
  Application/
  Rules/
  Infrastructure/
  Presentation/
  Features/
    MapEditor/
    ControlConsole/
    PieceLibrary/
  Bootstrap/
  Editor/
  Tests/
```

首批控制在约 9–11 个 asmdef：

```text
Sundoll.Core                    → 无 Unity 引用
Sundoll.Application.Contracts  → Core
Sundoll.Rules.Contracts        → Core + Application.Contracts
Sundoll.Rules.NoRules          → Rules.Contracts
Sundoll.Application            → Core + Contracts + Rules.Contracts
Sundoll.Infrastructure         → Core + Application.Contracts（具体适配器可引用 UnityEngine）
Sundoll.Presentation           → Core + Application.Contracts + UnityEngine
Sundoll.Features               → Core + Application.Contracts + Presentation
Sundoll.Bootstrap              → Application + Infrastructure + Features + Rules.NoRules
Sundoll.Tests.EditMode
Sundoll.Tests.PlayMode
```

`Features/Sundoll.Features.asmdef` 覆盖其三个功能子目录，避免脚本落入预定义 `Assembly-CSharp`；出现团队并行、复用或编译时间问题时再拆成功能级 asmdef。asmdef 引用不传递，实际使用 Core DTO 的程序集必须显式引用 Core。禁止全局 Service Locator、`FindObjectOfType` 和包办一切的 `GameManager`。

### 4.4 Unity 技术基线

M0 开工日再通过 Unity Hub 和官方发布页核对，推荐原则为：

- 使用当时稳定的 Unity 6 LTS 最新补丁，并锁定精确 Editor 与包版本。
- URP 2D Renderer。
- UI Toolkit：工作台面板、项目中心、Hierarchy、Inspector 和棋子库。
- Input System：视口、拖放、工具上下文与快捷键。
- Unity Test Framework。
- Addressables：只管理随构建发布的内置内容。
- 用户图片：复制到项目 asset blob store，由受限的运行时图片加载器处理。
- JSON Codec 隐藏在 `ISaveCodec` 后，不让序列化库类型进入 Domain。

暂不引入 DOTS/Entities、CRDT、大型 DI 框架、具体联网框架或动态 C# 插件。

### 4.5 Unity 场景策略

- `Bootstrap` Scene 负责生命周期和 Composition Root。
- `Workbench` Scene 负责 UI 根节点、正交相机和视图容器。
- 不为每张用户地图创建一个 Unity Scene。
- Tilemap 或自定义网格只作为 `MapDocument/MapContentVersion` 的投影，选型由 M0 性能 Spike 决定。
- 棋子优先使用 SpriteRenderer/SortingGroup + Pool。
- ScriptableObject 只用于开发期配置与内置内容 Authoring；运行时可变项目数据不得写入 ScriptableObject。

---

## 5. 领域模型与术语

### 5.1 文档层级

```text
ProjectDocument
├─ ContentManifest
├─ PieceCatalog
├─ MapDocument[]                 # 可编辑草稿
├─ MapContentVersion[]           # 显式发布的不可变地图版本
└─ ScenarioDocument[]
   ├─ BoardInstance[]            # 固定引用 MapContentVersion
   ├─ PieceInstance[]
   ├─ BoardObjectInstance[]
   ├─ DynamicAnnotation[]
   ├─ SemanticRelation[]
   └─ SharedSceneState
```

### 5.2 Revision/Version 术语

不同概念不得都含糊地称作“版本”：

| 术语 | 含义 |
| --- | --- |
| `WorldRevision` | 每个成功权威事务递增一次，用于因果位置 |
| `EntityVersion` | 单个实体的并发前置条件 |
| `MapContentVersionId` | 用户显式发布地图时创建的不可变内容版本 |
| `SaveRevisionId` | 一次成功持久化快照的身份 |
| `JournalStreamId` | 一条恢复日志分支/纪元的身份；用户回退会创建新 Stream |
| `OperationSequence` | 在一个 Journal Stream 内，每个成功事务对应的连续序号 |
| `Format/WorldSchemaVersion` | 新产品存档容器和世界 DTO 的兼容版本 |

每画一笔只改变 `MapDocument` 和 `WorldRevision`，不会自动产生新的 `MapContentVersion`。Board 只能引用已经显式发布的版本；若 UI 提供“一键发布并创建场景”，底层也必须明确提交 `PublishMapContent + CreateBoardInstance` 原子事务，不能暗中产生版本。

### 5.3 地图草稿、内容版本与实例

- `MapDocument`：地图制作器中的可编辑草稿。
- `MapContentVersion`：一次显式发布产生的不可变地图内容。
- `BoardInstance`：主控台中的场景实例，固定引用精确 `MapContentVersionId`。
- 已存在的 Board 不自动追随地图草稿变化。
- Alpha 不实现模板三方合并或实例自动升级；需要新版本时创建新 Board，或由用户执行明确的替换流程。

地图内容包含：

- 尺寸、方格坐标拓扑和内容层顺序。
- Terrain、Wall、Object、Interaction、StaticAnnotation。
- Map Object 的稳定 ID、状态机定义和默认状态。

主控台运行态包含：

- Piece、Fog/Mask、DynamicAnnotation。
- 每个 Map Object 在创建 Board 时生成唯一 `BoardObjectInstanceId`，保存来源 `MapObjectId` 与当前状态。
- 场景当前 Board 与共享主持状态。

图层的内容和顺序属于项目；编辑器隐藏、锁定与当前选择属于本机 Workspace State；未来面向玩家的可见性属于独立 Audience Policy，三者不得混合。

### 5.4 棋子定义、实例与视觉资产

`PieceDefinition` 保存：

- `DefinitionId`
- 名称、分类、标签
- `VisualAssetRef`
- Footprint
- 通用默认属性
- Capability/Interaction Keys
- Entity Version

`PieceInstance` 保存：

- `PieceId`
- `DefinitionId`
- 互斥的 `PieceLocation`
- 实例属性覆盖
- 所属者/未来 Audience 扩展位
- Entity Version

Alpha 使用项目内 Piece Catalog。修改定义会明确影响同项目中引用该定义的实例，实例覆盖仍保持独立。跨项目个人全局库和定义差异升级放到 Post-Alpha。

视觉资产独立保存：

- 内置全新占位内容：`addr:<content-id>`。
- 用户导入资源：`blob:<sha256>`。
- Definition ID、资源 Hash 和原文件名是三件不同的事。
- 找不到 Definition 或图片时显示占位，但保留原始引用与实例。

### 5.5 `PieceLocation` 是唯一空间真相

```text
Unplaced
OnBoard(boardInstanceId, layerId, boardPose)
InContainer(parentEntityRef, slot, order)
Attached(parentEntityRef, slot, localPose, order)
```

- 一个棋子同一时刻只能有一个空间父级。
- `InContainer` 不保存绝对棋盘坐标。
- `Attached` 只保存相对父级 Pose；世界坐标是表现层计算结果，不回写第二份绝对位置。
- 堆叠与骑乘使用稳定 slot/order 表达。
- Parent 可以是 Piece 或 `BoardObjectInstance`。
- 容器/附着图必须无循环，禁止非法自环。
- 空间父子链必须留在同一个 Scenario/Board 作用域内；跨 Board 关系以后通过显式传送语义设计。
- 自动 follow 属于会生成 Move 命令的行为系统，Alpha 延期。

### 5.6 坐标与稳定 ID

- 地块和格上物件使用整数格坐标。
- 棋子使用整数格坐标 + 量化局部偏移，支持关闭吸附而不保存任意浮点 Transform。
- 旋转使用明确量化值。
- Entity/Project/Document/Layer/Relation 使用各自的 128 位稳定 ID。
- 格子用 Board/Map ID + 坐标表达，不为每格创建 GUID。

禁止长期引用：

- `GetInstanceID()`、NetworkObject ID。
- Scene 层级路径、绝对文件路径、显示名或文件名。
- `UnityEngine.Object`、Transform、Quaternion 原值。

### 5.7 通用关系与交互物

非空间语义关系使用 `SemanticRelation`：

- Relation ID
- Source/Target `EntityRef`
- 稳定 Relation Type Key
- 版本化 Payload
- Entity Version

空间关系只能使用 `PieceLocation`。基础核心检查引用存在、无环、基数和结构不变量；攻击、治疗、交易等玩法含义属于未来规则层。

地图内容中的门、箱子等 `MapObject` 具有稳定 `MapObjectId`、Action Key、State Key 和合法状态转换。创建 Board 时，每个 Map Object 产生独立 `BoardObjectInstanceId`；`BoardObjectInstance`、`PieceLocation.parentEntityRef` 和 `SemanticRelation.EntityRef` 统一使用带 Board 作用域的实例身份。核心可以验证“状态转换是否存在”，但“谁能开锁、是否触发检定”属于未来规则。

---

## 6. 功能实施方案

### 6.1 地图制作器

实现原则：

- 地图文档是权威状态，Tilemap/Renderer 是投影。
- 工具只生成命令，不能直接改 Tilemap 数据。
- 绘制期间只显示 Preview，结束时提交一次事务。
- Dirty Region 只刷新受影响区域。
- 大量地块不创建独立 MonoBehaviour。

推荐实现顺序：

1. 视口、选择、平移、缩放、网格和吸附。
2. 画笔、橡皮、拾取。
3. 直线、矩形、填充。
4. 框选、多选、复制、剪切、粘贴和旋转。
5. 内容图层和本机图层编辑状态。
6. Undo/Redo、历史与大操作内存预算。
7. 门、箱子两类状态对象。
8. 发布 `MapContentVersion`，并从它创建 Board。

首轮逻辑目标至少支持 `256×256` 方格。渲染可见区/Chunk 尺寸由基准测试决定，不写入长期存档契约。只有完整快照数据证明过大时，才考虑逻辑 Chunk。

### 6.2 项目棋子库

实现顺序：

1. PieceDefinition CRUD 与验证。
2. 全新中性占位图形。
3. 用户图片选择、解码、尺寸/格式限制和内容 Hash。
4. 缩略图、按需加载和纹理释放。
5. 分类、标签、搜索和筛选。
6. 缺失资产占位、重复内容检测和重新绑定。
7. 从定义创建实例与实例属性覆盖。

用户图片不能进入 Addressables，也不能以 base64 写进 JSON。原文件名只用于展示，身份由内容 Hash 决定。

### 6.3 主控台

主控台是 Workbench，不是一个全能 `GameManager`。推荐工作流：

1. 创建 Scenario。
2. 选择已发布的 Map Content Version 创建 Board。
3. 从项目棋子库选择 Definition，在 Board 上创建 Instance。
4. 拖动时显示 Ghost，松手提交 `MovePiece`。
5. Inspector 通过有语义的命令修改属性。
6. 上下文菜单查询通用 Interaction，再由权威端重新验证并执行。
7. 迷雾、标注、对象状态和多地图切换进入 Scenario 权威状态。

项目树、Hierarchy、Inspector、History 通过 Presenter/ViewModel 调用 Application Facade，不查找并直接修改 Scene 对象。

### 6.4 通用交互

```text
点击 / 拖拽 / 右键
  → InteractionIntent(source, target, gesture)
  → IInteractionResolver 查询 ActionDescriptor
  → 默认动作或上下文菜单
  → 一个或多个 DomainCommand
  → Authority 原子验证并提交
```

首批动作：

- Move、Rotate、Flip、Hide、Reveal。
- Stack、ReorderStack。
- PutInContainer、TakeOut。
- Attach、Detach。
- Open、Close、Toggle State。

碰撞和距离查询只发现候选对象，不在 `OnCollision`/`OnTrigger` 中直接修改关系或权威状态。

---

## 7. 新格式存档、恢复与内容

### 7.1 设计目标

“优秀的存档”在 Alpha 中具体意味着：

- 正常保存可验证，失败时不会破坏上一个完整版本。
- 崩溃后可以恢复完整事务，不应用半个命令。
- 自动保存与手动保存对用户可见、可管理。
- 项目可搬家、可校验、可在 Windows/macOS 互开。
- 图片缺失不会导致棋子或地图实体丢失。
- Schema 可以从本产品 v1 向后续版本连续升级。

它不意味着一开始就实现云同步、任意版本降级、差分快照或完整事件溯源。

### 7.2 两种产品格式

- `<项目名>.sundollproj/`：日常编辑目录。
- `<项目名>.sundollpkg`：只读便携 ZIP 包，用于本产品项目的搬家与分享；导入后再成为项目目录。

Alpha 推荐先用“完整不可变 JSON Revision + 内容寻址二进制资产”，避免过早实现 JSON 对象库和可达性 GC：

```text
MyProject.sundollproj/
  HEAD.json
  revisions/
    <save-revision-id>/
      revision-manifest.json
      project.json
      catalog.json
      maps/*.json
      scenarios/*.json
  assets/
    <sha256>.<ext>
  thumbnails/
    <sha256>.webp
  journal/
    <journal-stream-id>/
      <first-sequence>-<segment-id>.log
  staging/
```

每个 Save Revision 包含完整的逻辑世界快照；图片等大二进制通过 Hash 共享。Retention 限制 Revision 数量。Alpha 不自动删除内容寻址资产，只显示总占用和潜在孤立资产；安全回收等到引用扫描和恢复策略经过专项验证后再加入。只有真实性能数据证明快照成本不可接受时，才升级为对象级内容寻址或逻辑 Chunk。

### 7.3 原子保存流程

1. 主线程取得一致、不可变的纯 C# Snapshot DTO，并记录读取到的 HEAD generation。
2. 新二进制资产先写入同卷临时文件，flush 后按 SHA-256 重新校验，再原子 rename 到 `assets/<hash>`；若目标已存在也必须验证现有文件。
3. Revision 只能引用已经可靠存在的资产。后台随后在同一个 `.sundollproj` 文件系统中的随机 staging 目录写入新 Revision；后台不访问 Unity Object。
4. 写入 `revision-manifest.json`，记录所有逻辑文件、大小和 SHA-256。
5. flush 后重新打开并验证 Schema、引用、不变量与 Hash，再将完整 Revision 原子提交到 `revisions/<id>`。
6. 提交 HEAD 前取得项目写锁并重新检查 expected generation；不一致时中止并报告并发修改，不能静默 last-writer-wins。
7. 根部极小的 `HEAD.json` 通过同目录临时文件 + 同卷 replace 最后提交。
8. 只有 HEAD 成功后，才允许压实已覆盖的 Journal 和清理超出保留策略的 Revision；Alpha 不自动删除 asset blob。

如果进程在任一步骤中退出，上一个 HEAD 仍应指向完整 Revision。HEAD 损坏时扫描并验证 Revision，向用户展示恢复候选，不盲目删除孤立但完整的 Revision。

Windows 与 macOS 的 atomic replace 和 durable flush 必须分别做故障注入验证，不能只依赖理论假设。

### 7.4 Recovery Journal

- Snapshot 是长期真相，Journal 只保留有限恢复窗口。
- Journal 使用多个有界 Segment，不无限追加一个文件。
- 一个成功事务写成一个完整 `AcceptedOperationBatch`；`WorldRevision` 和该 Stream 的 `OperationSequence` 各递增一次。
- Batch 至少含长度/Hash、Command ID、Journal Stream ID、World Revision、单一 Operation Sequence 和版本化 Payload。
- 恢复只接受完整且 Hash 正确的 Batch；允许丢弃损坏尾部，不允许应用半个事务。
- Snapshot 记录 `snapshotJournalStreamId` 与 `snapshotOperationSequence = S`，正常崩溃恢复只重放同一 Stream 中连续的 `sequence > S`。
- 用户主动恢复/回退到历史 Revision 时，提交新的 HEAD generation 并开启新的 Journal Stream；旧分支 Tail 不得自动重放到新分支。
- 新 HEAD 提交成功后，才退休同一 Stream 中已被 Snapshot 覆盖的 Segment。
- “已安全落盘”只在完整 Batch durable flush 后显示；崩溃允许丢失仍标记为未持久化的尾部事务，不能丢失已经标记安全的事务。

### 7.5 自动保存与保留策略

建议初始默认值在 M2 实测后确认：

- 每 3 分钟或累计 25 个正式事务触发后台自动保存，取先到者。
- 保留最近 10 个自动 Revision 和最近 7 个每日恢复点。
- 手动标记的快照不会被自动清理，直到用户明确删除。
- 始终保留当前 HEAD 和至少一个已验证 last-known-good。
- 显示磁盘占用，并在空间不足时停止创建新 Revision，而不是覆盖旧的完整档。

这些是新 Unity 产品自身的可靠性机制，与历史项目备份无关。

### 7.6 Manifest 与 Canonical State

`HEAD.json` 最低字段：

- Project ID、Active Save Revision ID、Active Journal Stream ID、Generation。

Revision Manifest 最低字段：

- Save Revision ID、Parent Revision ID。
- Format Version、World Schema Version、`minReaderFormatVersion`、`minReaderWorldSchemaVersion`。
- Snapshot World Revision、Snapshot Journal Stream ID、Snapshot Operation Sequence。
- Canonical State Hash。
- 逻辑文件表、大小与 Hash；Manifest 不把自身列入自身 Hash。
- Content Manifest Hash、Ruleset ID/Hash、扩展依赖。
- 应用版本、Build ID、UTC 时间和安全展示摘要。

Canonical State Hash 只覆盖权威世界字段，排除时间戳、缩略图、本机偏好、Undo、诊断和显示摘要。坐标和旋转使用量化值；集合按稳定 ID 排序；数字和日期不依赖系统区域设置。

### 7.7 Schema 与扩展数据

版本轴分开：

- Container `formatVersion`
- `worldSchemaVersion`
- Accepted Operation Type/Payload Version
- 扩展 Component Schema Version
- Content/Rules Hash
- 未来 Network Protocol Version
- App/Build Version

Pre-v1 开发存档明确可以丢弃。M7 冻结 v1 后，才承诺新产品自身的连续升级：`V1 → V2 → V3`。版本升级转换器必须是纯函数、有真实 Golden Save、先在临时世界完成并验证，再允许写入新版。

扩展数据使用稳定信封：

```json
{
  "typeId": "package-id:component-id",
  "version": 1,
  "data": {}
}
```

未知扩展保存原始 UTF-8 Payload 及其 Hash，未安装扩展时仍可 round-trip。不得依赖 CLR 完整类型名、输入驱动的反射实例化或 `BinaryFormatter`。

### 7.8 便携包与输入安全

`.sundollpkg` 默认只包含：

- 一个已提交 Save Revision。
- 该 Revision 引用的项目资产。
- Package Manifest 与可选缩略图。

不包含 Journal、旧 Revision、本机布局、Undo、诊断、账号或认证信息。导出采用临时 ZIP、重新打开验证、再原子 rename。导入在隔离 staging 解包，拒绝路径穿越、symlink、重复 Entry、解压炸弹、非法 GUID、越界坐标、NaN/Infinity、超大图片与悬空引用；完整验证后才创建项目 HEAD。

---

## 8. 规则层接缝

### 8.1 当前只实现最小契约

```text
IRulePolicy.Evaluate(ProposedTransaction, ReadOnlyWorld, ActorContext)
    → RuleDecision(Allow | Deny | Replace | Append)
```

- 当前产品实现 `NoRules/AllowAllRules`。
- `GetAvailableInteractions` 只为 UI 提示，执行时必须重新评估。
- Replace/Append 产生的命令重新经过权限、Revision 和结构校验，并与原命令原子提交。
- 规则不能访问 GameObject、Scene、UI、文件系统或具体网络 SDK。
- 规则不能直接改状态，只能返回决策或命令。
- 时间与随机由权威端注入，最终结果写入 Accepted Operation，重放时不重新随机。
- 结构不变量始终属于 Core：ID 唯一、引用存在、坐标合法、关系无环和单一空间父级。

### 8.2 接缝证明

M6A 建立一个独立测试程序集，只实现极小测试规则，例如“某区域禁止移动”并在一次事务中追加一个中性标记。验收：

- 测试规则可以允许、拒绝、替换或追加事务。
- 规则追加内容不能绕过 Actor 与结构校验。
- 删除测试规则并切回 AllowAll 后，Domain、存档外壳和主 UI 不需修改。
- 这只是架构测试，不是产品玩法。

### 8.3 延期内容

- RuleState、反应链、脚本 VM 和动态插件。
- 具体角色卡、数值系统、骰子、战斗和法术。
- 规则包市场、运行时任意代码和安全沙箱。

---

## 9. 未来联机接缝

### 9.1 权威模型

```text
客户端意图
  → 版本化 Command
  → 主机/服务器验证身份、权限、Revision 与规则
  → 权威端创建 ID、提交事务并分配 Sequence
  → AcceptedOperation
  → 按 Audience 生成 ProjectedDelta
  → 客户端投影 Unity View
```

永远不把客户端 Transform、GameObject、NetworkObject ID 或完整客户端 State 当作权威真相。Recovery Journal 是本地权威恢复流；面向客户端的 Projection Snapshot/Delta 是独立的 Audience 流，二者不能直接复用或共享 Sequence。

### 9.2 Alpha 从第一天保留

- `IAuthorityGateway` 与 `LocalAuthority`。
- 可序列化、版本化、幂等的 Command。
- World Revision、Entity Version 与 Operation Sequence。
- 可重放 Accepted Operation 和 Snapshot DTO。
- Canonical State Hash。
- 权威端生成 Entity ID 和随机结果。
- Domain/Save DTO 不引用任何网络框架类型。

### 9.3 M6B Loopback 证明

只做进程内模拟，不安装真实网络 SDK，但所有消息必须经过与传输无关的真实编码/解码边界，不能直接传递 C# 对象。最小协议包含：

```text
ProtocolEnvelope(messageVersion, authoritySessionId, clientInstanceId, message)
CommandEnvelope(commandId, typeId, payloadVersion, baseRevision, preconditions, payload)
CommandResult(commandId, status, committedRevision, conflict/error)
ProjectedDelta(projectionStreamId, projectionSequence, worldRevision, deltaTypeId, payloadVersion, payload)
ProjectionSnapshot(streamId, atSequence, snapshotId, projectionSchemaVersion, projectionHash, payload)
ReconnectRequest(knownProjectionStreamId, lastAckProjectionSequence, knownSnapshotId, knownProjectionHash)
```

模拟会话把 Actor 身份绑定到连接上下文，Authority 不信任 Command Payload 自报的 Actor。幂等结果缓存的作用域固定为 `(AuthoritySessionId, authenticated Client/Actor, CommandId)`，并覆盖约定的断线重试窗口。验证内容：

- 两个模拟客户端向同一 Authority 提交命令。
- 相同 Command ID 重试不会重复创建或修改实体。
- 不相交的 stale 修改在其读写实体版本仍有效时可重新验证。
- 冲突返回结构化结果，不静默覆盖。
- 丢失成功响应后重试可得到原结果。
- Audience 专属 Projection Snapshot + Delta Tail 在断线、重复和 Projection 窗口压实后仍收敛；不得直接向客户端发送 Recovery Journal Batch。
- 每个模拟客户端的 Projection Hash 等于 Authority 为该 Audience 生成的 Hash。

Loopback 通过后再专项比较 Unity Multiplayer/Transport、Mirror、FishNet 或文档型 WebSocket 协议。选择依据是房间、Relay、部署、重连、带宽、内容同步、许可与维护成本，而不是先选框架再改领域模型。

### 9.4 正式网络专项再实现

- TLS、认证、Actor 绑定、Resume Token。
- 房间、邀请、Relay、匹配和 Presence。
- 资产在线下载、Content/Rules 握手。
- 主机迁移、专用服务器、运维和监控。
- 带宽压缩、Interest Management 和完整 Audience Security。

---

## 10. 实施策略与里程碑

> 每个里程碑必须满足退出条件后再进入下一阶段。“界面看起来能用”不能替代数据、重建和恢复验收。只有用户明确下达“开始 M0”后才执行本节。

### M0：决策与一次性技术验证（1 周）

工作：

- 锁定 Unity 精确 LTS、Windows/macOS、后端、最低硬件与分辨率。
- 用全新合成内容验证 UI Toolkit 桌面拖放、Inspector、中文路径。
- 比较 Unity Tilemap 与必要的自绘可见区渲染。
- 验证运行时图片导入、缩略图、纹理释放和尺寸限制。
- 验证 IL2CPP 序列化与两平台文件 atomic replace/durable flush。
- 产出 ADR、术语表、性能基线、项目格式 v0 草案和范围冻结。

退出条件：技术路线、坐标模型、目标规模和产品范围得到确认；Spike 不依赖历史目录且可以丢弃，随后才初始化正式工程。

### M1：正式工程与最小纵向闭环（2 周）

工作：

- Bootstrap、首批 asmdef、Core、Application 与 Presentation 骨架。
- Command Bus、Local Authority、AllowAll Rule Policy。
- 从空白新建 Project/Map，绘制一个格子并显式发布最小 Map Content Version。
- 创建最小 Scenario 与 Board Instance，再创建一个全新几何占位棋子，以 `PieceLocation.OnBoard` 放置、移动并 Undo/Redo。
- 最小完整 Snapshot 保存、关闭、加载。
- Windows/macOS smoke build 与存档互开。

退出条件：真实链路 `MapDocument → PublishMapContent → MapContentVersion → ScenarioDocument → BoardInstance → PieceInstance.OnBoard` 完整贯通；销毁所有 View 后可以只凭纯 C# 状态重建相同棋盘；Core 测试无需 Scene；一次拖拽只产生一个正式操作。

### M2：新格式存档与内容基础（3 周）

当前进度：M2 的新格式存档、Journal、自动保存、内容 blob、`.sundollpkg`、后台保存队列、OS 文件写锁与故障注入最小切片已实现并通过 Unity Editor、EditMode、Play Mode 与 macOS universal build 验证。由于 Windows、跨平台互开和两平台强制退出矩阵尚未执行，M2 退出条件仍未完全关闭；详见 [M2 结果报告](./SundollWorld/Docs/M2-结果报告.md)。

工作：

- 稳定 ID、Content Resolver、用户图片 blob 和缩略图。
- 完整不可变 Save Revision、Manifest、HEAD 原子提交。
- 分段 Journal、自动保存、LKG、Retention 与恢复 UI 雏形。
- `.sundollpkg` 安全导入/导出。
- Golden Save、Canonical Hash、故障注入与损坏样本。
- Schema 仍是 pre-v1 草案，不承诺兼容。

退出条件：两平台在保存每个关键阶段强制退出后，都能恢复旧 HEAD、新 HEAD 或明确列出的完整 Revision；磁盘满和无权限不会覆盖旧档。

### M3：地图制作器 MVP（3–4 周）

当前进度：M3 功能范围已实现：正式 Workbench Scene、UI Toolkit 固定布局、正交 Camera、五个 Tilemap 内容层、Input System 画笔/橡皮擦/直线/矩形/填充、光标中心缩放、中键平移、Esc 与 Cmd/Ctrl 快捷键、拾取/框选/多选剪贴板、复制/剪切/粘贴/旋转、五层显隐/锁定/排序 Workspace State 2、稳定 ID 门箱对象、Schema 2 读取默认值与不可变发布；最新 Unity EditMode 为 57/57，PlayMode 为 1/1，macOS universal IL2CPP 构建与 Smoke 已通过。p95 性能/60 FPS 实测、真实鼠标视觉复验和 Windows/跨平台矩阵仍未关闭，详见 [M3 结果报告](./SundollWorld/Docs/M3-结果报告.md)。

工作：

- 视口、网格、吸附、内容图层和编辑器图层状态。
- 画笔、橡皮、拾取、直线、矩形、填充。
- 框选、多选、复制粘贴、旋转和批量 Undo/Redo。
- Dirty Region、可见区渲染和性能测试。
- 门、箱子两种状态对象。
- Map Draft 发布为 Map Content Version。

退出条件：能从空白持续制作、保存并重开一张中等地图；批量操作全部原子且可撤销；发布版本不可变。

### M4：项目棋子库与空间交互（3 周）

当前进度：M4 第一批已实现：Asset/Definition/Instance 纯数据模型、内容寻址资产去重、四种互斥位置、堆叠/收纳/附着/解除、旋转/翻面/显隐、关系循环校验、版本化 Command/Journal 接入，以及 Workbench 的占位棋子投影；EditMode 65/65、PlayMode 2/2。正式棋子库 UI、运行时图片导入/缩略图、棋子操作 UI、纹理性能预算仍在进行，详见 [M4 结果报告](./SundollWorld/Docs/M4-结果报告.md)。

工作：

- Definition/Instance/Asset 分离。
- 图片导入、缩略图、分类、标签、搜索和筛选。
- PieceLocation 的四种状态。
- 堆叠、收纳、附着、翻面、显隐和关系循环校验。
- 缺失资源占位、重复二进制去重和纹理预算。

退出条件：棋子定义复用、位置关系、撤销、保存重载和缺图恢复全部可靠；不存在坐标与父子关系双重真相。

### M5：主控台整合（4 周）

工作：

- 扩展 M1 的 Scenario/Board，加入多地图切换与完整主持工作流。
- Workbench、Hierarchy、Inspector、History 和快捷键。
- 迷雾、动态标注、对象状态和通用交互菜单。
- 保存/恢复状态与错误报告完整 UI。
- 面板、个人相机和 Selection 与权威项目状态分离。
- 用全新创建的真实工作流验证 DTO 与性能。

退出条件：用户能从空白项目主持一次完整的规则无关场景并可靠续档。此时只是**内部功能闭环候选**，存档仍标记 pre-v1，不得作为承诺长期保留用户项目的外部版本发布。

### M6A：规则接缝证明（1 周）

工作与退出条件：独立微型测试规则可 Allow/Deny/Replace/Append，并重新通过权限和结构校验；不修改 Core、Save 外壳或主 UI。

### M6B：Loopback 联机准备度（2 周）

工作与退出条件：进程内两个模拟客户端的消息全部经过传输无关编解码，完成幂等、冲突、Projection Snapshot + Delta Tail、断线追赶和最小 Audience Projection；各自 Projection Hash 与 Authority 收敛；不安装真实网络框架。

### M7：存档 v1 冻结与 Beta 加固（3–4 周）

工作：

- 根据真实地图、棋子关系、规则接缝和 Operation 契约冻结新存档 v1。
- 创建 v1 Golden Saves 和后续 Migration Registry 门槛。
- 性能优化、View Pool、纹理生命周期和长时间编辑测试。
- UI 自动化、模糊/损坏输入、崩溃恢复和恢复说明。
- Windows/macOS 打包、跨平台互开、升级与用户引导。

退出条件：性能、恢复、跨平台构建、v1 Golden Save 和安全输入全部过门槛，形成首个可对外测试的 Beta 候选。

---

## 11. 测试与质量门槛

### 11.1 每个功能的 Definition of Done

- 有 Domain/EditMode 测试。
- 有成功、拒绝、Undo/Redo 和批量原子性测试。
- 保存加载后 Canonical Hash 相同。
- 销毁 View 后能重建。
- 不直接修改 Scene 作为业务结果。
- 错误可以安全展示，失败不会丢掉已有完整档。
- Windows/macOS 至少各一次 Smoke 验证。

### 11.2 存档与恢复

- `Load(Save(S))` 语义等价且 Canonical Hash 相同。
- Snapshot + Accepted Operation 重放等于直接执行后的状态。
- 对 Revision 写入、Manifest、flush 和 HEAD replace 每阶段注入退出。
- 对新 asset blob 的临时写入、Hash 校验、原子 rename 和“目标已存在但损坏”分别测试。
- Journal torn tail 只能丢弃尾部，不能应用半个事务。
- HEAD 损坏可扫描并验证 Revision。
- 磁盘满、无权限、取消和并发保存不破坏旧 HEAD；expected generation 不一致时拒绝静默覆盖。
- 用户回退到历史 Revision 后开启新 Journal Stream，不会重新应用旧分支 Tail。
- Revision Retention 不会误删仍被引用的资产；Alpha 的孤立 asset blob 只报告、不自动删除。
- `.sundollpkg` 拒绝路径穿越、symlink、重复 Entry 和解压炸弹。
- 过新 Schema 安全拒绝写入；未知扩展往返不丢失。

### 11.3 命令、Undo 与交互

- 批量刷图、多选移动要么全成功要么全失败。
- Command → Undo → Redo 的 Hash 分别回到提交前和提交后。
- Preview 不进入 Accepted Operation；结束交互只产生一个 Batch。
- 相同 Command ID 重复提交不重复执行。
- PieceLocation 始终只有一个空间父级；容器/附着不能形成循环。
- 非法直接 Transform 修改会在重新投影时被权威状态覆盖。

### 11.4 扩展接缝

- Mock Rule 可拒绝、替换或追加命令而无需修改主干。
- Loopback 消息全部经过传输无关编解码；重复、乱序、断线和冲突后仍能通过 Projection Snapshot + Delta Tail 收敛。
- Actor 身份来自模拟会话绑定，不信任 Command Payload；Recovery Journal 不作为客户端 Projection 流。
- Audience Projection 不以完整权威世界 Hash 代替玩家可见 Hash。
- Domain、Command DTO 和 Save DTO 中不存在具体网络框架类型。

### 11.5 暂定性能基线

M0 必须在真实目标设备上校准，首轮目标为：

- 合成地图 `256×256`、最多 10 个内容/运行层。
- 1000 个可见棋子保持 2560×1440 视口平移缩放 60 FPS；数据层另做 10000 实例压力测试。
- 普通交互主线程响应 p95 小于 50 ms。
- Snapshot 捕获主线程停顿 p95 小于 16 ms，单次最大目标小于 50 ms。
- 基准项目后台 Save Revision 提交 p95 小于 2 秒，主线程不等待磁盘 flush。
- 10000 个 Accepted Operation Batch 恢复重放目标小于 5 秒。
- 图片导入有单图像素、解码内存和总纹理预算，不以原图无限驻留。

如果完整 Revision 达不到保存目标，先量测瓶颈，再选择 Copy-on-Write、Map Chunk 或对象级内容寻址，不预先增加复杂度。

---

## 12. 工期与依赖

| 阶段 | 估算 | 关键依赖 |
| --- | ---: | --- |
| M0 技术验证 | 1 周 | 用户确认范围与目标平台 |
| M1 纵向闭环 | 2 周 | M0 技术路线通过 |
| M2 存档与内容 | 3 周 | M1 Domain/Command/Snapshot |
| M3 地图制作器 | 3–4 周 | M1 命令、M2 基础保存 |
| M4 棋子库与交互 | 3 周 | M1 领域模型、M2 资产管线 |
| M5 主控台 | 4 周 | M3 + M4 |
| M6A 规则证明 | 1 周 | M5 真实事务模型 |
| M6B Loopback | 2 周 | M2 Journal/Snapshot + M5 |
| M7 v1 与 Beta 加固 | 3–4 周 | M6 契约验证 |

一人全职估算：

- M0–M5 内部功能闭环：16–17 人周。
- M0–M6 架构验证完成：19–20 人周。
- M0–M7 可对外测试 Beta：22–24 人周。
- 20% 风险缓冲后：架构验证完成 23–24 人周，Beta 27–29 人周。

多人并行不会按人数线性缩短，地图、持久化和主控台在核心模型处仍有集成成本。

---

## 13. 主要风险与控制措施

| 风险 | 影响 | 控制措施 |
| --- | --- | --- |
| 地图、棋子库和主控台同时横向扩张 | 很久没有可用闭环 | M1 先做最小纵向切片；每阶段有端到端退出条件 |
| Unity View 成为事实来源 | 存档、规则、联机分裂 | Core 无 Unity；View 销毁重建作为持续门禁 |
| 命令管线被 UI/碰撞回调绕过 | Undo 和恢复失真 | 所有正式修改只允许 DomainCommand；代码审查与测试封锁直接写状态 |
| 存档过度设计 | 核心功能延期 | Alpha 用完整 Revision + 二进制 CAS；量测后再引入对象 CAS/Chunk |
| Schema 过早冻结 | 真实交互出现后大量迁移 | M6 证明接缝后，M7 才冻结 v1；pre-v1 明确可丢弃 |
| 完整快照造成长帧或磁盘膨胀 | 编辑卡顿、空间不足 | 不可变 Snapshot、后台写盘、Retention、预算和真实性能门槛 |
| Windows/macOS 文件语义不同 | 原子保存失效 | M0/M2 双平台故障注入，不把单平台行为当保证 |
| UI Toolkit 桌面拖放或复杂控件受限 | Workbench 进度受阻 | M0 做真实 Spike；固定工作台布局；地图本体由场景渲染 |
| 大地图或大量棋子渲染过慢 | 主控台不可用 | Dirty Region、可见区、Pool、纹理预算；先基准再考虑 DOTS |
| 高分辨率用户图片耗尽内存 | 卡顿或崩溃 | 限制尺寸、缩略图、按需加载、纹理释放和导入报告 |
| 通用交互模型过于抽象 | 难以完成真实功能 | 先实现明确动作与 PieceLocation；自动化和泛化留到真实需求出现 |
| 空的未来接口过多 | 未验证抽象拖慢开发 | M1 只保留 Authority/Rules；网络接口到 M6 Loopback 时再增加 |
| 未来把完整世界发送给玩家 | 隐藏信息泄漏 | 预留 Audience Projection；玩家状态 Hash 与权威完整 Hash 分离 |
| 新素材来源不清 | 无法发布 | 从零建立来源/作者/许可清单；Alpha 默认中性自制占位内容 |

---

## 14. 建议记录的 ADR

1. 产品是 Unity Runtime 桌面应用，不是 Unity Editor 插件。
2. 使用 M0 验证后的 Unity 6 LTS 精确补丁。
3. Alpha 只支持 2D 方格。
4. 历史项目只提供思想参考，零代码、数据、素材和格式依赖。
5. 纯 C# Project/World State 是唯一权威状态。
6. Workspace、Preview 和 Domain 三类动作严格分开。
7. 所有正式变更经过版本化命令与 Local Authority。
8. Accepted Operation 可持久重放；Domain Notification 只在进程内使用。
9. UI Undo 按文档分区且不等于 Recovery Journal。
10. Map Draft 只有显式发布才产生不可变 Map Content Version。
11. Board Instance 固定引用 Map Content Version，不自动合并草稿变化。
12. Project Piece Catalog 是 Alpha 唯一可编辑棋子库；跨项目全局库延期。
13. PieceLocation 是唯一空间真相；普通 Relation 不决定坐标。
14. 完整不可变 Save Revision + 内容寻址二进制资产是 Alpha 存档模型。
15. HEAD 最后原子提交；Snapshot 为主，分段 Journal 为辅。
16. Pre-v1 存档不承诺兼容，M7 冻结新产品格式 v1。
17. 当前使用 AllowAll Rule Policy；测试规则只证明接缝。
18. 未来联机是主机/服务器权威 Snapshot + Projected Delta。
19. 在 Loopback 通过前不选择具体网络框架。
20. UI Toolkit 负责工作台，Unity 2D 场景负责地图。
21. Addressables 只管理内置内容；用户资产进入项目 blob store。
22. 初期不用 DOTS、CRDT、动态 C# 插件和大型 DI 框架。

---

## 15. 开工前确认清单

- [ ] 首发平台是否确认 Windows + macOS。
- [ ] 产品形态是否确认 2D 顶视 Runtime 桌面应用。
- [ ] Alpha 是否只做方格，六边形/3D 延期。
- [ ] Unity 精确 Editor、渲染管线和构建后端。
- [ ] 最低硬件、目标分辨率、最大地图和可见棋子规模。
- [ ] 固定工作台布局是否接受，完整 Docking 延期。
- [ ] 项目棋子库是否满足 Alpha，跨项目全局库延期。
- [ ] Map Draft → 显式发布 → Board Instance 的工作流是否接受。
- [ ] `.sundollproj/.sundollpkg` 是否作为正式扩展名。
- [ ] 自动保存频率、Revision Retention 与磁盘预算。
- [ ] Pre-v1 存档可丢弃、M7 才冻结 v1 的策略。
- [ ] M6A/M6B 作为架构证明、而非真实规则与正式联网。
- [ ] 全新中性占位素材与来源/许可记录方式。
- [ ] 项目显示名、C# 命名空间和应用标识。

---

## 16. 本次停止点

本轮只交付规划文件：

- 已把路线从“迁移旧项目”改为“Unity 原生从零开发”。
- 已明确历史项目只提供产品思想，不产生代码、数据、素材、存档或验收依赖。
- 已形成产品范围、领域模型、命令管线、存档、规则接缝、联机接缝和里程碑。
- **未创建 Unity 工程，未安装任何包，未写 C#，未创建 Scene/Prefab，未在 Unity 工程中导入、复制或依赖历史项目的代码、数据或素材，也未修改历史项目的运行代码、数据和素材。**

只有用户明确下达“开始 M0”或等价指令后，才建立一次性技术 Spike；Spike 通过并确认 ADR 后，才初始化正式产品工程。

---

## 17. 开工日复核入口

- Unity 6 发布与支持：[https://unity.com/releases/unity-6](https://unity.com/releases/unity-6)
- Unity Manual：[https://docs.unity3d.com/Manual/](https://docs.unity3d.com/Manual/)
- UI Toolkit：[https://docs.unity3d.com/Manual/UIElements.html](https://docs.unity3d.com/Manual/UIElements.html)
- Addressables：[https://docs.unity3d.com/Packages/com.unity.addressables@latest](https://docs.unity3d.com/Packages/com.unity.addressables@latest)
- Unity Multiplayer：[https://docs.unity.com/ugs/en-us/manual/mps-sdk/manual](https://docs.unity.com/ugs/en-us/manual/mps-sdk/manual)

具体包版本必须以 M0 锁定的 Editor LTS 兼容矩阵为准，规划阶段不写未经工程验证的精确包版本。
