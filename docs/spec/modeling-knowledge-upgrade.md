# 建模知识升级 v2 —— 恐怖游戏资产（鬼怪/人物/环境）高级方法

> **2026-10-04 二次学习成果：完整知识库见**
> `C:\Users\qing_\Documents\deepseek-harness\default-workspace\建模知识库\`（14 篇 / 1200 行：几何拓扑 · 硬表面布尔 · 有机体角色 ·
> UV 烘焙 · 材质 PBR · 灯光渲染 · 动画绑定 · 程序化 · 优化导出 · AI 生成与参考锁定 · 恐怖专项 · 流程质检 · 速查表）。
> 本文（v2）作为本项目最小必读版保留；深入问题查知识库对应分册。

> 来源：内置 `blender-modeling` / `blender-materials` 技能 + 全网检索（恐鬼症美术复盘、幽灵资产工作流、
> URP 幽灵着色器、LOD 指南、人体比例 Canon），叠加本项目 9 版迭代已证实结论。
> 本文是 `procedural-humanoid-method.md`（v1）的**升级**，v1 已被渲染图证实的结论仍然有效，此处只增补与修正。

---

## 一、恐怖鬼怪的造型设计法则（恐鬼症/PT 复盘结论）

恐鬼症美术复盘（[zyouxi 灵异名场面美术复盘](https://www.zyouxi.com.cn/bg/htpd/2026-04-11/462757.html)）与 PT/Silent Hill 的共性，**恐怖感来自比例失真，不是加细节**：

1. **瘦长失真（Lanky）优先于肌肉**：身高/肩宽比 ≥ 4.5:1，四肢长于正常 15–25%，手垂过膝。
   本项目 `BODIES` 表已有 lanky 档，保持并强化。
2. **头部小 + 眼睛大**：头高 ≤ 1/8 身高（正常 1/7.5），眼睛占脸宽 35%+。恐怖感 = "比例上的错误"。
3. **剪影优先**：先在剪影上做识别度（细长躯干 + 拉长的四肢 + 前倾姿态），材质再简单也成立。
4. ⚠ **【2026-10-05 用户指示作废本条】原第 4 条写的是"下半身消失 / 无腿漂浮 = 经典手法，保留"**。
   用户明令：
   > 「鬼的建模是通用的，不与鬼的类型绑定，所有鬼的建模都正常化，不要说比如随到幻影就用没腿的建模，
   >  这是错误的，所有的鬼都随机几个建模（分男女建模），只是按机制体现不同而已」
   **所以：一律产出完整人形（有腿有臂有头）。** "无腿/雾状尾梢/漂浮"在本项目**禁止使用** ——
   它既违反"建模正常化"，也违反"模型不与类型绑定"（会导致某些鬼凭外形泄露类型）。
   替代手法：靠**比例失真**（瘦长、手过膝、前倾）与**姿态**制造恐怖感，而不是砍掉肢体。
5. **眼睛 = 唯一焦点**：身体低饱和、低亮度（0.05–0.15 灰），眼睛高亮自发光。红眼/白眼切换
   保留为两套部件（已实现），**但要加"黑暗中仍可见"**：Unity 侧用 Emission + Bloom，阈值调低。
6. 皮肤**次表面散射（SSS）**：鬼怪苍白皮肤用 Subsurface Weight≈0.3、Radius=(1.0,0.3,0.25)，
   手电筒照上去会透光，这是"鬼质感"的关键（[VR 恐怖角色制作指南](https://upcommons.upc.edu/entities/publication/f686264d-f95d-4fc6-a9f8-6f4d44085e3b/full#1)）。

## 二、人体比例 Canon（修正 v1 的未解问题）

Wikipedia [Canon of Proportions](https://en.m.wikipedia.org/wiki/Canon_of_Proportions)：正常成人
**7.5 头身**（文艺复兴 8 头身是理想化）。项目参数表 `headHeads=7.5` 正确。
v1 §七 的未解问题"融合半径与设计粗细解耦"，**正解就一个参数**：

> **元球 `element.size_x/y/z`（squash）压细肢体，不要压 radius。**
> 融合靠的是**沿肢轴的 field 重叠**（radius），横截面的粗细靠 **size 轴的 squash**。
> 所以：radius 保持"融合保证值"（珠间距 ≤ radius/3），把 squash 轴压到 0.55~0.75 得到细杆四肢。

已在 `link_seg(p0,p1,r0,r1,squash=(sx,sy,sz))` 里留了接口，v1 一直传 (1,1,1)——**改传 squash 即升级**。

## 三、几何质量（内置技能 + 实测）

1. **修改器栈顺序（死记）**：`Mirror → Array → Solidify → Bevel → Subdivision Surface`。
   SubSurf 放 Bevel 前 = 挤压伪影，是最常见的业余错误。
2. **硬表面必须有 Bevel**：`width=0.012m, segments=2~3, limit_method=ANGLE`。无 Bevel 的立方体
   就是用户说的"矩形方体"。
3. **部件连接必须互相穿插 5–15mm**，禁止面贴面——数学上相接也会渲染出接缝（实测）。
4. **有机体唯一通途：Metaball**（v1 结论），元球 `radius` 是影响范围，表面 ≈ radius×0.55。
5. **每次改动后看渲染**，不要信参数：正交正视图 + 落地对齐 + 全高入画（`tools/body-views.mjs` 已做）。

## 四、移动端顶点预算与 LOD（Tripo LOD 指南 + 实测）

- **单只鬼 ≤ 8k 顶点**（一屏 3~5 只）。元球分辨率 absolute 米数：low=0.012 / med=0.008。
- 生成后用 `DECIMATE(COLLAPSE)` 压到目标（比例 = 目标/当前），导出前 UV 展开（无 UV → 10× 顶点膨胀，实测）。
- **Unity LOD Group**：LOD0 = 8k（≤5m）、LOD1 = 3.5k（5–12m）、LOD2 = 1.2k（>12m 或雾中剪影）。
  雾里只有剪影可见时，最低档可以**关掉眼睛以外的所有材质**。
- 顶点着色器摆动（vertex sway）替代骨骼动画做"漂浮鬼"：成本近乎零、恐怖感翻倍，是幽灵资产
  的标准做法（[BlenderNation 幽灵游戏资产教程](https://www.blendernation.com/2017/09/21/tutorial-series-creating-ghost-game-asset-blender/)）。
  需骨骼的（开门、扑击）再上 8–12 骨小骨架，权重用 Automatic Weights。

## 五、恐怖灯光与雾（Unity 侧，可直接用）

1. **雾是恐怖游戏第一画质手段**：URP 内置 Exponential Fog（密度 0.04–0.08，颜色近黑偏冷
   #0A0D12）零成本；进阶用 [BT Spectral Fog](https://assetstore-fallback.unity.com/packages/vfx/shaders/fullscreen-camera-effects/bt-spectral-fog-next-gen-volumetric-fog-for-urp-394724) 体积雾。
2. **手电筒 = Spot + Cookie + 锥形雾**：Range 8–12m、SpotAngle 35–45°、开阴影；光锥在雾里
   形成可见光柱，这是恐鬼症氛围核心。
3. **闪烁灯**：Point Light intensity 用 `Mathf.PerlinNoise` 或随机步进驱动，闪烁时同步调
   Emission 材质强度（鬼眼睛在闪烁瞬间亮起 = 主界面要求的"灯光闪烁时远处刷新鬼怪"）。
4. **鬼眼可见性**：URP Emission + Bloom（Threshold 0.9，Intensity 1.2），眼睛材质 emission
   强度 3–6（Blender 预览里小网格要 ×1500 才亮，Unity 里用 HDR 颜色直接 >1）。
5. 参考：[URP Ghost Shaders](https://assetstore.unity.com/packages/vfx/shaders/urp-ghost-shaders-38092)、
   [Volumetric Light System](https://assetstore.unity.com/packages/vfx/shaders/volumetric-light-system-fast-lights-fog-for-urp-hdrp-built-in-333788)。

## 六、材质升级（内置技能 12 配方，只列本项目要用的）

| 对象 | 配方 | 关键值 |
|---|---|---|
| 鬼怪皮肤 | Principled + SSS | Base(0.62,0.6,0.58) 苍灰、Rough 0.5、SSS Weight 0.3、Radius(1,0.3,0.25) |
| 红眼 | **Emission 替代 BSDF** | (1.0,0.04,0.03)、Strength 1500（小网格） |
| 白眼 | Emission | (1.0,1.0,0.98)、Strength 1500 |
| 医院墙 | Matte plastic/混凝土 | Rough 0.85–0.95、Metallic 0（金属度只能是 0 或 1，中间值必烂） |
| 金属器械/门把手 | Metallic=1 | 钢 F0 (0.56,0.57,0.58)、Rough 0.25–0.5 |

glTF 导出注意：**程序纹理不导出**，要贴图就得 bake；本项目纯色 + 顶光即可，先不引入贴图。

## 七、自研工具现状 → 升级动作（whisper 仓库）

> **2026-10-04 补充：本机 Blender MCP 已内置下列能力，优先直接用，不要重写。**
> 服务地址 `http://127.0.0.1:8765/mcp`；详细说明见工作区 `MCP-建模能力升级.md`。
> - `blender_bodygen(build, height, legs, limb_squash, target_verts)` —— 8 种体型预设参数化出人/鬼，
>   `headHeads` 为头身比唯一真源，脚底自动贴地，返回头身比/肩腰髋宽度/是否穿地。
> - `blender_qa(image_path, reference_path?)` —— 把渲染图变成文字：前景比例、主体包围盒、裁切边、
>   10 段剪影宽度、主色 hex+占比、ASCII 色块图、A/B 差异图（自动排除地面）。
> - `blender_finalize(save_path, export_path, target_verts, ...)` —— UV + Decimate + 平滑 + 存盘 +
>   导出 + 预览 + QA 一条命令。
> - `blender_python` 脚本内可用 `dsh_compat`：`info()` / `keep_unused_data()` / `principled()` /
>   `eevee()` / `fog_glow()` / `MetaballBuilder` / `metaball_surface_factor()` / `face_front_y()` /
>   `qa_image()` / `finalize()`。

