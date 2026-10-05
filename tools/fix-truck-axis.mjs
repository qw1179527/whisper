#!/usr/bin/env node
// fix-truck-axis.mjs —— 把 truck_eurocargo.glb 转回正确的 Y-up 朝向。
//
// ## 问题（gate-asset-bbox 抓到的真实缺陷）
// 实测当前 GLB 的包围盒：
// ```
//   X [-1.35,  1.35]   宽 2.7    ✓
//   Y [-1.533, 7.62]   9.153     ✗ 这是车【长】，却落在 Y 上
//   Z [0,      3.52]   3.52      ✗ 这是车【高】，却落在 Z 上
// ```
// 而 `asset-manifest.json` 的 `footprintNote` 明确写着
// **「footprint=[宽(x),深(z)]，单位米」** —— 即本工程用 **Y-up**：
// Y = 高度、Z = 深度/长度。
//
// ## 这不是"一开始就错"，是【退化】
// `tools/fix-truck-contains-bounds.mjs` 里记录了前一位智能体**实测过**的正确值：
// ```
//   X ±1.35（含后视镜）· Y 0..3.52（含车顶导流罩）· Z −1.53..7.62（含坡道与驾驶室）
// ```
// 而 `footprint=[2.7, 9.153]` 正是照那组值写的。**⇒ 后来某次替换几何时被转错了。**
//
// ## 交叉验证（为什么确定是模型错、而不是门禁错）
// 12 个套件里，按 (X,Z) 解读 footprint 匹配 **11 个**、按 (X,Y) 解读只匹配 3 个；
// 唯一在 (X,Z) 下不匹配的就是货车。且其余套件的 Y 值都符合"高度"语义
// （bed_b Y=0.52 床是矮的 · cabinet_a Y=1.16 柜子是高 · morgue Y=1.69）。
//
// ## 变换（算出来的，不是试出来的）
// 要同时满足「Y 变成 [0, 3.52]」与「Z 保持 [−1.533, 7.62]」，唯一解是
// ```
//   (x, y, z) → (x, 3.52 − z, y)
// ```
// 线性部分矩阵 `[[1,0,0],[0,0,−1],[0,1,0]]` 的**行列式 = +1** —— 是真旋转，不是镜像。
// （若用纯交换 (x,z,y)，行列式 −1 = 镜像：会翻面、法线反向、后视镜/方向盘左右颠倒。）
//
// ## 用法
//   node tools/fix-truck-axis.mjs --check    # 只测不改
//   node tools/fix-truck-axis.mjs            # 就地修（真源 + 运行时副本 + 清单哈希）
import fs from 'node:fs';
import crypto from 'node:crypto';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const SRC = path.join(ROOT, 'unity/Assets/ThirdParty/CC0/kits/truck_eurocargo.glb');
const RUNTIME = path.join(ROOT, 'unity/Assets/Resources/Kits/truck_eurocargo.glb.bytes');
const MANIFEST = path.join(ROOT, 'unity/Assets/Data/asset-manifest.json');
const checkOnly = process.argv.includes('--check');

const r3 = (x) => Math.round(x * 1000) / 1000;

function parseGlb(buf) {
  if (buf.readUInt32LE(0) !== 0x46546c67) throw new Error('不是 GLB');
  let off = 12, json = null, bin = null, jsonChunk = null;
  const chunks = [];
  while (off + 8 <= buf.length) {
    const len = buf.readUInt32LE(off), type = buf.readUInt32LE(off + 4);
    const data = buf.subarray(off + 8, off + 8 + len);
    chunks.push({ type, len, dataOff: off + 8 });
    if (type === 0x4e4f534a) json = JSON.parse(data.toString('utf8'));
    if (type === 0x004e4942) bin = data;
    off += 8 + len;
  }
  return { json, bin, chunks };
}

function bboxOf(json, bin) {
  const mn = [Infinity, Infinity, Infinity], mx = [-Infinity, -Infinity, -Infinity];
  for (const a of json.accessors) {
    if (!a.min || !a.max || a.type !== 'VEC3') continue;
    for (let i = 0; i < 3; i++) { mn[i] = Math.min(mn[i], a.min[i]); mx[i] = Math.max(mx[i], a.max[i]); }
  }
  return { mn, mx };
}

const buf = fs.readFileSync(SRC);
const { json, bin } = parseGlb(buf);
const before = bboxOf(json, bin);

console.log('=== 修前包围盒 ===');
console.log(`  X [${r3(before.mn[0])}, ${r3(before.mx[0])}]  尺寸 ${r3(before.mx[0] - before.mn[0])}`);
console.log(`  Y [${r3(before.mn[1])}, ${r3(before.mx[1])}]  尺寸 ${r3(before.mx[1] - before.mn[1])}`);
console.log(`  Z [${r3(before.mn[2])}, ${r3(before.mx[2])}]  尺寸 ${r3(before.mx[2] - before.mn[2])}`);

// 期望：Y = 高度 [0, 3.52]，Z = 长度 [−1.533, 7.62]
const EXPECT = { yMin: 0, yMax: 3.52, zMin: -1.533, zMax: 7.62 };
const needsFix = Math.abs(before.mx[1] - EXPECT.yMax) > 0.05 || Math.abs(before.mx[2] - EXPECT.zMax) > 0.05;

if (!needsFix) { console.log('\n✓ 朝向已正确，无需修改'); process.exit(0); }
if (checkOnly) { console.log('\n✗ 朝向错误（Y 应为高度、Z 应为长度）—— 去掉 --check 即修'); process.exit(1); }

