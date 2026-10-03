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

---

## 十四、速查手册（Recipes：想做什么，敲什么）

```bash
cd ~/whisper

# ── 验证与门禁 ──
bash unity-check.sh                      # 一键链（21 步；务必确认 exit 0 且全部步骤都跑了）
node tools/gate-test.mjs                 # 功能测试门禁（含 T6 启动关键契约）
node tools/gate-physics.mjs              # 物理规则门禁
node tools/gate-physics.mjs --inject-random   # 门禁自检：注入必须判红（验门禁本身可信）
bash tools/unity-syntax-check.sh         # Roslyn 语义检查（含 Assets/Editor）
cd native/csharp-verify && bash ../../native/dotnet.sh run --nologo   # 156 条断言

# ── 生成与一致性 ──
node tools/gen-meta.mjs                  # 生成缺失的 Unity .meta
node tools/gen-meta.mjs --check          # 只校验（CI 用）
node tools/gen-asmdef.mjs --check        # asmdef 与 V9 §13.1 规则表一致
node tools/gen-design-tokens.mjs --check # DesignTokens 与 data/design-tokens.json 一致
node tools/data-mirror.mjs               # 真源镜像一致性

# ── 出包与真机 ──
tools/git.sh push origin master:main     # 推送即触发 CI（只有 unity/** 变更才触发）
GH_T=<token> node tools/gh/put-secret.mjs <owner/repo> <NAME> <value>   # 程序化写 Secrets
bash tools/verify-apk-on-device.sh "/storage/emulated/0/DSH专用/whisper-unity-0.1.N.apk" 25
node tools/shz-install.mjs <apk>         # 只装不验（注意：shz 不转发 stdin，此脚本可能不适用）

# ── 看真机日志（不经脚本）──
shz "logcat -d -b all -s Unity:V UnityPlayer:V GameActivity:V AndroidRuntime:E"
shz "screencap -p /sdcard/s.png" && cp /storage/emulated/0/s.png ~/tmp/s.png
shz "pidof com.whisper.projectwhisper"

# ── 迁移打包 ──
bash tools/make-migration-zip.sh         # 生成 DSH-MIGRATION-*.zip 到 /storage/emulated/0/DSH专用/
```

---

## 十五、数值真源速查（`data/config.json` = 唯一真相源，代码不得硬编码）

| 键 | 值 | 说明 |
|---|---|---|
| `network.tickRate` | 60 | 固定 60 Tick/s（V9 §13.4） |
| `network.batchEveryTicks` | 3 | 每 3 Tick（50ms）一批 → 20 批/秒 |
| `network.bandwidth.upKbps` / `downKbps` | 6 / 12 | 上行/下行预算（预算批 100 字节/600 字节） |
| `network.transformSendHz` | 10 | 玩家位姿发送频率 |
| `player.walkSpeedMps` / `runSpeedMps` / `crouchSpeedMps` | 3.5 / 5.6 / 1.6 | 三形态速度；**必须 ≥ 比值自洽**（跑≥走≥蹲） |
| `monsterBehavior.contactSanityLoss` | 35 | 接触损失理智（**正数=损失量**，消费方取负） |
| `monsterBehavior.lostContactSeconds` | 12 | 失去视线后转搜索的秒数 |
| `monsterBehavior.finalRageWindowBeforeExtractionSec` | 60 | 撤离前狂暴窗口 |
| `level.startGraceSeconds` | 20 | 开局保护期 |
| `level.matchSeconds` | [600, 900] | 对局时长区间 |
| `economy.formula.*` | evidence 200 / ally 150 / efficiency 100 / deepScale 1.3 | 残响碎片公式 |
| `sanity.max` | 100 | 理智上限；5 档区间不重叠 |
| `monsters.{stitcher,whisperer,coroner}` | 速度 3.6/4.2/3.2 · 听觉阈值 30/10/55 · 视野 14/6/20 | 三怪差异化（V9 §7 表7-2） |
| `stimulusSources.*` | 跑 52/15m · 走 8/8m · 蹲 8/3m · 喊叫 80/25m | 刺激源强度/半径 |
| `performanceGates.apkMaxMb` | 200 | 包体硬门禁（V9 §13.8） |
| `capacity.ccuHardCap` | 100 | 联机容量硬顶 |
| `voiceCalibration.targets.crossDeviceAgreement` | 0.9 | M0 验收硬指标 |

