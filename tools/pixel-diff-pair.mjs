#!/usr/bin/env node
/**
 * pixel-diff-pair.mjs — 两张/多张 PNG 的逐像素差异（区域可选），用于"点击后画面是否变化"这类判定。
 *
 * 与 tools/kit-view-diff.mjs 的分工：那个认 `<A-with>/<B-without>` 文件名模式、专做套件 A/B；
 * 这个是无模式的通用比对，可指定区域（x0,y0,x1,y1 比例），并直接给"变化像素占比"。
 *
 * 用法：node tools/pixel-diff-pair.mjs <基准图> <对照图...> [--region x0,y0,x1,y1]
 */
import fs from 'node:fs';
import zlib from 'node:zlib';

function decodePng(buf) {
  if (buf.readUInt32BE(0) !== 0x89504e47) throw new Error('不是 PNG');
  let off = 8, w = 0, h = 0, bitDepth = 0, colorType = 0;
  const idat = [];
  while (off < buf.length) {
    const len = buf.readUInt32BE(off);
    const type = buf.toString('ascii', off + 4, off + 8);
    const data = buf.subarray(off + 8, off + 8 + len);
    if (type === 'IHDR') { w = data.readUInt32BE(0); h = data.readUInt32BE(4); bitDepth = data[8]; colorType = data[9]; }
    else if (type === 'IDAT') idat.push(data);
    else if (type === 'IEND') break;
    off += 12 + len;
  }
  if (bitDepth !== 8) throw new Error('只支持 8bit PNG');
  const ch = colorType === 6 ? 4 : colorType === 2 ? 3 : 0;
  if (!ch) throw new Error(`不支持的颜色类型 ${colorType}`);
  const raw = zlib.inflateSync(Buffer.concat(idat));
  const stride = w * ch;
  const out = Buffer.alloc(w * h * 4);
  let prev = Buffer.alloc(stride), p = 0;
  for (let y = 0; y < h; y++) {
    const filter = raw[p++];
    const line = Buffer.from(raw.subarray(p, p + stride)); p += stride;
    for (let x = 0; x < stride; x++) {
      const a = x >= ch ? line[x - ch] : 0, b = prev[x], c = x >= ch ? prev[x - ch] : 0;
      let v = line[x];
      if (filter === 1) v += a; else if (filter === 2) v += b;
      else if (filter === 3) v += (a + b) >> 1;
      else if (filter === 4) {
        const pp = a + b - c, pa = Math.abs(pp - a), pb = Math.abs(pp - b), pc = Math.abs(pp - c);
        v += (pa <= pb && pa <= pc) ? a : (pb <= pc ? b : c);
      }
      line[x] = v & 0xff;
    }
    for (let x = 0; x < w; x++) {
      const s = x * ch, d = (y * w + x) * 4;
      out[d] = line[s]; out[d + 1] = line[s + 1]; out[d + 2] = line[s + 2];
      out[d + 3] = ch === 4 ? line[s + 3] : 255;
    }
    prev = line;
  }
  return { w, h, data: out };
}

const args = process.argv.slice(2);
let region = null;
const ri = args.indexOf('--region');
if (ri >= 0) { region = args[ri + 1].split(',').map(Number); args.splice(ri, 2); }
const [base, ...rest] = args;
if (!base || rest.length === 0) { console.error('用法：node tools/pixel-diff-pair.mjs <基准图> <对照图...> [--region x0,y0,x1,y1]'); process.exit(2); }

const A = decodePng(fs.readFileSync(base));
let x0 = 0, y0 = 0, x1 = A.w, y1 = A.h;
if (region) { x0 = Math.round(region[0] * A.w); y0 = Math.round(region[1] * A.h); x1 = Math.round(region[2] * A.w); y1 = Math.round(region[3] * A.h); }
const label = region ? `区域 [${x0},${y0}→${x1},${y1}]` : '整幅';

console.log(`基准：${base}  ${A.w}x${A.h} · 比对 ${label}`);
console.log('对照图'.padEnd(34) + '变化像素%  平均差  最大差  判定');
console.log('-'.repeat(74));
for (const f of rest) {
  const B = decodePng(fs.readFileSync(f));
  if (A.w !== B.w || A.h !== B.h) { console.log(`${f} 尺寸不一致，跳过`); continue; }
  let changed = 0, sum = 0, max = 0, n = 0;
  for (let y = y0; y < y1; y++) for (let x = x0; x < x1; x++) {
    const o = (y * A.w + x) * 4;
    const d0 = Math.abs(A.data[o] - B.data[o]), d1 = Math.abs(A.data[o + 1] - B.data[o + 1]), d2 = Math.abs(A.data[o + 2] - B.data[o + 2]);
    const d = Math.max(d0, d1, d2);
    if (d > 8) changed++;
    sum += (d0 + d1 + d2) / 3; if (d > max) max = d; n++;
  }
  const pct = (changed / n) * 100;
  const verdict = pct >= 1 ? '有变化 ★' : pct >= 0.05 ? '轻微' : '无变化 ✗';
  console.log(f.split(/[\\/]/).pop().padEnd(34) + `${pct.toFixed(3)}%`.padEnd(11) + `${(sum / n).toFixed(3)}`.padEnd(8) + `${max}`.padEnd(8) + verdict);
}
