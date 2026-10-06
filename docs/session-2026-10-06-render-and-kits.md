# 会话记录 2026-10-06（下半场）：画质链根因 · 关卡/套件收口 · 跳脸接线

> 本文只写**有证据**的结论。每条都给出可复核的出处（文件:行 / 命令 / 实测数字）。
> 用途：上下文被压缩后，接手的人（或下一个我）读这一份就能接上，不必重新勘察。

## 一、画质链的根因（**用户点名的 24 项画质里，一片在架构上够不着**）

| # | 事实 | 证据 |
|---|---|---|
| B1 | **URP 没启用**：管线是 Built-in。`com.unity.render-pipelines.universal@17.0.3` 只躺在 `Packages/manifest.json` 里 | `unity/ProjectSettings/GraphicsSettings.asset:40` = `m_CustomRenderPipeline: {fileID: 0}`；同文件 `:61` 的 `m_RenderPipelineGlobalSettingsMap` 反而有 URP 记录 |
| B2 | **质量档 = Very Low**：`m_CurrentQuality: 5`，该档 `shadows: 0`（无阴影）、`pixelLightCount: 0`（不渲染逐像素附加光） | `unity/ProjectSettings/QualitySettings.asset` |
| B3 | **后处理是空的**：`components: []` | `unity/Assets/DefaultVolumeProfile.asset` |
| B4 | **自研 PostFx 在 URP 下没有位置**：`WhisperPostFx.shader` 是 `OnRenderImage` + `CGPROGRAM` 的 Built-in 血统 | 文件头自述 + 官方对照表（自定义后处理在 URP 只有 Full Screen Pass 一条路） |
| B5 | 三张自定义着色器全是 Built-in 血统；全仓 C# **零** `UniversalRenderPipeline`/`RenderPipelineAsset` 引用 | `grep` |

**⇒ 抗锯齿/TAA、辉光、体积光、色差、颗粒、阴影质量、反射/折射 这一整片，在 Built-in + VL 档下无法交付。**
**修法**：ProjectSettings 是编辑器生成的 YAML（`unity/ProjectSettings/README.md` 明令禁止手写）⇒ 必须写
`-executeMethod` 的 Editor 脚本，在云端 CI 里建并指派 URP Asset + 填 Volume Profile + 修 QualitySettings。

### 一个决定顺序的硬事实（**先迁着色器，再开管线**）
官方 pass tag 表把 `ForwardAdd` 列入**不支持**清单。`WhisperLitPbr.shader` 的 Pass 2（`ForwardAdd`，手电筒/点光）
在 URP 下**根本不执行且不报错** —— 而"手电筒"正是本作的核心照明手段。
**⇒ 若先开 URP 再迁着色器，手电筒会静默死掉**（比变品红更危险：品红看得见，静默失效看不见）。

参考资料：`docs/reference-urp17-setup.md`（1345 行 · 95 个已探活的官方 URL · 22 处显式 UNKNOWN）。

## 二、关卡与套件：一个"生成了却从没被引用"的结构性缺陷

| # | 事实 | 证据 |
|---|---|---|
| G1 | 套件生成器**本就是按房间尺寸参数化**的（`recipe(arch, W, H, D, doors)`），`variants` 模式给每个「基础套件 × 尺寸 × 门洞」等效类出一个套件 | `tools/gen-kits.mjs` 头部 + `buildPlan()` |
| G2 | **但三张图的关卡 DSL 从没升到变体 id**：28 个房间里 11 个仍写通用名 → 套件适配检查判红 | `node tools/gen-kits.mjs --mode variants --lint-only` |
| G3 | 后果：`hall_main` 楼板是 18×3，摆进 7×3 的客厅 = **悬挑 11m / 缺口 2m**；同类房间共用 18m 的壳 | `lintRoomFit()` |
| G4 | `variants` 模式下"房间未适配"是硬失败 → **22 个变体一个都生成不出来** | `tools/gen-kits.mjs` 的 `if (MODE === 'variants' && unfit.length)` |
| G5 | 仓库的 `asylum_v1.json` **落后于**它自己的生成器：生成器输出 15 房间（含 lobby/boiler/两层走廊），仓库只有 11 | 跑一次生成器后 diff |
| G6 | `hall_main_entrance_safe` 的标称尺寸是 4×3，而疗养院生成器写 `h: 3.5` → 与另两张图的同尺寸玄关**落进两个等效类**，命名对不上 | 实测 `[DBG]` 打印 |

