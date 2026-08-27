# M7 1000-piece projection performance evidence

日期：2026-08-27

## 场景

- Unity：`6000.3.22f1`
- 测试：`M7PieceProjectionMeasures1000VisiblePiecesAndSteadyStateAllocations`
- XML：`../TestResults_PlayMode_20260827_221144.xml`
- 运行入口：`scripts/unity-run-tests.sh all`
- 目标视口：`2560×1440`
- 目标帧率：`60 FPS`
- 实际运行方式：Unity batchmode / `-nographics`，实际视口 `640×480`
- 棋子：1000 个可见、已放置到 256×256 地图的占位棋子

## 结果

- PlayMode 全套：`13/13` 通过，0 失败，0 跳过。
- 预热后的投影刷新 p95：`12.198 ms`。
- 预热后的投影刷新最大值：`12.198 ms`。
- 稳态刷新分配 p95：`0 B`。
- 稳态刷新分配最大值：`0 B`。
- 样本数：刷新和分配各 10 次。

## 解释与限制

本证据确认 1000 个可见棋子在当前投影路径上的 CPU 刷新和托管稳态分配基线，并不等同于真实桌面窗口的 60 FPS。真实 2560×1440 Player、GPU 帧时间、渲染线程分配和 60 分钟连续编辑/主持仍需单独执行；在没有 Windows 环境的情况下，Windows/跨平台发布矩阵仍保持 Blocked。
