#!/usr/bin/env node
/**
 * pixel-metrics.mjs — 真机截屏像素判据（多帧 + 帧间差异 + 基线并列 + 原图留证）
 *
 * ## 为什么有这个工具
 * "氛围不够暗""看不出墙体/地板/门洞""视角转不动"这类反馈，如果只靠嘴说，就永远无法回归。
 * 本工具把"好不好看/有没有真的变"拆成可测量指标，并让**判据参与退出码**：
 * 打印而不影响退出码 = 判据没有参与 = 假绿（本项目历史上就是这么放行黑屏的）。
 *
 * ## 与手机端口径完全一致（不可随意改，否则与历史基线不可比）
 *   lum      = (r*299 + g*587 + b*114) / 1000
 *   distinct = 颜色种类（每通道取高 4 bit 量化）
 *   stddev   = 亮度标准差（按 STEP 采样）
 *   edgeRatio= 相邻采样点亮度差 > 24 的比例（横向 + 纵向）
 *   采样步长 STEP = 4
 *   判红规则：洋红为主 / colors ≤ 2 或 edgeRatio < 0.5% / stddev < 8
 *   帧间差异 = 44×20 降采样亮度网格的平均绝对差（0~255 尺度）
 *
 * ## 历史基线（用于并列对比）
 *   首包黑屏（纯色）  ： 亮度 31.3 · 标准差 ~0   · 颜色 1 种 · 边缘 0%
 *   0.1.20（能看见） ： 亮度 213  · 标准差 28.6 · 颜色 25 种 · 边缘 1.8%
 *
 * ## 用法
 *   node tools/pixel-metrics.mjs <png...|目录> [--json <out.json>] [--label 备注]
 *
 * ## 判定退出码
 *   0 = 每帧都判定为"有场景内容"（无洋红/无纯色/无均匀）
 *   1 = 判据失败（纯色 / 均匀 / 洋红 / 无有效帧）
 *   2 = 无法判定（PNG 位深或颜色类型不支持，或有帧解不开 —— 按未通过处理，不当作通过）
 *
 * ## ⚠️ 已知假绿边界（实测，务必知情）
 *   本工具的指标是**整帧**统计。如果画面里 3D 视口是空的、但上层有 HUD 文字与按钮，
 *   那么"颜色种数 / 边缘密度"会被 UI 撑起来，于是**整帧判"有场景内容"是假绿**。
 *   实测证据（2026-10-04，真机 2800×1280 横屏）：
 *     · 整帧：颜色 488 种 · 边缘 1.668% → 本工具判 ✓
 *     · 纯 3D 视口（x<2340, y560..1024，108 万像素）：**只有 4 种颜色 · 边缘 0.059%**，
 *       主色 RGB(97,93,84) ×76.8% + 次色 ×15.8% —— 两个平坦色块，完全没有几何
 *     · HUD 文字块自身：235 种颜色 · 边缘 5.31% ← 假绿来源
 *   ⇒ **要判定"3D 里到底有没有东西"，必须把统计范围限制在纯 3D 视口**
 *     （排除 HUD 文字区、右侧按钮条、底部导航栏）。本工具的 `dominantPct`（主色占比）
 *     只是弱判据：UI 会把单色占比稀释到 20%~25%，所以 ≥92% 的红线抓不到这种画面。
 *     真正可靠的证据是区域统计 —— 见 `_evidence/falsegreen-audit.mjs`。
 *
 * ## 实现注意（踩过的坑）
 *   解 PNG 时 `cur.copy(prev)` 之后 `cur` 已不是当前行 —— 任何"再遍历一次像素"的逻辑
 *   都必须在**解滤镜阶段**顺手算，或对独立的 unfiltered buffer 重算；否则会得到 NaN 指标。
 */

import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';

const STEP = 4;
const EDGE_DELTA = 24;
const GRID_W = 44, GRID_H = 20;
const BASELINE = {
  blackscreen: { label: '首包黑屏（纯色）', lum: 31.3, std: 0.0, colors: 1, edge: 0.0 },
  v0120: { label: '0.1.20（能看见场景）', lum: 213.0, std: 28.6, colors: 25, edge: 1.8 },
};

