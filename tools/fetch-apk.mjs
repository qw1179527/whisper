#!/usr/bin/env node
// 从 CI 拉取最新的 Android APK —— **带分块重连 + 断点续传 + 字节校验**。
//
// ## 为什么需要这个脚本（实测的痛）
// GitHub artifact 下载走 `pipelines.actions.githubusercontent.com`，经代理时**极不稳定**：
//   · #23 那次：24.3 MB 用了 **973 秒**（25 KB/s）—— 光下载就比构建还久
//   · 换到好节点时：同一份 2.5 MB/s → **10 秒**
// 而**一次下载失败就得从头再来**，是这个环节最要命的地方。
//
// ## 分块重连为什么有效（在 Unity 安装包上实测过）
// 长连接的速率会**单调衰减**（实测 12 分钟周期：满速 → 衰减到 0.4 MB/s → 被掐断 → 重连又满速）。
// 所以**主动每 64MB 断开重连一次**，就一直待在满速窗口里：
//   单条长连接：平均 **0.55 MB/s**
//   分块重连：  平均 **2.4~2.7 MB/s**  → **约 4.5 倍**
//
// ## 与"下载 Unity 安装包"那次踩过的坑对应（都在这里防住）
//   ① 绝不超写：写入前按剩余字节**切片**（上次超写 19.5MB 导致 MD5 不符、整个包作废）
//   ② 单实例：不需要（artifact 很小），但**断点续传**必须有（上次靠它扛过 terminated + HTTP 404）
//   ③ 完成判据：**精确等于 artifact 的 size_in_bytes**，不是"下完了"
//
// 用法：
//   GH_TOKEN=xxx node tools/fetch-apk.mjs                 # 拉最新一次成功构建的 APK
//   GH_TOKEN=xxx node tools/fetch-apk.mjs --run 24        # 指定 run 号
//   GH_TOKEN=xxx node tools/fetch-apk.mjs --out /path/x.apk
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const REPO = process.env.GH_REPO || 'qw1179527/whisper';
const TOKEN = process.env.GH_TOKEN || process.env.GH_NEW;
// ── 分块参数：**按文件大小自适应**（第一版参数错了，实测卡死）────────────────
// 分块重连是为**大文件**设计的：它对付的是"长连接速率单调衰减、约 12 分钟一个周期"
// （4.2GB 的 Unity 安装包上实测：单连接均速 0.55 MB/s → 分块重连 2.4 MB/s，4.5 倍）。
//
// 但 artifact 只有 30MB 量级 —— 用 `16MB/60秒` 切块时，慢节点上**每块还没下完就被超时掐断**，
// 结果永远在原地重连、零进展（实测连续 6 次重连无进展）。衰减周期是分钟级，
// 而 30MB 根本跑不到那么久 —— 小文件**根本不该分块**。
//
// 所以：小于 `SMALL_MB` 的一律**单条长连接**（块=整个文件，超时给足）。
const SMALL_MB = 64;
const CHUNK_MB = Number(process.env.FETCH_CHUNK_MB ?? 16);   // 仅大文件用
const CHUNK_SEC = Number(process.env.FETCH_CHUNK_SEC ?? 60); // 仅大文件用
const BIG_TIMEOUT_SEC = Number(process.env.FETCH_BIG_TIMEOUT_SEC ?? 1800); // 小文件：单连接总预算

if (!TOKEN) { console.error('缺 GH_TOKEN（或 GH_NEW）环境变量'); process.exit(2); }
const H = { Authorization: `Bearer ${TOKEN}`, Accept: 'application/vnd.github+json' };
const log = (m) => console.log(`[${new Date().toISOString().slice(11, 19)}] ${m}`);

// ── 参数 ──
const argv = process.argv.slice(2);
const argOf = (k, d = null) => { const i = argv.indexOf(k); return i >= 0 ? argv[i + 1] : d; };
const wantRun = argOf('--run') ? Number(argOf('--run')) : null;
const outPath = argOf('--out') || '/storage/emulated/0/DSH专用/whisper-latest.apk';

// ── 1. 找 run 与 artifact ──
const runs = await (await fetch(`https://api.github.com/repos/${REPO}/actions/runs?per_page=20`, { headers: H })).json();
if (!runs.workflow_runs) { console.error('取 runs 失败：' + JSON.stringify(runs).slice(0, 200)); process.exit(1); }
const cands = runs.workflow_runs
  .filter((x) => x.name === 'unity-android' && x.conclusion === 'success')
  .filter((x) => (wantRun ? x.run_number === wantRun : true));
if (!cands.length) { console.error(wantRun ? `找不到成功的 run #${wantRun}` : '找不到任何成功的 unity-android 构建'); process.exit(1); }
const run = cands[0];
log(`目标构建：#${run.run_number} · ${run.head_sha.slice(0, 7)} · ${run.updated_at}`);

const arts = await (await fetch(run.artifacts_url, { headers: H })).json();
const art = (arts.artifacts || []).find((a) => /apk/i.test(a.name));
if (!art) { console.error('该构建没有 APK 产物'); process.exit(1); }
log(`产物：${art.name} · ${(art.size_in_bytes / 1048576).toFixed(1)} MB · expired=${art.expired}`);
if (art.expired) { console.error('产物已过期（GitHub 保留期到了），请重新触发构建'); process.exit(1); }

