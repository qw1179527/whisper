#!/usr/bin/env node
/**
 * glb-material-read.mjs — 扩展 `GlbReader` 读材质（修「管线丢弃 GLB 材质」的缺口）
 *
 * ## 缺口（上一轮定位）
 * `GlbReader.Primitive` 只有 `Positions/Normals/Uvs/Indices`，**没有材质**；
 * 于是 `LevelBuilder` 给每个部件套一个纯色平材质 → **所有套件的 PBR 材质都被丢弃**
 * （样板 v3 的 6 层车漆/玻璃/橡胶…搬不进来，就是这个原因）。
 *
 * ## 本轮做「第一步：读进来」
 * 1. `Primitive` 增加 `MaterialIndex`（**默认 -1**：老套件没有材质也不受影响）；
 * 2. 新增 `KitMaterial` 结构（`BaseColor`/`Metallic`/`Roughness`，glTF 标准 `pbrMetallicRoughness` 字段）；
 * 3. `Model` 增加 `Materials` 列表；
 * 4. `TryRead` 解析 `materials[]`，并在遍历 primitive 时记录 `primitive.material` 索引。
 *
 * ## 为什么这一步是**纯增量、不动既有行为**
 * · 既有 11 个套件的 GLB **本来就没有材质**（`gen-kits.mjs` 只写几何 + 顶点色/单材质名），
 *   解析后 `MaterialIndex` 全是 -1、`Materials` 为空 → 装配逻辑照旧走"平材质"分支，视觉不变；
 * · 新增字段都有默认值，**不改变任何既有判据**（M9/M10 只看容器结构与哈希，不受影响）。
 *
 * ## 第二步（下一轮）
 * `KitMeshLibrary` 暴露材质 → `LevelBuilder`/`TruckScene` 按 `MaterialIndex` 取真实 PBR 参数，
 * 于是货车 6 个 primitive 各自拿到自己的材质（车漆/底盘/玻璃/橡胶/塑料/车牌）。
 *
 * 用法：node tools/glb-material-read.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'unity/Assets/Scripts/Gameplay/Level/GlbReader.cs');
const checkOnly = process.argv.includes('--check');
const log = [];
const fail = (m) => { console.error('[glb-mat] ✗ ' + m); process.exit(1); };

const raw = fs.readFileSync(FILE, 'utf8');
if (raw.includes('KitMaterial')) { console.log('[glb-mat] 已应用，跳过'); process.exit(0); }
let s = raw;
const sub = (from, to, what) => {
  const n = s.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  s = s.replace(from, to);
  log.push('  ✓ ' + what);
};

// ── ① Primitive 加 MaterialIndex + 新增 KitMaterial 结构 ──────────────
sub('            /// <summary>三角索引（每 3 个一个面）。</summary>\n            public int[] Indices;',
  [
    '            /// <summary>三角索引（每 3 个一个面）。</summary>',
    '            public int[] Indices;',
    '            /// <summary>',
    '            /// 本 primitive 用的材质下标（对应 <see cref="Model.Materials"/>）；**-1 = 没有材质**。',
    '            /// 为什么给默认值 -1：既有 11 个套件的 GLB 里没有材质，必须让它们的行为**完全不变**',
    '            /// （装配逻辑见到 -1 就照旧用平材质）。',
    '            /// </summary>',
    '            public int MaterialIndex = -1;',
  ].join('\n'),
  '① Primitive 加 MaterialIndex');

sub('        public sealed class Model',
  [
    '        /// <summary>',
    '        /// 套件材质（glTF 标准 `pbrMetallicRoughness` 的核心三项）。',
    '        /// 出处：glTF 2.0 规范 `materials[].pbrMetallicRoughness`。',
    '        /// **为什么只取这三项**：它们是决定"看起来像什么材料"的主因；',
    '        /// 贴图（baseColorTexture 等）需要 UIImage 资源通道，本仓目前没有，故不假装支持。',
    '        /// </summary>',
    '        public struct KitMaterial',
    '        {',
    '            /// <summary>基色（RGBA，线性 0..1）。glTF 默认 [1,1,1,1]。</summary>',
    '            public float R, G, B, A;',
    '            /// <summary>金属度 0..1。glTF 默认 1.0。</summary>',
    '            public float Metallic;',
    '            /// <summary>粗糙度 0..1。glTF 默认 1.0。</summary>',
    '            public float Roughness;',
    '            /// <summary>材质名（诊断用；glTF 里可选）。</summary>',
    '            public string Name;',
    '            /// <summary>glTF 规范默认值：白、金属 1、粗糙 1。</summary>',
    '            public static KitMaterial Default => new KitMaterial { R = 1f, G = 1f, B = 1f, A = 1f, Metallic = 1f, Roughness = 1f };',
    '        }',
    '',
    '        public sealed class Model',
  ].join('\n'),
  '② 新增 KitMaterial 结构');

// ── ② Model 加 Materials 列表 ─────────────────────────────────────────
sub('            public readonly List<Primitive> Primitives = new List<Primitive>();',
  [
    '            public readonly List<Primitive> Primitives = new List<Primitive>();',
    '            /// <summary>本模型声明的材质（glTF `materials[]`）；空 = 该 GLB 没有材质。</summary>',
    '            public readonly List<KitMaterial> Materials = new List<KitMaterial>();',
  ].join('\n'),
  '③ Model 加 Materials 列表');

// ── ③ 解析 materials[] ────────────────────────────────────────────────
sub('            var m = new Model { Name = "glb" };',
  [
    '            var m = new Model { Name = "glb" };',
    '',
    '            // ── 材质解析（glTF `materials[]`）────────────────────────────────',
    '            // 为什么在这里做：装配端要按 `primitive.material` 索引取参数；',
    '            // 不读出来，程序化几何之外的模型就永远是"一整块纯色"（本项目既有 11 个套件正是如此）。',
    '            var mats = ListOf(MiniJson.GetOrNull(root, "materials"));',
    '            if (mats != null)',
    '            {',
    '                foreach (var matObj in mats)',
    '                {',
    '                    var mm = MapOf(matObj);',
    '                    var km = KitMaterial.Default;',
    '                    if (mm != null)',
    '                    {',
    '                        km.Name = MiniJson.GetOrNull(mm, "name") as string;',
    '                        var pbr = MapOf(MiniJson.GetOrNull(mm, "pbrMetallicRoughness"));',
    '                        if (pbr != null)',
    '                        {',
    '                            var bc = ListOf(MiniJson.GetOrNull(pbr, "baseColorFactor"));',
    '                            if (bc != null && bc.Count >= 3)',
    '                            {',
    '                                km.R = FloatOf(bc[0]); km.G = FloatOf(bc[1]); km.B = FloatOf(bc[2]);',
    '                                km.A = bc.Count >= 4 ? FloatOf(bc[3]) : 1f;',
    '                            }',
    '                            var mf = MiniJson.GetOrNull(pbr, "metallicFactor");',
    '                            if (mf != null) km.Metallic = FloatOf(mf);',
    '                            var rf = MiniJson.GetOrNull(pbr, "roughnessFactor");',
    '                            if (rf != null) km.Roughness = FloatOf(rf);',
    '                        }',
    '                    }',
    '                    m.Materials.Add(km);',
    '                }',
    '            }',
  ].join('\n'),
  '④ 解析 materials[] 到 Model.Materials');

// ── ④ 遍历 primitive 时记录材质索引 ───────────────────────────────────
sub('                    if (posRef == null) { reason = "primitive 缺 POSITION"; return false; }',
  [
    '                    if (posRef == null) { reason = "primitive 缺 POSITION"; return false; }',
    '',
    '                    // 记录本 primitive 的材质下标（缺省 -1 = 没有材质，装配端据此走平材质回退）',
    '                    int materialIndex = -1;',
    '                    var matRef = MiniJson.GetOrNull(prim, "material");',
    '                    if (matRef != null)',
    '                    {',
    '                        int mi = IntOf(matRef);',
    '                        if (mi >= 0 && mi < m.Materials.Count) materialIndex = mi;',
    '                    }',
  ].join('\n'),
  '⑤ primitive 记录 material 索引');

// ── ⑤ 把材质索引写进 Primitive ───────────────────────────────────────
sub('var p = new Primitive { Positions = positions, Normals = normals, Uvs = uvs, Indices = indices };',
  'var p = new Primitive { Positions = positions, Normals = normals, Uvs = uvs, Indices = indices, MaterialIndex = materialIndex };',
  '⑥ 材质索引写进 Primitive');

if (!checkOnly) {
  const bak = FILE + '.bak-glbmat';
  if (!fs.existsSync(bak)) fs.writeFileSync(bak, raw, 'utf8');
  fs.writeFileSync(FILE, s, 'utf8');
}
console.log('[glb-mat] GlbReader 读材质（第一步：读进来）' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
console.log('  ⚠ 仍需：把 materialIndex 写进 Primitive（见下一步）+ FloatOf 助手是否存在');
