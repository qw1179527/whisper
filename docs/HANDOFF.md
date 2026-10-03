# Project Whisper · 完整交接文档

> 最后更新：2026-10-03 19:35（提交 `dd55342` · 58 次提交）
> **用途：上下文压缩后仅凭本文件即可继续工作。** 所有结论都标注了验证方式与证据；不确定的写"未验证"，不写猜测。
> 纪律：**不掩饰缺口**；**「构建成功」≠「能玩」**；一切结论附可复核证据。

---

## 〇、一句话现状

真机**能启动、能看见几何、能走、有怪物**（历史上第一次），但**自由视角与暗调配色的修正版还没验过**；
CI 出包链稳定（#19/#20/#21/#22 全 success）。本机门禁 **exit 0 · 156 条断言 0 失败**。

**当前第一优先**：CI #23（`dd55342`）出包后跑真机验收，确认自由视角可转、画面变暗。

---

## 一、项目是什么

**Project Whisper（低语计划）**：Unity 6 的 Android 合作恐怖游戏（语音驱动机制，四人一房）。

- **方案基线**：`/storage/emulated/0/DSH专用/恐怖整合.pdf`（V9.0 终稿，五版整合）
  - 全文提取：`docs/spec/V9-fulltext.txt`（1209 行）· V5~V8 归档于同目录
  - 冲突以 V9 §3「冲突裁决终表」为准
- **仓库**：`$HOME/whisper`（本地分支 **master** → 远端 **origin/main**，私有）
- **远端**：`github.com/qw1179527/whisper`
- **开发环境**：Android 手机 + DSH（bionic libc）· **本机无 Unity 引擎** → 出包走 GitHub Actions
- **规模**：45 个 C# / 6846 行 · 3 个 Editor 脚本 · 10 个 asmdef · 41 个工具脚本

---

## 二、真机验收实测结果（三版对比，全部有证据）

设备 **RMX5062 · Android 16 (API 36) · 中国移动**。工具：`tools/verify-apk-on-device.sh`（Shizuku 驱动）

| 版本 | 提交 | 安装/包名 | boot | 进程 | **像素判据** | 结论 |
|---|---|---|---|---|---|---|
| 0.1.0 | `11b4fa1` | ✅ 但包名 `com.DefaultCompany.unity` | — | — | 全黑 | 首包，黑屏 |
| 0.1.19 | `fba2bc1` | ✅ `com.whisper.projectwhisper` | ✅ BOOT OK 23ms | ✅ pid 稳定 | ✗ 纯色块（亮度 31 · 标准差 **0.0** · 边缘 0%） | 黑屏根因已修，但画面纯色 |
| **0.1.20** | `b7ad671` | ✅ 同上 | ✅ BOOT OK **17ms** | ✅ pid 稳定 | ✅ **有场景内容**（亮度 213 · 标准差 **28.6** · 颜色 **25 种** · 边缘 **1.8%**） | **首次真正渲染出 3D 场景** |

**0.1.20 的实测截屏内容（已人工看图确认）**：
```
Project Whisper · 运行中
Tick 1722 · 60 fps · tickRate=60
接口: INetService 已注入 · IVoiceService 已注入 · IBackendService 已注入（无后端模式，§15.2）
关卡 asylum_v1: 房间 11 · 走廊 10
几何着色器 ✓ · 相机 ✓ · Boot 17 ms
```
3D 视图内可见：**走廊纵深、墙体透视、地板、以及中间的门洞**（相机按"朝最近的门口看"摆放）。

**0.1.20 遗留问题（用户真机反馈 + 我的实测）**：
| 反馈 | 判定 | 状态 |
|---|---|---|
| 视角不能转动 | **真缺口**：只做了"朝向跟随移动方向"，没有转头操作 | ✅ 已修（`dd55342`，待验） |
| 不能走 | 预期内：0.1.20 不含玩家控制器（在 `2bacd43`） | ✅ 0.1.21+ 已含 |
| 氛围不如灰盒、好亮 | 有效：安全区墙 **207/255**、整屏平均 **213/255** | ✅ 已修（`dd55342`，待验） |

---

## 三、许可证这条路（别再重复趟）

