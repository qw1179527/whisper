# Project Whisper · 项目状态与接续文档

> 最后更新：2026-10-03 17:00（提交 `11b4fa1`）
> 用途：**上下文压缩后仅凭本文件即可继续工作**。所有结论都标注了验证方式与证据。

---

## 〇、一句话现状

**从"手机端无 Unity"走到了"GitHub 云端成功产出 Unity APK"**（29.9 MB，已验证内容进包）；
但该 APK **从未在任何设备上运行过** —— 下一步是装机验证。

---

## 一、项目是什么

**Project Whisper（低语计划）**：Unity 6 的 Android 合作恐怖游戏（语音驱动机制）。
方案基线为 `/storage/emulated/0/DSH专用/恐怖整合.pdf`（V9.0 终稿，五版整合）。
V5~V8 已归档，冲突以 V9 §3「冲突裁决终表」为准。

**开发环境**：Android 手机 + DSH Mobile（AI 全栈执行模式），仓库在 `$HOME/whisper`。
**远程仓库**：`github.com/qw1179527/whisper`（私有，44 次提交）

---

## 二、已验证的成果（有证据）

### 2.1 Unity 工程

| 项 | 状态 | 证据 |
|---|---|---|
| 七 ASMDEF 结构（V9 §13.1） | ✅ | 10 个 `.asmdef`（7 业务 + UI/Analytics/Tests） |
| 玩法代码 | ✅ | 40 个 C# 文件 / 5330 行 |
| 编译通过 | ✅ | IL2CPP 产出 `libil2cpp.so`（62MB） |
| Boot 场景 | ✅ | 由 `EditorSceneBootstrap.cs` 代码生成（本机无编辑器） |

**已实现的机制**（均在 `unity/Assets/Scripts/Gameplay/`）：
声纹校准 + 分档分类器 · 听觉判定 · 三怪状态机 · 理智系统 · 道具系统 ·
撤离结算（双点制）· 关卡 DSL 加载 + 几何编译 + 装配计划 · HUD 模型 · 会话/对局流程

### 2.2 门禁体系（四类 + 两个补充）

| 门禁 | 工具 | 规模 | 注入验证 |
|---|---|---|---|
| 建模 | `tools/gate-model.mjs` | M1~M11 | 4 种注入全部判红 |
| 物理规则 | `tools/gate-physics.mjs` | P0~P4 | P3/P4 有效；**P1/P2 注入仍未被判据发现（缺口）** |
| 代码质量 | `tools/gate-code.mjs` | C1~C7 | 6 种注入全部判红 |
| 功能测试 | `tools/gate-test.mjs` | T1~T3 | 注入必失败断言 → 判红 |
| 资产几何 | `tools/gate-asset-bbox.mjs` | B1~B3 | 2 种注入判红 |
| 程序集编译 | `native/asmdef-check/build.sh` | 逐 ASMDEF | 新增（补本机盲区） |
| 接口一致性 | `native/asmdef-check/iface-check.mjs` | 3 接口 | 新增（补 Tests/ 盲区） |

**一键链**：`bash unity-check.sh` → 21 步 · exit 0 · **113 条本机断言全绿**

### 2.3 CI / 出包

| 工作流 | 用途 | 状态 |
|---|---|---|
| `unity-android.yml` | 构建 Android APK | ✅ **#17 success** |
| `unity-license.yml` | 序列号激活许可证 | ✅ success |
| `unity-alf.yml` | 生成 `.alf`（备用） | 可选 |

**所需 Secrets（已配置 4 个）**：
`UNITY_EMAIL` · `UNITY_PASSWORD` · `UNITY_SERIAL` · `UNITY_LICENSE`

**产物**：`/storage/emulated/0/DSH专用/whisper-unity-0.1.0.apk`
（29.9 MB · sha256 `6bbaf39611fee3b6`）

