#!/usr/bin/env node
/**
 * register-kit-glb.mjs — 把一个 GLB 登记为"套件"（走本仓既有资产管线约定）
 *
 * ## 为什么需要它
 * 门禁 `gate-model.mjs` 的 M9/M10 读的是**真源目录**
 * `unity/Assets/ThirdParty/CC0/kits/<file>`（`manifest.sourceRoot`），
 * 并核对 `sha256` 与 `bytes`；运行时 `KitMeshLibrary` 又从
 * `Assets/Resources/Kits/<id>.glb.bytes` 加载。手工放文件必然对不上门禁。
 *
 * ## 三处放置（照抄既有 11 个套件的实际布局，实测确认）
 * | 位置 | 用途 |
 * |---|---|
 * | `unity/Assets/ThirdParty/CC0/kits/<id>.glb` | **真源**（gate-model M9/M10 读这里） |
 * | `unity/Assets/StreamingAssets/Kits/<id>.glb` | 打包直读（StreamingAssets 原样进包） |
 * | `unity/Assets/Resources/Kits/<id>.glb.bytes` | 运行时 `Resources.Load`（**路径写 `Kits/<id>.glb`，不要写 `.bytes`** —— Resources 会剥最后一个扩展名） |
 *
 * ## 清单条目字段（照抄实测到的既有条目）
 * `id · kind · file · tags · addressablesGroup · sha256 · bytes · triangles · generator · footprint · resPath`
 *
 * ## 纪律
 * · **清单是唯一真源、禁止手工改**（本仓规程）→ 本脚本负责写入；
 * · 写入前备份清单；同 id 已存在时**更新**而不是追加（幂等）；
 * · 写完必须跑 `node tools/gate-model.mjs` 验证 M9/M10/M11。
 *
 * 用法：node tools/register-kit-glb.mjs --src <glb路径> --id <kitId> [--kind prop] [--tags a,b] \
 *        [--footprint W,D] [--triangles N] [--generator "说明"]
 */
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const argv = process.argv.slice(2);
const argOf = (n, d) => { const i = argv.indexOf(n); return i >= 0 && i + 1 < argv.length ? argv[i + 1] : d; };

const src = argOf('--src', null);
const id = argOf('--id', null);
if (!src || !id) { console.error('[kit] ✗ 需要 --src <glb> 与 --id <kitId>'); process.exit(1); }
if (!fs.existsSync(src)) { console.error(`[kit] ✗ 源文件不存在：${src}`); process.exit(1); }

const kind = argOf('--kind', 'prop');
const tags = argOf('--tags', 'level').split(',').map((t) => t.trim()).filter(Boolean);
const generator = argOf('--generator', 'tools/register-kit-glb.mjs（外部 GLB 登记）');
const fpArg = argOf('--footprint', null);
const triArg = argOf('--triangles', null);

const buf = fs.readFileSync(src);
const sha = crypto.createHash('sha256').update(buf).digest('hex');

// ── ① 三处放置 ────────────────────────────────────────────────────────
const canon = path.join(ROOT, 'unity/Assets/ThirdParty/CC0/kits', `${id}.glb`);
const stream = path.join(ROOT, 'unity/Assets/StreamingAssets/Kits', `${id}.glb`);
const resBytes = path.join(ROOT, 'unity/Assets/Resources/Kits', `${id}.glb.bytes`);
for (const [p, how] of [[canon, 'copy'], [stream, 'copy'], [resBytes, 'bytes']]) {
  fs.mkdirSync(path.dirname(p), { recursive: true });
  fs.writeFileSync(p, buf);      // bytes 与 glb 内容相同（Resources 只按扩展名区分加载方式）
  console.log(`  ✓ ${how === 'copy' ? 'glb' : 'bytes'} → ${path.relative(ROOT, p)}（${buf.length} 字节）`);
}

// ── ② 从 GLB 里读真实三角形数（不猜：解析 JSON chunk 的 accessor）──────
function glbTriangles(b) {
  if (b.readUInt32LE(0) !== 0x46546C67) return null;          // 'glTF'
  const total = b.readUInt32LE(8);
  let off = 12;
  while (off + 8 <= Math.min(total, b.length)) {
    const clen = b.readUInt32LE(off), ctype = b.readUInt32LE(off + 4);
    const data = b.subarray(off + 8, off + 8 + clen);
    if (ctype === 0x4E4F534A) {                                // 'JSON'
      const j = JSON.parse(data.toString('utf8'));
      let tris = 0;
      for (const m of j.meshes ?? []) for (const p of m.primitives ?? []) {
        const acc = p.indices != null ? j.accessors?.[p.indices] : j.accessors?.[p.attributes?.POSITION];
        if (acc?.count) tris += Math.floor(acc.count / 3);
      }
      return { tris, nodes: (j.nodes ?? []).length, materials: (j.materials ?? []).length, meshes: (j.meshes ?? []).length };
    }
    off += 8 + clen + ((4 - (clen % 4)) % 4);
  }
  return null;
}
const info = glbTriangles(buf);
const triangles = triArg ? Number(triArg) : (info?.tris ?? 0);
console.log(`  GLB 结构：meshes=${info?.meshes} nodes=${info?.nodes} materials=${info?.materials} triangles=${triangles}`);

// ── ③ 回写清单（幂等：同 id 更新）─────────────────────────────────────
const MAN = path.join(ROOT, 'unity/Assets/Data/asset-manifest.json');
const rawMan = fs.readFileSync(MAN, 'utf8');
const man = JSON.parse(rawMan);
const entry = {
  id,
  kind,
  file: `kits/${id}.glb`,
  tags,
  addressablesGroup: kind === 'room' ? 'kits_rooms' : 'kits_props',
  sha256: sha,
  bytes: buf.length,
  triangles,
  generator,
  resPath: `Kits/${id}.glb`,
};
if (fpArg) entry.footprint = fpArg.split(',').map(Number);

const list = Array.isArray(man.kits) ? man.kits : [];
const idx = list.findIndex((k) => k.id === id);
if (idx >= 0) { list[idx] = { ...list[idx], ...entry }; console.log(`  · 清单已有 ${id} → 已更新`); }
else { list.push(entry); console.log(`  · 清单新增 ${id}`); }
man.kits = list;
if (man.verticesByKit && !man.verticesByKit[id]) {
  // 顶点数由 gen-kits 维护；外部 GLB 没有它也能过门禁，这里不臆造，只提示
  console.log('  ⚠ verticesByKit 无此套件（该字段由 gen-kits.mjs 维护；外部 GLB 不写）');
}
const bak = MAN + '.bak-kitreg';
if (!fs.existsSync(bak)) fs.writeFileSync(bak, rawMan, 'utf8');
fs.writeFileSync(MAN, JSON.stringify(man, null, 2) + '\n', 'utf8');
console.log(`  ✓ 清单已回写（${list.length} 个套件）`);
console.log('  下一步：node tools/gate-model.mjs（验 M9/M10/M11）');
