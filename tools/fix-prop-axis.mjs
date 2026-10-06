#!/usr/bin/env node
/**
 * fix-prop-axis.mjs — 把「Z-up 导出」的大厅道具转成本工程的 **Y-up** 约定。
 *
 * ## 问题（实测，不是推断）
 * `tools/gen-hall-kits.mjs` 头部写明了工程契约：
 * > 配方用 Blender Z-up（z=高度）；导出 `export_yup=True` → **GLB 的 Y 是高度**，
 * > 与 whisper 关卡契约（Y=高度）一致。
 *
 * 而这批道具**是用 `export_yup=False` 导出的**（保留了 Blender 的 Z-up），实测：
 * ```
 *   Hall_IBeamColumn   X 0.340  Y 0.340  Z 3.100   ← 立柱 3.1m 的高跑到 Z
 *   Hall_Barrel        X 0.615  Y 0.615  Z 0.893   ← 桶高跑到 Z
 *   Hall_PalletWood    X 1.200  Y 0.800  Z 0.066   ← 托盘该"平躺"（薄轴在 Y），现在立在 Z
 *   Hall_RackUpright   X 0.132  Y 0.028  Z 1.802   ← 立柱高跑到 Z
 *   Hall_LampIndustrial X 0.597 Y 0.597  Z 1.076   ← 吊灯高跑到 Z
 * ```
 * **尺寸本身全是对的**（3.1m 立柱、1.2×0.8 标准托盘、0.615×0.893 油桶），**只是轴放错了**。
 *
 * ## 为什么必须先在数据上修，而不是"摆放时转一下"
 * 摆放时转意味着**每个调用点都要记得转**，且忘一个就躺倒一个 ——
 * 这正是本项目反复出现的失效模式（结构信息靠"记得"而不是靠数据本身）。
 * 数据修对了，任何调用点都自然正确。这与 `tools/fix-truck-axis.mjs` 同一思路。
 *
 * ## 变换：纯旋转 `(x, y, z) → (x, z, −y)`
 * · 立柱：高 Z 3.100 → Y 3.100 ✓
 * · 托盘：薄轴 Z 0.066 → Y 0.066 ✓（平躺）
 * **不需要平移**：Blender Z-up 的底面本就在 z=0，旋转后落在 y=0 ✓
 * （货车那次的 `3.52 − z` 是因为它的底面不在 0，需要额外平移。）
 *
 * ## 不改哪些（避免"手痒改坏好的"）
 * `Hall_CrateWood`（0.604/0.604/0.603，立方体，转不转等价）·
 * `Hall_RackBeam` / `Hall_ElectricPanel` / `Hall_DuctSection` / `Hall_PipeFlange`
 * —— 这四个的长轴/薄轴已经在合理位置，**没有证据说它们错了**。
 *
 * 用法：node tools/fix-prop-axis.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const KITS = path.join(ROOT, 'unity/Assets/Resources/Kits');
const MIRROR = path.join(ROOT, 'unity/Assets/Resources/Models/hall');   // 同一份资产的旧位置，必须一起同步
const checkOnly = process.argv.includes('--check');

/** 需要修的道具 + 修后应有的 (X,Y,Z) 尺寸（用于复核，不是"跑完就算"）。 */
const PROPS = [
  { id: 'Hall_IBeamColumn',    expect: [0.340, 3.100, 0.340] },
  { id: 'Hall_RackUpright',    expect: [0.132, 1.802, 0.028] },
  { id: 'Hall_Barrel',         expect: [0.615, 0.893, 0.615] },
  { id: 'Hall_PalletWood',     expect: [1.200, 0.066, 0.800] },
  { id: 'Hall_LampIndustrial', expect: [0.597, 1.076, 0.597] },
];

const r3 = (x) => Math.round(x * 1000) / 1000;

function parseGlb(buf) {
  if (buf.readUInt32LE(0) !== 0x46546c67) throw new Error('不是 GLB（magic 不符）');
  let off = 12, json = null, bin = null;
  while (off < buf.length) {
    const len = buf.readUInt32LE(off), type = buf.readUInt32LE(off + 4);
    const data = buf.subarray(off + 8, off + 8 + len);
    if (type === 0x4e4f534a) json = JSON.parse(data.toString('utf8'));
    else if (type === 0x004e4942) bin = data;
    off += 8 + len + ((4 - (len % 4)) % 4);
  }
  return { json, bin };
}

