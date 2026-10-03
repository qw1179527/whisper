# 机制缺口登记（如实留痕 · 不掩饰）

> 纪律：本文件只登记**已证实**的缺口，每条含「现象 / 证据 / 影响 / 处置」。
> 不在本文件里的东西不得声称"已处理"。

## G1 深处撤离点 +30% 未生效（灰盒遗留）
- **现象**：配置 `economy.formula.deepExtractionScale = 1.3`，但碎片公式不含它。
- **证据**：灰盒 `__m11` 结算页计算为 `round(evidence*200 + 150 + (elapsed<600 ? 100 : 0))`，
  未乘 `rewardScale`；C# 移植保持同行为，并由断言锁定：
  `[3.10] 已知缺口被显式登记：rewardScale 未参与碎片计算`（深处与标准同碎片即证明缺口仍在）。
- **影响**：UI 文案显示「×1.3」，实际奖励与标准点相同 → 玩家会认为深处撤离点不值得冒险，
  双点制的风险/收益设计失效。
- **处置**：**交人类裁决**。修法一行（`Compute` 里乘 `deepExtractionScale`），但会改变经济数值，
  且灰盒对拍会因此出现差异——需要先定"以配置为准"还是"以 0.6.0 行为为准"。

## G2 跨层走廊（stairs）DSL 能力缺失
- **现象**：V9 的太平间是「地下」，但 Level DSL 与校验器不支持跨层走廊
  （`走廊两端楼层不同 → 校验失败`）。
- **证据**：关卡 `_note` 已登记；`tools/validate-levels.mjs`、C# `LevelLoader`、
  Java 镜像三处同规则拒绝跨层。
- **影响**：太平间只能以 `floor:0` + 位置/命名表达"深处"，纵向空间设计受限。
- **处置**：待实现 stair 能力（DSL 增加 `stairs` 或允许 `corridor.floors`）。

## G3 三接口之外的 §13.4 同步面仅覆盖 ①③④
- **现象**：②（声纹刺激）走瞬时 RPC（已有 `SendVoiceStimulus`），①③④已由只读状态面覆盖；
  但「道具/门的**权威变更**入口」尚无接口（当前只读 `PropState`，没有 `RequestDoorToggle` 之类）。
- **影响**：玩法层能读门状态，但不能经接口请求开门 → 开门逻辑暂时只能留在 Net 实现内。
- **处置**：下一步在 `INetService` 增加受权限定的变更方法（Host 权威 + 客户端预测需一起设计）。

## G4 Unity 本体不可用（环境限制，非设计缺口）
- **现象**：本机无 Unity、无 `UnityEngine*.dll`、`download.unity3d.com` 404、bionic 非 glibc。
- **影响**：`GameBootstrap`/`LevelBuilder` 无法在本机真跑；PlayMode 用例未被执行过。
- **已做的替代验证**（不是"跑过了"的同义替换，而是分别说明覆盖了什么）：
  · 纯逻辑层（Core/Gameplay 24 个源）由本机 .NET 8 真编译真跑，107 条断言；
  · Unity 依赖文件由 Roslyn + 最小 Unity 桩做语法/语义检查（允许错误必须为 0）；
  · 三大机制由 1140/1716/21 例向量与灰盒实现对拍，差异 0。
- **仍未覆盖**：Unity 运行时的实际行为（组件生命周期、材质/网格、UI 布局）。
  需要 GameCI（GameCI + Unity Personal 三 secret，约 40 分钟/次）或一台装了
  Unity 6000.0.38f1 的机器。

## G5 色板三条待裁决（V9 §11 ↔ 产品色板）
- `color-faded` / `color-warning`：产品侧名值皆无（V9 §11 有定义、产品未落地）。
- `color-dark` `#0D0D0D` vs 产品 `soot` `#0E0D0C`：**ΔRGB=1/0/1，唯一"近似但不等值"** →
  会造成实际视觉偏差，不能与 `ghost↔mold` 的"同值异名"混为一谈。
- **处置**：交人类裁决（改产品值 or 改 V9 值）。

