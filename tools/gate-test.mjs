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

// ── T6 启动关键契约（真机黑屏事故的直接判据）──
//
// 背景（2026-10-03 首次装机验证，稳定 60fps 出帧但全黑）：
//   `LevelBuilder` 用 `Shader.Find("Standard")` 建材质，而本工程**没有任何 .mat 资产**
//   引用 Standard（几何全部运行时生成），Unity 打包时把它剥离出包 → Shader.Find 返回 null
//   → `new Material(null)` 抛 ArgumentNullException("shader") → 关卡一个对象都没建出来 → 黑屏。
//   同一轮还暴露：场景里没有相机、`Text.font` 为 null（uGUI 不画字）。
//
// 这三条都属于"本机门禁全绿，真机上却什么都看不见"——所以必须落成静态判据。
{
  const t6 = [];

  // 判据必须只看**代码**，不看注释 —— 否则"在注释里记录这次事故"反而会被判红（实测踩到）。
  // 这不是可有可无的洁癖：本项目刻意保留事故原委注释（如记录 Shader.Find("Standard") 的教训），
  // 一个不剥注释的扫描器会把"写清楚为什么不能这么做"判成"又这么做了"。
  const stripComments = (s) => s
    .replace(/\/\*[\s\S]*?\*\//g, ' ')   // 块注释
    .replace(/^\s*\/\/\/.*$/gm, ' ')     // XML 文档注释
    .replace(/\/\/.*$/gm, ' ');          // 行注释（上面三步已把注释内的 URL 一并去掉）

  // ① 着色器资产必须存在，且路径与代码常量一致（Resources 内容无条件进包，不依赖剥离策略）
  const shaderPath = 'unity/Assets/Resources/Shaders/WhisperUnlitColor.shader';
  const lbSrc = stripComments(fs.readFileSync(path.join(ROOT, 'unity/Assets/Scripts/Gameplay/Level/LevelBuilder.cs'), 'utf8'));
  const lbConst = /UnlitShaderResourcePath\s*=\s*"([^"]+)"/.exec(lbSrc);
  if (!fs.existsSync(path.join(ROOT, shaderPath))) t6.push(`关卡着色器资产缺失：${shaderPath}`);
  else if (!lbConst) t6.push('LevelBuilder 未声明 UnlitShaderResourcePath（着色器与本机检查失去契约）');
  else {
    const want = `unity/Assets/Resources/${lbConst[1]}.shader`;
    if (path.normalize(want) !== path.normalize(shaderPath))
      t6.push(`着色器路径与代码常量不符：常量指向 ${want}，实际资产 ${shaderPath}`);
    const sh = fs.readFileSync(path.join(ROOT, shaderPath), 'utf8');
    // 必须暴露 _Color（Material.color 与 MaterialPropertyBlock 都写这个属性名）
    // 【2026-10-06 放宽：允许属性前缀】URP 官方迁移清单第 10 步要求把主色写成 `[MainColor] _Color`
    // （让 `Material.color` 正确映射）。原正则 `^\s*_Color\s*\(` 只认行首直接跟 `_Color`，
    // 于是**照官方要求加特性的写法反而被判红** —— 那是判据过窄，不是代码错。改为允许可选的 `[Attr]` 前缀。
    if (!/^\s*(\[[^\]]+\]\s*)*_Color\s*\(/m.test(sh)) t6.push('着色器未声明 _Color 属性（Material.color 将无效）');
    if (!/Shader\s+"Whisper\/UnlitColor"/.test(sh)) t6.push('着色器名不是 "Whisper/UnlitColor"（与 LevelBuilder.UnlitShaderName 失去契约）');
  }

  // ② 禁止"按名字查找可能被剥离的内置着色器"——真机事故的原形态
  if (/Shader\.Find\("Standard"\)/.test(lbSrc)) t6.push('LevelBuilder 仍在使用 Shader.Find("Standard")（该内置着色器会被剥离，真机黑屏根因）');
  if (/new\s+Material\(\s*Shader\.Find\(/.test(lbSrc)) t6.push('LevelBuilder 仍在用 new Material(Shader.Find(...)) 建材质（可能拿到 null）');
  if (!/Resources\.Load<Shader>/.test(lbSrc)) t6.push('LevelBuilder 未通过 Resources.Load<Shader> 取几何着色器');

  // ③ 启动前必须建好相机与字体（否则几何建出来也看不见、HUD 不画字）
  // 【按类找，不按文件找 · 2026-10-06】`GameBootstrap` 是 **partial** 类，
  // 代码分布在 GameBootstrap.cs / .Api.cs / .Lifecycle.cs / .Boot.cs / .Spawn.cs / .Temperature.cs。
  // 原实现只读 GameBootstrap.cs —— 于是**把方法挪进分部文件就能悄悄绕过 T6**（判据被削弱）。
  // 现在读**全部**分部并拼接，判据覆盖整个类：既修好了拆分后的误报，也让绕过不再可能。
  const bootDir = path.join(ROOT, 'unity/Assets/Scripts/Runtime');
  const bootSrc = stripComments(
    fs.readdirSync(bootDir)
      .filter((n) => /^GameBootstrap(\.[A-Za-z]+)?\.cs$/.test(n))
      .sort()
      .map((n) => fs.readFileSync(path.join(bootDir, n), 'utf8'))
      .join('\n'),
  );
  if (!/new GameObject\(\s*"MainCamera"\s*,\s*typeof\(Camera\)\s*\)/.test(bootSrc))
    t6.push('GameBootstrap 未建相机（Boot 场景无相机 → 什么都渲染不出来）');
  if (!/_status\.font\s*=/.test(bootSrc)) t6.push('GameBootstrap 未给 HUD 设置字体（Text.font 为 null 时 uGUI 不绘制任何文字）');
  if (!/GetBuiltinResource<Font>/.test(bootSrc)) t6.push('GameBootstrap 未取内置字体');

  // ④ 没有关卡着色器就不许构建（构建期门禁，v4 的 CS 判据见 docs/mechanism-gaps.md）
  if (!/GeometryShader/.test(bootSrc)) t6.push('GameBootstrap 未自检 GeometryShader（黑屏应能在 HUD 上一眼看出）');

  // ⑤ .shader 的 GUID 必须由生成器管（缺 meta 时 Unity 会在 CI 里当成新资产，GUID 漂移）
  if (!fs.existsSync(path.join(ROOT, shaderPath + '.meta'))) t6.push('着色器缺 .meta（GUID 不稳定，Unity 资产库会漂移）');

  if (t6.length === 0) ok('T6 启动关键契约：着色器资产+路径+属性 · 无被剥离的 Shader.Find · 相机与字体齐备');
  else { bad(`T6 启动关键契约 ${t6.length} 项不满足（真机会黑屏）`); for (const t of t6) console.log('     · ' + t); }
}

console.log(`\n[gate-test] 结果：通过 ${oks.length} · 失败 ${fails.length}${fails.length ? ' ✗' : ' ✓'}`);
process.exit(fails.length ? 1 : 0);
