# 建模与套件约定 · 工程参考（whisper）

> **用途**：写新生成器、接新套件、摆大厅道具之前读这一份。它只记录**有出处**的约定；
> 查不到出处的一律写 `UNKNOWN`（见 §5），不写"大概是这样"。
>
> **不覆盖**：玩法/网络/UI 约定；本文件只管"几何资产从 Blender 到产品里"这条链。

## 0. 怎么读这份文件

### 0.1 真源顺序
1. **用户直接指令**（最高）
2. **电脑端文档** `PC/01-工程源码/docs/`（`five-fixes-and-modeling-redo-plan.md` 等）
3. **仓库内工程约定**：`AGENTS.md`、各工具头部注释里记录的实测教训

出处：`P/docs/authority-and-constraints-2026-10-06.md:35-40`。PDF 方案（V5~V9）**已退出真源序列**（同文件 `:4-9`、`:40`）。

### 0.2 路径前缀
| 前缀 | 绝对路径 | 说明 |
|---|---|---|
| `P/` | `/data/user/0/app.dsh.mobile/files/dsh-home/whisper/` | 手机上的工程仓（脚本最新，2026-10-06） |
| `PC/` | `/storage/emulated/0/DSH专用/电脑上/` | 电脑端快照 |
| `PC/知识/` | `PC/05-建模知识/` | 建模知识文档（与 `P/docs/` 下同名文件是同一份内容） |
| `PC/存档/` | `PC/09-工作区whisper独有存档/工作区whisper独有/` | **只读存档**：`tools/gen-hall-kits.mjs`、`tools/kit-builder.py`、`tools/lib/kit-hall.mjs`、`tools/sync-footprints.mjs`、10 件 `unity/Assets/ThirdParty/CC0/props/Hall_*.glb` **只存在这里** |
| `PC/04-Blender/` | `PC/04-Blender/` | 只有 `gen-kits.mjs`，与 `P/tools/gen-kits.mjs` **md5 相同**（`11a42ec243b555c3ba5b713a37190045`） |

### 0.3 证据标注（本文件全篇遵守）
- **【文档】**：有文件 + 行号，可点回原文。
- **【实测】**：本轮直接读 GLB 字节得到（方法见 §0.4），可复跑。**它不是文档里写下的约定**，是当前产物的客观状态。
- `UNKNOWN — 证据缺口 #n`：无任何出处，指向 §5 的第 n 条。

### 0.4 复跑方法（本节所有【实测】数字都由此而来）
- 读"**含节点平移**的真实包围盒"：照 `P/tools/gate-asset-bbox.mjs:68-91` 的口径（遍历 `scenes[0].nodes`，逐级累加 `node.translation`，再合并各 `primitive.attributes.POSITION` 的 accessor `min/max`）。
- ⚠ **不要用** `P/tools/glb-bbox.mjs` 单独下结论：它只合并 accessor 的 min/max、**忽略 node.translation**（`P/tools/glb-bbox.mjs:13-27`），对"原点不在几何中心"的资产（§2.2 B 代那 10 件）会给出错误印象。它适合快速判"长轴在哪个轴"。
- 读结构/材质/属性：解析 GLB 的 JSON chunk（`magic=0x46546c67`，长度在偏移 12，JSON 从偏移 20 开始），见 `P/tools/register-kit-glb.mjs:63-82`。
- 本轮实测覆盖：`P/unity/Assets/Resources/Kits/` 22 个 + `P/unity/Assets/ThirdParty/CC0/` 12 个 + `PC/存档/…/CC0/props/` 10 个 = **44 个套件 GLB**。

---

## 1. 套件 / 资产契约（权威）

### 1.1 单位
- 单位**米**；配方里的 `size`/`h` 一律是**全尺寸**，不是半长。
- 出处：`PC/存档/tools/lib/kit-hall.mjs:17`；`PC/存档/tools/kit-builder.py:72`（`size = 全尺寸`）；`P/tools/gen-kits.mjs:113-118`（`mkBox` 收 min/max，任一轴尺寸非正即抛错）。

### 1.2 坐标系：两条创作链，一个 GLB 口径

| 链 | 创作空间 | 导出开关 | 出处 |
|---|---|---|---|
| **gen-kits 链**（房间套件 `hall_main*`/`hospital_ward`/`morgue` + 家具 `bed_b`/`cabinet_a`） | **Y-up**：`y` = 高度，`x`/`z` = 地面平面 | `export_yup=False` | `P/tools/gen-kits.mjs:113-127`（`mkBox(name,x0,x1,y0,y1,z0,z1)`；轮子 `mkWheel(...cy...)` 的 `cy` 是高度）、`:232-235`（楼板 `y∈[-0.10,0]`、天花板 `y∈[H-0.12,H-0.005]`）、`:638-641` |
| **kit 链**（大厅工业道具 10 件、货车样板） | **Blender Z-up**：`z` = 高度 | `export_yup=True` | `PC/存档/tools/kit-builder.py:6-12`（含 A/B 实测证据）、`:215-216`；`PC/存档/tools/lib/kit-hall.mjs:9-14`；`PC/存档/tools/gen-hall-kits.mjs:14-17` |

**唯一权威口径（两条链的共同产物要求）**：GLB 是 **Y-up**，`Y` = 高度，地面平面 = XZ，`footprint = [宽(x), 深(z)]`（米）。
- 出处：`P/unity/Assets/Data/asset-manifest.json:245`（`footprintNote` 原文）；`P/docs/p1-lobby-kits-starting-point-2026-10-06.md:60-62`（「套件的 Y-up 约定：`footprint=[宽(x),深(z)]`，Y=高度」）；`P/unity/Assets/Scripts/Runtime/TruckScene.cs:46-48`（Blender Z-up 导出 → Unity Y-up 的换算结果）。
- ⚠ 反例（**别照抄**）：`PC/知识/truck-kit-integration-and-glbmaterial-gap.md:9` 曾把 `export_yup=False` 写成"本仓轴系契约"，而那一版货车 GLB 的 Y 正是**车长 9.15 m**（轴转错，后来由 `P/tools/fix-truck-axis.mjs` 修回）。
- 轴系硬门禁是 `gate-asset-bbox`（`P/docs/authority-and-constraints-2026-10-06.md:23`）。
- ⚠ **`export_yup` 的取值只对某一条链成立**：`PC/知识/five-fixes-and-modeling-redo-plan.md:68-69`「`gen-kits.mjs` 的 `export_yup=False` 是**正确**的，不要再"修"它」说的是 **gen-kits 链**（因为它的配方本来就是 Y-up）。把这条规则照抄到 Z-up 配方上，产物就会"高度跑到 Z"（事故见 §4.3、§4.4）。

### 1.3 原点与落位
- 权威句子：`PC/存档/tools/lib/kit-hall.mjs:18`「每个模块**贴地**（最低 z ≈ 0），便于 LevelBuilder 直接按 **y=0** 落位」。
- 运行时的做法：`P/unity/Assets/Scripts/Runtime/TruckScene.cs:143-144`「套件顶点已在"世界坐标"（构建时应用了 node 变换），故直接置于本车原点下。高度对齐：**套件脚底在 y=0**」。
- node 变换确实被读取器烘焙：`P/unity/Assets/Scripts/Gameplay/Level/GlbReader.cs:33`（`Primitive` = "已按 node 世界变换烘焙到模型空间"）、`:245`（`world.XformPoint(...)`）。
- **房间套件是例外**（不是"落地"而是"落房"）：楼板顶面 `y=0` 就是房间地面、原点 = 房间中心，装配时部件 `localPosition = Vector3.zero`：`P/tools/gen-kits.mjs:229-235`、`P/unity/Assets/Scripts/Gameplay/Level/LevelBuilder.cs:115-117`。
- **道具的二次对齐**：关卡装配把整套道具下移 `-minY`（`LevelBuilder.cs:328-336`）；大厅 `HallScene.Furnishing.PlaceKit` **不做任何原点修正**（`P/unity/Assets/Scripts/Runtime/HallScene.Furnishing.cs:90-108`）。
- **`mount`（仅 kit 链有此概念）**：`PC/存档/tools/gen-hall-kits.mjs:196-197` 原文——
  `floor` = 贴地落位（min Y ≈ 0）；`ceiling` = 吊顶件（**连接点在 Y=0，向下悬挂**）。配方侧标注：吊灯 `PC/存档/tools/lib/kit-hall.mjs:58`、风管 `:169`。
  【实测】档案里 `Hall_LampIndustrial.glb` 世界 bbox `Y ∈ [-0.680, 0.060]`（连接点在 Y=0、向下悬挂 ✓）；`Hall_DuctSection.glb` `Y ∈ [0.145, 0.575]`（**在 Y=0 之上**，与"向下悬挂"的定义不符）→ 缺口 #7。

