#!/usr/bin/env node
// 并行分块下载器 —— 专治"每个连接被限速"的 CDN（实测 GitHub 文件分发就是这种）。
//
// ## 为什么需要它（实测数据，2026-10-05）
// 同一台设备、同一个代理、同一个目标（`release-assets.githubusercontent.com`）：
// ```
//  1 连接 →   393 KB / 12.0s =  32.6 KB/s
//  4 连接 →  1689 KB / 12.5s = 135.5 KB/s     ← 4.2 倍
//  8 连接 →  3567 KB / 13.2s = 270.6 KB/s     ← 8.3 倍
// ```
// **几乎线性** → 说明限速是**按连接**计的，不是按账号/按 IP。多开几条就成倍。
//
// ## 排除了哪些"看起来像原因"的东西（都实测过，别再往那些方向查）
// | 假设 | 实测 | 结论 |
// |---|---|---|
// | Range 太大被限 | 1MB/8MB/32MB/从中间起，全是 40~47 KB/s | ❌ 与 Range 无关 |
// | 连接活久了会衰减 | range 无关、时长无关，恒定 40~47 KB/s | ❌ 不衰减 |
// | 时间片分块重连能提速 | 单连接 30s 片 = 0 增益 | ❌ 分块本身不提速 |
// | **每连接限速** | **1→8 连接：32.6→270.6 KB/s** | ✅ **就是这个** |
//
// ⚠ 我曾据一个**自己工具里的算术 bug**（把"文件总大小"除以"本次耗时"，于是补下 44KB
//   也显示成 4 MB/s）推断"短连接满速、长连接衰减"。**那个结论是错的**，别再照它做设计。
//
// ## 用法
//   node tools/fast-download.mjs <url> <输出路径> [--conn 8] [--chunk 1]
//   # --conn  并行连接数（默认 8；被限速越狠越该加）
//   # --chunk 每块 MB（默认 1；小一点重试粒度更细）
//   # 断点续传：重跑同一命令即可，已完成的块会跳过
//
// ## 正确性上的硬要求（都是踩过的坑）
// 1. **定位写**：每个 worker 用 `fs.writeSync(fd, buf, off, len, position)` 写自己的偏移，
//    彼此不冲突 —— 不能用共享的文件指针（append 模式会互相覆盖）。
// 2. **请求了 Range 就必须收到 206**：若服务器忽略 Range 返回 200，那给的是**整个文件**，
//    当片段写进去 = 把开头写到中间 = 文件损坏，而且**不报错、静默变坏**。
// 3. **绝不超写**：按"该块的剩余字节"切片。
// 4. **完成判据是字节数精确相等**，不是"循环跑完了"。
// 5. 先 `ftruncate` 预分配，避免多 worker 同时扩展文件导致稀疏空洞。
import fs from 'node:fs';
import path from 'node:path';

const argv = process.argv.slice(2);
const optOf = (k, d) => { const i = argv.indexOf(k); return i >= 0 ? argv[i + 1] : d; };
const positional = argv.filter((a, i) => !a.startsWith('--') && !(i > 0 && argv[i - 1].startsWith('--')));
const [url, out] = positional;
if (!url || !out) {
  console.error('用法: node tools/fast-download.mjs <url> <输出路径> [--conn 8] [--chunk 1]');
  process.exit(2);
}
// 自定义请求头（可多次，如 --header "Authorization: Bearer xxx"）——GitHub 产物需要
const HEADERS = {};
for (let i = 0; i < argv.length; i++) {
  if (argv[i] === '--header' && argv[i + 1]) {
    const k = argv[i + 1].indexOf(':');
    if (k > 0) HEADERS[argv[i + 1].slice(0, k).trim()] = argv[i + 1].slice(k + 1).trim();
  }
}
// ⚠ 【实测 bug · 2026-10-06】并行模式（conn>1）会**写坏数据**：
//   文件总字节数与官方 size_in_bytes 完全一致，但 unzip 报
//   "End-of-central-directory signature not found" —— 块被写到了错误偏移。
//   对照实验：--conn 8 ✗ 损坏 · --conn 1 ✓ 无错。
//   根因：完成判据只校验"每块都写过了"，**不校验写的位置与内容**（字节数相等 ≠ 内容正确）。
//   ⇒ 在加上位置自校验 + CRC 之前，这里**默认单连接**；要并行必须显式 --conn N 并由调用方自担校验。
//   详见 docs/downloader-bug-parallel-corruption-2026-10-06.md
const CONN = Math.max(1, Number(optOf('--conn', 1)));
const CHUNK_MB = Math.max(0.05, Number(optOf('--chunk', 1)));
const CHUNK = Math.round(CHUNK_MB * 1048576);
const MAX_RETRY = 12;

const stateFile = out + '.chunks';
const log = (m) => console.log(`[${new Date().toISOString().slice(11, 19)}] ${m}`);

