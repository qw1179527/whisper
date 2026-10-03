# Project Whisper · 项目状态与接续文档

> 最后更新：2026-10-03 18:40（提交 `b7ad671`）
> 用途：**上下文压缩后仅凭本文件即可继续工作**。所有结论都标注了验证方式与证据。
> 纪律：不掩饰缺口；「构建成功」≠「能玩」；一切结论附可复核证据。

---

## 〇、一句话现状

**真机第一次跑起来了**：装机成功、`BOOT OK`（23ms）、HUD 完整渲染、60fps 稳定、无崩溃重启。
**但 3D 视图还是纯色**（相机/雾问题已修，待 CI #20 出包复验）。
方案基线 V9；本机门禁全绿（132 断言）。

---

## 一、项目是什么

**Project Whisper（低语计划）**：Unity 6 的 Android 合作恐怖游戏（语音驱动机制）。
方案基线 `/storage/emulated/0/DSH专用/恐怖整合.pdf`（V9.0 终稿，五版整合）；
V5~V8 归档，冲突以 V9 §3「冲突裁决终表」为准。
全文提取在 `docs/spec/V9-fulltext.txt`。

- 仓库：`$HOME/whisper`（40+ 次提交）
- 远端：`github.com/qw1179527/whisper`（私有，分支 **master → origin/main**）
- 开发环境：Android 手机 + DSH（bionic libc），**本机无 Unity 引擎** → 出包走 GitHub Actions

---

## 二、真机验收结果（2026-10-03，CI #19 / `whisper-unity-0.1.19.apk`）

设备 RMX5062 · Android 16 (API 36) · 中国移动。工具：`tools/verify-apk-on-device.sh`

| 判据 | 结果 | 证据 |
|---|---|---|
| V1 安装 + 包名 | ✅ | `com.whisper.projectwhisper`（PlayerSettings 真生效）· 版本 0.1.1 |
| V2 进程 / 无重启循环 | ✅ | pid 三次采样一致（排除首包那种"闪屏"形态） |
| V3 boot 走完 | ✅ | `[Whisper] BOOT OK · 23 ms · 房间 11 · 门 20 · 道具 7 · 可走格 405 · 着色器 Whisper/UnlitColor` |
| V4 无着色器异常 | ✅ | logcat 无 `ArgumentNullException(shader)` |
| V5/V6 屏幕有场景 | ❌ | 亮度 207/255 · 标准差 2.1 · **边缘密度 0.0%** |

**HUD 实测截图内容**（真的渲染出来了）：
```
Project Whisper · 运行中
Tick 305 · 60 fps · tickRate=60
接口: INetService 已注入 · IVoiceService 已注入 · IBackendService 已注入（无后端模式，§15.2）
关卡 asylum_v1: 房间 11 · 走廊 10
几何着色器 ✓ · 相机 ✓ · Boot 23 ms
```
3D 视图是一片均匀 `#D8CFBB`（= `ColorBone`，安全区墙色）→ 相机贴在墙外/雾洗白。

**已修、待 CI #20 复验**：
1. 着色器去掉 `multi_compile_fog` + `UNITY_TRANSFER/APPLY_FOG`
   （雾是全局效果，依赖 Lighting 设置，而本工程连 ProjectSettings 都没有 → 不可控；关卡仅 22m×10m 不需要雾）
2. 相机改为**站在房间内、朝最近门口方向看**（原先放在 `spawn-2.5m`，而入口房间只有 4m×3m → 已落到墙外）

---

## 三、许可证这条路（别再重复趟）

| 步骤 | 结论 |
|---|---|
| Unity 官方发布索引 | Linux 编辑器**只有 X86_64**（6000.3.25f1，4.2GB） |
| 本机架构 | `aarch64`，无 arm64 编辑器 |
| `download.unity3d.com` | 对本区域 **404** |
| `id.unity.com` | 对本机网络**超时不可达** |
| 个人版激活方式 | **只能通过 Unity Hub**（官方手册明确） |
| 命令行 `-username/-password` | 能登录但 **0 entitlements** |
| 电脑 Hub 生成的 `.ulf` | 含 MachineBindings，**CI 上无效** |
| **最终解法** | `.ulf` 的 `DeveloperData` base64 解码得**明文序列号**，用 `UNITY_SERIAL` 激活 ✅ |