**本轮做完的**：
1. 三张图 DSL 全部升到变体 id → **房间适配 28/28**；
2. 加 `KIT_ID_OVERRIDES` 显式登记既成 id（`hall_main_entrance_safe`），不让命名规则去猜；
3. 疗养院入口层高 3.5 → 3.0，与清单标称尺寸对齐；
4. **本机 Blender 5.0.1 生成出 22 个套件**（每个 15~63 KB），清单回写 + Resources/StreamingAssets 镜像同步；
5. 入清单的那 22 个套件让「资产清单准入（C3）」从**咨询**变成**硬通过**。

### 工具侧的两处真修（都是为了绕开本机限制，而不是碰运气）
- **Blender 会被 proot SIGKILL**（`vpid 1: terminated with signal 9`）：不是内存不足（MemAvailable 4.5 GB），
  是启动器头部注释写明的 proot seccomp 与 Blender Python 初始化冲突。
  ⇒ 给生成器加 `--only <ids>`（**只影响调 Blender 那一步**，自检/适配/回写仍按全量计划算）
  与 `--list`（打印权威计划 id）。**"重跑就好"不是修法。**
- **分批时的清单口径**：`--only` 之外的套件若还没生成，登记为"待补"而**不中断** ——
  否则首批已生成好的文件会白做（实测踩过：首批 4 个成功，却因后面的 id 还没记录而整批退出，清单一行没写）。
  不变量：**写进清单的每一条都必须在磁盘上真的存在**。

## 三、跳脸：一个"做完了但永远不会播"的孤儿

| 事实 | 证据 |
|---|---|
| `JumpscareView.cs`（174 行，模型驱动鬼脸 + 红/白眼自发光 + 扑向镜头）**来自电脑端未提交工作** | `git log -- JumpscareView.cs` → `63923f5` |
| `GameBootstrap.OnPlayerKilled(bool redEyes)` 存在，但**全仓零调用者** | `grep OnPlayerKilled` → 只命中定义处 |
| `GameSession.Tick` 的接触判定**只扣理智、永不致死**（配置注释写明"接触惩罚而非即死"） | `GameSession.cs` 的 `Sanity.MonsterContact()` 分支 |

**⇒ 症状：跳脸永远不会触发。** 与本项目记录在案的失效模式同类
（几何层 / 内容管线 / 怪物实例化 / 理智系统 / 10 个大厅道具）。

**本轮接线**（保留两套设计的裁决）：
- **巡逻/调查期**贴到玩家 → 只扣理智（保住 `monsterBehavior.contactNote` 的刻意设计：
  「碰到就结束对局会让『没说话也被抓』变成必然」）；
- **追击期**贴到玩家（距离 ≤ `death.caught.killDistanceM`）→ **即死 + 跳脸**（用户要求 + 恐鬼症官方语义）；
- 纯逻辑层（`GameSession`，不依赖 UnityEngine）用**事件** `PlayerKilled` 通知表现层，
  由组合根 `GameBootstrap` 订阅 → `JumpscareView`。红眼/白眼按**致死的那只怪**查配置
  （`death.caught.redEyeWhenKilledBy`），不是在代码里写 if。
- `SessionOutcome` 增加 `DeathCause` / `KilledBy`（此前抓到致死与理智崩溃在结果里**无法区分**）。

**验证**：3 条新断言走**产品自身的入追路径**（保护期已过 + `SeenByPlayer` + 距离 ≤12m），
不是硬塞 `brain.Enter("chase")` —— 实测硬塞会被 `Step()` 的"失联 12 秒 → 回巡逻"掉回去，
那测的就不是产品路径了。