---

## 十六、工具清单（41 个，按用途分组）

| 组 | 工具 |
|---|---|
| **门禁（6）** | `gate-model` · `gate-physics` · `gate-code` · `gate-test` · `gate-asset-bbox` · `gate-editor-api` |
| **生成器（7）** | `gen-asmdef` · `gen-meta` · `gen-manifest` · `gen-design-tokens` · `gen-kits` · `gen-asylum-v1` · `tokens-map-check` |
| **校验（6）** | `arch-guard` · `config-lint` · `cs-lint` · `data-mirror` · `validate-assets` · `validate-levels` |
| **移植对拍（5）** | `compare-parity` · `compare-trajectory` · `voice-port-vectors` · `hearing-port-vectors` · `monster-port-vectors` |
| **灰盒提取（6）** | `extract-config` · `extract-modules` · `extract-spec` · `split-modules` · `spec-ledger` · `verify-sourcetree` |
| **构建/打包（4）** | `bundle-web` · `verify-bundle` · `make-migration-zip` · `version` |
| **真机（2）** | `shz-install` · `verify-apk-on-device` |
| **运行时探针（2）** | `nav-probe`（怪物导航仿真）· `smoke-run`（启动冒烟） |
| **契约/语法（3）** | `contract-diff` · `unity-syntax-check` · `git.sh` |

---

## 十七、排障表（症状 → 先怀疑什么 → 怎么处理）

| 症状 | 首先怀疑 | 处理 |
|---|---|---|
| 真机**全黑** | 着色器被剥离 / 场景无相机 / `Text.font` 为 null | 看 logcat 有无 `ArgumentNullException(shader)`；确认 `Resources/Shaders/*.shader` 在包内 |
| 真机**洋红屏** | 着色器编译失败 | 看 CI 构建日志的 Shader error；检查 pragma 与宏是否配对 |
| 真机**纯色块** | 相机贴墙 / 雾把画面洗白 / 几何没建出来 | 看 `BOOT OK` 行的房间/门/道具计数；用像素判据看标准差与边缘密度 |
| 真机**反复闪屏** | 崩溃重启循环（pid 会变） | `shz "pidof <pkg>"` 采三次看是否一致；`logcat -s AndroidRuntime:E` |
| **摇杆不动** | Input System（新/旧）不匹配 | `Packages/manifest.json` 有 `com.unity.inputsystem` 但无 `ProjectSettings.asset` → 需真机确认 `activeInputHandler` |
| 一键链**只跑到第 N 步** | `set -euo pipefail` 掩盖后续步骤 | 看日志尾部确认是否到 `[21/21]`；修掉红的那步再跑 |
| 门禁**自报"不可信"** | 判据写错（如把 C# 写成 `Math.Random`） | 用 `--inject-*` 自检；判据必须在注入后判红 |
| `.meta` 缺失导致 CI 报 CS0234 | Unity 不把 .cs 归入 asmdef | `node tools/gen-meta.mjs` 然后提交 |
| CI **构建失败**但本地绿 | 桩为臆造 API 背书 | 看 CI 日志的真实 error CS；查 `data/unity-api-registry.json` 有无出处 |
| 推送**403** | 令牌只读 | 细粒度令牌需 `Contents: Read and write` |
| `pm install` 失败 `fuse:s0` | 直接从共享存储装 | 先 `shz "cp <共享路径> /data/local/tmp/x.apk"` 再装 |
| 产物下载**很久** | GitHub artifact 慢（实测 ~16 分钟） | 属正常；用后台任务盯，别在前台等 |

---

## 十八、术语表