### 1.4 footprint 语义
- 定义（清单真源逐字）：`footprint=[宽(x),深(z)]`，单位米，**0 度放置时**的占地尺寸；道具按 `rot` 旋转后参与碰撞/门洞判定 —— `P/unity/Assets/Data/asset-manifest.json:245`。
- 生成期怎么来：道具由部件 bbox 的 XZ 推出（`P/tools/gen-kits.mjs:489-495`）；kit 链由 **GLB 实测包围盒**推出（`PC/存档/tools/gen-hall-kits.mjs:169-177`：`footprint = [size[0], size[2]]`）。
- 门禁核对：`P/tools/gate-asset-bbox.mjs:12-16`（B1 可解析 / B2 footprint ↔ 占地 XZ / B3 躺平），实现 `:140-166`（XZ 差 > 0.05 m 判红）。
- **三处运行期消费点表**（手工维护必然漂移，必须由脚本重建）：`PC/存档/tools/sync-footprints.mjs:5-19` 列出
  ① `LevelGeometry.cs` 的 `KitFootprint`（碰撞盒，缺 key 静默回退 `0.8×0.8`）
  ② `LevelAssembly.cs` 的 `KitFootprint`（可见占位盒，同回退）
  ③ `tools/gen-asylum-v1.mjs` 的 `FOOTPRINT`（生成期越界/压门洞检查）。
  当前内容：`P/unity/Assets/Scripts/Gameplay/Level/LevelGeometry.cs:226-230`（只有 `bed_b`/`cabinet_a`）、`LevelAssembly.cs:72-77`（同）、`P/tools/gen-asylum-v1.mjs:154-157`（`bed_b`/`cabinet_a`/`hospital_ward`/`hall_main`/`morgue`）。
- ⚠ **漂移已发生**【实测】：清单 `hall_main=[18,3]`、`morgue=[2,3]`，而 `gen-asylum-v1.mjs:156` 仍写 `hall_main:[16.0,3.0]`、`morgue:[3.0,3.0]`；且 `sync-footprints.mjs` **不在手机仓**（只在 `PC/存档/`）→ 缺口 #8。
- 旋转口径（两处一致，勿各写一套）：`rot%180 ∈ [45,135)` 即视为 90° 放置、宽深互换 —— `P/unity/Assets/Scripts/Gameplay/Level/LevelGeometry.cs:238-240`、`LevelAssembly.cs:82-85`、`P/tools/gen-asylum-v1.mjs:158-165`。

### 1.5 命名契约
- **套件 id**：工业道具 `Hall_<Name>`；房间类 `hall_main` / `hospital_ward` / `morgue`，变体追加房间 id：`hall_main_<roomId>`。出处：`P/tools/gen-kits.mjs:420-427,475-486`。
- **变体 id → 基础 id**：前缀匹配、**取最长者**（`P/tools/gen-kits.mjs:430-441`）。运行时 `KitMeshLibrary.GetParts(kitId)` 用的键就是**关卡 DSL 里的变体 id**（同文件 `:434-435` 注释）。
- **部件（节点）名 = 语义名**：房间套件**必须有名为 `floor` 的部件**（footprint 真源，门禁按名找它）；道具回退顺序 `floor → body → frame` —— `P/tools/gate-asset-bbox.mjs:140`，同口径 `P/tools/gen-kits.mjs:510-512`。
- **Blender 对象名（kit 链）= `GEO-<id>`**：`PC/存档/tools/lib/kit-hall.mjs:270`；【实测】档案 10 件的节点名均为 `GEO-Hall_*`。
- **材质名**：
  · kit 链 = 角色名 `steel/dark/coat/wood/enamel/brass/glass`（`PC/存档/tools/kit-builder.py:34-45`）；
  · gen-kits 链 = `role_<role>`（`P/tools/gen-kits.mjs:624`）；
  · PC 能力包所出 = `MAT-<preset>`（【实测】`MAT-metal_painted`/`MAT-wood_oak`/`MAT-plastic_gloss`/`MAT-metal_brushed`；预设名出处 `PC/知识/modeling/HANDOFF-建模.md:78`）。
- **运行时按材质名映射材质族，不按下标**（下标随导出顺序变）：`P/unity/Assets/Scripts/Runtime/TruckScene.cs:174,195`（`FamilyOf(string name)`）、`P/unity/Assets/Scripts/Runtime/HallScene.Furnishing.cs:130-146`（同处注释「这条是 `TruckScene` 用血换来的」）。

### 1.6 文件形态与落点（三处放置）
| 落点 | 形态 | 谁读 |
|---|---|---|
| `unity/Assets/ThirdParty/CC0/{kits,props}/<id>.glb` | 原始 `.glb` | **真源**；`gate-model` M9/M10、`gate-asset-bbox` 读这里（目录由 `manifest.sourceRoot` 决定，`P/unity/Assets/Data/asset-manifest.json:5`） |
| `unity/Assets/StreamingAssets/Kits/<id>.glb` | 原始 `.glb` | 兜底 + 构建产物取证（`KitMeshLibrary.cs:122-137`、`P/tools/verify-packed-kits.mjs:12-18`） |
| `unity/Assets/Resources/Kits/<id>.glb.bytes` | **`.bytes`** | 运行时首选（`KitMeshLibrary.cs:108-129`） |

- 三处放置的权威表格：`P/tools/register-kit-glb.mjs:11-16`；同一口径：`P/tools/gen-kit-resources.mjs:12-16`。
- **加载路径写 `Kits/<id>.glb`，不要写 `.glb.bytes`**（Resources 只剥**最后一个**扩展名）：`P/unity/Assets/Scripts/Gameplay/Level/KitMeshLibrary.cs:116-120`；测试也用同一条路径钉住：`P/unity/Assets/Scripts/Tests/EditMode/KitAssetTests.cs:20-24,64`。
- 为什么不能把 `.glb` 直接放 Resources：Unity 用 **ModelImporter** 处理 `.glb`，而 Unity 原生不支持 glTF → 既没网格也不是文本资产，`Resources.Load<TextAsset>` **恒为 null**；`.bytes` 才被当二进制文本资产导入（`KitMeshLibrary.cs:16-22,109-114`）。Android 上 StreamingAssets 在 APK 内、**不能**用 File API 读（`gen-kit-resources.mjs:44-58`）。
- `Assets/Resources/**` 里的资产**无条件进包** → 套件必须落到 Resources 才算"在产品里"（`P/tools/gen-kit-resources.mjs:6-11`）。
- **清单是资产的唯一入口、禁止手工改**：`P/unity/Assets/Data/asset-manifest.json:4`（`note` 原文）；`PC/知识/five-fixes-and-modeling-redo-plan.md:63-64`。
- 清单条目字段（照抄既有条目）：`id · kind · file · tags · addressablesGroup · sha256 · bytes · triangles · generator · footprint · resPath` —— `P/tools/register-kit-glb.mjs:18-19,91-102`；`kind ∈ {room, prop}`（`P/tools/gen-kits.mjs:420-427`）。
- `mount` 字段只有 kit 链会写（`PC/存档/tools/gen-hall-kits.mjs:196-197`）；**现清单里 0 条**【实测】→ 缺口 #7。

