# M7 clean build evidence: Visual Scripting removed

日期：2026-08-27

## 目的

验证移除未使用的 `com.unity.visualscripting` 后，M7 macOS universal IL2CPP 构建是否仍能成功，并确认旧 `Library/Bee` 中的 TypeDB 重复类型诊断是否属于缓存/包残留。

## 环境

- Unity：`6000.3.22f1`
- 构建类型：macOS Standalone universal IL2CPP
- 临时干净目录：`/private/tmp/sundollworld-clean-m7-final-vxrWTL`
- 构建日志：`/private/tmp/sundollworld-clean-m7-final-vxrWTL/Build_M7_clean_final.log`

## 结果

- BuildResult：`Succeeded`
- BuildReport：`errors=0`、`warnings=1`
- `TypeDB: Class` 计数：`0`
- `visualscripting` 计数：`0`
- 产物：`/private/tmp/sundollworld-clean-m7-final-vxrWTL/SundollWorld/Builds/SundollWorld-v03-M7-macOS-universal.app`
- 主执行文件架构：`x86_64 + arm64`
- 主执行文件 SHA-256：`0295daec607b7df2adf97391b05fa1963c08930d1e0053fca7a4941eb2fb8022`

## 唯一 warning

Unity Cloud Diagnostics/native symbols 上传 token 为空：

```text
Access token is empty. Native symbols will not be uploaded for this build.
```

这不是 C# 编译 warning，也不是运行时代码错误；如果以后需要上传 native symbols 到 Unity Cloud，再配置对应 token。

## 结论

TypeDB 重复类型诊断在干净构建中已清零。当前本地工作副本如复用旧 `Library/Bee`，可能仍看到历史缓存噪声；发布审计应以干净克隆或干净导入的构建结果为准。