console.log('\n=== 应用变换 (x, y, z) → (x, 3.52 − z, y) ===');
const SHIFT = EXPECT.yMax;   // 3.52
const binCopy = Buffer.from(bin);   // 就地改这份副本
let nPos = 0, nNrm = 0;

for (const [pi, pr] of json.meshes[0].primitives.entries()) {
  // ── POSITION：旋转 + 平移 ──
  const pa = json.accessors[pr.attributes.POSITION];
  const pv = json.bufferViews[pa.bufferView];
  const pBase = (pv.byteOffset || 0) + (pa.byteOffset || 0);
  const pMin = [Infinity, Infinity, Infinity], pMax = [-Infinity, -Infinity, -Infinity];
  for (let i = 0; i < pa.count; i++) {
    const o = pBase + i * 12;
    const x = binCopy.readFloatLE(o), y = binCopy.readFloatLE(o + 4), z = binCopy.readFloatLE(o + 8);
    const nx = x, ny = SHIFT - z, nz = y;
    binCopy.writeFloatLE(nx, o); binCopy.writeFloatLE(ny, o + 4); binCopy.writeFloatLE(nz, o + 8);
    pMin[0] = Math.min(pMin[0], nx); pMax[0] = Math.max(pMax[0], nx);
    pMin[1] = Math.min(pMin[1], ny); pMax[1] = Math.max(pMax[1], ny);
    pMin[2] = Math.min(pMin[2], nz); pMax[2] = Math.max(pMax[2], nz);
    nPos++;
  }
  pa.min = pMin; pa.max = pMax;   // **必须同步**：门禁与运行时都读它

  // ── NORMAL：只旋转、**不平移**（法线是方向向量）──
  const na = json.accessors[pr.attributes.NORMAL];
  const nv = json.bufferViews[na.bufferView];
  const nBase = (nv.byteOffset || 0) + (na.byteOffset || 0);
  for (let i = 0; i < na.count; i++) {
    const o = nBase + i * 12;
    const x = binCopy.readFloatLE(o), y = binCopy.readFloatLE(o + 4), z = binCopy.readFloatLE(o + 8);
    binCopy.writeFloatLE(x, o); binCopy.writeFloatLE(-z, o + 4); binCopy.writeFloatLE(y, o + 8);
    nNrm++;
  }
  console.log(`  primitive[${pi}]：${pa.count} 顶点 · ${na.count} 法线`);
}

// ── 重新组装 GLB（JSON 长度会变 → 重新补位）──
const jsonStr = JSON.stringify(json);
const jsonPad = (4 - (jsonStr.length % 4)) % 4;
const jsonBuf = Buffer.from(jsonStr + ' '.repeat(jsonPad), 'utf8');
const binPad = (4 - (binCopy.length % 4)) % 4;
const binOut = binPad ? Buffer.concat([binCopy, Buffer.alloc(binPad)]) : binCopy;

const header = Buffer.alloc(12);
header.writeUInt32LE(0x46546c67, 0);
header.writeUInt32LE(2, 4);
header.writeUInt32LE(12 + 8 + jsonBuf.length + 8 + binOut.length, 8);
const jHead = Buffer.alloc(8); jHead.writeUInt32LE(jsonBuf.length, 0); jHead.writeUInt32LE(0x4e4f534a, 4);
const bHead = Buffer.alloc(8); bHead.writeUInt32LE(binOut.length, 0); bHead.writeUInt32LE(0x004e4942, 4);
const out = Buffer.concat([header, jHead, jsonBuf, bHead, binOut]);

fs.writeFileSync(SRC, out);
fs.writeFileSync(RUNTIME, out);

// ── 复核 ──
const after = bboxOf(parseGlb(fs.readFileSync(SRC)).json, null);
console.log('\n=== 修后包围盒 ===');
console.log(`  X [${r3(after.mn[0])}, ${r3(after.mx[0])}]  尺寸 ${r3(after.mx[0] - after.mn[0])}`);
console.log(`  Y [${r3(after.mn[1])}, ${r3(after.mx[1])}]  尺寸 ${r3(after.mx[1] - after.mn[1])}`);
console.log(`  Z [${r3(after.mn[2])}, ${r3(after.mx[2])}]  尺寸 ${r3(after.mx[2] - after.mn[2])}`);
const okY = Math.abs(after.mx[1] - EXPECT.yMax) < 0.05 && Math.abs(after.mn[1] - EXPECT.yMin) < 0.05;
const okZ = Math.abs(after.mx[2] - EXPECT.zMax) < 0.05 && Math.abs(after.mn[2] - EXPECT.zMin) < 0.05;
console.log(okY && okZ ? '  ✓ Y/Z 与「正确值」记录一致' : '  ✗ 仍未达标');

// ── 清单哈希必须同步（清单是唯一真源、禁止手改，但哈希是文件的函数）──
const sha = crypto.createHash('sha256').update(out).digest('hex');
const man = JSON.parse(fs.readFileSync(MANIFEST, 'utf8'));
const k = man.kits.find((x) => x.id === 'truck_eurocargo');
const oldSha = k.sha256, oldBytes = k.bytes;
k.sha256 = sha; k.bytes = out.length;
fs.writeFileSync(MANIFEST, JSON.stringify(man, null, 2) + '\n');
console.log(`\n=== 清单同步 ===`);
console.log(`  sha256 ${oldSha.slice(0, 12)}… → ${sha.slice(0, 12)}…`);
console.log(`  bytes  ${oldBytes} → ${out.length}（顶点位置变了，压缩后字节数会变）`);