| 术语 | 含义 |
|---|---|
| **V9** | 方案终稿（`恐怖整合.pdf`），五版整合，冲突以其 §3 裁决表为准 |
| **灰盒版** | WebView + 自研 WebGL2 光栅器的旁路验证台（`baseline/index-*.html`），**不是** Unity 版 |
| **门禁** | 自动化判据（六项主门禁 + 两项补充），必须"注入验证有效 + 正常态通过"，不允许假绿 |
| **假绿** | 看着通过、实际没验（判据没参与、只比对常量、无法判定当成通过…）。本项目最提防的失效模式 |
| **T6 启动关键契约** | `gate-test` 里盯"着色器资产+相机+字体齐备"的判据（黑屏事故的产物） |
| **打卡** | 本会话语境 = **质检审计轮次**（用户定义），每轮要有台账 → `docs/qa-ledger.md` |
| **C1~C4** | V9 §19.1 的四条代码优先纪律（场景零手工 / UI 代码构建 / 资产清单驱动 / 运行时烘焙占位） |
| **四类同步对象** | V9 §13.4：①玩家位姿 ②声纹事件（瞬时 RPC，不走状态同步）③道具门状态 ④对局阶段 |
| **三层免参** | V9 §19.5：改数值不碰代码（配置 → 代码 → 场景三层） |
| **零信令房间码** | 把主机地址编进码里（`Net/RoomCode.cs`），从而不需要信令服务器 |

---

## 十九、决策记录（为什么这么选，以及代价）

| 决策 | 理由 | 代价 / 风险 |
|---|---|---|
| **出包走 GitHub Actions** | 本机无 Unity 引擎（aarch64 + bionic + 区域 CDN 404 + 内存不足，四项都实测过） | 一次迭代 25~45 分钟；产物下载另需 ~16 分钟 |
| **几何全部运行时用代码生成** | 无 Unity 编辑器 → 场景零手工（V9 §19.1 C1） | 无法用编辑器调美术；资产管线更难接通（套件至今没进包） |
| **自研 Unlit 着色器并放 `Resources/`** | 黑屏根因是内置着色器被剥离；`Resources/` 无条件进包 | 没有光照模型 → 观感偏平，靠配色乘数补偿 |
| **配色算式搬进纯逻辑 `LevelPalette`** | 门框 `×1.3` 截顶这类错误在 Unity 侧只看得到结果 | 多一层间接 |
| **联机走 IPv6 直连 + 房间码** | 实测 IPv4 对称 NAT 无法打洞；Photon 有硬顶、Unity Relay 要绑卡 | 对面没 IPv6 就连不上（只能同 WiFi） |
| **子代理只审计不改文件** | 保持独立性；审计与开发分离 | 子代理跑完即停，无法真常驻 → 需主动唤醒 |
| **`INetService` 作为唯一 SDK 槽位** | 换 SDK 只换实现，玩法代码不动（V9 §13.2） | 只遮挡 SDK，**不遮挡架构假设**（Host 迁移、批量发送仍要自己写） |
| **不提交手写 `ProjectSettings.asset`** | 手写 YAML 无对照物，写错整包构建失败 | 所有 PlayerSettings 必须用 `BuildConfigurator` 代码表达 |

---

## 二十、迁移到电脑端 DSH 的清单

**已打包进 `DSH-MIGRATION-*.zip`（核心包，可移植部分）**：
- `whisper/` 完整仓库（含 **58 次提交的 git 历史**、全部源码、41 个工具、docs、data 真源）
- DSH 配置与状态：`profiles/` · `external/` · `storages/`（已剔除可能残留凭据的 `session_projcache`）·
  `sessions/` · `AGENTS.md` · `graded-state` · `router-standard` · `super-injector`
- `方案原文/`：5 份 PDF + 已提取文本
- `README-MIGRATION.md`：电脑端的开工步骤

**刻意不打包（`DSH-MIGRATION-*-engines-arm64.zip` 单独放，或按需重装）**：
| 内容 | 体积 | 为什么不建议带 |
|---|---|---|
| `engine/extensions/*`（19 个） | **4.2 GB** | 全是 **aarch64 + bionic** 二进制，电脑上**跑不了**；应用电脑端 DSH 扩展中心原生安装 |
| `whisper/native/dotnet` | 472 MB | 同上（Android/arm64 版 .NET 8）；电脑端用 `native/fetch-dotnet.sh` 或系统 dotnet |