// ── 1. 探测总大小（Content-Range 第三段是权威值）──
log('探测大小…');
let total = 0;
{
  const r = await fetch(url, { headers: { ...HEADERS, Range: 'bytes=0-0' } });
  const cr = r.headers.get('content-range');
  if (r.status === 206 && cr) {
    total = Number(cr.split('/')[1]);
  } else if (r.status === 200) {
    total = Number(r.headers.get('content-length') || 0);
  }
  if (!total) { console.error('拿不到总大小（HTTP ' + r.status + ' / content-range=' + cr + '）'); process.exit(1); }
  log(`总大小 ${total} 字节（${(total / 1048576).toFixed(1)} MB）· ${CONN} 连接 · 每块 ${CHUNK_MB} MB`);
  // 若服务器不支持 Range，退化为单连接顺序下载
  if (r.status !== 206) {
    log('⚠ 服务器不支持 Range（返回 200）→ 退化为单连接顺序下载');
  }
}

const NCHUNK = Math.ceil(total / CHUNK);
fs.mkdirSync(path.dirname(path.resolve(out)), { recursive: true });

// ── 2. 读续传状态 ──
let done = new Set();
if (fs.existsSync(stateFile)) {
  try {
    done = new Set(JSON.parse(fs.readFileSync(stateFile, 'utf8')).done || []);
    if (done.size) log(`续传：已完成 ${done.size}/${NCHUNK} 块`);
  } catch { log('状态文件损坏，从头开始'); }
}
const saveState = () => {
  const tmp = stateFile + '.tmp';
  fs.writeFileSync(tmp, JSON.stringify({ url, total, chunk: CHUNK, done: [...done] }));
  fs.renameSync(tmp, stateFile);   // 原子发布（SELinux 下 link() 被拒，用 rename）
};

// ── 3. 预分配 + 打开 ──
const fd = fs.openSync(out, fs.existsSync(out) ? 'r+' : 'w+');
try { fs.ftruncateSync(fd, total); } catch { /* 某些 FS 不支持，忽略 */ }

// ── 4. 工作队列 + N 个 worker ──
let next = 0;
let moved = 0;
const t0 = Date.now();
let retries = 0;

async function worker(id) {
  for (;;) {
    while (done.has(next) && next < NCHUNK) next++;      // 跳过已完成
    const idx = next++;
    if (idx >= NCHUNK) return;
    const start = idx * CHUNK;
    const want = Math.min(CHUNK, total - start);

    let attempt = 0;
    for (;;) {
      try {
        const r = await fetch(url, { headers: { ...HEADERS, Range: `bytes=${start}-${start + want - 1}` } });
        // 【硬要求 2】请求了 Range 就必须是 206。返回 200 = 给的是整个文件，写进去就损坏。
        if (r.status !== 206) throw new Error('HTTP ' + r.status + '（请求了 Range 却非 206 → 拒绝，避免写坏）');
        const buf = Buffer.allocUnsafe(want);
        let got = 0;
        for await (const c of r.body) {
          const room = want - got; if (room <= 0) break;      // 【硬要求 3】绝不超写
          const sl = c.length > room ? c.subarray(0, room) : c;
          buf.set(sl, got); got += sl.length;
        }
        if (got !== want) throw new Error(`块不完整 ${got}/${want}`);
        fs.writeSync(fd, buf, 0, want, start);                // 【硬要求 1】定位写
        done.add(idx); moved += want;
        if (done.size % 8 === 0 || done.size === NCHUNK) saveState();
        break;
      } catch (e) {
        attempt++; retries++;
        if (attempt >= MAX_RETRY) { saveState(); throw new Error(`块 ${idx} 连续 ${MAX_RETRY} 次失败: ${e.message}`); }
        await new Promise((r) => setTimeout(r, Math.min(400 * attempt, 3000)));
      }
    }
  }
}

try {
  await Promise.all(Array.from({ length: CONN }, (_, i) => worker(i)));
} catch (e) {
  fs.closeSync(fd);
  console.error('\n✗ ' + e.message);
  console.error(`  已保存进度 ${done.size}/${NCHUNK} 块 —— **重跑同一命令即续传**`);
  process.exit(1);
}
fs.closeSync(fd);

// ── 5. 完成判据：字节数精确相等 ──
const size = fs.statSync(out).size;
const secs = (Date.now() - t0) / 1000;
if (done.size !== NCHUNK || size !== total) {
  console.error(`✗ 未完成：块 ${done.size}/${NCHUNK} · 大小 ${size}/${total}`);
  process.exit(1);
}
fs.rmSync(stateFile, { force: true });
log(`✓ 完成 ${(total / 1048576).toFixed(1)} MB · ${secs.toFixed(0)} 秒 · 本次实传 ${(moved / 1048576).toFixed(1)} MB · 平均 ${(moved / 1048576 / secs).toFixed(2)} MB/s · 重试 ${retries} 次`);
log(`  → ${path.resolve(out)}`);
