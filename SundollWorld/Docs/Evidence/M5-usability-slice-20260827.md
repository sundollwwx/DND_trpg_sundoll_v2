# M5 usability slice evidence

日期：2026-08-27

本轮把 M5 的单格主持操作补成可连续使用的工作流：

- 迷雾揭示/隐藏笔刷支持可调半径，整次拖动合并为一个 `M5.SetFogBatch` 命令；整笔可一次撤销/重做，并通过 Command Envelope 编解码。
- 动态标注增加“拖动动态标注”工具，移动通过现有 `M5.UpsertAnnotation` 命令提交，保留文本、颜色和显隐状态。
- 玩家预览通过已有 `M6ProjectionBuilder` 生成 Audience Projection 快照，过滤未揭示地图格和隐藏棋子；预览中的地图编辑与右键菜单为只读。
- 动态标注在玩家预览中不显示，主持态仍可从可重建的投影显示。

## 验证

- EditMode：84/84 通过。
- PlayMode：12/12 通过。
- 新增覆盖：`FogBrushRoundTripsAsOneVersionedCommand`、`PlayerPreviewUsesAudienceProjectionAndIsReadOnly`。
- XML：`../TestResults_EditMode_20260827_214602.xml`、`../TestResults_PlayMode_20260827_214602.xml`。
- Unity：`6000.3.22f1` batchmode；未发现 C# 编译错误、空引用或缺失引用。

测试日志仍会记录 Unity Licensing Module 的 `Access token is unavailable` 通信提示，但本轮测试完整结束且没有测试失败；该提示属于许可证/云诊断通道，和项目代码编译结果分开记录。

## 未关闭

真实窗口鼠标手感、2560×1440 稳定帧率/分配门槛、60 分钟 Soak Test，以及 Windows 构建和跨平台存档矩阵仍待 M7 发布验收。
