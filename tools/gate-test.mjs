#!/usr/bin/env node
/**
 * gate-test.mjs — 功能测试门禁（用户要求的第四类门禁）
 *
 * 与其它三道门禁的分工：
 *   gate-model   几何/资产**静态**合法性（不跑逻辑）
 *   gate-physics 规则与数值的**静态**自洽（不跑逻辑）
 *   gate-code    源码质量（不看行为）
 *   gate-test    **真跑逻辑**：本机 .NET 真编译 + 真执行 107 条断言（含端到端整局回放）
 *
 * 判据：
 *   T1 编译必须干净（0 error）
 *   T2 断言必须 0 失败
 *   T3 断言总数不得下降（防"删断言让门禁变绿"）—— 与 .gate-test-baseline 比对
 *   T4 失败必须可复现：失败时打印断言名 + 失败清单，并给出复现命令
 *   T5 分类计数可读：按 Check 名称前缀归类统计，便于看"哪一类覆盖薄"
 *
 * ## 明确不覆盖（如实声明）
 *   · Unity 运行期行为（组件生命周期、渲染、uGUI 布局）——本机无 Unity，未能验证
 *   · PlayMode 用例（Assets/Tests/PlayMode/BootSmokeTests.cs）从未执行过 —— 未能验证
 *   · 真机安装与运行 —— 未能验证
 *
 * 用法：node tools/gate-test.mjs [--update-baseline] [--inject-fail]
 */
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const VERIFY_DIR = path.join(ROOT, 'native/csharp-verify');
const BASELINE = path.join(ROOT, '.gate-test-baseline.json');
const DOTNET = path.join(ROOT, 'native/dotnet.sh');
const args = process.argv.slice(2);

const fails = [], oks = [];
const ok = (m) => { oks.push(m); console.log('  ✓ ' + m); };
const bad = (m) => { fails.push(m); console.log('  ✗ ' + m); };

console.log('[gate-test] 功能测试门禁（本机真编译 + 真跑断言）');

