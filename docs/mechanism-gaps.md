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
  · 纯逻辑层（Core/Gameplay 24 个源）由本机 .NET 8 真编译真跑，70 条断言；
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