// ── 2. 分块重连下载（可续传）──
const tmp = path.join(ROOT, 'tmp', `artifact-${run.run_number}.zip`);
fs.mkdirSync(path.dirname(tmp), { recursive: true });
const TOTAL = art.size_in_bytes;
const zipUrl = `https://api.github.com/repos/${REPO}/actions/artifacts/${art.id}/zip`;
const CHUNK = CHUNK_MB * 1024 * 1024;
const t0 = Date.now();
let bad = 0;

while (true) {
  const have = fs.existsSync(tmp) ? fs.statSync(tmp).size : 0;
  if (have === TOTAL) break;
  if (have > TOTAL) { log(`✗ 超出官方大小（${have} > ${TOTAL}）→ 删除重下`); fs.rmSync(tmp, { force: true }); continue; }
  // 小文件：一次要完剩下的全部 + 长超时；大文件：按块切 + 每块主动断开
  const small = TOTAL < SMALL_MB * 1048576;
  const want = small ? (TOTAL - have) : Math.min(CHUNK, TOTAL - have);
  const budgetSec = small ? BIG_TIMEOUT_SEC : CHUNK_SEC;
  if (have === 0) log(small
    ? `  文件 ${(TOTAL / 1048576).toFixed(1)} MB < ${SMALL_MB} MB → **单条长连接**（不分块，超时 ${budgetSec}s）`
    : `  文件 ${(TOTAL / 1048576).toFixed(1)} MB ≥ ${SMALL_MB} MB → 分块重连（${CHUNK_MB}MB/${CHUNK_SEC}s）`);
  const t1 = Date.now();
  try {
    const ac = new AbortController();
    const timer = setTimeout(() => ac.abort(), budgetSec * 1000);
    const r = await fetch(zipUrl, { headers: { ...H, Range: `bytes=${have}-${have + want - 1}` }, redirect: 'follow', signal: ac.signal });
    // 【必修，与 dl-art.mjs 同一个 bug】请求了 Range 就必须收到 206：
    //   若跳转后的签名 URL 忽略 Range 而返回 200，它给的是**整个文件**，
    //   当成续传片段写入 = 把开头写到尾部 = 文件损坏（实测在 40MB 的 artifact 上踩过，白跑 1417 秒）。
    if (have > 0 ? r.status !== 206 : !r.ok) {
      clearTimeout(timer);
      throw new Error('HTTP ' + r.status + (have > 0 ? '（请求了 Range 却非 206 → 拒绝，避免写坏）' : ''));
    }
    const fh = fs.openSync(tmp, 'a');
    let got = 0;
    try {
      for await (const c of r.body) {
        const room = TOTAL - (have + got);            // ① 绝不超写
        if (room <= 0) break;
        const slice = c.length > room ? c.subarray(0, room) : c;
        fs.writeSync(fh, slice); got += slice.length;
        if (got >= want || (!small && Date.now() - t1 > CHUNK_SEC * 1000)) break;   // 大文件才主动断开避衰减
      }
    } finally { fs.closeSync(fh); clearTimeout(timer); }
    bad = 0;
    const s = (Date.now() - t1) / 1000;
    log(`  ${((have + got) / 1048576).toFixed(1)}/${(TOTAL / 1048576).toFixed(1)} MB · 本块 ${(got / 1048576).toFixed(1)}MB/${s.toFixed(0)}s = ${(got / 1048576 / s).toFixed(2)} MB/s`);
  } catch (e) {
    bad++;
    log(`  重连（${e.name === 'AbortError' ? '按时断开' : e.message}）· 连续 ${bad} 次`);
    await new Promise((r) => setTimeout(r, bad > 3 ? 8000 : 800));
    if (bad > 60) { console.error('重连超过 60 次，放弃（网络太差，稍后再跑本脚本会**续传**）'); process.exit(1); }
  }
}
const secs = (Date.now() - t0) / 1000;
log(`✓ 下载完成 ${(TOTAL / 1048576).toFixed(1)} MB · ${secs.toFixed(0)} 秒 · 平均 ${(TOTAL / 1048576 / secs).toFixed(2)} MB/s`);

// ── 3. 解出 APK ──
const work = path.join(ROOT, 'tmp', `artifact-${run.run_number}`);
fs.rmSync(work, { recursive: true, force: true });
fs.mkdirSync(work, { recursive: true });
execFileSync('unzip', ['-o', '-q', tmp, '-d', work]);
const found = [];
const walk = (d) => { for (const e of fs.readdirSync(d, { withFileTypes: true })) { const p = path.join(d, e.name); e.isDirectory() ? walk(p) : (e.name.endsWith('.apk') && found.push(p)); } };
walk(work);
if (!found.length) { console.error('解压后没找到 .apk'); process.exit(1); }
fs.mkdirSync(path.dirname(outPath), { recursive: true });
fs.copyFileSync(found[0], outPath);
const sz = fs.statSync(outPath).size;
log(`✓ 落地 ${outPath}（${(sz / 1048576).toFixed(1)} MB）`);
log(`  来源断言：run #${run.run_number} · sha ${run.head_sha.slice(0, 7)}`);