**Secrets（已配 4 个）**：`UNITY_EMAIL` · `UNITY_PASSWORD` · `UNITY_SERIAL` · `UNITY_LICENSE`

---

## 四、踩过的真实事故（每条都有实证）

| # | 事故 | 根因 | 现在怎么守 |
|---|---|---|---|
| 1 | **真机黑屏**（首包） | `Shader.Find("Standard")` 被剥离→返回 null→`new Material(null)` 抛异常；且场景无相机、`Text.font` 为 null | 着色器作为资产放 `Resources/`（无条件进包）；相机与字体代码创建；`gate-test` **T6** 盯住 |
| 2 | **CI 构建失败**（#18） | 我**臆造 Unity API**：写成 `UnityEditor.SplashScreen`，真名是 `PlayerSettings.SplashScreen`。而我手写的 Unity 桩替这个错误背书 → 本机假绿 | `data/unity-api-registry.json` + `tools/gate-editor-api.mjs`：每个 Editor Unity API 必须有官方文档出处；反射式调用豁免 |
| 3 | **缺 96 个 `.meta`** | Unity 不把 .cs 归入 asmdef → 181 条 CS0234 | `tools/gen-meta.mjs`（确定性 GUID）+ 门禁 |
| 4 | **asmdef 引用用对象形式** | `{name:...}` 被 Unity 静默忽略 | 改字符串数组；**生成器同步**（否则生成器与文件漂移，一键链第 6 步长期判红、后 15 步被掩盖） |
| 5 | **`set -euo pipefail` 掩盖后续步骤** | 第 3 步一红，第 4~21 步从不执行；质检发现"HEAD 的 132 全绿"在工作区不可复现 | 每次改动后跑**完整** `unity-check.sh` 并确认 exit 0 |
| 6 | **假绿四连**（质检第 1 轮抓出） | ① `WireFormat` 三类型缺 `<summary>` ② `gate-physics` P2 判据写成 C# 里不存在的 `Math.Random` ③ 真机脚本 V5/V6 只打印不参与退出码 ④ `StimulusSize` 常量写成 10 实际 14 | 全部修掉；判据必须**参与退出码**，常量必须与**实测字节**一致 |
| 7 | **线格式常量自证** | 断言只比对常量，常量错了也发现不了；我反推偏移错了三次 | 改为"先用空批量出固定开销，再算增量"；新增 `StateBatchFixedBytes=10` |

---

## 五、门禁体系（四类 + 四个补充）

| 门禁 | 工具 | 规模 | 注入验证 |
|---|---|---|---|
| 建模 | `tools/gate-model.mjs` | M1~M11 | 4 种注入全判红 |
| 物理规则 | `tools/gate-physics.mjs` | P0~P4 | P2 已修（正常绿+注入红）；**P1 注入仍是伪造的** |
| 代码质量 | `tools/gate-code.mjs` | C1~C7 | 6 种注入全判红 |
| 功能测试 | `tools/gate-test.mjs` | T1~T3 + **T6 启动关键契约** | 注入必失败断言 → 判红 |
| 资产几何 | `tools/gate-asset-bbox.mjs` | B1~B3 | 2 种注入判红 |
| 程序集编译 | `native/asmdef-check/build.sh` | 逐 ASMDEF | 新增 |
| 接口一致性 | `native/asmdef-check/iface-check.mjs` | 3 接口 | 新增 |
| **Editor API 出处** | `tools/gate-editor-api.mjs` | 35 处引用 / 36 项台账 | 新增（防臆造 API） |

**一键链**：`bash unity-check.sh` → 全部步骤 · exit 0 · **132 条本机断言 0 失败**

---

## 六、联机方案（含真机实测结论）

**实测（本机 RMX5062 / 中国移动）**：
- **IPv4 侧是对称 NAT**：三个 STUN 服务器给出同 IP 不同端口（:4339/:4201/:4858）→ **UDP 打洞不可行**
- **IPv6 侧全局可路由且端口守恒**：绑定 `[2409:...]:38000`，三个 STUN 服务器看到的都是同一地址同一端口
  → **端到端无 NAT，拿地址就能直连**

