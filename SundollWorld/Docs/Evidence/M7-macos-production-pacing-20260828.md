# M7 macOS production pacing evidence

日期：2026-08-28

## 新增验证入口

- `scripts/macos-player-pacing.sh`：启动当前 macOS Universal IL2CPP Player，固定 2560×1440 窗口、1000 个可见棋子和 Workbench 相机轻微平移/缩放；默认通过 macOS LaunchServices `open -n -W` 启动，`MACOS_PLAYER_PACING_LAUNCH_MODE=direct` 仅用于诊断回退。
- 默认采样 3600 帧；可通过 `MACOS_PLAYER_PACING_SAMPLE_FRAMES` 和 `MACOS_PLAYER_PACING_TIMEOUT_SECONDS` 扩展到长时运行。
- Player 通过 `-sundoll-m7-perf-target-fps 60` 使用正式 Workbench 的 vSync 关闭、软件目标 60 FPS 策略。
- 原有不带该参数的性能入口仍保持解除限速，用于测量渲染承载能力。

## 短时真实窗口结果

环境：Unity `6000.3.22f1`、macOS Universal IL2CPP、Metal、2560×1440、1000 个棋子。

命令（脚本内 `open` 启动路径）：

```sh
MACOS_PLAYER_PACING_SAMPLE_FRAMES=1800 \
MACOS_PLAYER_PACING_TIMEOUT_SECONDS=120 \
./scripts/macos-player-pacing.sh
```

结果文件：`SundollWorld/Logs/Pacing_M7_macos_20260828_225906.json`

- 采样：1800 帧；目标 60 FPS；vSync `0`；实际窗口 `2560×1440`。
- 帧时间 p50：`16.6664 ms`。
- 帧时间 p95：`16.7637 ms`；最大 `20.9117 ms`。
- 超过 `16.667 ms`：`898/1800`。
- 托管分配 p95：`0 B`；最大 `0 B`。
- Player 退出码：`0`；日志未发现编译错误、空引用、丢失引用、崩溃签名。

判定：内存分配门槛通过；严格的每帧 `≤16.667 ms` 门槛未通过。该结果与此前限速 p95 `17.1309 ms` 同方向，属于 Unity 软件帧率上限的调度抖动；解除限速的渲染承载 p95 `4.5495 ms` 仍通过。当前 macOS M7 不宣称生产 60 FPS pacing 已关闭。

## macOS GUI 启动上下文诊断

同一构建和同一参数通过脚本直接执行（`MACOS_PLAYER_PACING_LAUNCH_MODE=direct`）连续两次在启动约 0.2 秒后以退出码 `134`（`SIGABRT`）终止，未生成 JSON，脚本日志为空。macOS DiagnosticReports 的两份 `.ips` 均指向主线程 `NSApplicationMain → LaunchServices → GetCurrentProcess → abort`，没有 C# 异常或业务栈：

- `~/Library/Logs/DiagnosticReports/SundollWorld-2026-08-28-225054.ips`
- `~/Library/Logs/DiagnosticReports/SundollWorld-2026-08-28-225515.ips`

只将启动方式改为 `/usr/bin/open -n -W` 后，同一构建完成 1800 帧，退出码 `0`，生成 `SundollWorld/Logs/Pacing_M7_macos_open_20260828.json`。因此 pacing 脚本默认使用 `open`；直接执行保留为复现平台启动问题的诊断开关。该证据支持“当前 macOS GUI 直接执行上下文触发 AppKit/LaunchServices 间歇性崩溃”，但不把 macOS 系统组件内部原因误报为项目业务根因。

## 长时采样

第一次 60 分钟、`216000` 帧的直接启动生产 pacing 采样在约 0.2 秒处触发上述 `SIGABRT`，未生成 JSON，因此不计为完成的 60 分钟证据；第二次 1800 帧直接启动复现同样失败。当前脚本已切换默认 `open` 启动方式，长时证据仍需重新运行。

- 日志：`SundollWorld/Logs/Pacing_M7_macos_20260828_105454.log`
- 结果：未生成（Player 启动即崩溃）。
- 脚本已改为按样本量和目标帧率自动加 25% 余量，并默认使用 `open` 启动；下一次运行使用 `4600` 秒保护上限。

可复用命令：

```sh
MACOS_PLAYER_PACING_SAMPLE_FRAMES=216000 \
MACOS_PLAYER_PACING_TIMEOUT_SECONDS=4600 \
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
