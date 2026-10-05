# 货车样板接入资产管线：已完成的部分与卡住的管线缺口（2026-10-05 · 第 26 轮）

> 门禁：`gate-model.mjs` **11/11 全过**（含 M9 12 个 GLB 有效 · M10 哈希一致 · M11 kit 引用匹配）

## 一、已完成：样板导出 GLB 并**正式登记为套件**

| 步 | 结果 |
|---|---|
| 导出 | `truck_eurocargo.glb` · **1680 顶点 / 3104 三角面 / 6 材质** · 包围盒 2.70 × 9.15 × 3.52 m · `export_yup=False`（本仓轴系契约） |
| 合并 | 64 个零件合并为 **1 个对象**（套件契约：`Kits/<id>.glb` 读成"一个套件"）；Blender 的 join **保留了 6 个材质槽** |
| 三处放置 | ① `Assets/ThirdParty/CC0/kits/`（**真源**，gate-model M9/M10 读这里）② `Assets/StreamingAssets/Kits/` ③ `Assets/Resources/Kits/<id>.glb.bytes`（运行时 `Resources.Load`，**路径写 `Kits/<id>.glb` 不写 `.bytes`**） |
| 清单 | `asset-manifest.json` 新增条目（`id/kind/file/tags/sha256/bytes/triangles/generator/footprint/resPath`），**由脚本写入、非手工改**（规程：清单是唯一真源） |
| 门禁 | `gate-model` → **M1–M11 通过 11 路 · 失败 0**；GLB 结构 `meshes=1 nodes=1 materials=6 triangles=3104` |

新增工具：`tools/register-kit-glb.mjs`（把任意 GLB 登记为套件：算 sha256、放三处、回写清单，幂等）。

## 二、⚠ 卡住的缺口：**管线在读取 GLB 时丢弃材质**

### 事实（读码确认，不是推断）
`GlbReader.Primitive`（`unity/Assets/Scripts/Gameplay/Level/GlbReader.cs:34-47`）**只有四个字段**：
```csharp
public float[] Positions;   // 顶点位置
public float[] Normals;     // 法线
public float[] Uvs;         // UV0
public int[] Indices;       // 三角索引
```
**没有材质字段。** 于是：
- `KitMeshLibrary.GetParts(kitId)` 把每个 GLB **primitive** 变成一个 `Mesh`；
- `LevelBuilder`（`:340`）给每个部件套一个**纯色平材质** `FlatMaterial(ToColor(LevelPalette.Prop(...)))`。

⇒ **整套管线都在丢弃 GLB 的材质**。这就是为什么样板 v3 辛苦分好的 6 层材质（车漆/底盘/玻璃/橡胶/塑料/车牌）**搬不进来** —— 不是我没搬，是**管线里没有这条通道**。

### 一个正好有利于修复的事实
货车 GLB 的 primitive 数 = **6**，**恰好等于材质数**（Blender 的 `join` 按材质槽拆分 primitive，
导出时 `Primitives created: 6`）。也就是说：
**「第 i 个 primitive ↔ 第 i 个材质」的对应关系是确定的**，缺的只是把材质信息从 glTF 读出来这一段。

### 修复方案（下一步，两条路，都已想清）
| 方案 | 做法 | 代价 |
|---|---|---|
| **A. 扩展 `GlbReader` 读材质**（推荐） | 解析 glTF 的 `materials[]`（`pbrMetallicRoughness.baseColorFactor/metallicFactor/roughnessFactor`）+ `primitive.material` 索引；`Primitive` 加一个 `MaterialIndex` 字段；`KitMeshLibrary` 暴露材质参数表；`LevelBuilder`/`TruckScene` 按索引取 | 改动集中在 3 个文件，且**对既有 11 个套件是纯增量**（它们的材质本来就没用上，改了只会让它们同样受益） |
| B. 在 Unity 侧按几何启发式分配 | 用部件的位置/尺寸猜"这块是玻璃还是车漆" | 脆弱、靠猜，与本项目"禁止 Guessing"冲突 —— **不推荐** |

## 三、我原本想做的与为什么停在这里
原计划：`TruckScene` 加 `TryBuildFromKit()`，优先加载 `truck_eurocargo`，用套件几何替换现在的 Cube 拼装。
**停下来的原因**：若按现有管线装配，货车会变成**一整块纯色**（6 个部件全用同一个平材质），
**外观反而比现在的 Cube 版更差**（至少现在还有 3 种材质区分）。
在缺口修好之前接入，是"看着像进展、实际是退步"。

## 四、下一步（按依赖顺序）
1. **方案 A**：扩展 `GlbReader` 读材质 → `KitMeshLibrary` 暴露材质 → 让套件装配保留材质
2. `TruckScene.TryBuildFromKit()`：加载 `truck_eurocargo`，替换 Cube 拼装（保留现实现为回退）
3. 出包真机看货车外观（**需要设备可用**；上一次设备被他人占用，见 `device-evidence-0.1.81-blocked.md`）
4. 之后：大厅场景精做 → 道具四大类 → 机制细化 → 过渡动画与真人化 → 真实物理与画质

## 五、本轮门禁
| 门禁 | 结果 |
|---|---|
| `tools/gate-model.mjs` | **通过 11 路 · 失败 0**（12 个套件 GLB 全部有效） |
| 本轮未改 C# | 故 Unity 双门禁维持上次实测：`unity-syntax-check` exit=0 · `unity-tests EditMode` **62/62** |
