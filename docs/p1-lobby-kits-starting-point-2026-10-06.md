# P1「大厅场景重做套件」—— 起点勘察（2026-10-06）

> 本文只记录**勘察结果与判据缺口**，不含改动。目的是让接手的一轮能直接动手，
> 而不是从"计划说大厅是 Box 拼的"重新查一遍。

## 一、计划的说法 vs 实测现状

`docs/five-fixes-and-modeling-redo-plan.md` §三 原文：
> 现状问题（真机截图可见）：大厅是**纯 Box 拼的方盒子**（地板/墙/柱/桁架），
> 货车是**纯 Cube 拼的长方体** …… 都没有倒角、没有细节层、没有合适材质分层。

**实测只对了一半**：

| 对象 | 实测 | 证据 |
|---|---|---|
| **货车** | **已用套件** | `TruckScene.cs:136` `KitMeshLibrary.GetParts(KitId)`；`truck_eurocargo.glb`（3104 三角形 / 192 KB）已在清单里。**计划写这条时套件还不存在。** |
| **大厅 `HallScene`** | **仍是纯 Box** | `HallScene.cs` 里 **44 处 `Box(_root, ...)`**，且**零 `KitMeshLibrary` 引用** |

⇒ **P1 的真正目标只有大厅那一半。**

## 二、大厅该用的套件已经存在，只是没接上

清单里 6 个 hall 套件，在关卡里的引用情况：

| 套件 | 被哪些关卡引用 |
|---|---|
| `hall_main` | asylum ×4 · bleasdale ×1 · tanglewood ×1 |
| `hall_main_entrance_safe` | bleasdale ×1 · tanglewood ×1 |
| `hall_main_corridor_link` | bleasdale ×1 |
| `hall_main_corridor_main_f1` / `_ward` | （待核） |
| **`hall_main_lobby`** | **【无人引用】** ← **它就是为大厅造的，但从来没接上** |

⇒ **P1 很可能是"接线"而不是"重做建模"** —— 但**必须先看到 `hall_main_lobby` 的实际质量**才能定。
（若质量不够，才是真"重做"，那属于 §三 的多轮工作量。）

## 三、判据缺口（**动手前必须先补**）

目标里 P1 的验收标准是：
> **判断标准：改完通过门禁链，且能在云端取证里看到画面对。**

而**当前云端取证不覆盖大厅**：
- `RenderEvidenceCapture` 按**关卡房间**取景（`corridor_main` / `entrance_safe` / `morgue_deep` …），
  那些是 asylum 的房间；大厅是**代码搭的、不属于任何关卡**。
- 所以**现在没有任何一张大厅的取证图**（改之前没有基线，改之后也无法比对）。

**⇒ P1 的第一步是给大厅加取证**，否则改了也证明不了。

## 四、下一轮的直接起点（按序）

1. **读 `kit-visibility` 的图** —— 判断 `hall_main_lobby` 等套件的实际质量
   （`node tools/agent-task.mjs kit-visibility`，产物在 `.agent-out/kit-visibility-*/`）
2. **给大厅加取证** —— 让 `RenderEvidenceCapture` 能对 `HallScene` 取景
   （或另写一个 `LobbyEvidenceCapture`），产出"改之前"的基线图
3. **把 `HallScene` 的 Box 换成套件装配** —— 几何、碰撞体（`BoxCollider` → `MeshCollider`
   或按套件重建）、灯光都要跟着走
4. **用第 2 步的取证比对** —— 看画面是否真的变好

## 五、已知的坑（来自本会话实测，别再踩）

1. **套件的 Y-up 约定**：`footprint=[宽(x),深(z)]`，Y=高度。`truck_eurocargo` 曾把车长转到 Y 轴
   （已修，见 `tools/fix-truck-axis.mjs`）。**接新套件时先用 `gate-asset-bbox` 验轴系。**
2. **`export_yup=False` 是正确的**，不要"修"它（§三.5 明确警告过）。
3. **MeshCollider 用非凸**（静态物体允许），才能表达"有外壳、内有空腔"——
   货车就是这么修的；大厅若有可进入的结构同理。
4. **改完必须跑真编译**（`gate-test`），不能只看规模门禁 —— 本会话已有一次教训
   （`ParseMaterials` 参数类型写错，连带 4 道门禁红）。