// ── 参数解析 ────────────────────────────────────────────────────────────
const argv = process.argv.slice(2);
const inputs = [];
let jsonOut = null, label = '';
for (let i = 0; i < argv.length; i++) {
  const a = argv[i];
  if (a === '--json') jsonOut = argv[++i];
  else if (a === '--label') label = argv[++i];
  else if (a === '--help' || a === '-h') { console.log('用法: node tools/pixel-metrics.mjs <png...|目录> [--json out.json] [--label 备注]'); process.exit(0); }
  else inputs.push(a);
}
if (inputs.length === 0) { console.error('✗ 没有输入：给 PNG 文件或包含 PNG 的目录'); process.exit(2); }

const files = [];
for (const p of inputs) {
  if (!fs.existsSync(p)) { console.error(`✗ 路径不存在：${p}`); process.exit(2); }
  if (fs.statSync(p).isDirectory()) {
    fs.readdirSync(p).filter((f) => f.toLowerCase().endsWith('.png')).sort().forEach((f) => files.push(path.join(p, f)));
  } else files.push(p);
}
if (files.length === 0) { console.error('✗ 目录里没有 PNG'); process.exit(2); }

// ── PNG 解码（bd=8 且 ct∈{2,6}；其余按"无法判定"退出 2，不当作通过）────
// 返回 { w, h, bpp, stride, buffer }，buffer 为**解滤镜后**的像素（行优先，无 filter 字节）
function unfilterPng(buf) {
  if (buf.readUInt32BE(0) !== 0x89504e47) throw { kind: 'format', msg: '不是 PNG（magic 不对）' };
  let pos = 8, w = 0, h = 0, bd = 0, ct = 0;
  const idat = [];
  while (pos + 8 <= buf.length) {
    const len = buf.readUInt32BE(pos);
    const type = buf.toString('ascii', pos + 4, pos + 8);
    const data = buf.subarray(pos + 8, pos + 8 + len);
    if (type === 'IHDR') { w = data.readUInt32BE(0); h = data.readUInt32BE(4); bd = data[8]; ct = data[9]; }
    else if (type === 'IDAT') idat.push(Buffer.from(data));
    else if (type === 'IEND') break;
    pos += 12 + len;
  }
  if (bd !== 8 || (ct !== 2 && ct !== 6)) throw { kind: 'format', msg: `PNG 格式 bd=${bd} ct=${ct} 无法分析` };
  const raw = zlib.inflateSync(Buffer.concat(idat));
  const bpp = ct === 6 ? 4 : 3, stride = w * bpp;
  const out = Buffer.alloc(h * stride);
  const prev = Buffer.alloc(stride);
  let p = 0;
  const paeth = (a, b, c) => { const q = a + b - c, pa = Math.abs(q - a), pb = Math.abs(q - b), pc = Math.abs(q - c); return (pa <= pb && pa <= pc) ? a : (pb <= pc ? b : c); };
  for (let y = 0; y < h; y++) {
    const f = raw[p++];
    const cur = out.subarray(y * stride, (y + 1) * stride);
    raw.copy(cur, 0, p, p + stride); p += stride;
    if (f !== 0) {
      for (let x = 0; x < stride; x++) {
        const A = x >= bpp ? cur[x - bpp] : 0, B = prev[x], C = x >= bpp ? prev[x - bpp] : 0;
        if (f === 1) cur[x] = (cur[x] + A) & 255;
        else if (f === 2) cur[x] = (cur[x] + B) & 255;
        else if (f === 3) cur[x] = (cur[x] + ((A + B) >> 1)) & 255;
        else if (f === 4) cur[x] = (cur[x] + paeth(A, B, C)) & 255;
      }
    }
    cur.copy(prev);
  }
  return { w, h, bpp, stride, buffer: out };
}

