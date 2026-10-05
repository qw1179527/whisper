#!/usr/bin/env node
/**
 * pixel-region-audit.mjs — 分区像素审计（专治 pixel-metrics 的"整帧假绿"）
 *
 * ## 为什么需要它
 * `pixel-metrics.mjs` 是**整帧**统计：当 3D 视口是空的、上层却有 HUD 文字与按钮时，
 * "颜色种数 / 边缘密度"会被 UI 撑起来 → 整帧判"有场景内容"是**假绿**（实测发生过）。
 * 本工具把一帧切成若干语义矩形（纯 3D 视口 / HUD 文字块 / 右侧按钮条 / 底部导航栏），
 * **逐像素**统计，从而回答真正要问的问题：**3D 视口里到底有什么？**
 *
 * ## ⚠️ 判据边界（被实测打脸后的修正，务必按这个读）
 *   "平坦" ≠ "什么都没渲染"。未打光（UnlitColor）的大平面本身就是"少颜色 + 低边缘"：
 *   一块占满视口的墙，与"真的空白"，在这两个指标上几乎无法区分。
 *   因此本工具给三档，而不是二元判定：
 *     · `empty`  视口内**只有 1~2 种颜色且边缘 ≈ 0** → 相机对着空处（真正的空白）
 *     · `flat`   3~8 种颜色、边缘 < 0.2% → **有几何但都是大块平色面**（画面"平"，不是"空"）
 *     · `present`颜色 > 8 或 边缘 ≥ 0.2% → 有明显结构与边界
 *   要区分 `empty` 与 `flat`，除了本工具，还应**逐列/逐行扫描颜色数**：
 *   空白的行/列恒为 1 色；有平面的行/列会在边界处跳到 2~3 色。
 *
 * ## 实测基准（2026-10-04 · 真机 RMX5062 · 2800×1280 横屏 · asylum_v1）
 *   帧                     视口精确颜色  视口边缘   逐列颜色数        判定
 *   feel/retry (yaw=0°)    1 种          0.000%     1,1,1,1,1,1      empty（相机对着空处）
 *   frames-asylum (yaw=206°) 4 种        0.059%     1,1,1,1,2,3      flat（左侧空、右侧三块平面）
 *   HUD 文字块             235 种        5.31%      —                UI，不是 3D
 *   ⇒ 结论：`yaw=0° 时视口是纯色一片（看不到任何东西）`；yaw=206° 时有**平面着色的大块多边形**
 *     （未打光、无纹理、无道具/怪物），但**不是"完全没渲染几何"**。
 *
 * ## 用法
 *   node tools/pixel-region-audit.mjs <png...|目录>
 *
 * ## 退出码
 *   0 = 至少一帧判为 flat 或 present（视口里有东西）
 *   1 = 所有帧都判为 empty（视口内 1~2 色、边缘≈0 → 相机对着空处）
 *   2 = 无法判定（格式不支持 / 没有输入）
 *
 * ## 区域定义（横屏 2800×1280）
 *   3D 视口 = x ∈ [0, 0.84W) × y ∈ [0.44H, 0.80H)   ← 排除 HUD 文字(左上)、右侧按钮条、底部导航栏
 *   比例按当前 UI 布局标定；换分辨率/布局需重新标定（工具会打印实际像素范围）。
 */

import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';

const V = { x1: 0.84, y0: 0.44, y1: 0.80 };

function unfilter(buf) {
  if (buf.readUInt32BE(0) !== 0x89504e47) throw { kind: 'format', msg: '不是 PNG' };
  let pos = 8, w = 0, h = 0, bd = 0, ct = 0; const idat = [];
  while (pos + 8 <= buf.length) {
    const len = buf.readUInt32BE(pos), type = buf.toString('ascii', pos + 4, pos + 8), d = buf.subarray(pos + 8, pos + 8 + len);
    if (type === 'IHDR') { w = d.readUInt32BE(0); h = d.readUInt32BE(4); bd = d[8]; ct = d[9]; }
    else if (type === 'IDAT') idat.push(Buffer.from(d));
    else if (type === 'IEND') break;
    pos += 12 + len;
  }
  if (bd !== 8 || (ct !== 2 && ct !== 6)) throw { kind: 'format', msg: `bd=${bd} ct=${ct} 无法分析` };
  const raw = zlib.inflateSync(Buffer.concat(idat));
  const bpp = ct === 6 ? 4 : 3, stride = w * bpp, out = Buffer.alloc(h * stride), prev = Buffer.alloc(stride);
  let p = 0;
  const paeth = (a, b, c) => { const q = a + b - c, pa = Math.abs(q - a), pb = Math.abs(q - b), pc = Math.abs(q - c); return (pa <= pb && pa <= pc) ? a : (pb <= pc ? b : c); };
  for (let y = 0; y < h; y++) {
    const f = raw[p++], cur = out.subarray(y * stride, (y + 1) * stride);
    raw.copy(cur, 0, p, p + stride); p += stride;
    if (f !== 0) for (let x = 0; x < stride; x++) {
      const A = x >= bpp ? cur[x - bpp] : 0, B = prev[x], C = x >= bpp ? prev[x - bpp] : 0;
      if (f === 1) cur[x] = (cur[x] + A) & 255;
      else if (f === 2) cur[x] = (cur[x] + B) & 255;
      else if (f === 3) cur[x] = (cur[x] + ((A + B) >> 1)) & 255;
      else if (f === 4) cur[x] = (cur[x] + paeth(A, B, C)) & 255;
    }
    cur.copy(prev);
  }
  return { w, h, bpp, stride, buffer: out };
}