## 已修缺口（留痕，避免重复踩）
| 编号 | 内容 | 修法与证据 |
|---|---|---|
| D1 | Level DSL 无布局字段 → LevelBuilder 不可实现 | 补 `pos/rotY/floor`、`doors[].id`、`corridor.doorA/B` + 几何校验 |
| D1b | **房间 pos 语义错**（我用"中心点"，灰盒是"最小角点"） | 三方统一为灰盒约定；门字段改 `offsetM/widthM`；见 `v0.6.3-coord-convention` |
| D2 | 套件 kind 与用途不匹配无人校验 | 房间/道具分别限定 `kind=room`/`kind=prop` |
| D3 | 事件池不可扩展 | 内建 6 型 + `x-/ext-/ns:` 前缀 |
| D5 | §13.4 ①③④ 无接口面 | `INetService` 增只读状态面 + 3 个变更事件 |
| D7 | 二次注入静默覆盖 | `Services.Install` 改 fail-fast（可 `replace:true`） |
| — | 事件类型用了想当然的简名（合法事件被判非法） | 对齐配置 `eventPool` 逐字一致 + 双向门禁 |
| — | 构建链假绿：javac 失败被吞、dex 里没有 manifest 声明的 launcher | 先 aapt 出 R.java → javac 退出码必须 0 → 门禁改查 launcher 类 → 打包后校验 dex |
| — | `contactSanityLoss` 正数当增量 → 理智反而上涨 | 按机制取负 + 配置符号门禁 `config-lint` |
| — | `chaseSpeedScale` 读错路径（被 `?? 1.6` 兜底掩盖） | 改读 `monsters.*` + 门禁规则 |
| — | `LevelBuilder` 引用不存在的 `DesignTokens.ColorConcrete`、漏 `using Whisper.Core` | Roslyn + Unity 桩检查器抓出（该检查器自身修了三次才不假绿） |

## G6 真机 3D 视图为纯色（2026-10-03 装机实测发现）
- **现象**：CI #19 装机后 `BOOT OK · 23ms`、HUD 完整渲染、60fps 稳定、无崩溃；
  但 3D 视图是一片均匀 `#D8CFBB`（= `ColorBone`，安全区墙色）。
  判据实测：亮度 207/255 · 标准差 2.1 · 边缘密度 0.0%。
- **已做**：① 着色器去掉 `multi_compile_fog` + `UNITY_TRANSFER/APPLY_FOG`
  （雾是全局效果，取值依赖 Lighting 设置，而本工程无 ProjectSettings → 不可控）；
  ② 相机从 `spawn-2.5m`（入口房间仅 4m×3m，实际已到墙外）改为**站房间内、朝最近门口看**。
- **状态**：⚠️ **待 CI #20 复验**。若仍为纯色，按 `#D8CFBB` 反查是哪个 lightZone 的哪一面体。

## G7 gate-physics 的 P1 注入是伪造的（质检第 1 轮）
- **现象**：`node tools/gate-physics.mjs --inject-hardcode` 自报「注入后仍未判红 —— 该门禁不可信」。
- **根因**：硬编码检测在 **P0（警告级，不判红）**；P1 只查"配置路径→代码出处"单向，
  对 8 处真实硬编码（含 `LevelBuilder.cs` 的 `1.3f` = 深处撤离系数）无判定权。
- **影响**：V9 §19.5「改数值不碰代码」这条实际**没有硬门禁**。
- **状态**：⚠️ 未修（P2 同类问题已修，见 G8）。
- **建议**：把 P0 的 8 条硬编码从"警告"升为"判红"，或让 P1 参与判定。