### 1.7 材质 / 顶点色
- 灰盒角色色表：kit 链 7 角色（`PC/存档/tools/kit-builder.py:34-45`，含 metallic/roughness 表）；gen-kits 链 6 角色（`P/tools/gen-kits.mjs:99-108`）。
- **kit 链每个套件 = 单网格单材质**（消除内部接缝）：`PC/存档/tools/kit-builder.py:16`、`:202-205`。【实测】档案 10 件均为 1 node / 1 primitive / 1 材质。
- **gen-kits 链每个部件一个材质、节点不合并**：`P/tools/gen-kits.mjs:620-635`。【实测】`hall_main.glb` = 29 nodes / 29 meshes / 29 prims / 5 材质。
- **货车 = 合并对象、按材质槽拆 primitive**：1 node / **6 primitives / 6 材质**（【实测】`P/unity/Assets/ThirdParty/CC0/kits/truck_eurocargo.glb`）；「第 i 个 primitive ↔ 第 i 个材质」的对应关系出处 `PC/知识/truck-kit-integration-and-glbmaterial-gap.md:33-36`。
- 运行时**只读 `pbrMetallicRoughness` 三项**（`baseColorFactor`/`metallicFactor`/`roughnessFactor` + `name`），**不支持贴图**：`P/unity/Assets/Scripts/Gameplay/Level/GlbReader.cs:121-139`；`PC/知识/glb-material-read-step1.md:17`（原文：「贴图需要纹理资源通道，本仓没有，故**不假装支持**」）。
- 缺材质时 `MaterialIndex = -1` → 装配端必须走平材质回退（`GlbReader.cs:44-49`）。
- **顶点色**：【实测】44 个 GLB **没有一个带 `COLOR_0`**；产品侧曾以"GLB 顶点色是白的"为理由改为按部件分别上色 —— `P/unity/Assets/Scripts/Gameplay/Level/KitMeshLibrary.cs:70-72`。
- 产品的 `MaterialFamily` 枚举**没有玻璃族**（【实测】只有 `Plaster/Concrete/Wood/Metal/RustMetal/Tile/Fabric`）→ 玻璃用 `Tile` + 压暗基色代替：`PC/知识/truck-material-fix-2026-10-05.md:14-20`；`P/unity/Assets/Scripts/Runtime/HallScene.cs:308-311`（同处注释）。

### 1.8 UV 与切线
- 期望：每个套件至少有 **UV0**。kit 链用 `smart_project(angle_limit=66°, island_margin=0.02)` 展 UV（`PC/存档/tools/kit-builder.py:145-155,210`）；gen-kits 链不显式展 UV，靠 Blender 图元的默认 UV（`P/tools/gen-kits.mjs:629-632`，只 `transform_apply(scale=True)`）。
- 【实测】44 个 GLB 的属性集合**全都是** `NORMAL,POSITION,TEXCOORD_0`：
  · **有** `TEXCOORD_0`（UV0 ✓）· **无** `TANGENT` · **无** `COLOR_0` · `primitive.mode` 未写（默认 4 = TRIANGLES）· 每个 POSITION accessor 都有 `min`/`max`（门禁与运行时都读它）。
- 运行时读 UV0、但材质不采样贴图；法线由 `RecalculateNormals()` 重算（`P/unity/Assets/Scripts/Gameplay/Level/KitMeshLibrary.cs:211-218`）→ 自带法线不参与渲染。
- **当前契约 = 有 UV0、无切线、无贴图**。要上贴图必须先扩 `GlbReader`（缺口 #9）。

### 1.9 面数预算与几何卫生
- mobile `building_module` = **200~1000 tri**：`PC/知识/modeling/HANDOFF-建模.md:54-56`（阈值表 `modeling-thresholds.json` 路径在同文件 `:49`）。
  kit 链把它写成常量 `TRI_BUDGET`（`PC/存档/tools/lib/kit-hall.mjs:261`）并在**生成期**判红、超预算**不写清单**（`PC/存档/tools/gen-hall-kits.mjs:109-111,161-167`）。
- gen-kits 链配方级上限用 **900**（`P/tools/gen-kits.mjs:560-561`）。
- 硬表面做法：Bevel **角度限制**（只倒 90° 硬边、不动圆柱面）：`PC/存档/tools/kit-builder.py:15,121-129`；`gen_hall_modules.py:8,82-91`（并注明 Bevel 必须在 SubSurf 之前）。
- 相接部件**重叠 5~15 mm**；`P/tools/gen-kits.mjs:90` 把最小重叠写成 `JOIN = 0.02`，注释原文「**绝不用 0 间隙贴合**（0 间隙 = 共面）」；共面重叠判红在 `:545-558`；天花板顶面再低 5 mm（`CEIL_TOP_GAP`，`:89,232-235`）。
- 命名禁止留默认名（`Cube.027` 之类）：`gen_hall_modules.py:11`。

### 1.10 门禁矩阵（谁在守契约）
| 门禁/测试 | 判据 | 覆盖 | 出处 |
|---|---|---|---|
| M6 | 道具落地不悬空、不压门洞通道 | 关卡 DSL 的 prop 落位 | `P/tools/gate-model.mjs:14` |
| M9 | GLB 容器有效（magic/version/length、JSON chunk、meshes/nodes/materials 计数） | 结构；**不证明"看起来对"** | `gate-model.mjs:20,27,315-333` |
| M10 | 清单记录 ↔ 产物（sha256/bytes） | 清单一致性 | `gate-model.mjs:21,336-348` |
| M11 | 关卡引用的 kit 都在清单且 kind 匹配 | DSL ↔ 清单 | `gate-model.mjs:22,351-363` |
| B1/B2/B3 | GLB 可解析 · footprint ↔ **占地节点** XZ（±0.05）· 占地件躺平 & 房间套件整体高 ≤ 房间层高+0.15 | **轴系与占地**（硬判据） | `P/tools/gate-asset-bbox.mjs:12-16,140-166` |
| 产物核验 | 构建产物里套件文件在、字节数/sha256 与清单一致、GLB 头合法 | "真的进包了" | `P/tools/verify-packed-kits.mjs:12-18` |
| EditMode | 清单里每个套件经产品路径取到非空字节、可被 `GlbReader` 解析 | Unity 侧可加载 | `P/unity/Assets/Scripts/Tests/EditMode/KitAssetTests.cs:26-52,64` |

⚠ **以上判据全部是 manifest-driven**（遍历 `manifest.kits`：`gate-asset-bbox.mjs:127`、`gate-model.mjs:336-363`、`verify-packed-kits.mjs:30`、`KitAssetTests.cs:26-52`）→ **未登记的资产对所有门禁与测试隐形**（§4.1）。

---

## 2. 锚点与朝向语义

### 2.1 房间套件（gen-kits 链：`hall_main*` / `hospital_ward` / `morgue`）
| 项 | 约定 | 出处 |
|---|---|---|
| 局部原点 | 房间**中心**在地面平面：几何 `x∈[-W/2,W/2]`、`z∈[-D/2,D/2]`，楼板 `y∈[-0.10,0]`（**楼板顶面 y=0 = 房间地面**） | `P/tools/gen-kits.mjs:229-235`；`P/unity/Assets/Scripts/Gameplay/Level/LevelBuilder.cs:115-117` |
| 高度 | 天花板顶面在 `y=H-0.005`，天花板厚 0.12、四边内收 0.09 | `P/tools/gen-kits.mjs:89,232-235` |
| "墙内表面"位置 | 房间边界内缩 `WALL_IN = WALL_T/2 = 0.11`（墙厚 0.22）；线脚凸出/嵌入量见 `TRIM_PROUD`/`TRIM_D` | `P/tools/gen-kits.mjs:84-96,194-199` |
| 朝向（通用） | 套件**不含门洞**：真实墙体走程序化"按门洞切段"，套件是实心壳（含楼板/天花板/墙裙/顶角线/家具） | `LevelBuilder.cs:93-98` |
| 门洞语义 | 门的**唯一真源是关卡 DSL**：`x0/x1/z0/z1` 是最小/最大角、`doors[].at` 是**沿墙绝对坐标**（east/west→z，north/south→x）、相邻两房门位必须一致 | `PC/知识/multi-map-build-plan.md:29-35` |
| 门套线脚 | 只对**门位确定**的架构做；`morgue` 不做门套（两间房南北门位不同，共用套件会错位） | `P/tools/gen-kits.mjs:205-224,246-248` |
| `hall` 架构朝向 | 顶梁沿长轴 **X**、贴 **±Z** 墙；壁柱贴 ±Z（间距 ~4.5 m、只在 `W≥6` 时做）；线管贴 ±Z、高 2.26~2.36 | `P/tools/gen-kits.mjs:270-274,275-285,286-291` |
| `ward` 架构朝向 | **窗在北墙（+Z）**（外框 1.40×1.10、凸出内表面 0.07、嵌入墙 0.04）；窗下暖气片同样在 +Z；顶梁沿 Z、位于 `x=±0.72` | `P/tools/gen-kits.mjs:302-306,308-323` |
| `morgue` 架构朝向 | 排水沟沿 **Z** 居中；两侧冷柜贴东西墙、**正面朝中央通道（±X）**；柜外沿距墙内表面 0.02 | `P/tools/gen-kits.mjs:335-338,340-360`（`face`/`dir` 在 `:346-347`） |
| 只做东西墙墙裙 | `TRIM_WALLS.morgue = ['east','west']` | `P/tools/gen-kits.mjs:158` |
| 变体与房间的一一对应 | 变体模式下房间必须引用"为它那一类出的那一个套件"（等号判据，防 DSL 退回旧 kit 名） | `P/tools/gen-kits.mjs:575-584,696-701` |

