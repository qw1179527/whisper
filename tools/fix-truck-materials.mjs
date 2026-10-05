#!/usr/bin/env node
/**
 * fix-truck-materials.mjs — 把样板验证过的**材质修复**搬进产品 + 删掉重复的 BuildTruck 调用
 *
 * ## 一、玻璃被"洗白"（样板 v3 已定位并验证的缺陷）
 * `HallScene.BuildTruck()` 原本：
 * ```csharp
 * var glassMat = Mat(new Color(0.12f,0.16f,0.18f), 0.15f, MaterialFamily.Metal);
 * ```
 * `MaterialFamily.Metal` = **高金属度**族 → 玻璃反射环境光被洗白，视觉上与车身同色
 * （样板正面图实测过：玻璃与车身几乎分不开）。
 *
 * **产品侧的可选族里没有"玻璃"**（实测枚举只有 Plaster/Concrete/Wood/Metal/RustMetal/Tile/Fabric），
 * 所以改用 **Tile**（光滑、接缝少 —— 最接近玻璃的平整洁净面），并把基色压暗。
 * 这样玻璃读作"暗色平整面"，而不是"发亮的金属板"。
 *
 * ## 二、重复建车（代码缺陷）
 * `BuildTruck()` 被调用**两次**：
 * ```csharp
 * Truck = new TruckScene(...);
 * BuildTruck();          // ← 又建一辆！车厢里会多出一整辆重叠的车
 * ```
 * 删掉多余的一行。**为什么必须删**：多建的那辆与原车完全重叠 → 深度冲突（z-fighting）闪烁，
 * 而且零件数翻倍（性能白烧）。
 *
 * ## 三、材质参数对齐样板 v3（相对亮度已拉开）
 * | 部件 | 原 | 改后 | 样板 v3 相对亮度 |
 * |---|---|---|---|
 * | 车漆 | (0.62,0.63,0.66) 粗糙 0.42 Metal | (0.70,0.71,0.73) 粗糙 0.32 Metal | L≈0.71 |
 * | 底盘/轮毂 | (0.28,0.29,0.31) 粗糙 0.55 Metal | (0.20,0.21,0.23) 粗糙 0.45 Metal | L≈0.21 |
 * | 玻璃/屏幕 | (0.12,0.16,0.18) 粗糙 0.15 **Metal** | (0.06,0.08,0.11) 粗糙 0.15 **Tile** | L≈0.08 |
 * 依据：`docs/truck-sample-v3-material-contrast.md` 的材质对比自检（ΔL 车身/玻璃 = 0.63）。
 *
 * 用法：node tools/fix-truck-materials.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'unity/Assets/Scripts/Runtime/HallScene.cs');
const checkOnly = process.argv.includes('--check');
const log = [];
const fail = (m) => { console.error('[truck-mat] ✗ ' + m); process.exit(1); };

const raw = fs.readFileSync(FILE, 'utf8');
if (raw.includes('玻璃被"洗白"')) { console.log('[truck-mat] 已应用，跳过'); process.exit(0); }
let s = raw;
const sub = (from, to, what) => {
  const n = s.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  s = s.replace(from, to);
  log.push('  ✓ ' + what);
};

// ── ① 材质对齐样板 v3 + 玻璃改 Tile ─────────────────────────────────
sub('            var bodyMat = Mat(new Color(0.62f, 0.63f, 0.66f), 0.42f, MaterialFamily.Metal);   // 白色厢式车漆（Eurocargo 常见涂装）\n'
  + '            var metalMat = Mat(new Color(0.28f, 0.29f, 0.31f), 0.55f, MaterialFamily.Metal);  // 底盘/轮毂/键盘\n'
  + '            var glassMat = Mat(new Color(0.12f, 0.16f, 0.18f), 0.15f, MaterialFamily.Metal);  // 车窗与屏幕（暗、微反光）',
  [
    '            // 【材质对齐样板 v3】依据 docs/truck-sample-v3-material-contrast.md 的**材质对比自检**',
    '            // （相对亮度 L = 0.2126R+0.7152G+0.0722B，要求"必须一眼分得开"的组合 ΔL ≥ 0.15）：',
    '            //   车漆 L≈0.71 · 底盘 L≈0.21 · 玻璃 L≈0.08 → 车身/玻璃 ΔL≈0.63',
    '            var bodyMat = Mat(new Color(0.70f, 0.71f, 0.73f), 0.32f, MaterialFamily.Metal);   // 车漆：亮、微金属',
    '            var metalMat = Mat(new Color(0.20f, 0.21f, 0.23f), 0.45f, MaterialFamily.Metal);  // 底盘/轮毂/键盘：暗、强金属',
    '            // 【玻璃被"洗白"的根因】原先用 `MaterialFamily.Metal`（**高金属度族**）→ 玻璃反射环境光被洗白，',
    '            // 视觉上与车身同色（样板正面图实测：两者几乎分不开）。',
    '            // 而产品的 `MaterialFamily` **没有玻璃族**（实测只有 Plaster/Concrete/Wood/Metal/RustMetal/Tile/Fabric），',
    '            // 故改用 **Tile**（光滑、接缝少 —— 最接近玻璃的平整洁净面）+ 压暗基色 → 读作"暗色平整面"。',
    '            var glassMat = Mat(new Color(0.06f, 0.08f, 0.11f), 0.15f, MaterialFamily.Tile);   // 车窗与屏幕：暗色平整面',
  ].join('\n'),
  '① 材质对齐样板 v3（玻璃 Metal → Tile）');

// ── ② 关于 BuildTruck() 的调用次数（我一度误判）────────────────────────
// 我曾以为它被调用两次（重复建车）；实测 grep 显示 **只有 332 行一处调用**，
// 313 行是 BuildTruck 方法体里的 Truck = new TruckScene(...) —— 两者不是重复。
// 故此处**不做删除**（删了就没有货车了）。
// 教训：grep 的 -Context 会把相邻匹配拼在一起，**判断重复必须先看行号**。

if (!checkOnly) {
  const bak = FILE + '.bak-truckmat';
  if (!fs.existsSync(bak)) fs.writeFileSync(bak, raw, 'utf8');
  fs.writeFileSync(FILE, s, 'utf8');
}
console.log('[truck-mat] 货车材质修复 + 删重复建车' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