// 同步真源（避免 C# 跑手读到陈旧副本 —— 复核第 9 条不一致就是这类漂移）
for (const f of ['unity/Assets/Levels/asylum_v1.json', 'unity/Assets/Data/asset-manifest.json', 'data/config.json']) {
  const src = path.join(ROOT, f);
  if (!fs.existsSync(src)) continue;
  const dst = path.join(VERIFY_DIR, path.basename(f));
  if (!fs.existsSync(dst) || fs.readFileSync(src).compare(fs.readFileSync(dst)) !== 0) {
    fs.copyFileSync(src, dst);
  }
}
// 注入一次失败（验证门禁真的会红）
if (args.includes('--inject-fail')) {
  // 注入一条**完整合法**的必失败断言（不是正则改写既有断言 —— 我第一版那么做直接产生语法错误，
  // 于是门禁报"编译失败"，看起来像判红但其实是自伤，不能算有效验证）。
  const prog = path.join(VERIFY_DIR, 'Program.cs');
  const backup = fs.readFileSync(prog, 'utf8');
  // 用**精确锚点**：第一条断言的名字。此前用 `indexOf('        Check(')` 会命中语句中间的
  // 缩进片段（Program.cs 里前面还有其它以 8 空格缩进、包含 "Check(" 的代码），插进去就成了语法错误。
  // 找**行首缩进的 Check(**（`m.index` 指向行首空格处）。
  // 不能只 indexOf('Check(')：Program.cs 前面有其它包含 "Check(" 的语句片段，
  // 插进去会变成语法错误（门禁报"编译失败"看起来像判红，实为自伤）。
  const m = /^\s{8,}Check\(/m.exec(backup);
  if (!m) { console.error('  [注入] 找不到行首 Check( 锚点'); process.exit(1); }
  const marker = m.index;
  const indent = backup.slice(marker, backup.indexOf('Check(', marker));
  fs.writeFileSync(prog, backup.slice(0, marker)
    + indent + 'Check("__INJECT__ 必失败断言", () => false);\n'
    + backup.slice(marker), 'utf8');
  console.log('  [注入] 已插入一条必失败断言（预期本门禁判红）');
  let injectedFailed = false;
  try {
    const res = runChecks('注入');
    injectedFailed = !res || res.failed > 0;
    if (res) console.log(`  [注入] 实测：通过 ${res.total - res.failed} · 失败 ${res.failed}`);
  } finally {
    fs.writeFileSync(prog, backup, 'utf8');
    console.log('  [注入] 已还原 Program.cs');
  }
  console.log(injectedFailed ? '  ✓ 注入验证有效：门禁正确判红' : '  ✗ 注入验证失败：门禁未判红（不可信）');
  process.exit(injectedFailed ? 0 : 1);
}

function runChecks(label) {
  let out = '';
  try {
    // 必须在 native/csharp-verify 下执行：dotnet.sh 用的是随仓库分发的本地 SDK/宿主，
    // 工作目录不对会直接失败（我第一版用 cwd: ROOT，结果报"0 个编译错误"却整体失败）。
    out = execFileSync('bash', ['../../native/dotnet.sh', 'run', '--nologo'], {
      cwd: VERIFY_DIR, encoding: 'utf8', timeout: 900000, maxBuffer: 64 * 1024 * 1024,
      env: { ...process.env, DOTNET_CLI_TELEMETRY_OPTOUT: '1', DOTNET_NOLOGO: '1' },
    });
  } catch (e) {
    out = String(e.stdout ?? '') + String(e.stderr ?? '');
    const errs = out.split('\n').filter((l) => /error CS/.test(l));
    if (errs.length) {
      bad(`T1 编译失败：${errs.length} 个编译错误`);
      for (const e2 of errs.slice(0, 3)) console.log('     · ' + e2.trim());
      console.log('   复现：cd native/csharp-verify && ../../native/dotnet.sh build --nologo');
      return null;
    } else {
      // 区分"编译失败"与"运行失败"：跑手自身非零退出（例如断言注入导致 Main 抛异常）时
      // 不能笼统报成编译错误 —— 我第一版就是这么误报的，掩盖了真实原因。
      bad(`跑手非零退出（exit ${e.status ?? '?'}）且无编译错误 —— 见下方原始输出尾部`);
      const tail = out.split('\n').filter((l) => l.trim()).slice(-6);
      for (const t of tail) console.log('     · ' + t.trim().slice(0, 160));
      console.log('   复现：cd native/csharp-verify && ../../native/dotnet.sh run --nologo');
      // 注意：跑手因断言失败而非零退出是**正常路径**（失败清单已打印），
      // 仍需继续解析"通过/失败"计数，否则注入验证看不到失败数。
    }
  }
  const mPass = out.match(/结果：通过 (\d+) · 失败 (\d+)/);
  const total = mPass ? Number(mPass[1]) + Number(mPass[2]) : (out.match(/✓/g) ?? []).length;
  const failed = mPass ? Number(mPass[2]) : 0;
  const names = [...out.matchAll(/·\s*(?:✓|✗)?\s*(.+)/g)].length;
  return { out, total, failed };
}

// ── 跑一次 ──
const r = runChecks('主');
if (!r) { console.log(`\n[gate-test] 结果：通过 ${oks.length} · 失败 ${fails.length} ✗`); process.exit(1); }

const { out, total, failed } = r;

// ── T1 编译干净 ──
if (/error CS/.test(out)) bad('T1 存在编译错误');
else ok('T1 编译干净（0 error）');

// ── T5 分类计数 ──
const lines = out.split('\n').filter((l) => /[✓✗]/.test(l) && !/^(  \[|结果)/.test(l));
const byCat = new Map();
for (const l of lines) {
  const name = l.replace(/^\s*[✓✗]\s*/, '').trim();
  const cat = /几何|碰撞|门|连通|内墙|道具|子步进/.test(name) ? '几何与碰撞'
    : /声纹|听觉|刺激|校准|人格/.test(name) ? '声纹与听觉'
    : /怪物|追逐|巡逻|调查|回归|状态/.test(name) ? '怪物状态机'
    : /理智|崩溃|档位/.test(name) ? '理智系统'
    : /撤离|经济|结算|证据/.test(name) ? '撤离与经济'
    : /配置|注入|接口|后端|网络|语音服务/.test(name) ? '配置与三接口'
    : /关卡|DSL|加载/.test(name) ? '关卡与 DSL'
    : '其它';
  byCat.set(cat, (byCat.get(cat) ?? 0) + 1);
}
console.log('  ── 分类计数 ──');
for (const [c, n] of [...byCat.entries()].sort((a, b) => b[1] - a[1])) console.log(`     ${c.padEnd(12)} ${n} 条`);

// ── T2 断言全过 ──
failed === 0 ? ok(`T2 断言全部通过（${total} 条）`) : (() => {
  bad(`T2 有 ${failed} 条断言失败`);
  const fl = out.split('\n').filter((l) => /✗/.test(l)).slice(0, 6);
  for (const l of fl) console.log('     · ' + l.trim());
  console.log('   复现：bash unity-check.sh（或 cd native/csharp-verify && ../../native/dotnet.sh run --nologo）');
})();

// ── T3 断言数不得下降（防删断言变绿）──
const cur = { total, at: new Date().toISOString(), categories: Object.fromEntries(byCat) };
if (args.includes('--update-baseline')) {
  fs.writeFileSync(BASELINE, JSON.stringify(cur, null, 2) + '\n', 'utf8');
  ok(`T3 断言数基线已更新为 ${total}`);
} else if (!fs.existsSync(BASELINE)) {
  fs.writeFileSync(BASELINE, JSON.stringify(cur, null, 2) + '\n', 'utf8');
  ok(`T3 断言数基线首次建立：${total} 条`);
} else {
  const base = JSON.parse(fs.readFileSync(BASELINE, 'utf8'));
  total >= base.total
    ? ok(`T3 断言数未下降：${total} ≥ 基线 ${base.total}（删除断言会让本门禁判红）`)
    : bad(`T3 断言数下降：${total} < 基线 ${base.total}（疑似通过删断言变绿；确认无误后跑 --update-baseline）`);
}

// ── T4 失败可复现（上面已打印复现命令即满足；此处确保失败时确有名单）──
if (failed > 0 && !/失败清单/.test(out)) bad('T4 失败但跑手未输出失败清单（无法复现）');

console.log(`\n[gate-test] 结果：通过 ${oks.length} · 失败 ${fails.length}${fails.length ? ' ✗' : ' ✓'}`);
process.exit(fails.length ? 1 : 0);