### 2.2 大厅工业道具（`Hall_*`）—— **两代同名产物并存**（本节的关键）
两代都叫 `Hall_<Name>`，但**配方、原点、材质命名都不同**：

**A 代 = kit 链产物**（`PC/存档/工作区whisper独有/unity/Assets/ThirdParty/CC0/props/Hall_*.glb`，10 件；手机仓里没有）
- 锚点规则**可从代码推出**：`join()` 把 `parts[0]` 设为 active → **对象原点 = 配方里第一个部件的中心**（`PC/存档/tools/kit-builder.py:107-118`），再按 `export_yup=True` 转到 GLB（`:215-216`）；材质名 = 角色名。
- 【实测】（含 node 平移的真实包围盒）：
  | id | node translation (GLB) | 世界 bbox（min → max） | 推出的锚点 |
  |---|---|---|---|
  | `Hall_Barrel` | `[0, 0.44, 0]` | X ±0.307 · Y 0→0.903 · Z ±0.307 | 桶轴中心、**底面 y=0**、footprint 居中 ✓ |
  | `Hall_IBeamColumn` | `[0, 0.011, 0]` | X ±0.21 · Y 0→3.11 · Z ±0.21 | 柱脚底面 y=0、footprint 居中 ✓ |
  | `Hall_PalletWood` | `[0, 0.055, 0.35]` | X ±0.60 · Y 0→0.126 · Z ±0.40 | 底面 y=0、1.20×0.80 footprint 居中 ✓ |
  | `Hall_CrateWood` | `[-0.293, 0.124, 0]` | X ±0.30 · Y 0→0.60 · Z ±0.30 | 底面 y=0、footprint 居中 ✓ |
  | `Hall_RackUpright` | `[0, 0.97, 0.04]` | X ±0.06 · Y 0→1.944 · Z ±0.10 | 底面 y=0、footprint 居中 ✓；**冲孔/连接面在 +Z** |
  | `Hall_RackBeam` | `[0, 0.08, 0]` | X ±1.22 · Y −0.02→0.203 · Z ±0.079 | 跨件，X 居中 ✓；两端端板 −X/+X |
  | `Hall_ElectricPanel` | `[0, 0.36, 0]` | X ±0.25 · Y 0.01→0.75 · Z −0.10→0.135 | 落地；**门/锁/把手面在 +Z** |
  | `Hall_DuctSection` | `[0, 0.36, 0]` | X ±1.145 · Y 0.145→0.575 · Z −0.46→0.286 | 沿 X 的管段；吊架在 −Z 侧 |
  | `Hall_LampIndustrial` | `[0, 0.03, 0]` | X ±0.299 · Y −0.68→0.06 · Z ±0.299 | **连接点在 Y=0、向下悬挂 ✓**（ceiling 语义成立） |
  | `Hall_PipeFlange` | `[0, 0.333, 0]` | X −0.158→0.333 · **Y −0.817→1.483** · Z ±0.157 | ✗ **长轴落在 Y**（2.30 m），与配方注释「长 2.30（沿 X）」（`kit-hall.mjs:75`）矛盾 → 缺口 #6 |
- **"哪一端是连接面"的答案（A 代，可推出）**：`Hall_RackUpright` 的**连接面 = +Z** —— 依据：配方里前肢 `post_f` 在 Blender `y=-0.04`、4 个冲孔在 `y=-0.058`（`PC/存档/tools/lib/kit-hall.mjs:241,252-254`），而 `export_yup=True` 的映射是 `glb_z = -blender_y`（`kit-builder.py:6-12`）→ +Z；`Hall_RackBeam` 是**沿 X 的跨件**（梁身 2.34 m、整体到 `x=±1.22`，`:216-218`）⇒ 一排货架 = 两根立柱沿 X 相距 ≈ 2.44 m、梁挂在立柱 **+Z** 面，"正面" = +Z。**此结论只对 A 代成立**。
- ⚠ A 代【实测】tri 全部落在 200~1000（396/444/472/484/668/680/806/860/888/968）→ 它是**符合 kit 链契约**的那一批。

**B 代 = 产品里现存的那批**（`P/unity/Assets/Resources/Kits/Hall_*.glb.bytes`，10 件；`Resources/Models/hall/` 还有一份**同 md5 副本**）
- 来源是 `gen_hall_modules.py` 家族的一个旧 revision（单节点、单材质，材质名 `MAT-*` 说明它在 PC 侧被能力包预设化过）；手机仓用 `P/tools/fix-prop-axis.mjs` 做过"**只旋顶点 + 抬到 y=0**"的修补（`:118-156`），**没有动 node.translation**（同文件只写 POSITION/NORMAL；`MIRROR` 在 `:44`）。
- 【实测】真实锚点状态（这是"摆不对"的直接原因）：
  | id | node translation (GLB) | 世界 bbox（min → max） | 后果 |
  |---|---|---|---|
  | `Hall_Barrel` | `[0, 0, 0.44]` | X ±0.307 · Y 0→0.893 · Z 0.133→0.747 | 贴地 ✓，**水平中心偏 +0.44 m (Z)** |
  | `Hall_IBeamColumn` | `[0, 0, 1.55]` | X ±0.17 · Y 0→3.10 · Z 1.38→1.72 | 贴地 ✓，**偏 +1.55 m (Z)** |
  | `Hall_PalletWood` | `[-0.514, 0, 0.055]` | X −0.60→0.60 · Y 0→0.066 · Z −0.345→0.455 | 贴地 ✓，**偏 −0.514 m (X)** |
  | `Hall_CrateWood` | `[0.291, 0, −0.24]` | X ±0.302 · Y ±0.302 · Z ±0.301 | **原点在立方体中心** → 按"底面"摆会陷 0.30 m |
  | `Hall_RackUpright` | `[0, 0.042, 1.30]` | X ±0.066 · Y 0.042→1.844 · Z 1.328→1.356 | **悬空 4.2 cm**；偏 +1.30 m (Z)；厚仅 0.028 m（薄板，非双肢） |
  | `Hall_RackBeam` | `[0, 0, 0]` | X ±1.203 · Y −0.034→0.034 · Z ±0.061 | 原点在中截面；"梁高"实际只有 0.068 m 在 Y |
  | `Hall_LampIndustrial` | `[0, 0, −0.425]` | X ±0.298 · Y 0→1.076 · Z −0.723→−0.127 | 被**落地化**：吊顶连接点不再在 Y=0（原为悬挂件） |
  | `Hall_DuctSection` | `[0, 0, 0]` | X ±1.145 · Y −0.33→0.33 · Z −0.21→0.344 | **仍是 Z-up**（高 0.554 在 Z）→ 会侧躺 |
  | `Hall_ElectricPanel` | `[0, 0, 0]` | X −0.235→0.23 · Y −0.125→0.08 · Z −0.31→0.336 | **仍是 Z-up**（高 0.646 在 Z、Y 只有 0.205） |
  | `Hall_PipeFlange` | `[0, 0, 0]` | X ±1.208 · Y −0.115→0.115 · Z −0.115→0.2 | **仍是 Z-up**（0.315 在 Z、Y 只有 0.23） |
