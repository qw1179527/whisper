#!/usr/bin/env node
/**
 * kit-view-diff.mjs — 对套件取证图做 A/B 像素差（"套件真的进了渲染"的量化判据）
 *
 * ## 为什么需要它
 * 一张图只能说明"画面里有东西"；只有**A（有套件）与 B（无套件，走产品兜底路径）的像素差**
 * 才能证明"套件确实参与了渲染"。目视能看出差别，但目视不可复核、也不能进 CI。
 * 本脚本把差别变成三个数：变化像素占比、平均绝对差、最大通道差。
 *
 * ## 自足实现（不引依赖）
 * 只用 Node 内置 zlib 解 PNG（IHDR + IDAT + 反滤波），支持 8bit RGB/RGBA（本项目产物即是）。
 *
 * 用法：node tools/kit-view-diff.mjs <目录>
 */
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';

/** 解一张 8bit PNG → {w,h,channels,data(RGBA 展开)} */
function decodePng(buf) {
  if (buf.readUInt32BE(0) !== 0x89504e47) throw new Error('不是 PNG');
  let off = 8, w = 0, h = 0, bitDepth = 0, colorType = 0;
  const idat = [];
  while (off < buf.length) {
    const len = buf.readUInt32BE(off);
    const type = buf.toString('ascii', off + 4, off + 8);
    const data = buf.subarray(off + 8, off + 8 + len);
    if (type === 'IHDR') {
      w = data.readUInt32BE(0); h = data.readUInt32BE(4);
      bitDepth = data[8]; colorType = data[9];
    } else if (type === 'IDAT') idat.push(data);
    else if (type === 'IEND') break;
    off += 12 + len;
  }
  if (bitDepth !== 8) throw new Error('只支持 8bit PNG');
  const ch = colorType === 6 ? 4 : colorType === 2 ? 3 : 0;
  if (!ch) throw new Error(`不支持的颜色类型 ${colorType}`);

  const raw = zlib.inflateSync(Buffer.concat(idat));
  const stride = w * ch;
  const out = Buffer.alloc(w * h * 4);
  let prev = Buffer.alloc(stride);
  let p = 0;
  for (let y = 0; y < h; y++) {
    const filter = raw[p++];
    const line = Buffer.from(raw.subarray(p, p + stride));
    p += stride;
    for (let x = 0; x < stride; x++) {
      const a = x >= ch ? line[x - ch] : 0;
      const b = prev[x];
      const c = x >= ch ? prev[x - ch] : 0;
      let v = line[x];
      if (filter === 1) v += a;
      else if (filter === 2) v += b;
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

const dir = process.argv[2] || '.';
const files = fs.readdirSync(dir).filter((f) => f.endsWith('.png'));
// 配对：<room>_<kit>_<view>_A-with[_lit].png ↔ ..._B-without[_lit].png
const pairs = [];
for (const f of files) {
  if (!f.includes('_A-with')) continue;
  const b = f.replace('_A-with', '_B-without');
  if (files.includes(b)) pairs.push([f, b]);
}
pairs.sort();

console.log(`目录 ${dir}`);
console.log(`配对 ${pairs.length} 组（A=有套件 / B=无套件，唯一变量是套件文件在不在）\n`);
console.log('视图'.padEnd(46) + '变化像素%  平均差  最大差  判定');
console.log('-'.repeat(86));

const rows = [];
for (const [fa, fb] of pairs) {
  const A = decodePng(fs.readFileSync(path.join(dir, fa)));
  const B = decodePng(fs.readFileSync(path.join(dir, fb)));
  if (A.w !== B.w || A.h !== B.h) { console.log(`${fa} 尺寸不一致，跳过`); continue; }
  let changed = 0, sum = 0, max = 0;
  const n = A.w * A.h;
  for (let i = 0; i < n; i++) {
    const o = i * 4;
    const d0 = Math.abs(A.data[o] - B.data[o]);
    const d1 = Math.abs(A.data[o + 1] - B.data[o + 1]);
    const d2 = Math.abs(A.data[o + 2] - B.data[o + 2]);
    const d = Math.max(d0, d1, d2);
    if (d > 8) changed++;
    sum += (d0 + d1 + d2) / 3;
    if (d > max) max = d;
  }
  const pct = (changed / n) * 100;
  const mean = sum / n;
  const verdict = pct >= 5 ? '可分辨 ★' : pct >= 0.5 ? '轻微' : '无可分辨差异 ✗';
  const label = fa.replace('_A-with', '').replace('.png', '');
  console.log(label.padEnd(46) + `${pct.toFixed(1)}%`.padEnd(11) + `${mean.toFixed(2)}`.padEnd(8) + `${max}`.padEnd(8) + verdict);
  rows.push({ label, pct, mean, max, verdict });
}

const ok = rows.filter((r) => r.pct >= 5);
console.log('-'.repeat(86));
console.log(`可分辨（变化像素 ≥5%）${ok.length}/${rows.length} 组`);
fs.writeFileSync(path.join(dir, 'ab-diff.json'), JSON.stringify({ pairs: rows, distinguishable: ok.length, total: rows.length }, null, 2));
