# 交付物说明（方案文档 · APK · 中间产物）

> 本文件解释包里每一个"给人看/给人装"的产物：**它是什么、能不能用、怎么自己核验**。
> 所有版本号与包名都是**从文件内部读出来的**（`aapt2 dump badging`），**不是照文件名抄的**——
> 因为实测发现**文件名与包内版本号不一致**（见第二节红字）。

---

## 一、方案文档（6 份，位于 `方案原文/`）

| 文件 | 体积 | 定位 | 权威性 |
|---|---|---|---|
| **`恐怖整合.pdf`** | 1.4 MB | **V9.0 终稿（五版整合）** | ★★★ **唯一权威基线**。冲突一律以它的 §3「冲突裁决终表」为准 |
| `恐怖多人联机游戏开发方案-Android版-V5.pdf` | 1.0 MB | V5 原始案（Android 版） | 归档。V9 已吸收，保留用于追溯"某个要求最早是谁提的" |
| `6abf455d…-V6.0-深度评审与全量完善版.pdf` | 1.5 MB | V6 深度评审 | 归档。含对 V5 的评审意见 |
| `恐怖多人联机游戏开发方案-V7.0-深度分析与后端设计全量完善版.pdf` | 1.5 MB | V7 后端设计完善 | 归档。§15 后端六能力的来源 |
| `恐怖多人联机游戏开发方案-V8.0-三版综合评审与混元推荐.pdf` | 1.5 MB | V8 三版综合评审 | 归档。**V9 §3 裁决表的直接来源** |
| `恐怖多人联机游戏开发方案-Android版-V5.docx` | 0.3 MB | V5 的可编辑原稿 | 归档（`.docx` 只此一份，其余都是 PDF） |

**已提取的全文文本**（便于检索与门禁对账）：`whisper/docs/spec/V5-fulltext.txt` ~ `V9-fulltext.txt`
（V9 全文 1209 行；`LEDGER.md` 是五版要求台账：399 章 / 318 条承重行）

**怎么用**：要确认"某个数值/流程到底该是什么"，**只查 `恐怖整合.pdf`（V9）**。
V5~V8 只在需要追溯设计演进时查。**不要**拿 V5~V8 的数值去改代码。

---

## 二、APK（3 个，位于 `交付物/`）

### 2.1 三个包分别是什么

| 文件 | 体积 | 包名（内部） | 包内版本（内部） | 是什么 |
|---|---|---|---|---|
| `whisper-unity-0.1.20.apk` | **25.0 MB** | `com.whisper.projectwhisper` | **`0.1.1`** ⚠️ | **Unity 6 真·主线**：IL2CPP + arm64，含关卡几何、玩家控制、三怪 |
| `whisper-graybox-0.7.5-dev.apk` | 85 KB | `com.whisper.graybox` | **`0.6.0`** ⚠️ | 灰盒版（WebView + 自研 WebGL2 光栅器），**逻辑旁路验证台** |
| `whisper-graybox-0.6.0.apk` | 85 KB | `com.whisper.graybox` | `0.6.0` | 同上，较早的一次构建 |

### 2.2 ⚠️ 已核实的三处不一致（**别信文件名，信包内**）

用 `aapt2 dump badging` 从 APK **内部**读出的事实：

| # | 现象 | 证据 | 影响 |
|---|---|---|---|
| **1** | **文件名 `0.1.20` 但包内是 `0.1.1`** | `versionCode='1' versionName='0.1.1'` | **根源**：`BuildConfigurator` 从 CI 的 `GITHUB_RUN_NUMBER` 派生版本号，但在 GitHub Actions 里**读到的是空值** → 每次都回落成 `0.1.1/1`。所以**文件名是唯一的版本区分**（靠我手动改名），包内版本号恒为 0.1.1。影响：按版本排障失效；上架需单调递增的 `versionCode`。**未修**（质检第 2 轮 N1） |
| **2** | **文件名 `0.7.5-dev` 但包内是 `0.6.0`** | 两个灰盒包内部都是 `versionCode='6' versionName='0.6.0'` | 灰盒构建脚本**没有更新版本号**。但两个文件**确实不同**（见下），所以不是重复文件 |
| **3** | **App 名不一致** | 灰盒 `application-label='低语计划'`；Unity `'Project Whisper'` | 品牌不统一。Unity 侧由 `BuildConfigurator.ProductName` 决定，灰盒侧另有一套 |