**免费结论**：
| 方案 | 判定 |
|---|---|
| Photon Fusion 免费档 | ⚠️ 100 CCU 硬顶，付费不可降档（单向门）→ 排除 |
| Unity Relay | ⚠️ UGS 要绑支付方式 → 排除 |
| **IPv6 直连 + 房间码内嵌地址** | ✅ **已实现**（`Net/RoomCode.cs`）：零服务器、零账号、零绑卡 |
| 同 WiFi 广播发现 | ✅ 免操作 |
| EOS | ✅ 真免费（无 CCU 上限），但要集成工作量 |

**线格式**（`Net/WireFormat.cs`，V9 §13.4 首次有可核对字节数）：
```
满房 4 人 + 4 理智 + 6 道具变更 = 68 字节 / 预算 100 字节
容量上限：每批最多 16 条道具/门变更
尺寸实测：空批固定 10 · 头 5 · 玩家 8 · 道具 3 · 理智 2 · 声纹 14
```
**硬边界**：对面若没有 IPv6，免费方案连不上（只能同 WiFi 或用电脑开一次中继）。

---

## 七、代码与工具地图

```
whisper/
├── unity/Assets/
│   ├── Scripts/
│   │   ├── Core/Contracts/     三接口 + 四类同步对象
│   │   ├── Core/DesignTokens.cs 41 个设计常量（V9 §11）
│   │   ├── Gameplay/           Level/ Voice/ Hearing/ Monsters/ Sanity/ Items/ Extraction/ Hud/ Session/ Match/
│   │   ├── Net/                LocalNetService(桩) · RoomCode · WireFormat
│   │   ├── Audio|Backend/      Local*Service 桩（SDK 唯一槽位）
│   │   ├── Runtime/GameBootstrap.cs  组合根：相机+HUD+配置+三接口+几何
│   │   ├── Editor/             BuildScript + BuildConfigurator + EditorSceneBootstrap
│   │   └── Tests/              EditMode×3 + PlayMode×1（**从未执行过**）
│   └── Resources/
│       ├── Data/config.json · asset-manifest.json
│       ├── Levels/asylum_v1.json
│       └── Shaders/WhisperUnlitColor.shader   ← 无条件进包（黑屏修复的关键）
├── native/
│   ├── csharp-verify/          132 条断言的跑手（本机真编译真跑）
│   ├── asmdef-check/           逐程序集编译 + 接口一致性
│   ├── unity-stubs/            Unity 桩（**注意：桩会为臆造 API 背书，见事故 #2**）
│   ├── unity-syntax/           Roslyn 语义检查
│   └── graybox-apk/            灰盒 APK 构建链
├── tools/                      40+ 工具（门禁/生成器/校验/真机验收）
└── data/unity-api-registry.json  Editor Unity API 出处台账
```

### 常用命令

```bash
bash unity-check.sh                    # 一键链（务必确认 exit 0 与全部步骤都跑了）
node tools/gate-test.mjs               # 功能测试门禁（含 T6）
node tools/gate-physics.mjs            # 物理规则门禁（含 --inject-random 注入验证）
node tools/gate-editor-api.mjs         # Editor API 出处门禁
bash tools/unity-syntax-check.sh       # Roslyn 语义检查（含 Assets/Editor）
node tools/gen-meta.mjs [--check]      # 生成/校验 Unity .meta
tools/git.sh <子命令>                   # 本机 git（修了 exec-path 与 CA 路径）

# 真机验收（需 Shizuku；约 90 秒）
bash tools/verify-apk-on-device.sh "/storage/emulated/0/DSH专用/whisper-unity-0.1.N.apk" 25
```

---

## 八、未完成清单

### P0 —— 阻塞"能玩"
| 项 | 说明 |
|---|---|
| **3D 视图复验** | 雾与相机已修，待 CI #20 出包验证屏幕上真有几何 |
| 玩家控制器 | `PlayerController.cs`：输入 → `LevelGeometry.Resolve` 子步进碰撞 → 相机跟随（**当前相机是固定机位**） |
| 场景实体接线 | `GameSession.Tick` ↔ Unity 生命周期（位置/状态同步到 GameObject） |

### P1 —— 玩法闭环
| 项 | 说明 |
|---|---|
| 怪物实例化 | `MonsterBrain` 逻辑已就绪，缺预制体与移动解析注入 |
| HUD 实渲染 | `HudModel` 已渲染；`HudBuilder` 未接真 uGUI |
| 交互层 | 证据拾取（0.9m）/ 电闸（1.6m）/ 道具使用 |
| 对局闭环 | 撤离双点 + 保护期 + 狂暴窗口 → 结算页 |

