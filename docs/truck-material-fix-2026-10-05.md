# 产品侧货车材质修复（2026-10-05 · 第 25 轮）

> 门禁：`unity-syntax-check` exit=0 · `unity-tests EditMode` **62/62**

## 一、把样板验证过的材质修复搬进产品

`HallScene.BuildTruck()` 的材质与样板 v3 对齐（依据 `docs/truck-sample-v3-material-contrast.md`
的**材质对比自检**：相对亮度 L = 0.2126R+0.7152G+0.0722B，要求"必须一眼分得开"的组合 ΔL ≥ 0.15）：

| 部件 | 原值 | 改后 | 样板 v3 相对亮度 |
|---|---|---|---|
| 车漆 | `(0.62,0.63,0.66)` 粗糙 0.42 Metal | `(0.70,0.71,0.73)` 粗糙 0.32 Metal | L≈0.71 |
| 底盘/轮毂 | `(0.28,0.29,0.31)` 粗糙 0.55 Metal | `(0.20,0.21,0.23)` 粗糙 0.45 Metal | L≈0.21 |
| **玻璃/屏幕** | `(0.12,0.16,0.18)` 粗糙 0.15 **Metal** | `(0.06,0.08,0.11)` 粗糙 0.15 **Tile** | L≈0.08 |

**玻璃这一行是本轮的关键**：原代码用 `MaterialFamily.Metal`（**高金属度族**）→ 玻璃反射环境光被洗白，
视觉上与车身同色（样板正面图实测过：两者几乎分不开）。
而产品侧的 `MaterialFamily` **没有玻璃族**（实测枚举只有
`Plaster / Concrete / Wood / Metal / RustMetal / Tile / Fabric`），
故改用 **Tile**（光滑、接缝少 —— 最接近玻璃的平整洁净面）+ 压暗基色 → 读作"暗色平整面"。

## 二、我一度误判并已纠正（记教训）

我先前用 `Select-String -Context` 读到：
```
Truck = new TruckScene(...);
BuildTruck();
```
于是判断"`BuildTruck()` 被调用两次 → 重复建车"，还写了删除逻辑。**实测 grep 行号后**：
- 第 **332** 行：`BuildTruck();` —— 唯一的调用
- 第 **313** 行：`Truck = new TruckScene(...)` —— 它在 `BuildTruck()` **方法体内**，不是重复

两者**不是重复**。脚本的锚点保护（命中 ≠ 1 就停止且不写盘）挡住了这次误删 ——
**教训：`-Context` 会把相邻匹配拼在一起，判断"重复"必须先看行号再下结论。**

## 三、仍未做（诚实列出）

| 项 | 说明 |
|---|---|
| 货车仍是 Cube 拼装 | 产品里还是 `TruckScene.cs` 的 `CreatePrimitive(Cube)`；**样板尚未接进 Unity**。接入要按资产管线规程（`gen-kits.mjs` 路径 + `asset-manifest.json` 唯一真源） |
| 倒角与细节件未进产品 | 样板的 2cm 倒角、32 边轮胎、侧板竖筋、挡泥板、后视镜、导流罩等**只在 Blender 样板里** |
| 未做 UV / 贴图 | 只有纯色 PBR（7 层），真"上色"还需 UV + 贴图 |
| 真机验证 | 未做（设备上一次被他人使用，见 `device-evidence-0.1.81-blocked.md`） |

## 四、下一步

1. **把样板的几何细节搬回 Unity**（倒角在 Unity 侧可用 `ProBuilder` 或直接把 Blender 样板导出 GLB 接入）；
   - 更稳的路径：**样板导出 GLB → 走既有资产管线登记进 `asset-manifest.json`** → `TruckScene` 改为加载该 GLB
   - 这样"建模在 Blender、装配在 Unity"，与本仓既有的 11 个套件流程一致
2. 大厅场景精做（用户已选"尽量还原官方布局"）
3. 道具四大类 → 机制细化 → 过渡动画与真人化 → 真实物理与画质（按用户约束顺序）
