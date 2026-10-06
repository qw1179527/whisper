# 10 个大厅工业道具：在包里、做完了、**从来没用过**（2026-10-06 发现）

## 现象
用户反馈「疗养院的地图墙壁房间啥的不还是绿色、灰色等纯色吗，谁家医院这样」、
「我在 DSH专用 上看见的是原来几十个版本前的样子」。
排查后**不是渲染问题，也不是版本落后问题** —— 是**资产断链**。

## 三重断链（逐条实测）
```
① 资产在哪：unity/Assets/Resources/Models/hall/Hall_*.glb    ← 10 个文件都在，带 .meta
② 清单里：  asset-manifest.json【零登记】                      ← 清单是资产唯一入口，没登记=不存在
③ 代码里：  rg "Hall_IBeamColumn|Hall_Barrel|Hall_RackUpright" unity/Assets → 【零命中】
④ 加载路径：KitMeshLibrary 读 Resources/Kits/<id>.glb          ← 目录也对不上（道具在 Models/hall/）
```
**⇒ 10 个工业道具（工字钢立柱/油桶/木箱/风管/配电箱/工业吊灯/托盘/法兰/货架横梁/货架立柱）
在包内、体积正常、从未被任何代码加载或摆放。**
而 `HallScene.BuildProps()` 至今用 `Box(...)` 方盒子拼货架、用方盒子拼箱子。

## 来源与"差一点永久丢失"
`电脑上/README-目录索引.md` 第 54 行原文：
> **`09-` 里的 10 个 `Hall_*.glb` 是真源里没有的**（大厅场景道具）。
> 这是审计才发现的，**只按"真源"思维整理会永久丢失**。

`09-工作区whisper独有存档/READ-ONLY-ARCHIVE.md` 是它的出处。
**本工程副本里这 10 个文件已在 `Resources/Models/hall/`**（说明有人搬进来了），
**但搬运止步于此 —— 清单、加载、摆放三段都没接。**

## 修法（按已查明的机制，不是猜）
1. **登记**：`asset-manifest.json` 加 `props[]` 段（或按既有 `kits[]` 形状加，`kind: "prop"`）。
   ⚠ 清单头部写明「**本清单是资产的唯一入口…禁止手工拖拽入库**」⇒
   **应走生成器**（`tools/gen-kit-resources.mjs` 的职责是把清单里的 `file` 确定性复制到
   `Resources/Kits/` 并回写 `resPath` + 校验 sha256）。道具应扩一条同构的产物路径。
2. **加载**：`HallScene` 用 `ModelLibrary`（`Resources.Load<TextAsset>("Models/hall/Hall_IBeamColumn.glb")`）
   或把道具纳入 `KitMeshLibrary` 的 `Kits/` 路径 —— **二选一，不要两套并存**。
3. **摆放**：替换 `HallScene.Furnishing.cs` 里对应的 `Box(...)`：
   · `BuildProps()` 的货架 4 块板 + 架上的箱子 → `Hall_RackUpright`/`Hall_RackBeam`/`Hall_CrateWood`
   · 油漆桶堆 → `Hall_Barrel`
   · 结构立柱（现在是方盒子）→ `Hall_IBeamColumn`
   · 顶灯 → `Hall_LampIndustrial`  · 墙边 → `Hall_ElectricPanel` / `Hall_DuctSection` / `Hall_PipeFlange`
   · 地面 → `Hall_PalletWood`
4. **验收**：改完跑 `gate-asset-bbox`（轴系）→ 全链 → **云端 `lobby-evidence` 出图比对**
   （大厅基线已在 `DSH专用/大厅基线-20261006/`，8 张，可作"改之前"参照）。

## 与 P1 的关系
这正是 `five-fixes-and-modeling-redo-plan.md` §三「建模精细重做」里
**成本最低、见效最快**的一条：**资产已经做完了**，缺的只是"接上"。
它不替代 §三 的其余部分（倒角/贴图/内饰），但能让大厅**立刻**从"方盒子"变成"工业仓库"。
