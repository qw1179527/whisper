# Project Whisper · 电脑端体检对齐报告（继承后基线）

> 生成：**2026-10-04** · 位置 `D:\DSH专用\whisper` · HEAD `78fc999`（66 提交 · 分支 `master`）
> 口径：**凡是可数的，一律以实测为准**；"声称"取自交接文档原文（附 `文件:行号`），
> "实测"取自本机命令输出。差值栏明确写「一致 / 已过期 / 口径不同 / 已改变 / 假过期」。
> 本文件是后续所有修正类的**唯一基线**——改之前先查这里，避免把已修项当未修项重复劳动。
>
> **第 2 版修订（2026-10-04）**：本文经**独立只读验证**（冷视角 subagent）后逐条修正 6 处，
> 其中 1 处是**方向性错报**（把"未修"写成"已修"，会直接造成漏修）——详见 §六「修订记录」。
> 凡标注「⚠️ 验证修正」的单元格即为本次修订点。

---

## 〇、先读这一段：环境里有两个工作副本（验证者指出的最大盲点）

| 副本 | 位置 | 是什么 | 现状 |
|---|---|---|---|
| **唯一真源（开发用）** | `D:\DSH专用\whisper` | 本次迁移落地的**源码副本**（44.96 MB / 751 文件），无 `Library/`、无 APK | 门禁链已在此跑通（§一 #15） |
| **历史工作副本（Unity 真跑发生地）** | `C:\Users\qing_\Documents\deepseek-harness\default-workspace\whisper` | 迁移前/期间用的副本，**同一 HEAD `78fc999`**；有 `Library/`（5.69 GB）与 25.08 MB APK | 已按决策降级为**只读存档**（`READ-ONLY-ARCHIVE.md`） |

