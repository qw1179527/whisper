# 仓库约定（repo-conventions）· Project Whisper

> 权威来源：V9.0（恐怖整合.pdf，终稿冻结版）§13 客户端工程架构 · §19 代码优先工程学 · §20.3 规格书门禁 · §22.2 质量门禁 v3
> 本文件是 AI 会话与人类审查共同遵守的契约。**任何与本文冲突的写法都视为缺陷**。

## 1. 模块边界（V9 §13.1，逐字实现）

七 ASMDEF，依赖方向单向：

```
Core（无外部依赖）
 ├─→ Gameplay（→Core）
 ├─→ Net（→Core+Gameplay）
 ├─→ Audio（→Core）
 ├─→ Backend（→Core）
 ├─→ Analytics（→Core）
 └─→ UI（→Core+Gameplay+Audio+Net+Backend）
```

**禁止循环依赖（原文）**：`Net 不引用 UI`、`Gameplay 不引用 Net/Backend`。

- asmdef 由 `tools/gen-asmdef.mjs` 从 V9 §13.1 规则表生成，**手改会被覆盖**；
- CI 用 `node tools/gen-asmdef.mjs --check` 校验漂移（违规直接 fail）。

## 2. 三接口抽象层（V9 §13.2，逐字纪律）

`INetService` / `IVoiceService` / `IBackendService` 定义在 **Core**。

- **玩法代码只依赖接口**；
- **SDK 实现类是唯一允许 import 第三方命名空间的位置**：
  - Photon Fusion 2 → 只允许出现在 `Assets/Scripts/Net/`
  - Unity Vivox → 只允许出现在 `Assets/Scripts/Audio/`
  - Firebase Unity SDK → 只允许出现在 `Assets/Scripts/Backend/`
- 该纪律服务三个目标（原文）：迁移梯队零改动、AI 产出爆炸半径可控、CI 可做架构守护静态扫描（扫描 `using` 指令，违规直接 fail）。
- 静态检查：`node tools/arch-guard.mjs`。

## 3. 代码优先四承诺（V9 §19.1，工程级实现手段）

| 承诺 | 原文要求 | 本仓库的实现手段 |
|---|---|---|
| **C1 场景零手工** | Boot 场景（空场景 + 一个 `GameBootstrap` 组件）是唯一「手工」场景，且本身也可由初始化脚本生成；所有关卡场景运行时拼装 | `Assets/Scenes/` 只允许存在 Boot 场景；关卡由 `LevelBuilder` 运行时从 JSON 拼装；场景文件纳入 `tools/arch-guard.mjs` 白名单检查 |
| **C2 UI 零编辑器** | uGUI 全部代码构建（`UiBuilder` 工具类，AI 友好的链式 API）；设计 Token 直接落为 C# 静态类（`DesignTokens.ColorPaper` 等） | `Assets/Scripts/UI/` 禁止 `.prefab`/`.unity` 资产；UI 只由 `UiBuilder` 构建；Token 来自 `Assets/Data/design-tokens.json` 生成的静态类 |
| **C3 资产零导入操作** | CC0 资产经脚本化管线入库；资产清单 manifest（JSON）驱动 → CI editor batchmode 导入、打 Tag、设 Addressables 分组 | `Assets/Data/asset-manifest.json` 是唯一资产入口；`ContentPipeline.GenerateAll()` 在 CI 执行导入与分组；禁止手工拖拽入库 |
| **C4 烘焙零编辑器** | NavMesh 用 `NavMeshSurface` 运行时烘焙；光照全实时；无 lightmap 依赖 | 仓库不得出现 lightmap 资产；NavMesh 只在运行时烘焙（门锁变动触发局部重烘焙，半径 15 米，V9 §13.7） |

## 4. 依赖版本锁定（V9 §12 · §16）

唯一真源：`unity/dependency-lock.json`。`unity/Packages/manifest.json` 由 `tools/gen-manifest.mjs` 生成。

**16KB 页对齐四件套（上架硬门槛，缺一不可）**：

| 组件 | 下限 | 本仓库锁定 |
|---|---|---|
| Unity Editor | 6000.0.38f1+ | 6000.0.38f1 |
| Firebase Unity SDK | ≥ 12.10.0 | 12.10.0 |
| Unity Vivox | ≥ 16.6.2 | 16.6.2 |
| Burst | ≥ 1.8.21 | 1.8.21 |

其余：`targetSdk = 36` · `minSdk = 26` · `Billing v8` · `APK ≤ 200MB` / `AAB install-time ≤ 150MB`。

## 5. 数值与调参纪律（V9 §13.3 · §19.5）

三层免构建调参，**改数值不碰代码，改内容不碰数值，改行为才构建**：

| 层 | 通道 | 延迟 | 适用 |
|---|---|---|---|
| 数值 | Remote Config | 秒级 | 怪物速度 / 理智损耗 / 声纹半径 / 阈值 |
| 内容 | Level DSL 热加载（dev）/ Addressables（线上） | 10 秒~分钟 | 布局 / 道具 / 事件 / 词条 |
| 行为 | CI 构建 | ~40 分钟 | 仅行为变更 |

Remote Config 键（六键，锁在 dependency-lock.json）：`monster_speed_multiplier` · `daily_free_room_limit` · `event_pool_enabled` · `min_version` · `ccu_warn_rooms`(20) · `ccu_full_rooms`(25)。

## 6. 质量门禁（V9 §22.2 七道，本仓库可执行的四条）

| 门禁 | 规则 | 执行方式 |
|---|---|---|
| G1 Spec-Gate | 接口契约与测试清单未经人类批准，实现不得开始 | Issue 模板状态字段（流程纪律） |
| G2 无测试不合并 | AI 模块必须附测试且 CI 全绿 | `unity-test` workflow 阻断 PR |
| G3 架构守护 | Gameplay/UI 禁止 import 第三方 SDK 命名空间 | `tools/arch-guard.mjs`（CI 静态扫描） |
| G4 性能门禁 | 包体/冷启动/内存/帧耗/Draw Call 阈值（§13.8） | 每日构建自动判红（待 Unity 环境就绪后接入） |
| G5 截图门禁 | UI/视觉 PR 必须附 Test Lab 截图与视频 | PR 模板检查（人工确认） |
| G6 真机验收 | 每任务在开发手机上按验收清单勾选通过 | 流程纪律（验收清单在规格书内） |
| G7 恐怖感一致性 | 资产入库前人工审查（品味即产品） | 审查清单入库（人为流程） |

## 7. 提交与 PR 纪律（V9 §20.3 · §24）

- 单任务 ≤ 1 个 AI 工作日；单 PR ≤ 400 行（手机审查的硬约束）；
- PR diff 超出规格书范围 = 直接打回；
- 连续两次审查不过 → **重写而非修补**；
- 每份任务一个规格书（`unity/docs/specs/SPEC-{编号}.md`），契约批准后冻结。

## 8. 目录约定

```
unity/
├── Assets/
│   ├── Scenes/            # 只允许 Boot 场景（C1）
│   ├── Scripts/{Core,Gameplay,Net,Audio,Backend,UI,Analytics}/
│   ├── Levels/            # Level DSL JSON（关卡即数据，§19.2）
│   └── Data/              # config.json / design-tokens.json / asset-manifest.json
├── Packages/manifest.json # 生成物（勿手改）
├── ProjectSettings/       # 见 project-settings.md
├── docs/
│   ├── ai-context/        # 文档喂入包四件（§27.1）
│   └── specs/             # 规格书
├── dependency-lock.json   # 依赖版本唯一真源
└── .github/workflows/     # unity-test / unity-build / android-min
```