- 谁被转过、谁被有意跳过：`P/tools/fix-prop-axis.mjs:48-54`（只列 5 件：IBeamColumn/RackUpright/Barrel/PalletWood/LampIndustrial）、`:31-34`（排除 CrateWood 立方体 + RackBeam/ElectricPanel/DuctSection/PipeFlange，理由「没有证据说它们错了」）。
- **B 代的"哪一端是连接面"：UNKNOWN — 证据缺口 #1**。全仓唯一相关文字是使用侧的自述：
  `P/unity/Assets/Scripts/Runtime/HallScene.Furnishing.cs:174-191` —— 「这套货架**两次都没摆对**，已回退 Box」「**⇒ 结论：问题不在我的间距算式，而在我不知道这批构件的【原点与朝向语义】。** 我只验证了包围盒（尺寸对、底面在 y=0），**没有验证"哪一端是它的连接面"**。靠包围盒摆装配件，本质上还是在猜」「下一步要做的（不是继续调坐标）：先把 `Hall_RackUpright` / `Hall_RackBeam` 单独渲染出来看清朝向，或读它们的生成脚本……拿到明确的锚点定义，**再回来摆**」。
- 当前实际接线：只有 `Hall_CrateWood` 被摆进大厅（`HallScene.Furnishing.cs:209`）；货架 3 排退回 4 块 `Box(...)`（`:196-201`）；喷漆罐/篮球/白板仍是程序化体（`:211-245`）。
- 未被任何门禁覆盖：这 10 件**不在清单**（【实测】`Hall_` 条目 0）→ `gate-asset-bbox` / M9-M11 / `verify-packed-kits` / `KitAssetTests` 全都看不见它们（§1.10）。

### 2.3 货车（`truck_eurocargo`）
| 项 | 约定 | 出处 |
|---|---|---|
| 局部原点 | **厢体后缘的地面**（`localPosition` 的 (0,0,0) 落在车尾箱体后沿、y=0 地面） | `P/unity/Assets/Scripts/Runtime/TruckScene.cs:85` |
| 前向 | **+Z = 车头方向**；构造参数 `yawDeg`（0 = 朝 +Z） | `TruckScene.cs:75-77,82,85` |
| 套件包围盒常量（实测值单列） | `KitHalfWidth=1.35` · `KitTopY=3.60` · `KitMinZ=-1.60` · `KitMaxZ=7.70` | `TruckScene.cs:44-58` |
| 曾经转错轴的记录 | 同处注释：「Blender（Z-up）导出为 x=2.70 · y=9.15 · z=3.52 → Unity Y-up 后：X ±1.35 · Y 0..3.52 · Z −1.53..7.62」 | `TruckScene.cs:46-48` |
| 现产物【实测】 | 1 node / 6 primitive / 6 材质；bbox X ±1.35 · Y 0.362→3.304 · Z −1.533→7.62（+Z 端 7.62 = 车头 ✓，−Z 端 −1.533 = 坡道 ✓） | 本轮实测（§0.4） |
| 车内分区 | 指挥区在前（`CommandCenter`）、装备区在后（`GearCenter`）、坡道在车尾（`RampCenter`，局部 `z=-0.8`） | `TruckScene.cs:104-118` |
| 轴系修复工具 | 变换唯一解 `(x,y,z)→(x, 3.52−z, y)`，行列式 +1（真旋转，非镜像） | `P/tools/fix-truck-axis.mjs:3-35` |
| 在大厅里怎么摆 | `tx=0.30W`、`tz=0.22L`、**yaw=180°** → 车头朝后墙（−Z）、车尾朝场内 | `P/unity/Assets/Scripts/Runtime/HallScene.cs:317-320`（⚠ 同函数 doc 注释 `:296-300` 仍写"车头朝 +X（横停）"，与代码不一致 → §4.19） |
| 零件名 | `Truck_Ramp` / `Truck_MonitorScreen` / `Truck_TaskPanel` / `Truck_MapPanel` —— **只在程序化回退路径里存在**（`TruckScene.cs:302,317,320,323`，注释「局内逻辑按名字找它做升降动画」） | 见缺口 #10 |

### 2.4 家具道具（gen-kits 链：`bed_b` / `cabinet_a`）
| 项 | 约定 | 出处 |
|---|---|---|
| `bed_b` 轴语义 | **X = 宽 0.90**（床架 ±0.43）· **Y = 高 0.80** · **Z = 长 2.00**（±0.97）；**床头板在 −Z 端**（`z∈[-1.00,-0.94]`）、**床尾板在 +Z 端**（`z∈[0.94,1.00]`）；脚轮 8 边形、轴沿 X、半径 0.05、贴地 | `P/tools/gen-kits.mjs:119-127,372-386` |
| `cabinet_a` 轴语义 | **X = 宽 0.80** · **Y = 高 1.25** · **Z = 深 0.50**；**抽屉正面朝 +Z**（面板 `z∈[0.18,0.23]`、把手 `z∈[0.22,0.25]`、标签牌 `z∈[0.22,0.25]`）；踢脚底 `y=-0.01` | `P/tools/gen-kits.mjs:388-402` |
| 落地方式 | 关卡装配按**整套道具的 `-minY`** 下移（所以配方不必自己贴地；但也因此"底面不在 0"不会被发现） | `P/unity/Assets/Scripts/Gameplay/Level/LevelBuilder.cs:328-336` |
| footprint | `bed_b=[0.9,2]`、`cabinet_a=[0.8,0.5]`（清单）；碰撞盒表同值 | `LevelGeometry.cs:226-230`、`LevelAssembly.cs:72-77` |

### 2.5 归纳：**已定义** vs **UNKNOWN**
| 族 | 原点 | 前/上 | 连接面 |
|---|---|---|---|
| 房间套件（gen-kits） | 房间中心 + 楼板顶面 y=0 | Y=上；"门/通道朝向"**不是套件的属性**（门洞在 DSL） | 不适用（壳件） |
| 家具 `bed_b`/`cabinet_a`（gen-kits） | 配方坐标（装配时按 `-minY` 落位） | Y=上；bed 头 −Z、cabinet 正面 +Z | 已定义（见 §2.4） |
| 工业道具 **A 代**（kit 链） | `parts[0]` 中心；floor 件底面落 y=0 | Y=上；RackUpright 连接面 **+Z**、Panel 门面 **+Z** | 可推出（§2.2） |
| 工业道具 **B 代**（产品里那批） | **逐件不同**（§2.2 表） | Y=上（但 4 件仍是 Z-up） | **UNKNOWN — 缺口 #1** |
| 货车 | 厢体后缘地面 | **+Z = 车头**，Y=上 | 坡道在 −Z 端 |

---

## 3. 生成器清单

> 只列与**几何资产**有关的脚本。"手机"= 在 `P/` 里存在且可跑；"仅 PC"= 只在 PC 侧/只读存档里。