## G8 已修：四处假绿（质检第 1 轮抓出，留痕）
| # | 问题 | 证据 | 修法 |
|---|---|---|---|
| 1 | `WireFormat.cs` 三类型缺 `<summary>` → C7 判红，`set -euo pipefail` 掩盖后 18 步 | 一键链只跑到 `[3/21]` | 补文档注释；此后每轮必须确认**全部步骤都跑了** |
| 2 | `gate-physics` P2 判据写成 `\bMath\.Random\b` —— **C# 里没有 Math.Random** | 门禁自报"不可信" | 改按 C# 真实写法；现正常绿 + 注入红 |
| 3 | `verify-apk-on-device.sh` V5/V6 **只打印，不参与退出码** → 黑屏也 exit 0 | 代码审查 + 双向标定 | node 段用退出码表达判定并转 ok()/bad()；pid 改三次采样 |
| 4 | `StimulusSize` 常量写 10 实际 14（断言只比对常量，常量错也发现不了） | 断言判红 | 改为**量真实编码字节** + 新增 `StateBatchFixedBytes=10` |

## G9 未验证：Unity API 出处台账不访问文档
- `tools/gate-editor-api.mjs` 只校验台账 URL 的**前缀**是否属官方域名，**从不访问页面**，
  因此"某个成员是否真的存在于该文档"未被验证（本机访问 `docs.unity3d.com` 无响应）。
- **状态**：⚠️ 已知边界（工具头注释已写明）。防的是"凭记忆写 API"这一类失效，不是签名正确性。

## G10 装机链路两个实测坑（已写入脚本注释）
- 直接从 `/storage/emulated/0/` 安装失败：`System server has no access to read file context u:object_r:fuse:s0`。
- `shz` 桥**不转发 stdin**（`shz "cat > f" < apk` 会挂住）；本进程也写不进 `/data/local/tmp`。
- **正解**：`shz "cp <共享存储路径> /data/local/tmp/x.apk"` → 再 `pm install -r`。

## G11 脚步可闻性口径缺失（V9 §7 只给了强度/半径，没给阈值口径）
- **实测事实**（已写成断言固化，`native/csharp-verify/Program.cs` [9]）：
  `crouch_footstep=8` · `walk_footstep=8` · `run_footstep=52`；三怪最低听觉阈值 = 低语者 10。
  ⇒ **走与蹲完全无声（8 < 10），只有跑会引怪（52 ≥ 10）**。
- **影响**：玩家只要不按跑就绝对安全 —— 潜行从"数值权衡"退化成"一个按键开关"。
  V9 §7 只定义了刺激源的强度/半径，没有定义"什么强度算可闻"，所以这不是代码 bug 而是**方案缺口**。
- **状态**：⚠️ 待人类裁决。三个方向：
  ① 认为二元潜行是有意设计 → 只需在方案里写明；
  ② 认为走路应当可闻 → 调 `walk_footstep.intensity`（如 12~15）或降低语者阈值；
  ③ 引入"距离影响可闻性"（当前 `Hearing` 在 `强度 < 阈值` 时**直接判不可闻**，距离根本不参与）。

## G12 怪物追击的"看见"未接入玩家可见性状态
- `MonsterDirector.CanSee` 已实现（距离 + 沿途查墙），但**玩家躲进柜子/趴下等状态尚未存在**，
  因此 `收殓人 ignoresLockers=true` 这个差异化目前无可生效的对象。
- **状态**：⚠️ 依赖 P1 交互层（柜子）落地后才能验证。当前不算缺陷，是尚未到达的范围。

## G13 Blender MCP 已接入（能力升级，非缺口，留痕备查）
- `http://192.168.1.17:8765/mcp` · Blender 5.0.1 · 23 个辅助工具全部 available。
- 意义：本机此前"无 Unity 编辑器 → 几何只能代码生成"是硬约束，但**真 3D 资产现在可以产出**。
  已登记的可做项：三怪外观 · 玩家道具（手电/电池/电闸/录像机）· 事件池视觉 · UI 图标 ·
  按 `asset-manifest` 契约重建 5 个套件。
- **注意（已核实的现状）**：那 5 个套件 GLB 虽然已生成且门禁验证通过（`gate-model M9/M10`），
  但**没有任何代码引用它们**，因此 `sharedassets0.assets` 里搜不到套件名 —— 它们**根本没进 APK**。
  真正的缺口不是"打的是旧资产"，而是"**打了一套没人用的资产**"（`LevelBuilder` 全用代码方块）。
  HANDOFF 里原先那条描述有误，已更正。