**电脑端开工顺序**（详见 `README-MIGRATION.md`）：
1. 解压 → 2. 用电脑端 DSH 的扩展中心装 `git`（必须）→ 3. `cd whisper && tools/git.sh log --oneline`
4. `bash native/fetch-dotnet.sh`（若要用本机断言）→ 5. `bash unity-check.sh` 期望 exit 0 · 156 断言
6. 配 4 个 GitHub Secrets（见 §3）→ 7. **强烈建议**：在电脑上装 Unity 6000.3.25f1，
   从此**不必再靠 CI 出包**（这是迁到电脑的最大收益：迭代从 45 分钟降到分钟级）

**电脑端的两个即刻收益**：
- **Unity 编辑器可用** → 能真机调试、能拖场景、能用 Profiler，出包不再依赖 CI
- **Blender 可本地跑** → 不需要再经局域网调 MCP（当前 Blender MCP 跑在你的电脑上，工作目录是
  `/storage/emulated/0/DSH专用/DSH文件`，与仓库是两个位置，需手工拷贝）

---

## 二十一、交付物清单与核验（详见 `docs/deliverables.md`）

### 方案文档（`方案原文/`）
| 文件 | 权威性 |
|---|---|
| **`恐怖整合.pdf`** | ★★★ **V9.0 终稿，唯一权威基线**（冲突以 §3 裁决表为准） |
| V5.pdf / V5.docx | 归档（追溯要求来源） |
| V6.0 / V7.0 / V8.0 PDF | 归档（V8 是 V9 §3 裁决表的直接来源） |
已提取全文：`docs/spec/V5-fulltext.txt` ~ `V9-fulltext.txt` + `LEDGER.md`（399 章 / 318 承重行）

### APK（`交付物/`）—— **别信文件名，信包内版本号**
| 文件 | 包内包名 / 版本 | 是什么 |
|---|---|---|
| `whisper-unity-0.1.20.apk`（25.0 MB） | `com.whisper.projectwhisper` / **`0.1.1`** ⚠️ | Unity 6 真·主线，已验启动+渲染 |
| `whisper-graybox-0.7.5-dev.apk`（85 KB） | `com.whisper.graybox` / **`0.6.0`** ⚠️ | 灰盒版（逻辑验证台，可玩） |
| `whisper-graybox-0.6.0.apk`（85 KB） | `com.whisper.graybox` / `0.6.0` | 较早构建，留作对照 |

**已核实的三处不一致**（用 `aapt2 dump badging` 从包内读出，非照文件名抄）：
1. **文件名 `0.1.20` ↔ 包内 `0.1.1`** —— 根源是 CI 里读不到 `GITHUB_RUN_NUMBER`，
   版本号每次静默回落（质检 N1，**未修**）。所以**文件名是唯一的版本区分**。
2. **文件名 `0.7.5-dev` ↔ 包内 `0.6.0`** —— 灰盒构建脚本没更新版本号；
   但两包确实不同（`game.js` 159,378 → 168,073 字节），关卡数据则完全相同。
3. **App 名不一致**：灰盒「低语计划」vs Unity「Project Whisper」。

**核验命令**：
```bash
bash whisper/native/android-tools/aapt2.sh dump badging whisper-unity-0.1.20.apk | head -3
```

**该装哪个**：看项目真身 → Unity 包（但**不含**最新的自由视角/暗调修正，那些在 CI `0.1.23`）；
验机制数值 → 灰盒 `0.7.5-dev`。两包可共存（包名不同），但别同时开（争音频/焦点）。

---
## 二十二、从这里接上（2026-10-03 19:40 收工点）

**收工状态**：仓库干净 · 全部已推送（`865f047`）· 本机门禁 exit 0 · 156 断言 0 失败 ·
迁移包 `DSH-MIGRATION-*-core.zip`（**70.8 MB** / 3110 条目 / `unzip -t` 无错误）已就绪。