## 四、判据的修法（把"快照"换成"结构不变量"）

`native/csharp-verify/Program.cs` 里 4 条断言原本写死 `Rooms.Count == 11` / 证据点 `== 5` /
光区 `safe=1 pressure=8 high-risk=2` / 碎片 `== 1250`。这些**把一次快照当判据**：
布局一改就红，而红的原因是"数字过期"而不是"东西坏了"。已改为：
- 房间数 ≥10 且 id 唯一、尺寸为正；
- 证据点存在、数量合理、**且每个都落在可走格上**（后者才决定"能不能真拿到"）；
- 光区三档齐备且安全区**只占少数**（`safe*3 < 总数`）—— 原断言"安全区唯一"不是不变量，
  布局补齐后大厅（一层）本来就是"楼上的安全集结点"；
- 碎片按公式推导（`evidence*200 + 150 + (elapsed<600 ? 100 : 0)`）而不是写死数字；
- 「相邻房共享边只允许门洞可穿」的 `allow` 由**门宽推导**（`ceil(width/0.5)+1`）而不是写死 3 ——
  实测：3m 宽的墙开 2.0m 门，6 个采样里 4 个落在门洞里，那是**完全正确的几何**。

## 五、已知未做 / 下一步（按建议顺序）

1. **着色器迁 URP**（守 `ForwardAdd` 那条：先迁再开）。建议顺序（风险切成可回滚的小步）：
   `WhisperUnlitColor` → `WhisperLitPbr`（补 `ShadowCaster` + `DepthOnly`）→ `WhisperPostFx`（改 Full Screen Pass）。
   每步都要同机位取证图与 Built-in 基线对比。
2. **Editor 脚本启用 URP + 填 Volume Profile + 修 QualitySettings**（配方见 `docs/reference-urp17-setup.md` §2 / §4.5）。
3. **orbit 取证图上有大面积 z-fighting**（共面闪烁条纹）——`lintKit` 的 C4 只覆盖套件内部，
   套件**之间**/套件与程序化墙体之间的共面没有判据。待查。
4. **大厅/锅炉房没有专属配方**：`hall_main_lobby` / `morgue_boiler` 目前复用的是 hall / morgue 架构
   （几何尺寸已对，但"大厅该有的开阔感"与"锅炉房该有的设备"还没有）。需要补 4 个配方：`stair`/`lift`/`lobby`/`boiler`。
5. **B 代大厅工业道具的锚点未定**（六重断链第 6 环）：`docs/reference-modeling-conventions.md` §2.2 给了逐件实测偏移，
   §5 缺口 #1 给了下一步（先用 Blender 出 front/left/top 三视图定锚点）。另有 **4 件仍是 Z-up**（摆放会侧躺）。
6. **音效系统整体缺失**（全仓零 `AudioSource`/`AudioClip`；`whisper.Audio` asmdef 下只有 `LocalVoiceService`）。
   跳脸目前**没有音效冲击**——这是"跳脸"完整度的明显缺口。

---

# 附：2026-10-06 下半场（URP 迁移）续记

## 六、URP 现在真的启用了（有云端日志原文）

`unity-agent`/`build-dev-mono` 的 Unity 日志里：
```
[UrpSetup] ✓ active render pipeline = WhisperURPAsset
        (UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset)
        · MSAA=2 · HDR=True · renderScale=1 · shadowDistance=20 · cascades=2 · additionalLights=4
[UrpSetup] Volume Profile Assets/DefaultVolumeProfile.asset → 6 个组件：
        Bloom Vignette ColorAdjustments Tonemapping FilmGrain ChromaticAberration
```
⇒ 用户点名的 **辉光 / 色差 / 颗粒 / 氛围 / 色泽 / 暗角** 第一次有了落点；
阴影（主光阴影 + 软阴影 + ShadowCaster pass）也从"没有"变成"有"。