function metricsOf(img) {
  const { w, h, bpp, stride, buffer } = img;
  const colors = new Map();
  let sum = 0, sum2 = 0, n = 0, edges = 0, pairs = 0, prevRow = null;
  for (let y = 0; y < h; y++) {
    const base = y * stride;
    const row = [];
    for (let x = 0; x < w; x += STEP) {
      const i = base + x * bpp, r = buffer[i], g = buffer[i + 1], b = buffer[i + 2];
      const lum = (r * 299 + g * 587 + b * 114) / 1000;
      row.push(lum);
      colors.set(((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4), 1);
      sum += lum; sum2 += lum * lum; n++;
    }
    for (let k = 1; k < row.length; k++) { pairs++; if (Math.abs(row[k] - row[k - 1]) > EDGE_DELTA) edges++; }
    if (prevRow) for (let k = 0; k < row.length; k++) { pairs++; if (Math.abs(row[k] - prevRow[k]) > EDGE_DELTA) edges++; }
    prevRow = row;
  }
  const lum = n ? sum / n : 0;
  const std = n ? Math.sqrt(Math.max(0, sum2 / n - lum * lum)) : 0;
  const edge = pairs ? edges / pairs : 0;
  let magenta = 0;
  for (const k of colors.keys()) { const r = (k >> 8) & 15, g = (k >> 4) & 15, b = k & 15; if (r >= 13 && g <= 3 && b >= 13) magenta++; }
  // 单色占比：逐像素精确颜色统计（用 STEP 采样即可，足够判"是否一个颜色压满全屏"）
  const exact = new Map();
  for (let y = 0; y < h; y += STEP) {
    const base = y * stride;
    for (let x = 0; x < w; x += STEP) {
      const i = base + x * bpp, k = (buffer[i] << 16) | (buffer[i + 1] << 8) | buffer[i + 2];
      exact.set(k, (exact.get(k) || 0) + 1);
    }
  }
  let domCount = 0, domKey = 0;
  for (const [k, c] of exact) if (c > domCount) { domCount = c; domKey = k; }
  return {
    w, h, lum, std, colors: colors.size, edge, magentaRatio: colors.size ? magenta / colors.size : 0,
    dominantPct: domCount / n, dominantColor: `RGB(${(domKey >> 16) & 255},${(domKey >> 8) & 255},${domKey & 255})`,
  };
}

// 44×20 降采样亮度网格（用于帧间差异；也是可事后复核的轻量指纹）
function gridOf(img) {
  const { w, h, bpp, stride, buffer } = img;
  const grid = [];
  for (let gy = 0; gy < GRID_H; gy++) {
    const y0 = Math.floor((gy * h) / GRID_H), y1 = Math.floor(((gy + 1) * h) / GRID_H);
    for (let gx = 0; gx < GRID_W; gx++) {
      const x0 = Math.floor((gx * w) / GRID_W), x1 = Math.floor(((gx + 1) * w) / GRID_W);
      let s = 0, c = 0;
      for (let y = y0; y < y1; y += STEP) {
        const base = y * stride;
        for (let x = x0; x < x1; x += STEP) {
          const i = base + x * bpp;
          s += (buffer[i] * 299 + buffer[i + 1] * 587 + buffer[i + 2] * 114) / 1000; c++;
        }
      }
      grid.push(c ? s / c : 0);
    }
  }
  return grid;
}

function verdictOf(m) {
  if (m.magentaRatio > 0.5) return 'magenta';
  // 【假绿修复】只判 colors/edge 会被"HUD 文字盖在空场景上"骗过：
  // 实测某帧整幅判 green，但纯 3D 区（x<2340, y560..1024，108 万像素）只有 **4 种颜色、边缘 0.059%**，
  // 主色 RGB(97,93,84) ×76.8% + 次色 ×15.8% —— 两个平坦色块，一点几何都没有；
  // 236 种颜色与 3.36% 边缘全部来自左上角 HUD 文字与右侧按钮。
  // 因此增加"单色占比"判据：一个颜色压到绝大多数 → 就是空场景。
  if (m.dominantPct >= 0.92) return 'flat';
  if (m.colors <= 2 || m.edge < 0.005) return 'flat';
  if (m.std < 8) return 'uniform';
  return 'ok';
}

console.log(`\n[pixel-metrics] 帧数 ${files.length}${label ? ' · ' + label : ''}`);
console.log('  基线对照：' +
  `纯色黑屏 亮度 ${BASELINE.blackscreen.lum} / 标准差 ${BASELINE.blackscreen.std} / 颜色 ${BASELINE.blackscreen.colors} / 边缘 ${BASELINE.blackscreen.edge}%` +
  ` ｜ 0.1.20 亮度 ${BASELINE.v0120.lum} / 标准差 ${BASELINE.v0120.std} / 颜色 ${BASELINE.v0120.colors} / 边缘 ${BASELINE.v0120.edge}%`);
console.log('  判定规则：洋红为主→shader 失败；**主色占比≥92%→纯色块（空场景）**；颜色≤2 或 边缘<0.5%→纯色块；标准差<8→只有背景色');

const rows = [];
let okFrames = 0, bad = 0, unknown = 0;
const grids = [];
for (const f of files) {
  const name = path.basename(f);
  try {
    const img = unfilterPng(fs.readFileSync(f));
    const m = metricsOf(img);
    const v = verdictOf(m);
    if (v === 'ok') okFrames++; else bad++;
    const tag = v === 'ok' ? '✓ 有场景内容' : (v === 'magenta' ? '✗ 洋红为主（shader 失败）' : (v === 'flat' ? '✗ 纯色块（几何没渲染出来）' : '✗ 亮度几乎均匀（只画了背景色）'));
    console.log(`  ${tag.padEnd(30)} ${name}  ${m.w}x${m.h} · 亮度 ${m.lum.toFixed(1)} · 标准差 ${m.std.toFixed(1)} · 颜色 ${m.colors} 种 · 边缘 ${(m.edge * 100).toFixed(1)}% · 主色 ${m.dominantColor} ×${(m.dominantPct * 100).toFixed(1)}%`);
    rows.push({ file: name, w: m.w, h: m.h, lum: +m.lum.toFixed(2), std: +m.std.toFixed(2), colors: m.colors, edgePct: +(m.edge * 100).toFixed(2), dominantPct: +(m.dominantPct * 100).toFixed(2), dominantColor: m.dominantColor, verdict: v });
    grids.push({ name, grid: gridOf(img) });
  } catch (e) {
    unknown++;
    console.log(`  ✗ 无法判定（${e.kind === 'format' ? e.msg : e.message}） ${name} —— 按未通过处理，不当成通过`);
    rows.push({ file: name, verdict: 'unknown', reason: e.kind === 'format' ? e.msg : e.message });
    grids.push(null);
  }
}

const diffs = [];
for (let i = 1; i < grids.length; i++) {
  const a = grids[i - 1], b = grids[i];
  if (!a || !b) continue;
  let s = 0;
  for (let k = 0; k < a.grid.length; k++) s += Math.abs(a.grid[k] - b.grid[k]);
  const mad = s / a.grid.length;
  diffs.push({ from: a.name, to: b.name, mad: +mad.toFixed(2), madPct: +((mad / 255) * 100).toFixed(2) });
}
if (diffs.length) {
  console.log('\n  帧间差异（44×20 降采样亮度网格的平均绝对差；视角转动/位移会让它明显 > 1）：');
  for (const d of diffs) {
    const v = d.mad < 1 ? '几乎相同（画面没变）' : (d.mad < 6 ? '有轻微变化（HUD/抖动级别）' : '有明显变化（视角或位置真的变了）');
    console.log(`    ${d.from} → ${d.to} : ${d.mad} / 255 (${d.madPct}%) — ${v}`);
  }
}

const summary = { label, frames: files.length, ok: okFrames, bad, unknown, baseline: BASELINE, frameMetrics: rows, frameDiffs: diffs };
if (jsonOut) { fs.writeFileSync(jsonOut, JSON.stringify(summary, null, 2)); console.log(`\n  JSON 报告 → ${jsonOut}`); }

console.log('');
if (unknown > 0) { console.log(`结论：有 ${unknown} 帧无法判定 → 判失败（退出码 2）`); process.exit(2); }
if (okFrames === 0) { console.log(`结论：没有一帧通过像素判据（失败 ${bad}）→ 判失败（退出码 1）`); process.exit(1); }
if (bad > 0) { console.log(`结论：${okFrames} 帧有场景内容，但 ${bad} 帧未通过 → 判失败（退出码 1）`); process.exit(1); }
console.log(`结论：${okFrames}/${files.length} 帧全部有场景内容（退出码 0）`);
process.exit(0);
