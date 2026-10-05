#!/usr/bin/env node
/**
 * kit-material-expose.mjs — 第二步：把「材质」从读取层暴露到装配层
 *
 * ## 承接
 * 上一轮 `GlbReader` 已能读出 glTF 材质（`KitMaterial` + `Primitive.MaterialIndex`）。
 * 本轮让 `KitMeshLibrary` 把它暴露出去，供装配端按部件套真实 PBR 参数。
 *
 * ## 设计（**为什么不破坏既有调用方**）
 * `GetParts(kitId)` 的签名与语义**一个字不改**（`LevelBuilder` 等既有调用方不受影响）。
 * 新增两个**并列**入口：
 * | 新 API | 用途 |
 * |---|---|
 * | `GetPartMaterials(kitId)` | 返回 `int[]`：每个部件用的材质下标（-1 = 无材质）。与 `GetParts` **同序** |
 * | `GetMaterials(kitId)` | 返回 `KitMaterial[]`：该套件声明的材质表（可能为空） |
 * 另加 `HasMaterials(kitId)` 供装配端快速判断"要不要走材质分支"。
 *
 * 为什么要与 `GetParts` **同序**：货车 GLB 的 6 个 primitive 各用 1 种材质，
 * 装配端是"按下标拿网格、同样下标拿材质"，同序才能一一对应。
 *
 * ## 缓存
 * 材质表与部件表一起缓存（同一个 `_cache` 键的派生数据），避免每次装配都重解析 GLB。
 * 新增两个字典：`_matCache`（材质表）、`_partMatCache`（部件→材质下标）。`Clear()` 一并清。
 *
 * 用法：node tools/kit-material-expose.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'unity/Assets/Scripts/Gameplay/Level/KitMeshLibrary.cs');
const checkOnly = process.argv.includes('--check');
const log = [];
const fail = (m) => { console.error('[kit-mat] ✗ ' + m); process.exit(1); };

const raw = fs.readFileSync(FILE, 'utf8');
if (raw.includes('GetPartMaterials')) { console.log('[kit-mat] 已应用，跳过'); process.exit(0); }
let s = raw;
const sub = (from, to, what) => {
  const n = s.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  s = s.replace(from, to);
  log.push('  ✓ ' + what);
};

// ── ① 缓存字段 ────────────────────────────────────────────────────────
sub('        static readonly Dictionary<string, Mesh[]> _cache = new Dictionary<string, Mesh[]>(StringComparer.Ordinal);',
  [
    '        static readonly Dictionary<string, Mesh[]> _cache = new Dictionary<string, Mesh[]>(StringComparer.Ordinal);',
    '        /// <summary>套件材质表缓存（与 <see cref="_cache"/> 同源同寿命）。</summary>',
    '        static readonly Dictionary<string, GlbReader.KitMaterial[]> _matCache = new Dictionary<string, GlbReader.KitMaterial[]>(StringComparer.Ordinal);',
    '        /// <summary>「部件 → 材质下标」缓存（与 <see cref="GetParts"/> **同序**；-1 = 无材质）。</summary>',
    '        static readonly Dictionary<string, int[]> _partMatCache = new Dictionary<string, int[]>(StringComparer.Ordinal);',
  ].join('\n'),
  '① 新增两个缓存字段');

// ── ② Clear 一并清 ────────────────────────────────────────────────────
sub('        public static void Clear() { _cache.Clear(); LastProblem = null; }',
  '        public static void Clear() { _cache.Clear(); _matCache.Clear(); _partMatCache.Clear(); LastProblem = null; }',
  '② Clear 清掉新缓存');

// ── ③ GetParts 里同时缓存材质 ─────────────────────────────────────────
sub('            _cache[kitId] = parts;\n            return parts;',
  [
    '            _cache[kitId] = parts;',
    '            // 顺手把材质表与"部件→材质下标"记下来（同一次解析，不重复读盘/解析）',
    '            // 不引入 System.Linq（本文件未 import 它）→ 显式拷贝，最小改动',
    '            var matArr = new GlbReader.KitMaterial[model.Materials.Count];',
    '            for (int mi = 0; mi < matArr.Length; mi++) matArr[mi] = model.Materials[mi];',
    '            _matCache[kitId] = matArr;',
    '            var idx = new int[parts.Length];',
    '            for (int i = 0; i < idx.Length; i++) idx[i] = model.Primitives[i].MaterialIndex;',
    '            _partMatCache[kitId] = idx;',
    '            return parts;',
  ].join('\n'),
  '③ GetParts 同时缓存材质（同一次解析）');

// ── ④ 新增三个公开入口 ────────────────────────────────────────────────
sub('        public static Mesh Get(string kitId)',
  [
    '        /// <summary>',
    '        /// 每个部件用的材质下标（与 <see cref="GetParts"/> **同序**）；-1 = 该部件没有材质。',
    '        /// 装配端用法：`var parts = GetParts(id); var mi = GetPartMaterials(id);`',
    '        /// 然后 `parts[i]` 配 `GetMaterials(id)[mi[i]]`。',
    '        /// </summary>',
    '        public static int[] GetPartMaterials(string kitId)',
    '        {',
    '            if (string.IsNullOrEmpty(kitId)) return null;',
    '            if (_partMatCache.TryGetValue(kitId, out var cached)) return cached;',
    '            // 未解析过 → 借 GetParts 触发一次解析（它会把材质一并缓存）',
    '            if (GetParts(kitId) == null) return null;',
    '            return _partMatCache.TryGetValue(kitId, out var again) ? again : null;',
    '        }',
    '',
    '        /// <summary>该套件声明的材质表（可能为空 = GLB 里没有材质，装配端应走平材质回退）。</summary>',
    '        public static GlbReader.KitMaterial[] GetMaterials(string kitId)',
    '        {',
    '            if (string.IsNullOrEmpty(kitId)) return null;',
    '            if (_matCache.TryGetValue(kitId, out var cached)) return cached;',
    '            if (GetParts(kitId) == null) return null;',
    '            return _matCache.TryGetValue(kitId, out var again) ? again : null;',
    '        }',
    '',
    '        /// <summary>该套件是否带材质（快速判断：装配端据此决定走材质分支还是平材质回退）。</summary>',
    '        public static bool HasMaterials(string kitId)',
    '        {',
    '            var mats = GetMaterials(kitId);',
    '            return mats != null && mats.Length > 0;',
    '        }',
    '',
    '        public static Mesh Get(string kitId)',
  ].join('\n'),
  '④ 新增 GetPartMaterials / GetMaterials / HasMaterials');

if (!checkOnly) {
  const bak = FILE + '.bak-kitmat';
  if (!fs.existsSync(bak)) fs.writeFileSync(bak, raw, 'utf8');
  fs.writeFileSync(FILE, s, 'utf8');
}
console.log('[kit-mat] 材质暴露到装配层' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
