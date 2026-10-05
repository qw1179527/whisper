# 唯一真源声明 · TRUE SOURCE

> 本文件是仓库对自己身份的声明：**这里就是开发的唯一真源。**

## 真源与副本

| 角色 | 路径 | 说明 |
|---|---|---|
| **唯一真源（开发在这里做）** | `D:\DSH专用\whisper` | 提交、跑门禁、出包、写真源数值——**只在这里** |
| 只读存档（历史副本） | `C:\Users\qing_\Documents\deepseek-harness\default-workspace\whisper` | 迁移前的电脑端副本，同一基线；仅作对照与回滚，**不要再开发** |
| 只读档案（继承物） | `C:\Users\qing_\Documents\deepseek-harness\default-workspace\_inherit\`、`…\交付物\` | 迁移包解出物与历史 APK/文档 |

- 迁移时点基线：HEAD `78fc999`（66 提交 · 分支 `master`）· 源与副本 tree 哈希一致
- C 盘副本的降级说明见其根目录 `READ-ONLY-ARCHIVE.md`（**文档级声明**：无只读属性、无 git 钩子、目录可写 —— 请自觉遵守，不要在那里改代码）

## 新真源特有的环境装配（不在 git 里，换机器需重建）

| 项 | 现状 | 为什么 |
|---|---|---|
| `native/dotnet/root` | **junction** → `C:\Users\qing_\.dsh\tools\dotnet`（.NET 8 SDK 8.0.311） | `unity-check.sh:81` 经 `native/dotnet.sh:9` 解析该路径做「真编译真跑」；`native/dotnet/` 被 `.gitignore:2` 忽略，故不进版本库 |
| Unity 编辑器 | `D:\Unity\6000.3.25f1\Editor\Unity.exe`（含 Android SDK/NDK/OpenJDK） | 本地出包，不再依赖 CI |
| `unity/Library` | 本副本**没有**（只搬源码） | Unity 首次打开工程时自动重建 |

**若第 21 步报错找不到 dotnet**：重建该 junction，或把 dotnet 放进 `native/dotnet/root`。

## 本仓库的硬约定

- `data/config.json` 是**数值唯一真源**，代码不得硬编码（V9 §19.5 三层免参）
- 不提交手写 `ProjectSettings.asset` 的**例外**：`78fc999` 已纳入首次真 Unity 生成的项目文件；此后构建设置仍以 `BuildConfigurator` 代码为准
- 门禁链 `bash unity-check.sh` 必须全步骤跑完（不只看"没报错"）；行数判据一律用 `[System.IO.File]::ReadAllLines()`，**不要用** `Get-Content | Measure-Object -Line`（PowerShell 5.1 会按 GB2312 误解码 UTF-8 丢行）

---
*建立于 2026-10-04（组级独立复核指出：真源声明此前只存在于 C 盘标记与一份未提交的报告里）。*