/** 由 POSITION accessor 的 min/max 求整体包围盒（**故意用 accessor 而非遍历顶点**：
 *  它就是门禁与运行时读的那份数据，用它复核等于在验"别人会看到的值"）。 */
function bboxOf(json) {
  const mn = [Infinity, Infinity, Infinity], mx = [-Infinity, -Infinity, -Infinity];
  for (const m of json.meshes || []) {
    for (const pr of m.primitives || []) {
      const a = json.accessors[pr.attributes.POSITION];
      if (!a || !a.min || !a.max) continue;
      for (let k = 0; k < 3; k++) { mn[k] = Math.min(mn[k], a.min[k]); mx[k] = Math.max(mx[k], a.max[k]); }
    }
  }
  return { mn, mx };
}

let fixed = 0, already = 0, failed = 0;

for (const prop of PROPS) {
  const src = path.join(KITS, prop.id + '.glb.bytes');
  if (!fs.existsSync(src)) { console.log(`✗ ${prop.id}：找不到 ${src}`); failed++; continue; }

  const buf = fs.readFileSync(src);
  const { json, bin } = parseGlb(buf);
  const before = bboxOf(json);
  const dim = (b) => [b.mx[0] - b.mn[0], b.mx[1] - b.mn[1], b.mx[2] - b.mn[2]];

  // 判据：Z 是长轴（高度跑错）才算"需要修"。已修过的文件跑 --check 应报"已正确"。
  const d = dim(before);
  // 【两级判据，分开判断 —— 这是实测教我的】
  // ① 轴向：尺寸对不对（Y 是不是高度）。不对 ⇒ 要旋转。
  // ② 基准：底面在不在 y=0。不对 ⇒ 只要平移。
  // 我第一版把两者当成一件事，结果转完发现 5 个道具的底面都在负数（原点在几何中心、不在脚底），
  // 而 `TruckScene` 的注释写明工程约定是**「套件脚底在 y=0」**。
  // 分开判断的好处：工具因此**幂等** —— 已转过但没平移的文件再跑一次，只补平移、不会转第二次。
  const needRotate = Math.abs(d[1] - prop.expect[1]) > 0.02;
  const needShift = !needRotate && Math.abs(before.mn[1]) > 0.02;

  if (!needRotate && !needShift) {
    console.log(`✓ ${prop.id}：已是 Y-up 且底面在 y=0（Y=${r3(d[1])}）`);
    already++;
    continue;
  }
  if (checkOnly) {
    console.log(`✗ ${prop.id}：仍是 Z-up（Y=${r3(d[1])}，应为 ${prop.expect[1]}）`);
    failed++;
    continue;
  }

  // ── 应用变换 ──
  // 两种操作可独立发生：旋转（轴不对）· 平移（基准不对）。平移量在旋转**之后**才能算准。
  const binCopy = Buffer.from(bin);
  let nPos = 0, nNrm = 0;
  for (const mesh of json.meshes || []) {
    for (const pr of mesh.primitives || []) {
      const pa = json.accessors[pr.attributes.POSITION];
      const pv = json.bufferViews[pa.bufferView];
      const pBase = (pv.byteOffset || 0) + (pa.byteOffset || 0);

      // 第 1 遍：只旋转（要平移，得先知道旋转后的最低点）
      if (needRotate) {
        for (let i = 0; i < pa.count; i++) {
          const o = pBase + i * 12;
          const x = binCopy.readFloatLE(o), y = binCopy.readFloatLE(o + 4), z = binCopy.readFloatLE(o + 8);
          binCopy.writeFloatLE(x, o); binCopy.writeFloatLE(z, o + 4); binCopy.writeFloatLE(-y, o + 8);
        }
      }
      // 第 2 遍：算旋转后的 Y 最低点 → 求平移量
      let yMin = Infinity;
      for (let i = 0; i < pa.count; i++) {
        const o = pBase + i * 12;
        yMin = Math.min(yMin, binCopy.readFloatLE(o + 4));
      }
      const dy = Math.abs(yMin) > 0.02 ? -yMin : 0;   // 把脚底抬到 y=0

      // 第 3 遍：平移 + 重算 min/max
      const pMin = [Infinity, Infinity, Infinity], pMax = [-Infinity, -Infinity, -Infinity];
      for (let i = 0; i < pa.count; i++) {
        const o = pBase + i * 12;
        let nx = binCopy.readFloatLE(o), ny = binCopy.readFloatLE(o + 4) + dy, nz = binCopy.readFloatLE(o + 8);
        binCopy.writeFloatLE(ny, o + 4);
        pMin[0] = Math.min(pMin[0], nx); pMax[0] = Math.max(pMax[0], nx);
        pMin[1] = Math.min(pMin[1], ny); pMax[1] = Math.max(pMax[1], ny);
        pMin[2] = Math.min(pMin[2], nz); pMax[2] = Math.max(pMax[2], nz);
        nPos++;
      }
      // ⚠ **必须同步 min/max** —— 门禁（gate-asset-bbox）与运行时都读它，不改等于留了个假账
      pa.min = pMin; pa.max = pMax;

      if (pr.attributes.NORMAL !== undefined) {
        const na = json.accessors[pr.attributes.NORMAL];
        const nv = json.bufferViews[na.bufferView];
        const nBase = (nv.byteOffset || 0) + (na.byteOffset || 0);
        for (let i = 0; i < na.count; i++) {
          const o = nBase + i * 12;
          const x = binCopy.readFloatLE(o), y = binCopy.readFloatLE(o + 4), z = binCopy.readFloatLE(o + 8);
          // 法线是方向向量：**只旋转、不平移**
          if (needRotate) { binCopy.writeFloatLE(x, o); binCopy.writeFloatLE(z, o + 4); binCopy.writeFloatLE(-y, o + 8); }
          nNrm++;
        }
      }
    }
  }

  // ── 重新组装 GLB ──
  const jsonStr = JSON.stringify(json);
  const jsonPad = (4 - (jsonStr.length % 4)) % 4;
  const jsonBuf = Buffer.from(jsonStr + ' '.repeat(jsonPad), 'utf8');
  const binPad = (4 - (binCopy.length % 4)) % 4;
  const binOut = binPad ? Buffer.concat([binCopy, Buffer.alloc(binPad)]) : binCopy;
  const header = Buffer.alloc(12);
  header.writeUInt32LE(0x46546c67, 0); header.writeUInt32LE(2, 4);
  header.writeUInt32LE(12 + 8 + jsonBuf.length + 8 + binOut.length, 8);
  const jHead = Buffer.alloc(8); jHead.writeUInt32LE(jsonBuf.length, 0); jHead.writeUInt32LE(0x4e4f534a, 4);
  const bHead = Buffer.alloc(8); bHead.writeUInt32LE(binOut.length, 0); bHead.writeUInt32LE(0x004e4942, 4);
  const out = Buffer.concat([header, jHead, jsonBuf, bHead, binOut]);

  // ── 复核（**先验再写**：不达标就不落盘，避免写出个半成品） ──
  const after = bboxOf(parseGlb(out).json);
  const da = dim(after);
  const ok = prop.expect.every((v, k) => Math.abs(da[k] - v) < 0.02);
  if (!ok) {
    console.log(`✗ ${prop.id}：变换后尺寸 [${da.map(r3).join(', ')}] 与期望 [${prop.expect.join(', ')}] 不符 —— **未写入**`);
    failed++;
    continue;
  }
  const baseOk = Math.abs(after.mn[1]) < 0.02;   // 底面应落在 y=0

  fs.writeFileSync(src, out);
  const mirror = path.join(MIRROR, prop.id + '.glb.bytes');
  if (fs.existsSync(mirror)) fs.writeFileSync(mirror, out);   // 旧位置同步，防止两份漂移

  console.log(`✓ ${prop.id}：Z-up → Y-up · ${nPos} 顶点 / ${nNrm} 法线 · `
    + `[${d.map(r3).join(', ')}] → [${da.map(r3).join(', ')}]${baseOk ? ' · 底面 y=0 ✓' : ' ⚠ 底面不在 y=0（' + r3(after.mn[1]) + '）'}`);
  fixed++;
}

console.log(`\n修 ${fixed} · 已正确 ${already} · 未达标/失败 ${failed}`);
if (failed && checkOnly) process.exit(1);