**因此**：本文中「Unity 真出包 25.08 MB」「首次真 Unity 跑 EditMode/PlayMode」这类**已经发生过的事实**，
其**证据指针在 C 盘副本与 `~\.dsh\backup-*` 目录**，在新真源里**找不到**（那不是漏证据，是搬家的必然）。
下一轮若要在**新真源**里复现"本地出包"，Unity 会在 `D:\` 重建 `Library/`（首次导入耗时，属正常）。

---

## 一、逐条对照表（声称 vs 实测）

| # | 项 | 声称值（出处） | 实测值（命令） | 差值 | 证据 |
|---|---|---|---|---|---|
| 1 | git 提交数 | **58 次提交**（`docs/HANDOFF.md:3`、`:475`） | **66** 提交 | **已过期**（+8） | `git rev-list --count HEAD` |
| 2 | 交接文档自身时点 | 提交 `dd55342` · 2026-10-03 19:35（`docs/HANDOFF.md:3`） | 实际 HEAD `78fc999`；`dd55342` 既不是 HEAD 也不是 `origin/main`（`865f047`） | **已过期** | `git log --oneline -1`；`git rev-parse origin/main` |
| 3 | C# 文件数 | **45 个 C#**（`docs/HANDOFF.md:28`） | **`unity/Assets/Scripts` 口径 = 45**（精确一致）；`unity/**` = **48**（+3 个 Editor 脚本，HANDOFF 同句已单列）；全仓（含 `native/`）= **61** | ⚠️ **验证修正：一致（口径已对齐，非过期）** | `Get-ChildItem unity/Assets/Scripts -Recurse -Filter *.cs` |
| 4 | C# 行数 | **6846 行**（`docs/HANDOFF.md:28`） | **`Scripts` 口径全行数**：`dd55342` 时 = **6846**（精确复现）；**当前 HEAD = 6876**（+30）。`unity/**` 全口径 = 7170 行；`native/` = 13 文件 / 2448 行 | ⚠️ **验证修正：口径就是 Scripts + 全行数（不是"含 native"），差值仅 +30** | `[System.IO.File]::ReadAllLines()` 逐文件累计 |
| 5 | 工具脚本数 | **41 个工具**（`docs/HANDOFF.md:28`、`:475`） | `tools/` 下 **41 个脚本** = 37 `.mjs` + 4 `.sh`（另有 1 个 `GITHUB-SETUP.md` 说明文件，共 42 个文件） | ⚠️ **验证修正：一致（原写"过期 +1"是把说明文件算成脚本，属假过期）** | `Get-ChildItem tools -File \| Group-Object Extension` |
| 6 | asmdef | **10 个 asmdef**（`docs/HANDOFF.md:28`） | **10** | **一致** | `-Filter *.asmdef` |
| 7 | .meta 数 | 包内 **106 个 `.meta`**（`docs/HANDOFF.md:544`） | 迁移包口径 = **106**（精确复现）；当前仓库 = **110** | ⚠️ **验证修正：声称是"迁移包内"口径，可复现；仓库 +4 属搬家后新增，不算过期** | `_inherit\DSH-MIGRATION-20261003\whisper` 下 `-Filter *.meta` |
| 8 | 本机断言 | **156 条断言 0 失败**（`docs/HANDOFF.md:12`、`:108`） | **通过 156 · 失败 0** | **一致** | `D:\DSH专用\_evidence\chain-pass1.log`（两遍 SHA256 相同 `FB36E829…4DFF8`） |
| 9 | 一键链步数 | **21 步**（`docs/HANDOFF.md:350`） | 标记 **22** 个（含 `6.5`；编号仍到 `[21/21]`） | **口径不同（无实质差异）** | 同上日志行首标记 |
| 10 | Unity 测试是否跑过 | `Tests/` EditMode×3 + PlayMode×1「**从未执行过**」（`docs/HANDOFF.md:166`） | 已被推翻：2026-10-04 08:18–08:27 有 EditMode/PlayMode 结果 XML 与日志（**在 C 盘副本/备份目录**） | **已改变（文档过期）** | `C:\Users\qing_\.dsh\backup-20261003-122606\editmode-results.xml`(14/16)、`editmode-results2.xml`(**16/16**)、`playmode-results.xml`(1/3)、`playmode-results2.xml`(**3/3**)、`unity-tests-*.log` |
| 11 | Unity 编辑器 | 手机端「**无 Unity 引擎**，出包必须走 CI，一次 25~45 分钟」（`docs/HANDOFF.md:27`、`:288`） | 本机 **已装 `D:\Unity\6000.3.25f1\Editor\Unity.exe`**（ProductVersion `6000.3.25f1_e1dba0a9aba4`），AndroidPlayer 下 SDK/NDK/OpenJDK 齐备；本地出的 APK = **25.08 MB**（26294316 B，在 **C 盘副本**的 `unity\build\Android\whisper-android.apk`） | **已改变（迁移最大收益兑现）** | `Get-Item …VersionInfo`；出包日志 `C:\Users\qing_\.dsh\backup-20261003-122606\unity-build-4.log:5657` |
| 12 | 摇杆头号未知（N5） | 有 inputsystem 包**但仓库无 `ProjectSettings.asset`** → `activeInputHandler` 由 CI 默认生成，「若新输入系统 Only 则摇杆完全不动」（`docs/HANDOFF.md:262-264`、`docs/qa-ledger.md:76`） | `unity/ProjectSettings/ProjectSettings.asset` **已提交**（798 行；提交后 `ProjectSettings/` 被跟踪 **24** 个文件）；`:781 activeInputHandler: 0`（= **旧**输入管理器） | **已改变（静态风险大幅下降，仍需真机确认）** | `git ls-files unity/ProjectSettings`；`ProjectSettings.asset:781` |
| 13 | EyeHeightM 三份拷贝（N7） | `PlayerController.cs:25`、`GameBootstrap.cs:84`、`:307`（`docs/qa-ledger.md:78`） | 仍是 **三处 `1.7f`**：`PlayerController.cs:25`（常量定义）＋ `GameBootstrap.cs:84`、`:307`（**裸字面量、未引用常量**；GameBootstrap 全文无 `EyeHeightM`） | **仍成立（未修）** | `Select-String '1\.7'` / `'EyeHeightM'` |
| 14 | 真机验收入口 | `tools/verify-apk-on-device.sh` **依赖 Shizuku（`shz`）**（`docs/HANDOFF.md:34`、`:195`） | Windows 无 `shz`（脚本 `:38` 有 `command -v shz … \|\| exit 1` 硬门）；本机 `adb` 可用，**当前无连接设备**；`df41717` 只做了跨平台修正，未改驱动方式 | **需重写（下一步工作项）** | `adb devices`；`git show --stat df41717` |
| 15 | 门禁链是否跨平台 | 一键链原为 bionic/Termux 工具链（`unity-check.sh` 注释「Termux arm64/bionic 版」） | 新位置 **exit 0 · 22 步全跑 · 五门数字全对**：`gate-model 11/0`、`gate-physics 4/0`、`gate-code 7/0`、`gate-test 4/0`、`gate-bbox 1/0` | **已达成（迁移后仍可信）** | `_evidence\chain-pass1.log`（`:16/:34/:46/:52/:70`）/ `chain-pass2.log`；`exit 0` 由"跑到收尾横幅"推断（日志无显式码） |
| 16 | 环境装配依赖 | 未在文档中登记 | `unity-check.sh:81` 走 `native/dotnet.sh` → 该脚本 `:9` 解析 `$SELF_DIR/dotnet/root`；`native/dotnet/` **未被 git 跟踪**（`.gitignore:2`），现由 **junction `native/dotnet/root`** → `C:\Users\qing_\.dsh\tools\dotnet`（727.7 MB，不复制） | **新增事实（文档缺口，需登记）** | `git check-ignore -v native/dotnet/root`；`Get-Item … LinkType` |

**对照条目数：16 条（≥10 ✅）· 其中 4 条经独立验证后由"过期/口径不同"改判为"一致/口径已对齐"**

---

## 二、上一轮 4 个未推提交改了什么（杜绝重复劳动）

`git log --oneline origin/main..HEAD` → 4 个提交，**`origin/main` 停在 `865f047`**：

| 提交 | 标题 | 实质内容 | 影响的残余 |
|---|---|---|---|
| `df41717` | fix(跨平台): 修 Windows 上的路径分隔符/shebang/脚本调用缺陷 | **21 个文件**：`native/*.sh` 全部 shebang、`tools/unity-syntax-check.sh`（rg 原生 Windows 路径分隔符坑）、`arch-guard`/`extract-modules`/`split-modules`/3 个移植对拍脚本、`unity-check.sh`、`tools/git.sh`、`make-migration-zip.sh`、`verify-apk-on-device.sh` | 使门禁链**能在** Windows 跑（§一 #15 的前提） |
| `db70ee1` | fix(unity): 修 3 个真 Unity 测试暴露的缺陷 | `LevelGeometry.cs`（删掉不可达标签 `next:;` 与错位注释）、`Tests/EditMode/LevelLoaderTests.cs`（±60 行）、`Tests/PlayMode/BootSmokeTests.cs`（`LogAssert.ignoreFailingMessages` 改为**日志产生前** `LogAssert.Expect`——旧写法使该用例**恒红**且掩盖真错误） | §一 #10：Unity 测试**首次真跑**并暴露真缺陷 |
| `78fc999` | chore(unity): 补入首次真 Unity 打开工程产生的项目文件 | **29 个文件 / 2958 行新增**（`git show --stat`）；其中 `unity/ProjectSettings/*` **新增 21 个文件**（含 `ProjectSettings.asset` 798 行）、`unity/Assets/Scenes/Boot.unity`、`DefaultVolumeProfile`、`UniversalRenderPipelineGlobalSettings`、`unity/.gitignore` 补 7 行。⚠️ **验证修正**：`ProjectSettings/` 当前被跟踪 **24** 个文件 = 本次新增 21 + 此前已有 3（`README.md`/`ProjectSettings-NOTE.md`/`ProjectVersion.txt`） | **推翻 N5 前提**（ProjectSettings 现已在仓库里） |
| `a372ef8` | docs: 补第 22 节「从这里接上」 | ⚠️ **验证修正**：它**并未在 origin 上**（`git merge-base --is-ancestor a372ef8 origin/main` → 假），它本身就是这 4 个未推提交之一（与 `865f047` 的 HANDOFF.md 差 38/32 行） | 说明迁移包生成时的 HEAD |

> ⚠️ **仍未推送**：CI 用的是 `origin/main` 的旧代码，这 4 个提交只在本地。

---

## 三、质检第二轮残余（N1~N7）在电脑端当前是否仍成立

来源：`docs/qa-ledger.md` R2（L69-L78）＋ 本机实测。

| 编号 | 残余（原文摘要） | 当前判定 | 实测证据 |
|---|---|---|---|
| **N1** | CI 版本号静默回落：`BuildConfigurator.cs:100-109` 从 `GITHUB_RUN_NUMBER` 派生，取不到回落 `0.1.1/1` 且只 `Debug.LogWarning` | ❌ **仍成立** | `BuildConfigurator.cs:33 FallbackBundleVersion="0.1.1"`、`:34 FallbackVersionCode=1`、`:106` 读环境变量、`:108` 仅 `problems.Add(...)`；`BuildScript.cs` 无任何本地版本来源。**闭环旁证（本地包实测）**：出包日志 `unity-build-4.log:354` = `版本 0.1.1 (code 1)`、`:371` = `未读到 GITHUB_RUN_NUMBER，版本号回落到 1` |
| **N2** | 3 条断言缺"位移非零"拦停（撞墙 / 大位移 / dt clamp） | ❌ **仍成立（3/3）** ⚠️ **验证修正：原文误写"撞墙那条已补"** | 三条全无位移拦停：`Program.cs:1258-1277`（撞墙）只判 `escaped==0`、`:1278-1288`（大位移）只判 `geo.Passable`、`:1289-1298`（dt clamp）只比 `ra/rb`。**关键区分**：`:1248` 的 `[假绿拦停]` 属于 `:1235`「斜向输入被归一化」断言，**不是**撞墙那条（我第 1 版挂错了断言）。全文共 3 处拦停：`:1248`（斜向）、`:1317`（脚步累计距离）、`:1337`（视角正交），**N2 的三条一条都没有** |
| **N3** | ★撞墙判据只查中心格 `geo.Passable`，真正防穿墙是半径感知 `BlockedAt` | ❌ **仍成立** | `Program.cs:1272` 用 `geo.Passable(m.X, m.Z)`（`LevelGeometry.cs:61` = 中心格）；半径感知 `BlockedAt` 存在（`:294-300`）且被 `Resolve`（`:276/:287-289`）使用，但该断言**未采样半径** → 丢半径夹紧也照样绿 |
| **N4** | 脚步断言判别力≈0：`expect` 由**同一被测常量**算出 + ±1 容差 | ❌ **仍成立** | `Program.cs:1315 int expect = (int)(3.0f / PlayerMotion.StrideLengthM)`、`:1318` 容差 ±1；`PlayerMotion.cs:55 StrideLengthM=0.75f` 同时驱动实现（`:186`）→ 自算自判。（`:1317` 的"累计距离不足"拦停**不改变**此结论：期望值仍自算） |
| **N5** | Input System 风险：无 `ProjectSettings.asset`，`activeInputHandler` 由 CI 默认生成 → 摇杆可能恒 0；桩恒返回 0 使本机永远绿 | 🟡 **前提已改变，需真机确认** | `ProjectSettings.asset` **已提交**且 `:781 activeInputHandler: 0`（旧输入管理器）→ 静态看 `Input.touchCount` 不再被"新系统 Only"清零；但 `Packages/manifest.json:6` 仍有 `com.unity.inputsystem 1.11.2`，**真机能否拖动仍未被证** |
| **N6** | `gate-editor-api` 只扫 `unity/Assets/Editor`，`Runtime/` 新增 Unity API 无出处门禁 | ❌ **仍成立** | `tools/gate-editor-api.mjs:37 EDITOR_DIR = path.join(ROOT, 'unity/Assets/Editor')`、`:50-51/:70` 仅读该目录（qa-ledger 原引 `:39,46` 已过期，本文的 `:37/:51` 为实测） |
| **N7** | `EyeHeightM = 1.7f` 三份拷贝 | ❌ **仍成立** | `PlayerController.cs:25`（常量）＋ `GameBootstrap.cs:84`、`:307`（裸 `1.7f`，GameBootstrap 全文无 `EyeHeightM` 引用） |

**残余判定汇总（修订后）**：仍成立 **6** 条（N1/N2/N3/N4/N6/N7）· 部分成立 **0** 条 ·
前提已改变待真机 **1** 条（N5）。
> ⚠️ 第 1 版曾写"仍成立 5 条 + N2 部分成立"——**那会把撞墙断言漏修**，已修正。

---

## 四、本报告的证据可复现清单

```powershell
cd 'D:\DSH专用\whisper'
git rev-list --count HEAD                                   # 66
git log --oneline origin/main..HEAD                         # 4 个未推提交
git merge-base --is-ancestor a372ef8 origin/main; $LASTEXITCODE   # 1（=不在 origin）
(Get-ChildItem unity/Assets/Scripts -Recurse -Filter *.cs).Count  # 45（HANDOFF 口径）
(Get-ChildItem unity -Recurse -Filter *.cs).Count                 # 48
(Get-ChildItem tools -File | Group-Object Extension)              # .mjs 37 + .sh 4 = 41 脚本
Select-String unity\ProjectSettings\ProjectSettings.asset -Pattern activeInputHandler   # :781 -> 0
Select-String unity\Assets\Scripts\Runtime\GameBootstrap.cs -Pattern '1\.7'             # :84 :307
Select-String tools\gate-editor-api.mjs -Pattern EDITOR_DIR                              # :37
Select-String native\csharp-verify\Program.cs -Pattern 'int expect|假绿拦停'             # :1315 / :1248 :1317 :1337
git show --name-only --format='' 78fc999 | Select-String 'unity/ProjectSettings'         # 21 个新增
```
行数口径提醒（`.cs` 行数必须用 `[System.IO.File]::ReadAllLines()`）：
PowerShell `Get-Content | Measure-Object -Line` **会漏行**（同一文件实测 5664 vs 真实 6876）——**不要用它做行数判据**。

门禁链证据（两遍逐字节相同）：`D:\DSH专用\_evidence\chain-pass1.log`、`chain-pass2.log`
（`SHA256 = FB36E82918DE63BE235D9FE3A45E364C24C43FADC8A58F94E0C1F8250BC4DFF8`）

抽查复现（任意 3 条）：
- #12 → `Select-String unity\ProjectSettings\ProjectSettings.asset -Pattern activeInputHandler` → `781:activeInputHandler: 0`
- #7  → 迁移包口径 `(Get-ChildItem '_inherit\...\whisper' -Recurse -Filter *.meta).Count` → `106`；当前仓库 `110`
- #13 → `Select-String unity\Assets\Scripts\Runtime\GameBootstrap.cs -Pattern '1\.7'` → `84:` 与 `307:` 两个裸字面量

---

## 五、这份体检对后续修正类的直接指令

1. **N1 必须修**：本地出包若要"版本可区分"，须给 `BuildConfigurator` 一条**非 CI 依赖**的版本来源
   （本地时间戳/自增计数），而不是回落 `0.1.1/1`（本地包已实测为 `0.1.1 (code 1)`）。
2. **N2 是 3/3 未修**（含撞墙那条）——修的时候要让"注入即判红"证明修好了；**别只补撞墙一条**。
3. **N3/N4 是判据判别力问题**（不是功能 bug）：N3 要让断言采样半径（`BlockedAt`），N4 要让期望值独立于被测常量。
4. **N5 的静态风险已降低，但不能算已验**：真机第一名要验的就是摇杆拖动是否真的驱动玩家。
5. **N6/N7 属机制保护与单一真源**：改动小、收益明确（Runtime 也纳入 API 出处门禁；`1.7f` 收敛到常量）。
6. **推送**：4 个提交未推 → CI 仍跑旧代码；推送需要用户在电脑端新建令牌（旧令牌已在旧会话明文泄漏，须废弃）。
7. **新真源里若要"本地出包"**：Unity 会在 `D:\` 重建 `Library/`（首次导入耗时）；不要在 C 盘旧副本上继续开发。

---

## 六、修订记录（独立验证 → 逐条修正）

| 修订点 | 第 1 版（错） | 第 2 版（正） | 来源 |
|---|---|---|---|
| **N2 判定** | "部分仍成立：撞墙那条已补拦停" | **3/3 仍成立**；`:1248` 属"斜向归一化"断言，与撞墙无关；全文 3 处拦停 = `:1248`/`:1317`/`:1337` | 独立验证者（冷视角）+ 本机复核 `Check(...)` 标题序列 |
| #3 C# 文件数 | "声称 45 / 实测 48 → 口径不同" | 45 = `unity/Assets/Scripts` 口径，**精确一致**；48 是 `unity/**`（含 Editor） | 独立验证者 |
| #4 C# 行数 | "6846 vs 5890，疑含 native 侧计法" | 6846 = `Scripts` 口径**全行数**，`dd55342` 时精确复现；当前 HEAD = 6876；5890 是 `Get-Content` 伪影 | 独立验证者 + 本机 `ReadAllLines` 对照 |
| #5 工具脚本数 | "41 → 42，已过期 +1" | **41 个脚本精确成立**（37 `.mjs` + 4 `.sh`）；1 个 `.md` 不是脚本（假过期） | 独立验证者 + 本机 `Group-Object Extension` |
| #7 `.meta` 数 | "106 → 110，已过期 +4" | 106 是**迁移包内**口径且精确可复现；仓库 110 属搬家后新增 | 独立验证者 + 本机复现 106 |
| #10 证据指针 | "见 §三 外链目录"（断链，§三无目录） | 写明真实路径 `C:\Users\qing_\.dsh\backup-20261003-122606\`（含 4 个结果 XML 与通过数） | 独立验证者 |
| §二 `a372ef8` | 注"（已在 origin）"（自相矛盾） | 它**不在** origin，是 4 个未推提交之一（`merge-base --is-ancestor` = 假） | 独立验证者 + 本机复核 |
| §二 `78fc999` | "24 个 `ProjectSettings/*.asset`" | **新增 21 个**；24 = 提交后**被跟踪总数**（另 3 个来自更早提交） | 独立验证者 + 本机 `git show --name-only` |
| §〇（新增） | 未区分两个工作副本 | 明确"真源在 D:、Unity 真跑发生在 C: 副本"，并解释 #10/#11 证据为何不在 D: | 独立验证者（漏报项） |

> 独立验证方式：只读 subagent，用 read/grep/glob + 只读命令独立复核 16 条对照、N1~N7、
> 4 个提交内容与全部引用行号，**未修改任何文件**（其结论与本机复核在上述 9 处一致）。