```
tools/whisper-model3.mjs  元球生成（✓ 唯一通途）   → 改：肢体加 squash 细杆 + 下缘 wisp 收尖
tools/body-views.mjs      生成进程内 4 视图渲染（✓）→ 加：暗场 + 手电筒单灯的恐怖视角渲染
tools/glb-bbox.mjs        客观包围盒（✓）
tools/verify-models.mjs   导出后字节核（✓）         → 加：LOD 三档顶点数断言（8k/3.5k/1.2k）
tools/gen-kits.mjs        房间套件（✓）             → 补：所有硬表面 Bevel（用户"矩形方体"根因）
```

升级后的示例鬼怪由本会话直接产出：`whisper-models/ghost-upgraded.blend` → `.glb`（MCP Blender 工具链
实测，含 SSS 皮肤、发光红眼、wisp 尾梢、UV、Decimate ≤8k、GLB 校验）。

## 八、给子智能体的十条铁律（照做，别手调）

1. 有机体 = 元球（珠间距 ≤ radius/3）；硬表面 = 立方体 + **Bevel** + SubSurf。
2. 修改器栈：Mirror → Array → Solidify → Bevel → SubSurf。
3. 部件穿插 5–15mm，禁止面贴面。
4. 修改器/几何每步后**渲染验证**，参数不算数。
5. 导出前：UV 展开 + Decimate 到预算 + `export_yup=False`。
6. 金属度 0 或 1，没有中间值。
7. 单只鬼 ≤ 8k 顶点，LOD 三档 8k/3.5k/1.2k。
8. 眼睛用 Emission（不是提亮 Base Color）；身体低饱和、靠 SSS 出质感。
9. 雾 + 手电筒锥光先于任何建模细节——氛围第一。
10. 每完成一件资产：`glb-bbox.mjs` + `verify-models.mjs` + 渲染图三件套才算完。
