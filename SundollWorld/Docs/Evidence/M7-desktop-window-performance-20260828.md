# M7 macOS desktop window performance evidence

日期：2026-08-28

## 条件

- Unity：`6000.3.22f1`
- Player：macOS Universal IL2CPP，Metal
- 实际 GPU：Apple M5（high power）
- 启动参数：`-screen-width 2560 -screen-height 1440 -screen-fullscreen 0 -sundoll-m7-perf`
- 场景：256×256 地图、1000 个可见棋子，采样期间持续轻微平移和缩放
- 预热：120 帧
- 采样：900 帧
- 生产运行策略：Workbench 目标 60 FPS；Standalone High 档关闭 vSync，使用软件帧率上限
- 性能门槛测量策略：关闭 vSync，并在采样期间解除软件帧率上限，测量可持续渲染承载能力；目标仍为每帧不超过 `16.667 ms`

## 结果

最新有效证据文件：`/private/tmp/sundollworld-m7-desktop-perf-uncapped-20260828.json`

- 实际窗口：`2560×1440`，目标 `60 FPS`
- 测量模式：`measurementTargetFrameRate=-1`、`measurementVSyncCount=0`
- 可见棋子：`1000`
- 帧时间 p50：`2.3956 ms`
- 帧时间 p95：`4.5495 ms`
- 帧时间最大：`29.9244 ms`
- 超过 `16.667 ms` 的帧：`1/900`
- 每帧托管分配 p50：`0 B`
- 每帧托管分配 p95：`0 B`
- 每帧托管分配最大：`0 B`
- Player 退出码：`0`

## 判定

- 渲染承载 p95 `<16.667 ms`：通过。
- 每帧分配 p95 `≤1 KB`：通过。
- 真实窗口启动与 1000 棋子场景：通过。
- 60 FPS 生产帧 pacing：本轮未以限速模式作为门槛；上一轮限速采样仍有约 17.131 ms 的 p95，因此保留“需在目标硬件上做长时帧 pacing/Soak 复验”的风险。

本轮相对照结果：Ultra+vSync 的 p95 为 `17.8223 ms`，High+vSync 的 p95 为 `32.3410 ms`，High+vSync 关闭但仍限速的 p95 为 `17.1309 ms`。这些结果表明旧采样包含显示同步/睡眠抖动；最新采样明确记录了解除限速后的渲染承载能力，不能替代长时间生产帧 pacing 验收。

这份证据来自真实 macOS Player 窗口，不是 batchmode 的 `640×480` 代理结果。正式 Workbench 外壳、地图视口和左右面板已经完成启动 Smoke；本文件不把初始化截图当作 1000 个棋子最终视觉排布证据。