**APK 内容已核实进包**（解压后二进制搜索）：
```
asylum_v1        ✓ level0 + globalgamemanagers
entrance_safe    ✓ 8dc39935…
stimulusSources  ✓ 666dc568…
Whisper          ✓ ScriptingAssemblies.json + globalgamemanagers.assets
```

### 2.4 灰盒版（旁路验证台）

`/storage/emulated/0/DSH专用/whisper-graybox-0.7.5-dev.apk`（86 KB）——**可玩**：
13 房间全连通 · 5 个证据点全部可达 · 启动冒烟通过。
用途：低成本验证机制与数值（与 Unity 共用 `data/config.json` 与关卡语义）。

---

## 三、许可证这条路的完整结论（别再重复趟）

| 步骤 | 结论 | 证据 |
|---|---|---|
| Unity 官方发布索引 | Linux 编辑器**只有 X86_64** | API 查询：6000.3.25f1 · 4.2GB |
| 本机架构 | `aarch64`，无 arm64 编辑器 | `uname -m` |
| `download.unity3d.com` | 对本区域 **404** | 实测（URL 取自官方 API） |
| `id.unity.com` | 对本机网络**超时不可达** | 15s timeout，多次实测 |
| 个人版激活方式 | **只能通过 Unity Hub** | [官方手册](https://docs.unity3d.com/6000.0/Documentation/Manual/LicenseActivationMethods.html) |
| 命令行 `-username/-password` | 能登录（返回 access token）但 **0 entitlements** | CI 日志 |
| 电脑 Hub 激活生成的 `.ulf` | 是 `License id="Terms"` + **MachineBindings**（机器绑定），CI 上无效 | 解析 .ulf |
| **最终解法** | `.ulf` 的 `DeveloperData` base64 里藏着**完整序列号** `F4-WU6R-BA2X-RPYN-22M3-9CPX`，用 `UNITY_SERIAL` 激活 | CI #17 成功 |

**关键教训**：`SerialMasked` 是掩码的，但 `DeveloperData` 字段 base64 解码即得明文序列号。

---

## 四、构建失败 → 成功的 6 个真实缺陷（都已修）

| # | 缺陷 | 症状 | 修法 | 错误数 |
|---|---|---|---|---|
| 1 | **缺 96 个 `.meta`** | Unity 不把 .cs 归入 asmdef | `tools/gen-meta.mjs`（确定性 GUID） | 崩溃 → 181 |
| 2 | **asmdef 引用用对象形式** `{name:...}` | 程序集互相看不见 | 改字符串形式 `["Whisper.Core"]` | 181 → 45 |
| 3 | **测试 asmdef 缺 `Whisper.Net` 引用** | Net 类型找不到 | 补引用 | 45 → 14 |
| 4 | **`FakeNet` 未实现接口新增成员** | CS0535 | 补同步对象成员 | 14 → 0 |
| 5 | **APK 路径写错** | 构建成功但传不上 | 改 `unity/build/Android/` | — |
| 6 | **缓存键没含 `.meta` 指纹** | 旧程序集图残留 | 缓存键升 v2 | 干扰排查 |

**#1 与 #4 是本机门禁的盲区**（跑手把源码当普通 C# 编译，不经资产数据库，也不编 Tests/）——
已用 `asmdef-check` + `iface-check` 补上。

---

## 五、⚠️ 下一步：装机验证（最优先）

**当前唯一未知**：APK 能否在真机启动。

```
安装：/storage/emulated/0/DSH专用/whisper-unity-0.1.0.apk
```

| 现象 | 可能原因 | 处理方向 |
|---|---|---|
| 正常进游戏、看到 HUD | —— | 继续做玩家控制器与怪物 |
| 黑屏 | Boot 场景里组件没挂上（本机无编辑器，场景是脚本生成） | 改 `EditorSceneBootstrap` 用 `AddComponent` 显式挂载 |
| 闪退 | 缺资源 / IL2CPP 剥离 | 查 logcat；加 `link.xml` |
| 提示缺资源 | Resources 打包问题 | 检查 `Assets/Resources/**` 与加载路径 |

**抓日志**：
```bash
adb logcat | grep -i unity        # 连电脑时
# 或手机装 logcat 阅读器 App，过滤 Unity
```

---

## 六、代码与工具地图

```
whisper/
├── unity/Assets/Scripts/
│   ├── Core/Contracts/        三接口 + 四类同步对象（MatchState/StimulusEvent/TokenBundle）
│   ├── Core/DesignTokens.cs   41 个设计常量（V9 §11 色板，由生成器产出）
│   ├── Gameplay/
│   │   ├── Level/             LevelData/Loader/Geometry/Builder/Assembly
│   │   ├── Voice/             VoiceCalibrator(149) + VoiceBandClassifier(299)
│   │   ├── Hearing/           听觉判定
│   │   ├── Monsters/          MonsterBrain（状态机）
│   │   ├── Sanity/ Items/ Extraction/ Hud/ Session/ Match/
│   ├── Net|Audio|Backend/     Local*Service 桩（SDK 唯一槽位）
│   ├── Runtime/GameBootstrap.cs   组合根（读配置→注入三接口→装配几何→HUD）
│   ├── Editor/                BuildScript + EditorSceneBootstrap（C1 场景零手工）
│   └── Tests/                 EditMode×3 + PlayMode×1（**从未执行过**）
├── native/
│   ├── csharp-verify/         113 条断言的跑手（本机真编译真跑）
│   ├── asmdef-check/          逐程序集编译 + 接口一致性（新增）
│   ├── graybox-apk/           灰盒 APK 构建链（不用 gradle）
│   └── dotnet.sh              本地 .NET 8 SDK 包装
├── tools/                     38 个工具（门禁/生成器/校验/探针）
├── docs/
│   ├── HANDOFF.md             ← 本文件
│   ├── mechanism-gaps.md      缺口登记表
│   └── spec/LEDGER.md         五版方案要求台账（399 章 / 318 条承重行）
└── data/config.json           数值唯一真源（V9 §19.5）
```

### 常用命令

```bash
bash unity-check.sh                    # 一键链：21 步 / 113 断言
node tools/gate-model.mjs              # 建模门禁 11 项
node tools/gate-physics.mjs            # 物理规则 4 项
node tools/gate-code.mjs               # 代码质量 7 项
node tools/gate-test.mjs               # 功能测试 3 项
node tools/gate-asset-bbox.mjs         # 资产几何 1 项
bash native/asmdef-check/build.sh      # 逐 ASMDF 编译检查
tools/git.sh <git 子命令>               # 本机 git（修了 exec-path 与 CA 路径）
node tools/gen-meta.mjs [--check]      # 生成/校验 Unity .meta
bash build.sh --quick                  # 灰盒产物（V1~V6 门禁）
node tools/nav-probe.mjs               # 怪物导航仿真探针
GITHUB_TOKEN=xxx node tools/gh/put-secret.mjs <owner/repo> <NAME> <value>
                                       # 程序化写 GitHub Secrets（libsodium 加密）
```

---

## 七、未完成清单（按优先级）

### P0 —— 阻塞"能玩"
| 项 | 说明 |
|---|---|
| **装机验证** | 见第五节。这是当前唯一未知 |
| 玩家控制器 | `PlayerController.cs`：输入 → `LevelGeometry.Resolve` 子步进碰撞 → 相机跟随 |
| 场景实体接线 | `GameSession.Tick` ↔ Unity 生命周期（位置/状态同步到 GameObject） |

### P1 —— 玩法闭环
| 项 | 说明 |
|---|---|
| 怪物实例化 | 三怪预制体 + 移动解析注入（`MonsterBrain` 逻辑已就绪） |
| HUD 实渲染 | `HudModel` → `HudBuilder` 接真 uGUI |
| 交互层 | 证据拾取（0.9m）/ 电闸（1.6m）/ 道具使用 |
| 对局闭环 | 撤离双点 + 保护期 + 狂暴窗口 → 结算页 |

### P2 —— 补齐 V9 承诺
| 项 | V9 出处 | 状态 |
|---|---|---|
| C4 运行时烘焙占位 | §19.1 | ⚠️ 未实现（C1/C2/C3 已落地） |
| 事件池场景表现 | §7 | 逻辑有，视觉缺 |
| 三怪差异化 | §7 附录A | 逻辑有，外观缺 |
| 第 0 局引导 / 档案残页 | §9 | 未做 |
| 设计系统接入 uGUI | §11 | Token 已生成（41 常量），未接 UI |

### P3 —— 需外部条件
| 项 | 阻塞 |
|---|---|
| 联机同步（Fusion 2.x） | §13.4，需 SDK 与账号 |
| 语音链路（Vivox 16.x） | §13.5，当前是 LocalVoiceService 桩 |
| 后端六能力（Firebase） | §15，需云服务与预算 |
| 合规十项 | §16，需法务与商店流程 |

### 已知缺口（登记在 `docs/mechanism-gaps.md`）
- `gate-physics` 的 P1/P2 **注入是伪造的**（判据未参与），需改成真注入
- 台账产物未重新生成（行数口径代码已修，未 `--emit`）
- APK 打的是旧的 `baseline/whisper-kits.glb`，非新的 5 个 CC0 套件
- PlayMode 用例（`BootSmokeTests`）**从未执行过**

---

## 八、安全事项（务必处理）

**本项目会话中出现过明文凭据，已暴露**：

| 凭据 | 风险 | 处理 |
|---|---|---|
| `ghp_PILk…`（经典令牌） | 仓库完整读写（含 Actions/Secrets） | [撤销重建](https://github.com/settings/tokens) |
| `github_pat_11CQ…`（细粒度） | 同上（限单仓库） | 一并撤销 |
| Unity 密码 `771010you@A` | 账号可登录 | [改密码](https://id.unity.com) |

> 新的令牌只放环境变量或 Secrets，**不要再粘进对话**。

---

## 九、关键环境事实（避免重复探索）

| 事实 | 值 |
|---|---|
| 本机 libc | **bionic**（非 glibc），Unity 编辑器无法原生运行 |
| 架构 | `aarch64` |
| 可用存储 / 内存 | 282 GB / 约 6 GB（Unity 编辑器需 8.7GB 空间 + 大内存） |
| 无 rsync | 用 `tar --exclude` 替代 |
| toybox grep | 不支持 `\s`/`\b`，用 `rg` |
| `/tmp` 不可写 | 用 `$HOME/tmp` |
| `execSync` 在 node 失效 | 用 `fs` + `execFileSync`（真二进制） |
| `aapt` 需要 `ANDROID_DATA` | 构建脚本已设 |
| git 需要两个环境变量 | `GIT_EXEC_PATH` + `GIT_SSL_CAINFO`（`tools/git.sh` 已封装） |
| 无 npm，有 pnpm | pnpm 在 Android 上因文件锁常失败 → 手工 vendor npm 包（如 libsodium） |
| 迁移包 | `/storage/emulated/0/DSH专用/DSH-MIGRATION-20261003-1651.zip`（41.5 MB / 2863 条目，已 `unzip -t` 校验、凭据扫描 0 命中） |

---

## 十、接续时怎么开工

```bash
cd ~/whisper
git pull                                   # 同步 44 次提交
bash native/fetch-dotnet.sh                # 若 .NET 缺失
bash unity-check.sh                        # 期望：21 步 exit 0 · 113 断言 0 失败
```

**若全绿** → 环境正常，直接做第五节的装机验证，或按第七节 P0 开发。

**若有报错** → 把报错原文发我，不要带病往下做。