| 步骤 | 结论 |
|---|---|
| Unity 官方发布索引 | Linux 编辑器**只有 X86_64**（6000.3.25f1，4.2GB） |
| 本机架构 | `aarch64`，无 arm64 编辑器 |
| `download.unity3d.com` | 对本区域 **404** |
| `id.unity.com` | 对本机网络**超时不可达**（15s） |
| 个人版激活方式 | **只能通过 Unity Hub**（官方手册明确） |
| 命令行 `-username/-password` | 能登录（返回 access token）但 **0 entitlements** |
| 电脑 Hub 生成的 `.ulf` | 含 MachineBindings，**CI 上无效** |
| **最终解法** | `.ulf` 的 `DeveloperData` base64 解码得**明文序列号**，用 `UNITY_SERIAL` 激活 ✅ |

**已配 Secrets（4 个）**：`UNITY_EMAIL` · `UNITY_PASSWORD` · `UNITY_SERIAL` · `UNITY_LICENSE`

---

## 四、真实事故清单（每条都有实证，别重犯）

| # | 事故 | 根因 | 现在怎么守 |
|---|---|---|---|
| 1 | **真机黑屏**（首包） | `Shader.Find("Standard")` 被剥离→null→`new Material(null)` 抛异常；且场景无相机、`Text.font` 为 null | 着色器作为资产放 `Resources/`（无条件进包）；相机与字体代码创建；`gate-test` **T6** 盯住 |
| 2 | **CI 构建失败**（#18） | 我**臆造 Unity API**（写 `UnityEditor.SplashScreen`，真名是 `PlayerSettings.SplashScreen`）；而我手写的 Unity 桩**替这个错误背书** → 本机假绿 | `data/unity-api-registry.json` + `gate-editor-api.mjs`：每个 Editor Unity API 必须有官方文档出处 |
| 3 | 缺 96 个 `.meta` | Unity 不把 .cs 归入 asmdef → 181 条 CS0234 | `tools/gen-meta.mjs`（确定性 GUID）+ 门禁 |
| 4 | asmdef 引用用对象形式 | `{name:...}` 被 Unity **静默忽略** | 改字符串数组；**生成器与文件必须同步**（曾漂移导致一键链第 6 步长期判红、后 15 步被掩盖） |
| 5 | **`set -euo pipefail` 掩盖后续步骤** | 第 3 步一红，第 4~21 步从不执行 | 每次改动后跑**完整** `unity-check.sh` 并确认 exit 0 与步数 |
| 6 | **四处假绿**（质检第 1 轮） | ① `WireFormat` 三类型缺 `<summary>` ② `gate-physics` P2 判据写成 C# 里不存在的 `Math.Random` ③ 真机脚本 V5/V6 只打印不参与退出码 ④ `StimulusSize` 常量写 10 实际 14 | 全部修掉；**判据必须参与退出码**，常量必须与**实测字节**一致 |
| 7 | 我的**测试本身假绿** | 斜向归一化断言从 `(0,0)` 出发——那是入口房间中心、**被柜子占着**，两个操作数都是 0 却因 `0==0` 通过 | 断言里加"位移必须非零"的拦停 |
| 8 | **CS0103 被允许表放过** | 允许表为容忍 Unity 静态成员而放行 CS0103，副作用是**同文件作用域错误一起放过**（`spawnX` 声明在 try 块内、块外引用） | 加判据：**若缺失名字在本文件被声明过 → 真错误**；已注入验证 |
| 9 | **门框过曝** | `LightZoneColor * 1.3f` 在浅色上必然截顶：bone 216×1.3 = 280.8 → 255,255,243 | 改用**向墨色混合**；配色算式搬进纯逻辑 `LevelPalette`，使"不溢出"可断言 |
| 10 | **C5 规模纪律判红** | `Boot()` 长到 135 行（上限 120） | 拆为编排 + 8 个阶段方法 |

---

## 五、门禁体系（六项主门禁 + 两个补充）