### 3.1 生产用生成器
| # | 脚本 | 产出 | 怎么跑（参数） | 在位 |
|---|---|---|---|---|
| 1 | `P/tools/gen-kits.mjs`（与 `PC/04-Blender/gen-kits.mjs` md5 相同） | `unity/Assets/ThirdParty/CC0/{kits,props}/<id>.glb` + **回写清单**（sha256/bytes/triangles/footprint/verticesByKit）+ `Resources/Data/asset-manifest.json` 镜像 | `node tools/gen-kits.mjs`（生成+回写）· `--check`（只校产物↔清单）· `--emit <dir>`（只出到别处，附 `kit-plan.json`）· `--lint-only`（只跑几何自检，不调 Blender）· `--mode canonical\|variants` | 手机 ✓ |
| 2 | `PC/存档/tools/gen-hall-kits.mjs` | `{sourceRoot}/props/Hall_*.glb` + 清单条目 + 2 处清单镜像 | `node tools/gen-hall-kits.mjs` · `--check`（只核对，不跑 Blender；要求清单里**已有**条目，否则报"不在清单内"，`:100`） | **仅 PC 存档** |
| 3 | `PC/存档/tools/kit-builder.py` | 由 #2 派发；每个套件一个 `<id>.glb`，末尾打印 `KIT_REPORT=<json>` | `blender -b --factory-startup --python tools/kit-builder.py -- --in <recipes.json> --out-dir <dir>` | **仅 PC 存档** |
| 4 | `PC/存档/tools/lib/kit-hall.mjs` | #2/#3 的**配方真源**：10 件工业道具的 `parts`/`role`/`mount`；导出 `HALL_KITS`/`kitsFor`/`TRI_BUDGET` | 被 #2 `import`；也可只读复核几何与尺寸 | **仅 PC 存档** |
| 5 | `P/docs/modeling/gen_hall_modules.py`（= `PC/知识/modeling/gen_hall_modules.py`，md5 `d15b17cbb2430102b575958356208ef4`） | **另一套** 10 件工业模块配方（工字钢柱/货架立柱/横梁/栈板/吊灯/管道/配电箱/木箱/油桶/风管） | 由 Blender 执行 `blender_python`；`__main__` 只 `print("BUILD_REPORT …")`（`:381-394`） | 手机 ✓（**但见下**） |
| 6 | `P/tools/register-kit-glb.mjs` | 把任意 GLB 登记为套件：三处放置 + 清单写入（幂等，更新同 id） | `node tools/register-kit-glb.mjs --src <glb> --id <id> [--kind prop] [--tags a,b] [--footprint W,D] [--triangles N] [--generator "…"]` | 手机 ✓ |
| 7 | `P/tools/gen-kit-resources.mjs` | 清单 → `Resources/Kits/<id>.glb.bytes` + `StreamingAssets/Kits/<id>.glb`，回写 `resPath`，同步 3 处清单镜像 | `node tools/gen-kit-resources.mjs [--check]` | 手机 ✓ |
| 8 | `PC/存档/tools/sync-footprints.mjs` | 把清单 footprint 重建到 3 处消费点表（两张 C# 表 + `gen-asylum-v1.mjs` 的 `FOOTPRINT`） | `node tools/sync-footprints.mjs [--check]` | **仅 PC 存档** |
| 9 | 关卡生成（影响 footprint/落位，不产网格）：`P/tools/gen-map.mjs`、`P/tools/gen-asylum-v1.mjs`（`--out`，`:362`）、`P/tools/lib/map-layouts.mjs` | `unity/Assets/Levels/*.json` | 见 `PC/知识/multi-map-build-plan.md:68-76` 的验收命令串 | 手机 ✓ |

### 3.2 修复 / 校验工具（改资产或判红，也算链的一部分）
| 脚本 | 作用 | 出处 |
|---|---|---|
| `P/tools/fix-prop-axis.mjs` | 把 Z-up 道具旋转到 Y-up 并把底面抬到 `y=0`（顶点级；**不动 node.translation**；同步 `Resources/Models/hall/`） | `:1-36,44,48-54,96-105,118-156` |
| `P/tools/fix-truck-axis.mjs` | 修 `truck_eurocargo` 的轴（真源 + 运行时副本 + 清单 sha256） | `:1-35` |
| `P/tools/gate-model.mjs` | M1–M11（M6 落位 / M9 容器 / M10 清单 / M11 引用） | `:9-25` |
| `P/tools/gate-asset-bbox.mjs` | B1/B2/B3：footprint ↔ GLB 占地 XZ、躺平、层高 | `:12-16` |
| `P/tools/glb-bbox.mjs` | 快速打印 bbox + 长轴判断（**忽略 node.translation**） | `:13-27` |
| `P/tools/verify-packed-kits.mjs` | 在**构建产物**里核验套件真的进包且可解析 | `:6-18` |

### 3.3 PC 侧、不在任何仓里的能力包（现状如此，必须知道）
- `C:\Users\qing_\Documents\blender-mcp\drivers\dsh_blender_kit.py`：77 个入口，用 `runpy.run_path` 加载即得（`mesh_health`/`texel_density`/`normalize_texel_density`/`apply_material_preset`/`audit_scene`/`lod_chain`/`lightmap_uv`/`bake_maps`/`finalize`），13 个材质预设 —— 出处 `PC/知识/modeling/HANDOFF-建模.md:66-84`；【实测】产品里那批 `MAT-*` 材质名与 `apply_material_preset` 的预设名一致（`HANDOFF-建模.md:78`）。
- 阈值真源：`C:\Users\qing_\Documents\blender-mcp\drivers\modeling-thresholds.json`（`HANDOFF-建模.md:49-62`）。
- MCP 服务器启动/健康检查：`run-server.ps1` + `http://127.0.0.1:8765/health`（`HANDOFF-建模.md:86-93`）。
- **手机侧**：可跑 Blender **5.0.1** headless（aarch64）。注意现存全部产物的导出器是 **Khronos glTF Blender I/O v5.2.40**【实测】→ 版本差对字节复现的影响未验证（缺口 #11）。

### 3.4 复跑命令（照抄即可）
```bash
cd <whisper 仓>
node tools/gen-kits.mjs --lint-only            # 只跑几何自检（不调 Blender，可在手机上跑）
node tools/gen-kits.mjs --check                # 产物 ↔ 清单（sha256/bytes）
node tools/gate-model.mjs                       # M1..M11
node tools/gate-asset-bbox.mjs                  # B1..B3（轴系/占地）
node tools/register-kit-glb.mjs --src <glb> --id <id> --kind prop --footprint W,D
node tools/gen-kit-resources.mjs --check
node tools/verify-packed-kits.mjs <解包后的产物目录>
```

---

## 4. 已知失效模式（规则化）

> 每条都来自真实事故记录；照"做 X，绝不 Y"执行。

1. **未登记 = 不存在（对所有门禁隐形）** — 全部判据都遍历 `manifest.kits`（§1.10）。→ **做**：新资产先登记（`P/tools/register-kit-glb.mjs` 或对应生成器）再谈摆放。**绝不**：把 GLB 拷进 Resources 就以为接上了（`P/docs/hall-props-orphaned-2026-10-06.md:8-17`：清单零登记 ⇒ 清单是资产唯一入口，没登记=不存在）。
2. **六重断链（10 件工业道具的完整事故）** — 原始四重见 `P/docs/hall-props-orphaned-2026-10-06.md:10-14`（不在清单 / 目录不对 `Models/hall` / 代码零引用 / 不是 `.glb.bytes`），第五重=轴（`P/tools/fix-prop-axis.mjs:1-40`），第六重=锚点语义（`P/unity/Assets/Scripts/Runtime/HallScene.Furnishing.cs:174-191`）。
   【实测】当前状态：①清单仍 **0 条**（断）②`Resources/Kits/*.glb.bytes` 已有，但 `Resources/Models/hall/` 仍存**同 md5 副本**（Resources 双份进包；除 `P/tools/fix-prop-axis.mjs:44` 的同步外无任何引用）③代码里只有 `Hall_CrateWood` 被摆（`HallScene.Furnishing.cs:209`），货架退回 Box（`:196-201`）④扩展名已正确 ⑤轴：5 件已转、**4 件仍 Z-up** ⑥锚点 UNKNOWN。
   → **做**：登记 + 定权威代 + 补齐轴 + 写清锚点。**绝不**：靠调坐标试摆（`HallScene.Furnishing.cs:186-191` 明令）。
