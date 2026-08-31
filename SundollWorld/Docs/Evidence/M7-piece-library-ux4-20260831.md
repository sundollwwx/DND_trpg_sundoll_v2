# UX4 棋子库页面验证证据

日期：2026-08-31

## 范围

本批次收尾棋子库工作区的筛选和视觉分区，不升级 World Save 或 Schema。修改继续使用现有 `WorkbenchSession`、`M4PieceLibraryFacade`、内容寻址资产目录和 `M7PieceLibraryGridController`。

## 已完成

- 名称、分类、标签和定义 ID 搜索。
- 动态分类筛选，以及全部/有缩略图/缺少缩略图筛选。
- 分类和素材状态的组合筛选；筛选条件纳入 Workbench 列表刷新缓存键。
- 保留固定高度、两列虚拟化缩略图网格，显示代理不超过 256px，继续使用引用计数 LRU 缓存。
- 将搜索筛选、当前定义编辑、素材导入、定义列表和棋盘实例分成独立视觉区块。
- 列表摘要显示过滤结果、总定义数和当前缩略图缓存项目数。

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
- PlayMode 新增/扩展覆盖：分类筛选、缺少/存在缩略图筛选、组合筛选、虚拟化行数和棋子库视觉分区节点。
- 两次 Unity 日志均通过版本专用许可证握手，未出现协议版本、超时或重连失败。

结果文件：

- `SundollWorld/TestResults/Local/TestResults_EditMode_20260831_034338.xml`
- `SundollWorld/TestResults/Local/TestResults_PlayMode_20260831_034338.xml`
- `SundollWorld/Logs/Test_EditMode_20260831_034338.log`
- `SundollWorld/Logs/Test_PlayMode_20260831_034338.log`

## 未覆盖范围

- 本轮没有重复运行 60 分钟 Soak，遵循已确认的跳过决定。
- Finder 拖放、Windows IL2CPP、跨平台存档互开和真实鼠标审美复验仍需目标环境证据。
- 素材导入本身已有 PlayMode/路径导入测试；原生文件选择器的最终点击流程仍属于 macOS 手工复验范围。