| 门禁 | 工具 | 规模 | 注入验证 |
|---|---|---|---|
| 建模 | `tools/gate-model.mjs` | M1~M11 | 4 种注入全判红 |
| 物理规则 | `tools/gate-physics.mjs` | P0~P4 | **P2 已修**（正常绿 + `--inject-random` 判红）；**P1 注入仍是伪造的**（见 G7） |
| 代码质量 | `tools/gate-code.mjs` | C1~C7 | 6 种注入全判红 |
| 功能测试 | `tools/gate-test.mjs` | T1~T3 + **T6 启动关键契约** | 注入必失败断言 → 判红 |
| 资产几何 | `tools/gate-asset-bbox.mjs` | B1~B3 | 2 种注入判红 |
| **Editor API 出处** | `tools/gate-editor-api.mjs` | 35 处引用 / 36 项台账 | 新增（防臆造 API） |
| 程序集编译 | `native/asmdef-check/build.sh` | 逐 ASMDEF | — |
| 接口一致性 | `native/asmdef-check/iface-check.mjs` | 3 接口 | — |

**一键链**：`bash unity-check.sh` → 全部步骤 · **exit 0** · **156 条本机断言 0 失败**
（2026-10-03 19:30 实测；`gate-model 11/0` · `gate-physics 4/0` · `gate-code 7/0` · `gate-test 4/0` · `gate-bbox 1/0`）

---

## 六、联机方案（含真机实测结论 + 已完成部分）

### 实测（本机 / 中国移动）
- **IPv4 侧是对称 NAT**：三个 STUN 服务器给出同 IP、**三个不同端口**（:4339/:4201/:4858）→ **UDP 打洞不可行**
- **IPv6 侧全局可路由且端口守恒**：绑定 `[2409:...]:38000`，三个 STUN 服务器看到的都是**同一地址同一端口**
  → **端到端无 NAT，拿地址就能直连**

### 免费路线判定
| 方案 | 判定 |
|---|---|
| Photon Fusion 免费档 | ⚠️ 100 CCU 硬顶，**付费后不可降回**（单向门）→ 排除 |
| Unity Relay | ⚠️ UGS 要绑支付方式 → 排除 |
| **IPv6 直连 + 房间码内嵌地址** | ✅ 已实现房间码与线格式，**零服务器/零账号/零绑卡** |
| EOS | ✅ 真免费（无 CCU 上限），但要集成工作量 |
| 同 WiFi 广播发现 | ✅ 免操作 |

### 已完成（有断言）
- `Net/RoomCode.cs`：把主机地址与端口编进房间码。**IPv6 码 31 字符 / IPv4 码 12 字符**，
  base32（只有大写字母与数字 2-7，可手抄口述）；容忍聊天软件带来的空白与零宽字符；脏输入返回 false 不抛异常。
- `Net/WireFormat.cs`：V9 §13.4 首次有可核对的字节数。
  ```
  满房 4 人 + 4 理智 + 6 道具变更 = 68 字节 / 预算 100 字节（上行 6KB/s ÷ 20 批/s）
  容量上限：每批最多 16 条道具/门变更
  尺寸实测：空批固定 10 · 头 5 · 玩家 8 · 道具 3 · 理智 2 · 声纹 14
  ```

### 未完成（`零成本联机` 大类的核心）
- **真正的 UDP 传输层**（`UdpV6NetService`）与 `INetService` 实现 —— 房间码与线格式已就绪，但**还没发包**
- `INetService` 尚缺"本地玩家输入上行"的口子（现在 `UpsertPlayer` 是桩的驱动接口，不在契约里）

**硬边界**：对面若没有 IPv6，免费方案连不上（只能同 WiFi，或用电脑开一次中继）。

---

## 七、代码与工具地图

