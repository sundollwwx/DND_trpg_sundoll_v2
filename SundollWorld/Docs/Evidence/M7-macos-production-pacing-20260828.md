# M7 macOS production pacing evidence

日期：2026-08-28

## 新增验证入口

- `scripts/macos-player-pacing.sh`：启动当前 macOS Universal IL2CPP Player，固定 2560×1440 窗口、1000 个可见棋子和 Workbench 相机轻微平移/缩放。
- 默认采样 3600 帧；可通过 `MACOS_PLAYER_PACING_SAMPLE_FRAMES` 和 `MACOS_PLAYER_PACING_TIMEOUT_SECONDS` 扩展到长时运行。
- Player 通过 `-sundoll-m7-perf-target-fps 60` 使用正式 Workbench 的 vSync 关闭、软件目标 60 FPS 策略。
- 原有不带该参数的性能入口仍保持解除限速，用于测量渲染承载能力。

## 短时真实窗口结果

环境：Unity `6000.3.22f1`、macOS Universal IL2CPP、Metal、2560×1440、1000 个棋子。

命令：

```sh
MACOS_PLAYER_PACING_SAMPLE_FRAMES=1800 \
MACOS_PLAYER_PACING_TIMEOUT_SECONDS=90 \
./scripts/macos-player-pacing.sh
```

结果文件：`SundollWorld/Logs/Pacing_M7_macos_20260828_105333.json`

- 采样：1800 帧；目标 60 FPS；vSync `0`；实际窗口 `2560×1440`。
- 帧时间 p50：`16.6883 ms`。
- 帧时间 p95：`17.3912 ms`；最大 `35.1028 ms`。
- 超过 `16.667 ms`：`1149/1800`。
- 托管分配 p95：`0 B`；最大 `0 B`。
- Player 退出码：`0`；日志未发现编译错误、空引用、丢失引用、崩溃签名。

判定：内存分配门槛通过；严格的每帧 `≤16.667 ms` 门槛未通过。该结果与此前限速 p95 `17.1309 ms` 同方向，属于 Unity 软件帧率上限的调度抖动；解除限速的渲染承载 p95 `4.5495 ms` 仍通过。当前 macOS M7 不宣称生产 60 FPS pacing 已关闭。

## 长时采样

已启动 60 分钟、`216000` 帧的相同生产 pacing 采样：

- 日志：`SundollWorld/Logs/Pacing_M7_macos_20260828_105454.log`
- 结果：`SundollWorld/Logs/Pacing_M7_macos_20260828_105454.json`
- 完成后需要补录 Player 是否持续运行、帧时间百分位、分配增长和异常签名。

可复用命令：

```sh
MACOS_PLAYER_PACING_SAMPLE_FRAMES=216000 \
MACOS_PLAYER_PACING_TIMEOUT_SECONDS=3700 \
./scripts/macos-player-pacing.sh
```

## 同批构建与测试

- EditMode：`TestResults_EditMode_20260828_104937.xml`，`85/85` 通过。
- PlayMode：`TestResults_PlayMode_20260828_104937.xml`，`13/13` 通过。
- macOS 构建日志：`Logs/Build_macOS_20260828_105028.log`。
- macOS Universal IL2CPP 产物：`Builds/SundollWorld-v03-M7-macOS-universal.app`。
- 主执行文件 SHA-256：`d0f86fdf736f5a372743516318c82b71cd8cd0eb207e858448694bd80df9648c`。
- 启动 Smoke：`Logs/Smoke_M7_macos_20260828_105232.log`，运行 45 秒、退出码 `0`。

本地 `Library/Bee` 构建报告仍出现历史 TypeDB 重复注册（本次 BuildReport `errors=34819`），但 BuildPipeline 成功；此前干净临时工程已验证 TypeDB `0`，故发布判定继续以干净导入证据为准。唯一构建 warning 仍是 Unity Cloud native symbols token 未配置。

## 许可证通道

本批测试、构建和许可证探针均使用 `LicenseClient-sundoll-6000.3.22`。启动前脚本现在会清理父进程为 `1`、且占用本项目通道的孤儿 Licensing Client；许可证探针 `LicenseCheck_20260828_104915.log` 已通过。