## 七、三个必须记下的真事故（都发生在同一天）

### ① 「管线已启用」必须是**编辑器级不变量**
只在 `BuildScript.BuildAndroid()` 里调 `UrpSetup.ConfigureUrp()` 的后果：
- `unity-android`（走 BuildScript）→ 成功；
- `unity-agent` 的 **render-evidence**（走 `RenderEvidenceCapture.Run`，**不经过 BuildScript**）
  → URP 从未启用，而着色器已是 URP 专用 ⇒ Built-in 下**没有任何颜色 pass**
  ⇒ **整屏纯黑**（30 张图全部退化成统一 9 KB、平均亮度 5.2、开灯/关灯差异 0.000%）。
⇒ 修法：`[InitializeOnLoadMethod]`，让**凡是能在 CI 里跑起来的东西**看到同一套设置。

### ② 我把雾的 `lerp` 参数写颠倒了（纯迁移事故）
`lerp(lit, fogColor, fogK)` 在 fogK=0 时返回 **fogColor** ⇒ 雾永远按最大浓度参与。
后果：`entrance_safe/orbit33` **开灯 2.9 < 关灯 17.6**（开灯反而更暗），被判"全黑"。
**本机门禁拦不住它** —— 门禁只能验"uniform 声明齐不齐、契约对不对"，
**验不了数学式对不对**。这条只有「看图 + 读像素」能发现。
⇒ 又一次印证本项目的失败模式：「门禁全绿但结果是错的」。

### ③ 手电的判据差距来自"灯的配置"，不是"判据太严"
迁 URP 后手电亮度差从 0.000% 掉到 0.643%（阈值 1%）。
根因两层：URP 附加光走**物理距离衰减**（Built-in 那版是手写 `1/(1+0.15d²)`）；
且取证手电没设 `range/spotAngle`（Unity 默认 10m/30°，锥太窄，取景点是走廊 → 全屏均值被摊薄）。
⇒ 修法是**把手电调成产品该有的样子**（range 18 / spotAngle 55 / **ForcePixel** / 强度 5.0），
**不是把阈值调低**。`ForcePixel` 尤其重要：`Auto` 在灯多时会被降级成顶点光，
那会让「手电不亮」变成**偶发**现象 —— 最难查的一类 bug。

## 八、判据自身跟着 URP 一起修（否则防线会静默失效）
`RenderEvidenceCapture` 的着色器契约检查原本只认 Built-in 写法，迁 URP 后：
- 代码块正则 `CGPROGRAM/ENDCG` → `(CG|HLSL)PROGRAM/END(CG|HLSL)`。
  原版在 URP 着色器上**一个块都找不到**，直接放弃 uniform 核对 ⇒ **白白丢掉这道防线**。
- 全局量标识符正则由词边界改成"前后不能是标识符字符"。
  原版会把辅助函数 `WhisperFogK` 里的子串误判成裸 `_WhisperFog`（误报）。
- `_Color` 判据允许 `[MainColor]` 特性前缀（URP 官方迁移清单第 10 步的要求），
  与 `tools/gate-test.mjs` 的 T6 **保持同一口径** —— 两处不一致会出现
  "本机门禁过、云端取证红"这种自相矛盾状态。

## 九、`build-dev-mono` 从未成功过（既有问题，非本轮引入）
```
#1 cancelled  2d9c587
#2 failure    758db8c
#3 failure    e704ace   ← 本轮
```
失败步骤是「校验产物形态（必须真的是 Mono）」：`unity/build/Android/whisper-dev-mono.apk` 不存在，
而日志显示那次构建实际走的是 **IL2CPP** 的 Bee 管线（`Prj/IL2CPP/...`）。
⇒ 待查：`WHISPER_DEV_MONO=1` 是否真的传到了 `BuildConfigurator`，以及产物落点是否一致。
**不在当前关键路径上**（取证与出货包都不走它），但它挡住了"手机端 Mono 热插拔"这条路。