```
whisper/
├── unity/Assets/
│   ├── Scripts/
│   │   ├── Core/Contracts/         三接口 + 四类同步对象（MatchState/StimulusEvent/TokenBundle）
│   │   ├── Core/DesignTokens.cs    41 个设计常量（V9 §11，由生成器产出）
│   │   ├── Gameplay/
│   │   │   ├── Level/              LevelData/Loader/Geometry/Builder/Assembly/**LevelPalette**(纯逻辑配色)
│   │   │   ├── Voice/              VoiceCalibrator(149) + VoiceBandClassifier(299)
│   │   │   ├── Hearing/            听觉判定（阈值/半径衰减）
│   │   │   ├── Monsters/           MonsterBrain(状态机) + **MonsterDirector**(总控·本机可断言)
│   │   │   ├── Sanity/ Items/ Extraction/ Hud/ Session/
│   │   │   └── Session/            GameSession + **PlayerMotion**(纯逻辑移动/视角)
│   │   ├── Net/                    LocalNetService(桩) + RoomCode + WireFormat
│   │   ├── Audio|Backend/          Local*Service 桩（SDK 唯一槽位）
│   │   ├── Runtime/                GameBootstrap(组合根) + PlayerController + MonsterViews
│   │   ├── Editor/                 BuildScript + BuildConfigurator + EditorSceneBootstrap
│   │   └── Tests/                  EditMode×3 + PlayMode×1（**从未执行过**）
│   └── Resources/
│       ├── Data/{config,asset-manifest}.json    数值与资产唯一真源
│       ├── Levels/asylum_v1.json                关卡 DSL
│       └── Shaders/WhisperUnlitColor.shader     ← 无条件进包（黑屏修复的关键）
├── native/
│   ├── csharp-verify/     156 条断言的跑手（本机真编译真跑）
│   ├── asmdef-check/      逐程序集编译 + 接口一致性
│   ├── unity-stubs/       Unity 桩（⚠️ 桩会为臆造 API 背书，见事故 #2）
│   ├── unity-syntax/      Roslyn 语义检查（CS0103 判据见事故 #8）
│   └── graybox-apk/       灰盒 APK 构建链
├── tools/                 41 个（门禁/生成器/校验/真机验收）
├── data/                  config 真源 + design-tokens + unity-api-registry
└── docs/                  HANDOFF(本文) + mechanism-gaps + spec/
```

### 常用命令

```bash
cd ~/whisper
bash unity-check.sh                    # 一键链（务必确认 exit 0 且**全部步骤都跑了**）
node tools/gate-test.mjs               # 功能测试门禁（含 T6 启动关键契约）
node tools/gate-physics.mjs --inject-random   # 门禁自检：注入后必须判红
bash tools/unity-syntax-check.sh       # Roslyn 语义检查（含 Assets/Editor）
node tools/gen-meta.mjs [--check]      # 生成/校验 Unity .meta
tools/git.sh <子命令>                   # 本机 git（已封装 GIT_EXEC_PATH + GIT_SSL_CAINFO）
tools/git.sh push origin master:main   # 推送（本地 master → 远端 main）

# 真机验收（需 Shizuku；约 90 秒）
bash tools/verify-apk-on-device.sh "/storage/emulated/0/DSH专用/whisper-unity-0.1.N.apk" 25
```

---

## 八、当前任务状态（分级计划 + 未完成清单）

### 分级计划（本会话）
- **L1 已锁定**（5 大类）：`真机体验修复` · `零成本联机` · `质检打卡台账` · `建模资产` · `出包与真机验收`
- **L2 未编写**（下一步就是给这 5 类分化小类）
- 北极星：为【只能用手机开发、无 Unity 编辑器、出包靠 CI 的项目】在【真机此前只能黑屏/纯色、机制逻辑完备但都不在产品里的处境下】达成【真机可玩 + 可零成本联机开黑，且每轮都有可复核证据与质检台账】
- 会话模式：**experience**（观感/手感为先，但建模与代码产物必须过 correct 门禁）
- 用户答复要点：「打卡」= **质检审计轮次，每轮要有台账**；质检常驻 = **每完成一项主动唤醒一轮**；
  **先完成联机制作，建模按进度随后**；收口 = **持续出包直到真机能玩，新问题立即修**

### P0 —— 阻塞"能玩"
| 项 | 说明 |
|---|---|
| **0.1.23 真机复验** | 自由视角可转 + 画面变暗（`dd55342` 已推，CI #23 出包后验） |
| 玩家控制器已实现但**未真机验证** | 摇杆/视角手感、**Input System 风险**（见下） |
| 怪物已实例化但**未真机验证** | 三怪是否真的在场景里巡逻、追击表现 |

### P1 —— 玩法闭环
| 项 | 说明 |
|---|---|
| 交互层 | 证据拾取（0.9m）/ 电闸（1.6m）/ 道具使用 —— 玩家还**不能与任何东西交互** |
| 对局闭环 | 撤离双点 + 保护期 + 狂暴窗口 → 结算页 |
| 理智系统接入 | 逻辑完备（有断言），但**没接进产品**（与几何层、怪物层同样的失效模式） |
| HUD 实渲染 | `HudModel` 已渲染；`HudBuilder` 未接真 uGUI |
| 柜子 | `收殓人 ignoresLockers=true` 目前**无可生效对象**（G12） |