**已实测验证**（打包后从包内 `.git` 直接跑）：
```
git log --oneline -5   → 60 次提交全部可读
git rev-list --count HEAD → 60
git show HEAD --stat   → 能取出内容（对象库完整）
```
包内含：55 个 `.cs` · 10 个 `.asmdef` · 106 个 `.meta` · 99 个 `.mjs` · 19 个 `.sh` ·
8 个 `.glb` · `.github/workflows/` · `baseline/index-0.6.0.html`（灰盒源码）· 完整 `.git`

**唯一在跑的事**：CI **#23**（`dd55342` = 自由视角 + 暗调配色 + 真机脚本判据修复），
出包后**第一件事就是真机验收**：

```bash
# ① 下载 artifact（whisper-android-apk）→ 存到 /storage/emulated/0/DSH专用/
#    用 GitHub 网页最省事：Actions → 该次运行 → 底部 Artifacts
# ② 装到真机并验收（约 90 秒）
bash tools/verify-apk-on-device.sh "/storage/emulated/0/DSH专用/whisper-unity-0.1.23.apk" 25
# ③ 人工看图确认两件事（脚本判不出"好不好看"）
#    (a) 画面明显变暗（安全区墙应从 207/255 降到约 93）
#    (b) 能在截屏里分辨出墙体 / 地板 / 门洞
```

**验收时必查的头号未知**：**摇杆与视角到底能不能动**。
`Packages/manifest.json` 里有 `com.unity.inputsystem`，但仓库无 `ProjectSettings.asset`，
`activeInputHandler` 由 CI 默认生成 —— 若是"新输入系统 Only"，`Input.touchCount` 恒为 0，
**摇杆完全不动**，而本机 Unity 桩恒返回 0，所以**本机永远绿**。（质检第 2 轮 N5）

**之后按这个顺序**：
1. 补质检第 2 轮残余：CI 版本号静默回落（N1）· 3 条断言缺"位移非零"拦停（N2/N3）·
   脚步断言判别力≈0（N4）· `gate-editor-api` 未覆盖 Runtime（N6）· `EyeHeightM` 三份拷贝（N7）
2. 继续分级计划的 **L2 小类编写**（L1 已锁定 5 大类：真机体验修复 / 零成本联机 / 质检打卡台账 / 建模资产 / 出包与真机验收）
3. **零成本联机**（用户指定优先）：`RoomCode` 与 `WireFormat` 已就绪且有断言，
   **缺的是真发包的 UDP 传输层**（`UdpV6NetService`）+ `INetService` 的输入端上行口子
4. 建模（Blender MCP 已可用，23 工具；工作目录 `/storage/emulated/0/DSH专用/DSH文件`，与仓库是两个位置需拷贝）

---

## 二十三、⚠️ 大陆 Unity 6 可用性（2026-10-03 核实，影响"迁到电脑装编辑器"这条路）

