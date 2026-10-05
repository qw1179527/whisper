# 管线缺口修复 · 第一步：GlbReader 读材质（2026-10-05 · 第 27 轮）

> 门禁：`unity-syntax-check` exit=0 · `unity-tests EditMode` **62/62**

## 一、背景：管线一直在丢 GLB 材质（上一轮定位）

`GlbReader.Primitive` 原先只有 `Positions / Normals / Uvs / Indices` —— **没有材质字段**，
于是 `LevelBuilder`（`:340`）给每个部件套一个**纯色平材质**。
**所有套件的 PBR 材质都被丢弃** —— 样板 v3 的 6 层材质搬不进来，根因就在这里。

## 二、本轮做了什么（第一步：**把材质读进来**）

`unity/Assets/Scripts/Gameplay/Level/GlbReader.cs`：

| 改动 | 内容 |
|---|---|
| 新增 `KitMaterial` 结构 | `R/G/B/A` · `Metallic` · `Roughness` · `Name`，并给出 glTF 规范默认值（白 / 金属 1 / 粗糙 1）。**只取 `pbrMetallicRoughness` 核心三项** —— 它们决定"看起来像什么材料"；贴图需要纹理资源通道，本仓没有，故**不假装支持** |
| `Model` 增加 `Materials` 列表 | 对应 glTF `materials[]`；空 = 该 GLB 没有材质 |
| 解析 `materials[]` | 读 `pbrMetallicRoughness.baseColorFactor / metallicFactor / roughnessFactor` 与 `name` |
| `Primitive` 增加 `MaterialIndex` | **默认 -1 = 没有材质**；解析时读 `primitive.material` 索引 |
| 创建 `Primitive` 时带上索引 | `new Primitive { …, MaterialIndex = materialIndex }` |

## 三、为什么这一步是**纯增量、不动既有行为**（重要）

- 既有 11 个套件的 GLB **本来就没有材质** → 解析后 `MaterialIndex` 全是 -1、`Materials` 为空
  → 装配逻辑照旧走"平材质"分支，**视觉完全不变**；
- 新增字段都有默认值，**不改变任何既有判据**（`gate-model` M9/M10 只看容器结构与哈希，不受影响）；
- `FloatOf` / `IntOf` / `ListOf` / `MapOf` 助手均已存在（先核实过再写，未凭印象调 API）。

## 四、下一步（第二步：**用起来**）

1. **`KitMeshLibrary` 暴露材质**：`GetParts` 已按 GLB primitive 划分网格（货车 = 6 个 primitive = 6 种材质），
   加一个 `GetMaterials(kitId)` 或让 `GetParts` 同时返回材质索引；
2. **`TruckScene.TryBuildFromKit()`**：加载 `truck_eurocargo`（已登记、门禁 11/11 通过），
   按 `MaterialIndex` 给每个部件套真实 PBR 参数 → 替换现在的 Cube 拼装（保留现实现为回退）；
3. 顺带让 `LevelBuilder` 的道具装配也走材质（既有 11 个套件没有材质，行为不变，但将来加材质即生效）；
4. 出包真机看货车外观（**需要设备可用**）。

## 五、本轮教训（又一次被自己的注释提醒到）
我在脚本末尾写了 `⚠ 仍需：把 materialIndex 写进 Primitive + FloatOf 助手是否存在` ——
**这说明我写脚本时自己就知道有两处没核实**。下笔前应该先把这两点查掉（本轮事后补做了），
而不是先把脚本写完再补。**"先核实依赖、再写代码"这条纪律要贯彻到脚本层面。**

## 六、门禁
| 门禁 | 结果 |
|---|---|
| `unity-syntax-check.sh` | exit=0 |
| `unity-tests.sh EditMode` | **62/62 passed** |
| `gate-model.mjs` | 上一轮实测 11/11（本轮未动套件与清单） |