### P2 —— 补齐 V9 承诺
| 项 | 状态 |
|---|---|
| **5 个套件 GLB** | ⚠️ 已生成且过门禁，但**没进 APK**（无代码引用）——真正的缺口是"打了一套没人用的资产" |
| 三怪外观 / 玩家道具 / 事件池视觉 / UI 图标 | 未做（**Blender MCP 已可用**，可产出） |
| C4 运行时烘焙占位（§19.1） | 未实现（C1/C2/C3 已落地） |
| 第 0 局引导 / 档案残页（§9） | 未做 |
| 设计系统接入 uGUI（§11） | Token 已生成，未接 UI |
| **关卡与方案不一致** | ⚠️ Unity 版 11 房间（safe/pressure/high-risk）vs 灰盒/方案 13 房间（safe/calm/pressure/deep/extraction）——**分区语义丢了一半**，是"氛围不如灰盒"的根因之一 |

### P3 —— 需外部条件
联机同步（方案已定 IPv6 直连，传输层未写）· 语音链路（Vivox 16.x，当前是桩）· 后端六能力（Firebase）· 合规十项

---

## 九、已知缺口（详见 `docs/mechanism-gaps.md`，编号 G1~G13）

**必须知道的几条**：
- **G6** 真机 3D 视图曾为纯色（0.1.20 已修复并有像素证据）
- **G7** `gate-physics` 的 **P1 注入仍是伪造的**：硬编码检测在 P0 且为**警告级不判红**，
  8 处真实硬编码永不阻断（`--inject-hardcode` 自报"该门禁不可信"）
- **G9** `gate-editor-api` 的出处台账**从不访问 URL**（只校验前缀），成员是否真在文档中未验证
- **G10** 装机链路两个坑（已写入脚本注释）：直接从 `/storage/emulated/0/` 装会失败（`fuse:s0`）；
  `shz` **不转发 stdin**
- **G11** **脚步可闻性口径缺失**：走/蹲脚步强度都是 8，**低于所有怪的最低阈值 10**
  ⇒ 只有跑会引怪，潜行退化成"一个按键开关"。V9 §7 只给了强度/半径、没给可闻性口径 → **待人类裁决**
- **G12** 怪物视线已实现（距离+沿途查墙），但"躲柜子"尚未存在，收殓人的差异化暂无可生效对象
- **G13** Blender MCP 已接入（23 工具），且更正了"套件已进包"的错误陈述

**质检第 2 轮指出、尚未修完的**：
- ⚠️ **CI 版本号静默回落**：`BuildConfigurator` 从 `GITHUB_RUN_NUMBER` 派生版本，但 CI 里**读不到该变量**
  → 实测 APK 内部恒为 `versionCode=1 / versionName=0.1.1`（aapt2 证据），文件名 `0.1.19` 是**假区分**
- ⚠️ 3 条断言缺"位移非零"拦停（撞墙 / 大位移 / dt clamp），且 ★撞墙判据**只查中心格、不查半径**
- ⚠️ 脚步断言的 `expect` 由被测常量自算 + ±1 容差 → **步幅错 33% 也发现不了**
- ⚠️ `gate-editor-api` **只扫 `Assets/Editor`**，`Runtime/` 新增的 Unity API 无出处门禁（与事故 #2 同源）
- ⚠️ `EyeHeightM = 1.7f` 有**三份拷贝**（`PlayerController.cs` / `GameBootstrap.cs` ×2）
- ⚠️ **Input System 风险**：`Packages/manifest.json` 有 `com.unity.inputsystem`，但无 `ProjectSettings.asset`，
  `activeInputHandler` 由 CI 默认生成。若是"新输入系统 Only"，`Input.touchCount` 恒 0 → **摇杆完全不动**
  （本机桩恒返回 0，所以本机永远绿）。**下次真机第一项就要确认摇杆能不能动。**

---

## 十、安全事项

**本会话对话记录里出现过明文凭据**（会话缓存 `$HOME/storages/session_projcache/sessions/*.json`）：

| 凭据 | 状态 |
|---|---|
| `ghp_PILk…`（经典） | 已失效 |
| `github_pat_11CQ…`（细粒度，**当前在用**，需 Contents 读写） | **用完请撤销** |
| Unity 账号密码 | **建议改密** |

> 新令牌只放环境变量或 Secrets，**不要再粘进对话**。

---

## 十一、关键环境事实（避免重复探索）

