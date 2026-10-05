#!/usr/bin/env node
// agent-task —— 智能体派 Unity 任务到云端、等它跑完、**并行下载产物**、解好放到位。
//
// ## 这是我在这个项目里的开发循环
// ```
// node tools/agent-task.mjs render-evidence
//   → 触发 unity-agent.yml
//   → 轮询直到结束（并打印进行到哪一步）
//   → 下载产物（用 tools/fast-download.mjs 的 8 并行策略）
//   → 解到 .agent-out/<task>-<run>/
//   → 打印可读的文件清单（PNG 我直接 read_image 看）
// ```
//
// ## 为什么要并行下载
// 实测同一目标：1 连接 32.6 KB/s → 8 连接 **270.6 KB/s（8.3 倍，几乎线性）**。
// 说明限速是**按连接**计的。取证任务的产物常有几十 MB 的 PNG，串行下会很痛。
//
// ## 两层判据（不只看作业绿不绿）
// 作业 success 只说明"命令没报错"。真正的判据在**产物里**：
//   · 取证类 → 必须有 PNG，且 tools/render-evidence.sh 自己会判 `*_OK` sentinel
//   · 测试类 → 必须有结果 XML，且 XML 里 failures 数才是权威
// 所以本脚本结束时会**列出产物清单**，让人（我）看得到"到底产出了什么"。
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const REPO = process.env.GH_REPO || 'qw1179527/whisper';
const TOKEN = process.env.GH_TOKEN || process.env.GH_NEW;
if (!TOKEN) { console.error('缺 GH_TOKEN / GH_NEW'); process.exit(2); }
const H = { Authorization: 'Bearer ' + TOKEN, Accept: 'application/vnd.github+json' };

const argv = process.argv.slice(2);
const task = argv.find((a) => !a.startsWith('--'));
if (!task) {
  console.error('用法: node tools/agent-task.mjs <task> [--method X] [--extra "..."] [--keep]');
  console.error('task: render-evidence | door-evidence | kit-visibility | kit-assets-selftest |');
  console.error('      tests-editmode | tests-playmode | build-android | shell');
  process.exit(2);
}
const optOf = (k, d = '') => { const i = argv.indexOf(k); return i >= 0 ? argv[i + 1] : d; };
const log = (m) => console.log(`[${new Date().toISOString().slice(11, 19)}] ${m}`);

// ── ① 触发 ──
log(`触发 unity-agent · task=${task}`);
const dr = await fetch(`https://api.github.com/repos/${REPO}/actions/workflows/unity-agent.yml/dispatches`, {
  method: 'POST', headers: H,
  body: JSON.stringify({ ref: 'main', inputs: { task, method: optOf('--method'), extra: optOf('--extra') } }),
});
if (dr.status !== 204) { console.error('触发失败 HTTP ' + dr.status + ' ' + (await dr.text()).slice(0, 200)); process.exit(1); }
log('已触发（204）');

// ── ② 找到这次 run ──
let run = null;
for (let i = 0; i < 25 && !run; i++) {
  await new Promise((s) => setTimeout(s, 6000));
  const j = await (await fetch(`https://api.github.com/repos/${REPO}/actions/workflows/unity-agent.yml/runs?per_page=5`, { headers: H })).json();
  run = (j.workflow_runs || []).find((x) => x.status !== 'completed') || (j.workflow_runs || [])[0];
}
if (!run) { console.error('没找到 run'); process.exit(1); }
log(`run #${run.run_number} · id=${run.id}`);

// ── ③ 轮询（打印步骤进度，长任务里能看到卡在哪）──
let lastStep = '';
for (let i = 0; i < 400; i++) {
  await new Promise((s) => setTimeout(s, 15000));
  let job = null;
  try {
    const j = await (await fetch(`https://api.github.com/repos/${REPO}/actions/runs/${run.id}/jobs`, { headers: H })).json();
    job = (j.jobs || [])[0];
  } catch { /* 网络抖一下不影响 */ }

  if (job) {
    const cur = (job.steps || []).find((s) => s.status === 'in_progress');
    const done = (job.steps || []).filter((s) => s.conclusion === 'success').length;
    if (cur && cur.name !== lastStep) { lastStep = cur.name; log(`▶ 已完成 ${done} 步 · ${cur.name}`); }
    if (job.status === 'completed') {
      log(`作业结束：${job.conclusion}`);
      if (job.conclusion !== 'success') {
        log('红在哪一步：');
        (job.steps || []).filter((s) => s.conclusion === 'failure').forEach((s) => log('   ✗ ' + s.name));
      }
      break;
    }
  }
}

// ── ④ 下载产物（8 并行）──
const arts = await (await fetch(`https://api.github.com/repos/${REPO}/actions/runs/${run.id}/artifacts`, { headers: H })).json();
const list = (arts.artifacts || []).filter((a) => !a.expired);
if (!list.length) { console.error('没有产物 —— 任务大概率没跑到产出那一步（看上面红在哪）'); process.exit(1); }

const outDir = path.join(ROOT, '.agent-out', `${task}-${run.run_number}`);
fs.rmSync(outDir, { recursive: true, force: true });
fs.mkdirSync(outDir, { recursive: true });

for (const a of list) {
  const zip = path.join(outDir, a.name + '.zip');
  log(`下载产物 ${a.name}（${(a.size_in_bytes / 1048576).toFixed(1)} MB）· 8 并行`);
  const t0 = Date.now();
  // 复用已实测 8.3 倍的并行下载器
  execFileSync(process.execPath, [
    path.join(ROOT, 'tools/fast-download.mjs'),
    `https://api.github.com/repos/${REPO}/actions/artifacts/${a.id}/zip`,
    zip, '--conn', '8', '--chunk', '2',
    '--header', 'Authorization: Bearer ' + TOKEN,
  ], { stdio: 'inherit' });
  log(`  用时 ${((Date.now() - t0) / 1000).toFixed(0)}s`);
  execFileSync('unzip', ['-o', '-q', zip, '-d', outDir]);
  fs.rmSync(zip, { force: true });
}

// ── ⑤ 列清单（我要看"到底产出了什么"）──
log('产物清单：');
const files = [];
const walk = (d) => {
  for (const e of fs.readdirSync(d, { withFileTypes: true })) {
    const p = path.join(d, e.name);
    if (e.isDirectory()) walk(p); else files.push(p);
  }
};
walk(outDir);
const rel = (p) => p.replace(outDir + '/', '');
for (const f of files.sort()) {
  const sz = fs.statSync(f).size;
  const tag = /\.png$/i.test(f) ? '🖼 ' : /\.xml$/i.test(f) ? '📋 ' : /\.apk$/i.test(f) ? '📦 ' : '   ';
  log(`  ${tag}${rel(f)}  (${(sz / 1024).toFixed(0)} KB)`);
}

// 测试结果 XML 的权威判据：直接读 failures/errors
for (const f of files.filter((x) => /test-results.*\.xml$/i.test(x))) {
  const t = fs.readFileSync(f, 'utf8');
  const m = t.match(/<test-run[^>]*total="(\d+)"[^>]*failed="(\d+)"[^>]*/);
  if (m) log(`  📋 ${rel(f)}: 共 ${m[1]} 条 · 失败 ${m[2]} 条 ${m[2] === '0' ? '✓' : '✗'}`);
}
log(`全部产物在：${outDir}`);
if (!argv.includes('--keep')) log('（PNG 我接下来直接 read_image 看）');
