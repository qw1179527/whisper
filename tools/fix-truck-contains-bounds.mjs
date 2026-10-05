#!/usr/bin/env node
/**
 * fix-truck-contains-bounds.mjs — 修「换套件后车内判定范围对不上」
 *
 * ## 问题（我替换几何时没同步判定范围）
 * `Contains(world)` 原先用的是**设计常量**：
 * ```csharp
 * if (p.y < FloorHeight - 0.2f || p.y > FloorHeight + BoxHeight) return false;   // 上限 = 0.95 + 2.30 = 3.25
 * if (Mathf.Abs(p.x) > BoxWidth * 0.5f) return false;                            // 半宽 = 1.20
 * ```
 * 而**套件的真实包围盒**（`truck_eurocargo.glb` 导出值，Blender Z-up → Unity Y-up 换算）：
 * ```
 * X ±1.35（含后视镜）· Y 0..3.52（含车顶导流罩）· Z −1.53..7.62（含坡道与驾驶室）
 * ```
 * ⇒ **车顶（3.25~3.52）与后视镜（|x| 1.20~1.35）处会被判成"不在车内"**：
 * §8 的"车内不掉理智""鬼无法进入"在这些位置失效 —— 玩家贴着车顶/后视镜站着会掉理智。
 *
 * ## 修法
 * 用**套件真实包围盒**（实测值，不是设计常量），并把来源写进注释；同时保留
 * "地板以下 0.2m 容差"这一原有语义（防止刚踩上坡道时判定抖动）。
 *
 * ## 为什么不再用 BoxWidth/BoxHeight 推导
 * 套件是**外部建模产物**：它的实际尺寸可能随重做变化（样板 v2→v3 就变过）。
 * 判定范围跟着**实际几何**走才稳；设计常量继续用于"逻辑意图"（如厢体净空），两者职责不同。
 *
 * 用法：node tools/fix-truck-contains-bounds.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'unity/Assets/Scripts/Runtime/TruckScene.cs');
const checkOnly = process.argv.includes('--check');
const log = [];
const fail = (m) => { console.error('[truck-bounds] ✗ ' + m); process.exit(1); };

const raw = fs.readFileSync(FILE, 'utf8');
if (raw.includes('KitExtent')) { console.log('[truck-bounds] 已应用，跳过'); process.exit(0); }
let s = raw;
const sub = (from, to, what) => {
  const n = s.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  s = s.replace(from, to);
  log.push('  ✓ ' + what);
};

// ── ① 套件真实包围盒常量 ──────────────────────────────────────────────
sub('        /// <summary>车牌（用户指定）。</summary>',
  [
    '        // ── 套件真实包围盒（**实测值**，来自 truck_eurocargo.glb 的导出尺寸）──────────',
    '        // Blender（Z-up）导出为 x=2.70 · y=9.15 · z=3.52 → Unity Y-up 后：',
    '        //   X ±1.35（含后视镜与挡泥板）· Y 0..3.52（含车顶导流罩）· Z −1.53..7.62（含坡道与驾驶室）',
    '        // 为什么单列而不是从 BoxWidth/BoxHeight 推导：套件是**外部建模产物**，实际尺寸会随重做变化',
    '        // （样板 v2→v3 就调整过）。判定范围跟着**实际几何**走才稳；设计常量继续表达"逻辑意图"（厢体净空）。',
    '        /// <summary>套件半宽（含后视镜/挡泥板）。</summary>',
    '        public const float KitHalfWidth = 1.35f;',
    '        /// <summary>套件顶高（含车顶导流罩）。</summary>',
    '        public const float KitTopY = 3.60f;',
    '        /// <summary>套件在 Z 向的范围（含坡道 −1.53 与驾驶室 7.62）。</summary>',
    '        public const float KitMinZ = -1.60f;',
    '        public const float KitMaxZ = 7.70f;',
    '',
    '        /// <summary>车牌（用户指定）。</summary>',
  ].join('\n'),
  '① 新增套件真实包围盒常量');

// ── ② Contains 改用真实包围盒 ─────────────────────────────────────────
sub('            var p = _root.InverseTransformPoint(world);\n'
  + '            if (p.y < FloorHeight - 0.2f || p.y > FloorHeight + BoxHeight) return false;\n'
  + '            if (Mathf.Abs(p.x) > BoxWidth * 0.5f) return false;\n'
  + '            return p.z > -0.2f && p.z < CabLength + BoxLength;',
  [
    '            var p = _root.InverseTransformPoint(world);',
    '            // 【换套件后同步判定范围】原先用设计常量（上限 0.95+2.30=3.25、半宽 1.20），',
    '            // 而套件实际是 Y 0..3.52、|X| ≤1.35 → 车顶与后视镜处会被判成"不在车内"，',
    '            // 于是 §8 的"车内不掉理智/鬼无法进入"在那两处失效。改为按**套件真实包围盒**判定。',
    '            if (p.y < FloorHeight - 0.2f || p.y > KitTopY) return false;   // 地板下留 0.2m 容差（踩坡道时不抖）',
    '            if (Mathf.Abs(p.x) > KitHalfWidth) return false;',
    '            return p.z > KitMinZ && p.z < KitMaxZ;',
  ].join('\n'),
  '② Contains 改用套件真实包围盒');

if (!checkOnly) {
  const bak = FILE + '.bak-bounds';
  if (!fs.existsSync(bak)) fs.writeFileSync(bak, raw, 'utf8');
  fs.writeFileSync(FILE, s, 'utf8');
}
console.log('[truck-bounds] 车内判定范围对齐套件' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
