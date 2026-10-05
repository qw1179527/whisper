# 建模质量规范（Lead → build-render 共享）

> 来源：`blender-modeling` 技能（bundled）+ 用户反馈「建模真烂」+ 实测踩坑。
> **凡是本项目要出 GLB 的几何（套件/道具/角色/鬼怪）都按本文做**，不要各写各的。

## 一、用户为什么说"烂"——三个可度量的原因

| 症状 | 度量判据 | 修法 |
|---|---|---|
| **"皆为纯色与矩形方体"** | 面数/部件比过低；同一物体上法线方向种类 ≤6 | **Bevel → SubSurf**（顺序不能反），圆角 0.02m/3 段 |
| **"存在建模重叠"** | 同向共面 / AABB 相交（`GeometryOverlapAudit` 已在测） | 见 §三"深插接合" |
| **"没有细节"** | 单件部件数 < 8、顶点数 < 200 | 拆件：主体 + 次级形（把手/脚/边框/装饰） |

## 二、修改器栈顺序（背下来，顺序错=出伪影）

```
Mirror → Array → Solidify → Bevel → Subdivision Surface → (Boolean)
```

**最典型的业余错误：SubSurf 放在 Bevel 之前** → 圆角处挤压变形。
- Bevel：`width=0.02`、`segments=3`、`limit_method='ANGLE'`、`angle_limit=30°`
- SubSurf：`levels=2`、`render_levels=3`

## 三、深插接合（治"建模重叠"与"接缝"）

两个部件**面贴面**即使数学上正好相接，渲染也会出现可见接缝；不同形状相接（圆柱插方块）更明显。

**做法**：相邻部件在接合处**互相插入 5~15mm**（插进去的体积藏在较大部件内部，外面看不到）。
```python
# ✗ 面贴面：接缝可见
pommel_z = -GRIP_LEN/2 - POMMEL_R
# ✓ 深插 1cm：接缝被藏在几何体内部
pommel_z = -GRIP_LEN/2 - POMMEL_R + 0.010
```
同一材质的部件，若要**完全无缝**，用 Boolean Union 合成一个网格。

## 四、对称体必须用 Mirror（不要复制-翻转）

角色/鬼怪/道具凡左右对称的，**只做一半 + Mirror 修改器**：
```python
mod = obj.modifiers.new('Mirror', type='MIRROR')
mod.use_axis[0] = True      # 沿 X 镜像
mod.use_clip = True         # 顶点吸附到轴上
mod.use_mirror_merge = True; mod.merge_threshold = 0.001
```
Mirror 放在栈的**第一位**。复制-翻转会破坏对称（浮点误差导致左右不等）。

## 五、平滑着色

圆润部件（球/圆柱/胶囊/有机体）**必须 `shade_smooth()`**，否则每个面片边界都看得见。
方块与硬表面件可以保持平直着色（或用 modifier 版的 Auto Smooth）。
⚠ **Blender 5.x 已移除 `Mesh.use_auto_smooth`** —— 不要写它，用 modifier 或逐面 `use_smooth`。

## 六、朝向约定的"三轴语义"（做细长物体时最常错）

细长/非对称物体（刀刃、管、板、骨头、道具手柄）三轴含义不同：
- **长轴** = 物体长度
- **宽轴** = 从"有用视角"看到的宽面
- **薄轴** = 最窄的截面

**永远让宽轴朝向主视角**，否则渲染出来是根"细棍"，跟实物完全不像。
本项目约定（与 Unity 一致）：**Y 向前、Z 向上**，导出时 `export_yup=False`。

## 七、剥尖（不要靠"把顶点缩到 0"）

尖端/锥形要**把顶端顶点收到中线并 `remove_doubles` 合并成一个点**，否则表面上尖端是"平口"，
而且留下退化拓扑（多个重合顶点）。
```python
# 收拢 → 合并（threshold 0.001）
bpy.ops.mesh.remove_doubles(threshold=0.001)
```

## 八、导出前必须过的自检（本项目已有工具）

1. `blender_validate_scene`（顶点/面/非流形/游离顶点/n-gon）
2. **`node tools/verify-models.mjs`** —— 从**产出字节**里读顶点/面/材质数。
   ⚠ 它抓到过真实事故：Blender 对 `hiddenRender=True` 的对象**静默导出 0 顶点**，
   而 `export_scene.gltf` **依然返回成功**。**"导出成功" ≠ "产物非空"。**
3. `node tools/gen-meta.mjs`（新资产必须有 `.meta`）

## 九、命名约定（全项目统一，不要各写各的）

| 前缀 | 用途 |
|---|---|
| `GEO-` | 网格对象 |
| `MAT-` | 材质 |
| `LGT-` | 灯光 |
| `CAM-` | 相机 |
| `COL-` | 集合 |

**并且：每个部件单独导出一个 GLB**（文件名 = 部件名）→ 本项目 `GlbReader` 不暴露节点名/材质，
"一个文件一个物体"是唯一零歧义的寻址方式（`Resources/Models/<model>/<部件名>.glb.bytes`）。

## 十、参考（恐鬼症的官方建模取向）

`web_search` 只返回了索引页、没有正文，所以**以下只写可确证的取向，不编细节**：
- 鬼的模型是**灰白/素色人形**，特征是**发光的眼睛**在暗处先被看到 —— 与本项目
  `MAT-GhostBody`（近黑 0.86 粗糙）+ 自发光眼虹膜的做法一致。
- 玩家角色更新（2026-05）强调**体型自定义**与**第一人称可见的手/装备**。
参考链接（供你自行核对，不当作正文引用）：
- [Ghost Model（Phasmophobia Wiki）](https://phasmophobia.fandom.com/wiki/Ghost_Model)
- [All Phasmophobia ghost models（Dot Esports）](https://dotesports.com/phasmophobia/news/all-phasmophobia-ghost-models)
- [Inside the Design of Phasmophobia's Player Character Update（Xbox Wire）](https://news.xbox.com/en-us/2026/05/05/phasmophobia-player-character-update/)

**待办（需要正文才能落地的）**：鬼的**体型分类清单**（几种身形、各自比例）与玩家的
**体型/身高自定义范围** —— 目前只有索引页，**不填数值**，等能取到正文再补。