3. **`export_yup` 必须按"创作空间"选，不能跨链照抄** — gen-kits（Y-up 配方）用 `False`（`P/tools/gen-kits.mjs:638-641`）；kit-builder（Z-up 配方）用 `True`（`PC/存档/tools/kit-builder.py:6-12,215-216`）。照抄错的后果：`footprint` 变成 `[宽,高]`、**碰撞盒与可见模型差 1.4 m**（`P/tools/gen-kits.mjs:638-640`、`P/tools/gate-asset-bbox.mjs:9-11`、`PC/存档/tools/sync-footprints.mjs:11` 记的病床事故）。真实事故：货车（`P/tools/fix-truck-axis.mjs:3-35`、`P/docs/authority-and-constraints-2026-10-06.md:46`）。
4. **"模型没错、是工具没算对"** — `P/tools/glb-bbox.mjs:13-27` 不看 `node.translation`，对"原点偏离几何中心"的资产会给出**局部盒**（看着完美）而真实落位是错的。→ **做**：用 `P/tools/gate-asset-bbox.mjs:68-91` 的口径（叠加节点平移）。**绝不**：只看一个 bbox 工具就下结论。
5. **原点不归一 ⇒ 摆放全靠猜** — `join()` 取 `parts[0]` 为 active（`PC/存档/tools/kit-builder.py:107-118`、`PC/知识/modeling/gen_hall_modules.py:65-79`）⇒ 原点 = 第一个部件中心；`P/tools/fix-prop-axis.mjs:118-142` 只动顶点不动节点平移 ⇒ 水平偏移/悬空（§2.2 表）。→ **做**：外部生成的套件在登记前必须给出书面的"原点在哪一点 + 哪面朝前"，并**加一条门禁**。**绝不**：默认"原点=底面中心"。
6. **ceiling 件不得按"底面 y=0"处理** — `P/tools/fix-prop-axis.mjs:102` 引用工程约定「套件脚底在 y=0」并对吊灯照做，结果把悬挂件落地化（【实测】`Hall_LampIndustrial` Y 0→1.076），与 `PC/存档/tools/gen-hall-kits.mjs:196-197` 的 `mount:'ceiling'`（连接点 Y=0、向下悬挂）冲突。→ **做**：落地化只对 floor 件；ceiling 件保连接点在 y=0、几何向 **−Y** 悬挂。
7. **材质会被管线丢掉** — `GlbReader.Primitive` 原只有 4 字段、`LevelBuilder` 给每件套平材质 ⇒ 样板 v3 的 6 层材质**搬不进来**（`PC/知识/truck-kit-integration-and-glbmaterial-gap.md:17-31`）。已修：读 `pbrMetallicRoughness` 三项（`PC/知识/glb-material-read-step1.md:11-21`、`P/unity/Assets/Scripts/Gameplay/Level/GlbReader.cs:121-139`）。→ **做**：验证要落到装配端（按材质名映射，`P/unity/Assets/Scripts/Runtime/HallScene.Furnishing.cs:127-146`）。**绝不**：把"GLB 里有材质"当成"产品里会显示"。
8. **别拿高金属度当玻璃** — 玻璃用 `Metal` 会被环境反射洗白、与车身同色（`PC/知识/truck-material-fix-2026-10-05.md:14-20`、`PC/知识/truck-sample-v3-material-contrast.md:5-13`）。→ **做**：玻璃用 `Tile` + 压暗基色；"必须一眼分得开"的组合要求相对亮度 `L=0.2126R+0.7152G+0.0722B` 的 **ΔL ≥ 0.15**（`PC/知识/truck-sample-v3-material-contrast.md:16-27`）。
9. **预算不是凭感觉** — `building_module` 200~1000 tri（`PC/知识/modeling/HANDOFF-建模.md:49-62`）；超预算**不许写清单**（`PC/存档/tools/gen-hall-kits.mjs:161-167`）。反例：`Hall_PipeFlange` 1644 tri 超限（`PC/知识/modeling/HANDOFF-建模.md:99-101`），而【实测】产品里那份仍是 1644 tri。
10. **楼板/天花板尺寸必须由房型参数化** — 写死（如 hall 楼板恒 16×3）会在大房间悬挑、小房间缺口（`P/tools/gen-kits.mjs:9-16`）；"套件按房间中心整块摆、不做裁剪"是**已知不足**（`P/unity/Assets/Scripts/Gameplay/Level/LevelBuilder.cs:97-98`：6/11 房间楼板不符、11/11 没天花板、9 对共面）。
11. **共面 = 闪** — 相接部件最小重叠 `JOIN=0.02`（`P/tools/gen-kits.mjs:90`）、配方级 5~15 mm（`PC/存档/tools/lib/kit-hall.mjs:19`）、天花板顶面再低 5 mm（`P/tools/gen-kits.mjs:89,234-235`）；共面重叠判红（`P/tools/gen-kits.mjs:545-558`）。**绝不**：0 间隙贴合。
12. **顶点/三角计数必须从产出字节量** — 配方角点计数与 GLB（平直着色拆点）差**精确 3.000 倍**，曾写出 `hall_main:56` vs 真实 `168` 的假账（`P/tools/gen-kits.mjs:47-56,785-793`）。
13. **"验证过 ≠ 在产品里"** — 类有测试但产品里从未 `new`（`PC/知识/truck-and-session-wiring.md:7-30`）；套件同族（`P/unity/Assets/Scripts/Gameplay/Level/KitMeshLibrary.cs:10-14`）。→ **做**：判据落在产物/真机（`P/tools/verify-packed-kits.mjs:6-10`）。
14. **结构信息必须读出来，不能凭印象写** — 猜材质字段名（`PC/知识/truck-and-session-wiring.md:43-52`、`P/unity/Assets/Scripts/Runtime/HallScene.cs:296-300`）；`Whisper.Gameplay` **不引用 UnityEngine**（`PC/知识/patch-script-rules.md:54-57`、`PC/知识/truck-and-session-wiring.md:49`）；schema 形状要照抄真源（`PC/知识/multi-map-and-truck-progress.md:24-27`）。→ **做**：动手前 `rg` 真实声明/程序集。
15. **补丁脚本纪律** — 禁内联 `node -e` 做文本替换（`PC/知识/patch-script-rules.md:8-9`）；补丁内容禁反引号/模板串、用数组+`join`（同文件 `:12-17`）；优先用 `edit` 工具（`:20-21`）；先 `node --check`（`:43-46`）；锚点必须从文件里读出、命中 ≠ 1 就停且不写盘（`:49-52`）。
16. **低层路径/坐标坑** — ASCII 文件名（中文路径会触发本机写文件故障：`PC/知识/multi-map-build-plan.md:39`）；`z0/z1` 是**最小/最大角**不是中心（`PC/知识/multi-map-and-truck-progress.md:27`）；门 `at` 是**沿墙绝对坐标**（`PC/知识/multi-map-build-plan.md:31-33`）；`Select-String -Context` 会把相邻匹配拼在一起、判断"重复"必须先看行号（`PC/知识/patch-script-rules.md:51`）。
17. **门禁的覆盖边界（别把绿当"好看"）** — M9 只证明容器/结构、**不证明观感**（`P/tools/gate-model.mjs:26-27`）；轴系与占地的硬判据是 `gate-asset-bbox`（`P/docs/authority-and-constraints-2026-10-06.md:23,46`）。→ **做**：外观类结论必须出图（`PC/知识/five-fixes-and-modeling-redo-plan.md:67` 要求 `blender_preview`/`blender_render` 出图人工核对）。
18. **同一资产别放两处** — 【实测】`Resources/Kits/Hall_*.glb.bytes` 与 `Resources/Models/hall/Hall_*.glb.bytes` md5 相同；`Resources/**` 无条件进包 ⇒ APK 里双份。→ **做**：一处真源（`ThirdParty/CC0/props/`）+ 一处运行期落点（`Resources/Kits/`）。
19. **注释与代码不一致时，以代码 + 实测为准** — `P/tools/gen-kits.mjs:25-29` 头部写"canonical（默认）"，`:78` 实际默认 **`variants`**；`P/unity/Assets/Scripts/Runtime/HallScene.cs:299` 注释写"车头朝 +X（横停）"，`:317-320` 实际 `yaw=180`、车头朝 −Z。→ **做**：读到不一致就顺手改注释（并留出处）。

---