| 事实 | 值 |
|---|---|
| 本机 libc / 架构 | **bionic**（非 glibc）· `aarch64` · Android 16 (API 36) |
| 存储 / 内存 | 约 282 GB / 约 6 GB |
| 无 Unity 引擎 | 出包必须走 GitHub Actions（**一次约 25~45 分钟**） |
| 产物下载 | 实测一次约 **16 分钟**（比构建还慢，是迭代节奏的实际瓶颈） |
| Shizuku（`shz`） | **可用** → `pm install` / `logcat` / `screencap` / `pm list` |
| 装机坑 1 | 直接从 `/storage/emulated/0/` 装会失败：`System server has no access to read file context u:object_r:fuse:s0` |
| 装机坑 2 | `shz` **不转发 stdin**；本进程写不进 `/data/local/tmp` |
| 装机正解 | `shz "cp <共享存储路径> /data/local/tmp/x.apk"` → 再 `pm install -r` |
| 无 rsync | 用 `tar --exclude` |
| toybox grep | 不支持 `\s`/`\b`，用 `rg` |
| `/tmp` 不可写 | 用 `$HOME/tmp` |
| git 需两个环境变量 | `GIT_EXEC_PATH` + `GIT_SSL_CAINFO`（`tools/git.sh` 已封装） |
| 推送 | `tools/git.sh push origin master:main`（本地 master，远端 main） |
| CI 触发路径 | `.github/workflows/unity-android.yml` 只在 `unity/**` 或该 workflow 变更时触发 |
| **Blender MCP** | `http://192.168.1.17:8765/mcp` · Blender **5.0.1** · 23 个辅助工具全 available · 工作目录 `/storage/emulated/0/DSH专用/DSH文件`（**注意：与仓库是两个位置，需拷贝**） |
| MCP 沙箱限制 | Blender 只能读写其工作目录；渲染/建模经局域网到电脑，**电脑需开着** |

---

## 十二、接续时怎么开工

```bash
cd ~/whisper
tools/git.sh log --oneline -12           # 看最近改了什么
bash unity-check.sh                      # 期望：全步骤 · exit 0 · 156 断言 0 失败
```
**若不全绿 → 先把红的那步修掉，不要带病往下做。**
（特别注意 `set -euo pipefail`：第一步失败会掩盖后续步骤，必须确认**全部步骤都跑了**。）

### 然后按这个顺序
1. **CI #23 真机复验**（`dd55342`）：
   ```bash
   bash tools/verify-apk-on-device.sh "/storage/emulated/0/DSH专用/whisper-unity-0.1.23.apk" 25
   ```
   判据：V1~V6 全绿 + **人工看图**确认（a）画面明显变暗（b）能在截屏里分辨出墙体/地板/门洞。
2. **确认摇杆与视角真的能动**（Input System 风险，见 G 清单）——这是 0.1.23 的**头号未知**
3. **补质检第 2 轮列出的残余**（CI 版本号、3 条拦停断言、半径判据、EyeHeightM 单一真源）
4. 继续 **L2 小类编写**（分级计划下一步）

### 每完成一项要做的事（用户要求）
- **唤醒质检子代理跑一轮**（`send_message` 给 `df093853-1420-47c3-929c-0a526ad71aa1`，
  提示它只读、不开子代理、结论必须带 `文件:行号` 或命令输出）
- **把该轮写进质检台账**（`docs/qa-ledger.md`，见「质检打卡台账」大类）
- 提交 + 推送（`tools/git.sh push origin master:main`），否则 CI 用的是旧代码

---

## 十三、交接时最容易踩的三个坑（用血换的）

1. **"逻辑完备"≠"在产品里"** —— 本项目**四次**栽在这里：几何层、内容管线（套件没进包）、
   怪物（状态机完备但没人实例化）、理智系统。**判定标准是"真机截屏里有没有它"，不是"断言绿不绿"。**
2. **自己写的桩会为臆造背书** —— 桩只能验证"内部一致性"，**永远无法验证"Unity 真的有这个成员"**。
   所以 Editor API 必须留官方文档出处（`gate-editor-api`）。
3. **"无法判定"绝不能当成"通过"** —— 像素格式不支持、判据没参与退出码、期待值由被测常量自算……
   这些都会产出"看着绿其实没验"的结果。**判据必须能失败。**
