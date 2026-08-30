# M7 Windows 验证交接

当前 macOS 侧已完成 Unity `6000.3.22f1` 下的 EditMode `87/87`、PlayMode `13/13`、macOS Universal IL2CPP 构建和 10 分钟操作 Soak。Windows 仍是发布阻塞项；本文件用于拿到 Windows 10/11 x64 机器后直接执行。

## 需要的环境

- Windows 10 或 Windows 11 x64。
- Unity Hub 和 Unity Editor `6000.3.22f1`。
- Unity 安装模块：Windows Build Support、Microsoft Visual Studio / Windows SDK、Windows IL2CPP Build Support。
- Git 和仓库访问权限；不需要上传任何许可证密钥或项目存档。

## 准备项目

```powershell
git clone https://github.com/sundollwwx/DND_trpg_sundoll_v2.git
cd DND_trpg_sundoll_v2\SundollWorld
```

用 Unity `6000.3.22f1` 打开 `SundollWorld`，等待导入和编译结束；不要删除或覆盖已有开发存档。Build Settings 的启动场景应为 `Assets/Sundoll/Scenes/M3Workbench.unity`。

## 按顺序执行

1. 在 Unity Test Runner 运行全部 EditMode 和 PlayMode 测试，保存 XML；目标分别为 `87/87` 和 `13/13`，失败数、忽略数都应为 0。
2. 菜单选择 `Sundoll > M7 > Build Windows x64 IL2CPP`，构建输出为 `Builds/SundollWorld-v03-M7-Windows-x64/SundollWorld.exe`。
3. 启动 `SundollWorld.exe`，完成新建项目、画图、保存、关闭、重开、继续编辑；确认保存状态为安全，地图和棋子没有丢失。
4. 在 Windows 上验证权限失败、磁盘不足、强制退出后的 HEAD/Revision 恢复；测试前复制一份测试项目，不要使用真实项目。
5. 将一个 macOS 生成的 `.sundollpkg` 导入 Windows，再将 Windows 导出的包带回 macOS，比较 Canonical Hash 和地图/棋子内容。

## 需要回传的证据

- Unity 版本、Windows 版本和 IL2CPP Build Support 已安装的截图或文本。
- EditMode/PlayMode XML 和 Unity Editor.log 中的错误/警告摘要。
- Windows 构建日志中 `M7 Windows x64 IL2CPP build result: Succeeded`，以及 `.exe` 文件路径和大小。
- 启动 Smoke 结果、强制退出恢复结果、`.sundollpkg` 双向互开结果和 Canonical Hash。

## 判定边界

Windows IL2CPP 构建、Windows 原子写盘、跨平台互开和双平台强制退出恢复全部通过后，M7 才能更新为 Beta Ready。若缺少 Windows 环境，当前保持“macOS Release Candidate / Windows 未验证”，不会误报为全平台完成。