## 5. 证据缺口（编号 + 具体下一步）

> 每条格式：**缺什么** → 为什么现在不能断言 → **下一步动作**。

1. **B 代 10 件工业道具的锚点与连接面语义**（哪一点是原点、哪一面是连接面/正面）。
   现无任何文档、注释或代码给出；唯一相关文字是使用侧自述"我不知道这批构件的原点与朝向语义"（`P/unity/Assets/Scripts/Runtime/HallScene.Furnishing.cs:174-191`）。
   → **动作**：① 出三视图：把 GLB 导入 Blender（手机 5.0.1 可跑）分别渲染 front/left/top 并读图；② 或做数值判据：按 Y 分带统计 X/Z 范围，找出冲孔/肋/把手的分布带（孔在哪个 Z 面）；③ 结论以字段形式写进清单（建议加 `anchor`/`forward`），并加进 `P/tools/gate-asset-bbox.mjs` 或新门禁。**在此之前不要摆这 10 件。**
2. **10 件道具不在清单** ⇒ 对所有门禁与 EditMode 测试隐形（§1.10 的 manifest-driven 事实）。
   → **动作**：二选一——(a) 把它纳入 kit 链（`PC/存档/tools/gen-hall-kits.mjs` 已有 `--check` 与预算判红）；(b) 用 `node tools/register-kit-glb.mjs --src … --id Hall_X --kind prop --tags prop,hall,industrial --footprint W,D` 登记，然后必跑 `P/tools/gate-model.mjs` + `P/tools/gate-asset-bbox.mjs`。
3. **4 件道具仍是 Z-up**【实测】`Hall_DuctSection` / `Hall_ElectricPanel` / `Hall_PipeFlange` / `Hall_RackBeam`（§2.2 表；排除理由见 `P/tools/fix-prop-axis.mjs:31-34`）。
   → **动作**：把 `P/tools/fix-prop-axis.mjs:48-54` 的 `PROPS` 扩到全 10 件（补 `expect` 尺寸），先 `--check` 看判定，再执行；执行后跑 `gate-asset-bbox.mjs`。
4. **A/B 两代同名产物并存、锚点不同、谁是权威未定**（A 代在 `PC/存档/…/CC0/props/`，B 代在产品 `Resources/Kits/`）。
   证据：§2.2 两张实测表；两代 tri 与材质命名都不同（A 代 396~968 且材质名=角色名；B 代 220~1644 且材质名=`MAT-*`）。
   → **动作**：定一处真源（建议 A 代：有配方、有 `mount`、单网格单材质、预算达标），删掉另一代与 `Resources/Models/hall/` 双份，并在清单 `generator` 字段写明配方文件。
5. **`gen_hall_modules.py` 无法复现现存产物**：全文**没有导出步骤**（无 `export_scene.gltf`/GLB 写出；`__main__` 只打印 `BUILD_REPORT`，`:381-394`），且部分尺寸与产物不符【实测】（如 `Hall_RackUpright` 产物高 1.802 m，而脚本默认 `H=2.60`，`:132`；`Hall_IBeamColumn` 3.10/0.34 与脚本一致，`:111`）。
   → **动作**：把 PC 侧真正的导出调用（能力包 `finalize`/`bake_maps`，见 `PC/知识/modeling/HANDOFF-建模.md:80-84`）归档进仓并写成可复跑的脚本；否则按 A 代重做这 10 件。
6. **档案 `Hall_PipeFlange.glb` 的轴与配方矛盾**：配方注释「Ø0.23 × 长 2.30（**沿 X**）」（`PC/存档/tools/lib/kit-hall.mjs:75`），而【实测】其长轴落在 **Y**（2.30 m）、X 只有 0.491 m。
   → **动作**：重跑 `node tools/gen-hall-kits.mjs`（PC 侧）并比对 bbox/sha256，确认是导出问题还是配方旧版。
7. **`mount` 字段没有消费方**：只有 kit 链写（`PC/存档/tools/gen-hall-kits.mjs:196-197`），清单里 0 条【实测】，运行时无人读。
   → **动作**：清单加 `mount` 并在装配端消费（`HallScene.PlaceKit` 加 ceiling 分支：把连接点对齐天花板 y、几何向下悬挂），或明确删除该概念并删掉配方里的 `mount`。
8. **footprint 三处消费点表已漂移 & 同步工具不在手机仓**：清单 `hall_main=[18,3]`/`morgue=[2,3]` vs `P/tools/gen-asylum-v1.mjs:156` 的 `[16.0,3.0]`/`[3.0,3.0]`；`sync-footprints.mjs` 只在 `PC/存档/`；两张 C# 表当前只列 `bed_b`/`cabinet_a`（`P/unity/Assets/Scripts/Gameplay/Level/LevelGeometry.cs:226-230`、`P/unity/Assets/Scripts/Gameplay/Level/LevelAssembly.cs:72-77`）。
   → **动作**：把 `PC/存档/tools/sync-footprints.mjs` 迁进 `P/tools/`，跑 `--check` 修平三张表，并把"重建三处表"接进验收串。
9. **UV/切线/贴图通道不存在**：全部 GLB 无 `TANGENT`、无 `COLOR_0`【实测】；`P/unity/Assets/Scripts/Gameplay/Level/GlbReader.cs:121-139` 只读 `pbrMetallicRoughness` 三项、不支持贴图；`P/unity/Assets/Scripts/Gameplay/Level/KitMeshLibrary.cs:211-218` 用 `RecalculateNormals()`。
   → **动作**：若要"上色"落地（用户第 3 条诉求），先定义 UV0 约定（kit 链已有 `smart_project` 66°/0.02；gen-kits 链只有图元默认 UV）+ 切线策略，再扩 `GlbReader`（TANGENT + baseColorTexture）并加"UV 质量/纹素密度"判据（阈值表里有 `texel_density_px_per_m.mobile=512`，`PC/知识/modeling/HANDOFF-建模.md:57-58`）。
10. **货车套件路径下部件无名**：6 个 primitive 的节点名都叫 `truck_eurocargo`【实测】，而 `Truck_Ramp`/`Truck_MonitorScreen`/`Truck_TaskPanel`/`Truck_MapPanel` 只在程序化回退里创建（`P/unity/Assets/Scripts/Runtime/TruckScene.cs:302,317,320,323`，注释说局内逻辑**按名字**找坡道做升降）。
    → **动作**：重新导出时给 6 个部件命名（或按材质/包围盒建"部件语义表"写进清单），否则局内交互接不上套件路径。
11. **手机 Blender 5.0.1 与产物导出器 5.2.40 的差异未验证**【实测】`asset.generator = "Khronos glTF Blender I/O v5.2.40"`（44 个文件全同）。
    → **动作**：在手机上跑一次 `node tools/gen-kits.mjs --emit tmp/kit-check`，与仓内产物比 sha256/bbox；若字节不可复现，记录"哪些差异是版本导致"，避免把版本差误判成回归。
12. **大厅没有云取证基线**：`RenderEvidenceCapture` 按**关卡房间**取景，大厅是代码搭的、不属于任何关卡 ⇒ 现在没有任何一张大厅取证图（改之前无基线、改之后无法比对）—— `P/docs/p1-lobby-kits-starting-point-2026-10-06.md:41-46`。
    → **动作**：先给大厅加取证（或另写 `LobbyEvidenceCapture`），产出"改之前"基线，再做套件替换（同文件 `:48-56`）。
13. **房间套件的"朝向"没有书面规则**（除 §2.1 已列出的分架构细节）：套件本身不含门洞，朝向语义由 DSL + 唯一的 `Quaternion.Euler(0, Rot, 0)` 决定 —— 该施加点在 `PC/存档/tools/kit-builder.py:12` 被引作 `LevelBuilder.cs:178`（旧版行号），**现行位置是 `P/unity/Assets/Scripts/Gameplay/Level/LevelBuilder.cs:312`**。
    → **动作**：把"套件局部轴 ↔ 房间/门"的对应写成一段规范（含"哪面朝北墙/哪个 rot 对应哪面"），否则每次新架构都要重新推。