### 事实（有证据）
中国区 **unity.cn 只提供「团结引擎」，内核是 Unity 2022.3 LTS**。证据：官方发布页的
`hubDeepLink` 字段直接暴露内核版本 —— `tuanjiehub://2022.3.62t16/…`、`2022.3.61t14`、
`2022.3.62t15`…，整页**没有一处 `6000.x`**。
（报道见 [团结引擎承接 Unity6 国内断供](https://picimos.com/news/Ne1207230373218619392)、
[海外商店停止对中国内地及港澳服务](https://www.stcn.com/article/detail/3660738.html)、
[团结引擎 2.0 发布](https://m.ithome.com/html/982616.htm)）

### 对本工程的影响：**团结引擎打不开本工程**
本工程 `ProjectVersion.txt` = **`6000.3.25f1`**；Unity **拒绝用更低版本打开更高版本的工程**。
硬改 `ProjectVersion.txt` 强开的话，这些都要动：
`URP 17.0.3→14.x` · `ugui 2.0.0→1.0.0` · `Addressables` · `InputSystem` · `AndroidApiLevel36→35`（很可能），
**并且会违反 V9 §12 的 16KB 页对齐硬要求**（`dependency-lock.json` 明记
`"reason": "16KB 页对齐要求 Unity ≥ 6000.0.38f1"`）—— 那是**方案级冲突**，不是配置问题。

### 关键结论：**出包链不受影响**
CI 跑在 GitHub 的**境外 runner** 上，从 Unity 全球服务器拉 6000.3.25f1。
实测 **#17/#19/#20/#21/#22 五次构建全部成功**，日志里就是
`Unity Editor version: 6000.3.25f1` / `Built from '6000.3/staging'`。
→ **中国区限制不影响本项目的出包**，因为编辑器从不在这台手机或大陆网络上运行。

### 电脑端的现实选项（按可行性）
1. **继续用 CI 出包**（现状，已验证）——不需要本地编辑器
2. **境外云主机/网络环境**装 Unity 6（本地迭代才会快）
3. **问 Unity 中国**团结引擎何时对齐 Unity 6 内核（其 2.0 号称"底层架构全面重构"，但发布页仍是 2022.3 内核）
4. ❌ 降到团结引擎硬开 —— 需改 5 处依赖 + **违反 V9 §12**，不建议

### 一条 1 分钟的实测（比推断可靠）
电脑上开 **Unity Hub → Installs → Install Editor**，看有没有 `6000.x`：
有 → 直接按 §20 装；只有团结引擎 → 走选项 1 或 2。

### §23 补充：团结引擎 2.0 与性能的真实关系（重要修正）

**修正 §23 的简化说法**：§23 说"团结引擎内核是 Unity 2022.3"——这对**当前可下载版本**成立
（下载页最新内核 `2022.3.62t16`），但**不能推及 2.0**。

**官方路线图原话**（[developer.unity.cn](https://developer.unity.cn/projects/67ee5b89edbc2a001d422228)）：
> "未来一年…一是**跟进 Unity 6 重要功能**；二是引擎易用性；三是**小游戏及小游戏宿主**；
> 四是**开源鸿蒙平台**；以及**车机 HMI**。"

> "将在今年第二季度上线的 **GPU Resident Drawer**、**Spatial-Temporal**…" ·
> "**虚拟几何体**、**实时动态全局光照** 作为**团结引擎独有能力**上线" ·
> "计划在 **2026 年实现适配全平台的 GPU Driven Pipeline**" ·
> "团结粒子系统…**10 万量级粒子**" · 鸿蒙 "**Render As Service**…节省渲染开销"

**2.0 发布披露**（[腾讯新闻](https://news.qq.com/rain/a/20260731A0CXR200)）：
Virtual Geometry（宣称 iPhone 13 流畅渲染约 **6 亿三角面**）· 实时动态 GI · 并行渲染 ·
**CoreCLR** · 移动端 HDRP · 一条管线按硬件自动适配。

| 维度 | 团结引擎 | Unity 6 |
|---|---|---|
| Unity 6 已有功能（GPU Resident Drawer / STP） | **在"跟进"**（落后） | 原生具备 |
| 独家渲染（虚拟几何体 / 自研实时 GI / GPU Driven Pipeline） | **领先** | 无 |
| 中国平台（鸿蒙 / 微信抖音小游戏 / 车机 HMI） | **独有且成熟** | 需自行改造 |
| 内核 | 可下载版 = 2022.3 系列 | 6000.x |

**⚠️ 无可引用的实测基准**：确有 IEEE 论文
（*Comparative Performance Analysis of Rendering Optimization Methods in Unity Tuanjie Engine,
Unity Global and Unreal Engine*）但抓取被 IEEE 反爬拦截，**读不到数据 → 不给任何性能数字**。

**对本工程：性能不是当前决策因素**
1. 工程是 `6000.3.25f1`，可下载团结引擎内核是 2022.3 → **打不开**
2. **V9 §12 的 16KB 页对齐 ≥ 6000.0.38f1** 是方案级硬约束，2022.3 内核不满足
3. 性能门禁（V9 §13.8）**一条都还没测过** —— 场景只有约 150 个方块、怪物未上屏；
   **没测基线就换引擎 = 拿性能当借口做提前优化**
4. 团结引擎招牌优势（小游戏/鸿蒙/车机）**都不是我们的目标平台**（V9 目标是 Android APK）