### P2 —— 补齐 V9 承诺
| 项 | V9 出处 | 状态 |
|---|---|---|
| C4 运行时烘焙占位 | §19.1 | ⚠️ 未实现（C1/C2/C3 已落地） |
| 事件池场景表现 | §7 | 逻辑有，视觉缺 |
| 三怪差异化外观 | §7 附录A | 逻辑有，外观缺 |
| 第 0 局引导 / 档案残页 | §9 | 未做 |
| 设计系统接入 uGUI | §11 | Token 已生成，未接 UI |
| **5 个套件 GLB 重建** | — | APK 仍打旧的 `baseline/whisper-kits.glb` |

### P3 —— 需外部条件
联机同步（已定 IPv6 直连方案，待实现传输层）· 语音链路（Vivox 16.x，当前是桩）· 后端六能力（Firebase）· 合规十项

### 已知缺口（登记在 `docs/mechanism-gaps.md`）
- `gate-physics` 的 **P1 注入仍是伪造的**（硬编码检测在 P0 且为警告级，不判红）
- 台账产物未重新生成（行数口径代码已修，未 `--emit`）
- APK 打的是旧的 `whisper-kits.glb`，非新的 5 个 CC0 套件
- PlayMode 用例（`BootSmokeTests`）**从未执行过**
- `gate-editor-api` 的出处台账**从不访问 URL**（只做前缀校验），成员是否真在文档中未验证

---

## 九、安全事项

**本会话对话记录里出现过明文凭据**（会话缓存位于
`$HOME/storages/session_projcache/sessions/*.json`，其中检出 3 个令牌）：

| 凭据 | 状态 |
|---|---|
| `ghp_PILk…`（经典令牌） | 已失效（撤销过） |
| `github_pat_11CQ…`（细粒度，**当前在用**，需 Contents 读写） | **用完请撤销** |
| Unity 账号密码 | **建议改密** |

> 新令牌只放环境变量或 Secrets，**不要再粘进对话**。

---

## 十、关键环境事实

| 事实 | 值 |
|---|---|
| 本机 libc / 架构 | **bionic**（非 glibc）· `aarch64` · Android 16 (API 36) |
| 无 Unity 引擎 | 出包必须走 GitHub Actions（一次约 25~45 分钟） |
| Shizuku（`shz`） | **可用** → 能 `pm install` / `logcat` / `screencap` / `pm list` |
| 装机坑 1 | 直接从 `/storage/emulated/0/` 装会失败：`fuse:s0` system_server 读不了 |
| 装机坑 2 | `shz` **不转发 stdin**；本进程写不进 `/data/local/tmp` |
| 装机正解 | `shz "cp <共享存储路径> /data/local/tmp/x.apk"` → 再 `pm install` |
| 无 rsync | 用 `tar --exclude` |
| toybox grep | 不支持 `\s`/`\b`，用 `rg` |
| `/tmp` 不可写 | 用 `$HOME/tmp` |
| git 需两个环境变量 | `GIT_EXEC_PATH` + `GIT_SSL_CAINFO`（`tools/git.sh` 已封装） |
| 推送 | `tools/git.sh push origin master:main`（本地分支 master，远端 main） |
| Blender | **MCP 可用**：`http://192.168.1.17:8765/mcp` · Blender 5.0.1 · 工作目录 `/storage/emulated/0/DSH专用/DSH文件` |

---

## 十一、接续时怎么开工

```bash
cd ~/whisper
tools/git.sh log --oneline -10          # 看最近改了什么
bash unity-check.sh                     # 期望：全步骤 · exit 0 · 132 断言 0 失败
```
**若不全绿 → 先把红的那步修掉，不要带病往下做。**
（特别注意：`set -euo pipefail` 会让第一步失败掩盖后续步骤，必须确认**全部步骤都跑了**。）

**当前第一优先**：CI #20 出包后跑
```bash
bash tools/verify-apk-on-device.sh "/storage/emulated/0/DSH专用/whisper-unity-0.1.20.apk" 25
```
看 V5/V6 是否转绿（3D 视图是否真有几何）。若仍为纯色，按 `#D8CFBB` 这个颜色反查是哪个 lightZone 的哪一面体。
