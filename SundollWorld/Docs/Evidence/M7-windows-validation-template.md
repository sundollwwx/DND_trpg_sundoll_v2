# M7 Windows 验证证据模板

> 本文件是 RC-0 模板，不代表 Windows 已验证。只有在 Windows 10/11 x64 上实际执行并保存原始日志、XML、构建物与截图后，才可填写 Passed。

## 候选冻结

- 冻结提交：`待填写`（必须与 `origin/main` 一致）
- GitHub 仓库：`https://github.com/sundollwwx/DND_trpg_sundoll_v2.git`
- Unity：`6000.3.22f1`
- Windows：`待填写`
- 执行时间：`待填写`
- 执行人/机器：`待填写`

## RC-2 自动化回归

| 项目 | 结果 | 证据 |
| --- | --- | --- |
| EditMode | `待填写`（候选目标至少 97，failed/skipped/inconclusive 必须为 0） | `TestResults_EditMode.xml` + Editor log |
| PlayMode | `待填写`（候选目标至少 16，failed/skipped/inconclusive 必须为 0） | `TestResults_PlayMode.xml` + Editor log |
| C# 编译错误 | `待填写` | Editor log |
| 未解释警告 | `待填写` | Editor log / 分类说明 |

## RC-3 Windows IL2CPP 与 Smoke

- Build Support / Windows IL2CPP / Visual Studio / Windows SDK：`待填写`
- 构建入口：`Sundoll.EditorTools.M7BuildValidation.BuildWindows64Il2Cpp`
- 构建日志成功标记：`M7 Windows x64 IL2CPP build result: Succeeded`
- `.exe`：`待填写`
- `_Data` 目录：`待填写`
- 构建大小/校验：`待填写`
- 启动 Smoke：`待填写`
- 新建—画图—棋子—主持—保存—关闭—重开—继续编辑：`待填写`

## RC-4 写盘与强制退出

> 仅对复制出的测试项目和测试存档执行，不要对正式用户存档注入故障。

| 场景 | 结果 | 证据 |
| --- | --- | --- |
| 权限失败 | `待填写` | 日志 + 保存状态 + 旧 HEAD |
| 磁盘不足故障注入 | `待填写` | 日志 + staging 清理 + 旧 HEAD |
| 并发 generation 冲突 | `待填写` | 日志 + 冲突结果 |
| 写盘中强制退出 | `待填写` | 重启日志 + HEAD/Revision |
| HEAD 缺失/损坏恢复 | `待填写` | 扫描结果 + Canonical Hash |
| 旧安全 Revision 保留 | `待填写` | Revision 列表 + Hash |

## RC-5 macOS ↔ Windows 互开

- macOS → Windows `.sundollpkg`：`待填写`
- Windows → macOS `.sundollpkg`：`待填写`
- 地图/棋子/对象/迷雾/标注一致性：`待填写`
- 预期 Canonical Hash：`待填写`（不得自行编造）
- 实际 Canonical Hash：`待填写`
- Hash 结果：`待填写`

## RC-6 显示与缩放

| 环境 | 项目中心 | 地图制作 | 棋子库 | 主控台 | 截图 |
| --- | --- | --- | --- | --- | --- |
| 1440×900 / 100% | `待填写` | `待填写` | `待填写` | `待填写` | `待填写` |
| 2560×1440 / 100% | `待填写` | `待填写` | `待填写` | `待填写` | `待填写` |
| Windows / 125% | `待填写` | `待填写` | `待填写` | `待填写` | `待填写` |
| Windows / 150% | `待填写` | `待填写` | `待填写` | `待填写` | `待填写` |

## 判定

- RC-0 候选准备：`进行中`
- RC-1 Windows 环境：`待验证`
- RC-2 自动化回归：`待验证`
- RC-3 IL2CPP / Smoke：`待验证`
- RC-4 写盘 / 强退：`待验证`
- RC-5 互开：`待验证`
- RC-6 缩放：`待验证`
- M7 Beta Ready：`禁止在上述阻塞项有未验证项时填写`