function stats(img, x0, x1, y0, y1) {
  const { bpp, stride, buffer } = img;
  const m = new Map(); let s = 0, s2 = 0, n = 0, e = 0, pairs = 0, prevR = null;
  for (let y = y0; y < y1; y++) {
    const base = y * stride, row = [];
    for (let x = x0; x < x1; x++) {
      const i = base + x * bpp, k = (buffer[i] << 16) | (buffer[i + 1] << 8) | buffer[i + 2];
      m.set(k, (m.get(k) || 0) + 1);
      const lum = (buffer[i] * 299 + buffer[i + 1] * 587 + buffer[i + 2] * 114) / 1000;
      row.push(lum); s += lum; s2 += lum * lum; n++;
    }
    for (let k = 1; k < row.length; k++) { pairs++; if (Math.abs(row[k] - row[k - 1]) > 24) e++; }
    if (prevR) for (let k = 0; k < row.length; k++) { pairs++; if (Math.abs(row[k] - prevR[k]) > 24) e++; }
    prevR = row;
  }
  const lum = s / n, std = Math.sqrt(Math.max(0, s2 / n - lum * lum));
  const sorted = [...m.entries()].sort((a, b) => b[1] - a[1]);
  return { px: n, colors: m.size, lum, std, edge: pairs ? e / pairs : 0, top: sorted.slice(0, 3), sorted, stride, bpp, buffer };
}

const inputs = process.argv.slice(2);
if (inputs.length === 0) { console.error('用法: node tools/pixel-region-audit.mjs <png...|目录>'); process.exit(2); }
const files = [];
for (const p of inputs) {
  if (!fs.existsSync(p)) { console.error(`✗ 路径不存在：${p}`); process.exit(2); }
  if (fs.statSync(p).isDirectory()) fs.readdirSync(p).filter((f) => f.toLowerCase().endsWith('.png')).sort().forEach((f) => files.push(path.join(p, f)));
  else files.push(p);
}
if (files.length === 0) { console.error('✗ 没有 PNG'); process.exit(2); }

console.log(`\n[pixel-region-audit] ${files.length} 帧 · 逐像素（不采样）`);
console.log(`  3D 视口定义：x ∈ [0, ${(V.x1 * 100).toFixed(0)}%W) × y ∈ [${(V.y0 * 100).toFixed(0)}%H, ${(V.y1 * 100).toFixed(0)}%H)`);
console.log('  三档判据：empty=1~2 色且边缘≈0（相机对空处）· flat=3~8 色且边缘<0.2%（大块平色面）· present=更多结构');

let geomFrames = 0, emptyFrames = 0, unknown = 0;
const results = [];
for (const f of files) {
  const name = path.basename(f);
  try {
    const img = unfilter(fs.readFileSync(f));
    const x0 = 0, x1 = Math.floor(img.w * V.x1), y0 = Math.floor(img.h * V.y0), y1 = Math.floor(img.h * V.y1);
    const s = stats(img, x0, x1, y0, y1);
    // 逐列颜色数（区分"一片空白"与"有平面块"的关键补充证据）
    const colCounts = [];
    for (let x = x0 + Math.floor((x1 - x0) * 0.04); x < x1; x += Math.max(1, Math.floor((x1 - x0) / 6))) {
      const set = new Set();
      for (let y = y0; y < y1; y += 3) { const i = y * s.stride + x * s.bpp; set.add((s.buffer[i] << 16) | (s.buffer[i + 1] << 8) | s.buffer[i + 2]); }
      colCounts.push(set.size);
    }
    const empty = s.colors <= 2 && s.edge < 0.001;
    const present = s.colors > 8 || s.edge >= 0.002;
    const kind = empty ? 'empty' : (present ? 'present' : 'flat');
    if (empty) emptyFrames++; else geomFrames++;
    const label = kind === 'empty' ? '✗ empty（相机对着空处）' : (kind === 'flat' ? '△ flat（有几何但都是大块平色面）' : '✓ present（有可见结构）');
    const tops = s.top.map(([k, c]) => `RGB(${(k >> 16) & 255},${(k >> 8) & 255},${k & 255})×${((c / s.px) * 100).toFixed(1)}%`).join(' + ');
    console.log(`  ${label.padEnd(34)} ${name}  视口 ${s.px}px · 精确颜色 ${s.colors} 种 · 亮度 ${s.lum.toFixed(1)} · 标准差 ${s.std.toFixed(1)} · 边缘 ${(s.edge * 100).toFixed(3)}% · 逐列颜色数 [${colCounts.join(',')}] · 主色 ${tops}`);
    results.push({ file: name, viewportPx: s.px, exactColors: s.colors, lum: +s.lum.toFixed(2), std: +s.std.toFixed(2), edgePct: +(s.edge * 100).toFixed(3), colCounts, kind });
  } catch (e) {
    unknown++;
    console.log(`  ✗ 无法判定（${e.kind === 'format' ? e.msg : e.message}） ${name}`);
  }
}

console.log('');
if (unknown > 0) { console.log(`结论：${unknown} 帧无法判定 → 判失败（退出码 2）`); process.exit(2); }
if (geomFrames === 0) { console.log(`结论：${emptyFrames}/${files.length} 帧视口判 empty —— 相机对着空处，**看不到任何东西**（退出码 1）`); process.exit(1); }
const flatN = results.filter((r) => r.kind === 'flat').length;
const presN = results.filter((r) => r.kind === 'present').length;
console.log(`结论：${geomFrames}/${files.length} 帧视口里有东西（present ${presN} · flat ${flatN}）—— 注意 flat 表示"大块平色面"，不是"完全没渲染"（退出码 0）`);
process.exit(0);