**两个灰盒包确实不同**（不是同一个文件换名字）：
| 项 | 0.6.0 | 0.7.5-dev |
|---|---|---|
| `assets/web/game.js` | 159,378 B | **168,073 B**（+8.7 KB，有真实代码改动） |
| `assets/web/index.html` | 34,267 B | 34,545 B |
| md5 | `bc9917ce…` | `19ee6be1…` |
| 内嵌关卡 | 13 房间 / 13 门 / 5 证据 / 12 道具 | **同上（关卡未变）** |
| 内嵌 buildStamp | `apk-2026-10-02 10:32` | **同上** |

### 2.3 该装哪个

| 你的目的 | 装这个 | 说明 |
|---|---|---|
| **看项目真身**（Unity 主线） | `whisper-unity-0.1.20.apk` | 已实测：能启动（`BOOT OK · 17ms`）· 能渲染出场景（像素判据通过）· 有玩家控制与三怪。**但视角/手感/暗调的最新修正不在此包**（那在 CI 的 `0.1.23`，尚未下载） |
| **验证机制与数值**（不吃性能） | `whisper-graybox-0.7.5-dev.apk` | 灰盒版可玩：13 房间全连通 / 5 证据点全可达 / 启动冒烟通过。**它是旁路验证台，不是产品** |
| 对照回归 | `whisper-graybox-0.6.0.apk` | 留作对照 |

> **注意**：两个灰盒包与 Unity 包**可以共存**（包名不同），但**不要同时开**——
> 它们会争抢音频/焦点。另外，若你之前装过早期黑屏版（`com.DefaultCompany.unity`），
> 那是**另一个包名**，请卸载掉以免混淆（我已在手机上卸载过）。

### 2.4 怎么自己核验（一条命令）

```bash
# 用仓库内自带的 aapt2 读包内真实信息
bash whisper/native/android-tools/aapt2.sh dump badging whisper-unity-0.1.20.apk | head -3
# 期望看到：
#   package: name='com.whisper.projectwhisper' versionCode='1' versionName='0.1.1' ...
#   application-label:'Project Whisper'
```
> 本仓库的 `tools/verify-apk-on-device.sh` 就是用这套判据做真机验收的
> （装包 → 包名核对 → logcat → 逐帧截屏 → 像素判据，**每条判据都参与退出码**）。

### 2.5 Unity 包的技术规模（来自 CI 构建产物，非估算）

```
libil2cpp.so 62 MB · libunity.so 19 MB · classes.dex 6.4 MB · 总体 25.0 MB
包内已核实：asylum_v1 关卡 · entrance_safe 房间 · stimulusSources 配置 · Whisper 程序集
```
> 注：**5 个套件 GLB 并不在包里**——它们已生成且过门禁，但没有任何代码引用，
> 所以 Unity 打包时没有包含（`sharedassets0.assets` 里搜不到套件名）。
> 关卡几何是**运行时用代码生成的方块**（V9 §19.1 C1「场景零手工」）。

---

## 三、开发中间产物（`whisper/native/*/out/` 里的 APK，共 5 个）

这些**不是给玩家用的**，是开发过程的对照组，保留用于回溯：

| 文件 | 是什么 |
|---|---|
| `native/micprobe/out/{base,unsigned,aligned,contract-mirror}.apk` | 早期"能否在手机端出包"的探针实验（4 个，各几~几十 KB） |
| `native/micprobe/out2/{base,signed}.apk` | 同上第二轮（含签名验证） |
| `native/deliver/whisper-contract-mirror-0.1.0.apk` | 契约镜像包（用于验证 manifest 契约，非游戏） |
| `native/graybox-apk/out/*.apk` | **灰盒构建链的原始产物**（`交付物/` 里的两个灰盒包就是从这拷出去的） |

**建议**：这些可以直接忽略。真正要看的只有 `交付物/` 下那 3 个。

---

## 四、一张表看懂"这个包里到底有什么能跑"

| 类别 | 能不能跑 | 备注 |
|---|---|---|
| **Unity APK**（25 MB） | ✅ 真机可跑 | 主线产品，已验证启动 + 渲染 |
| **灰盒 APK**（2×85 KB） | ✅ 真机可跑 | 逻辑验证台（WebView），非产品 |
| 方案 PDF（6 份） | — | 文档。**只信 V9（恐怖整合.pdf）** |
| `whisper/` 源码 + 工具 | ✅ 可跑 | 需装 git + .NET 8；`bash unity-check.sh` 期望 exit 0 · 156 断言 |
| CI 配置（`.github/workflows/`） | ✅ 可跑 | 推送即出包（一次 25~45 分钟） |
| `engine-extensions/`（引擎包内） | ❌ **电脑上跑不了** | aarch64+bionic 的 Android 二进制，留给档；电脑端用扩展中心原生装 |
| `native-dotnet/`（引擎包内） | ❌ 同上 | Android/arm64 版 .NET 8 |
