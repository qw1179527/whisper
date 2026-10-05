#!/usr/bin/env node
// 把 Unity 的 il2cpp 工具链改造成**能在 Android/aarch64 手机上跑**的形态。
//
// ## 背景（2026-10-05 实测突破）
// Unity 的 `il2cpp` 工具链（`Editor/Data/il2cpp/build/deploy/`）是**托管 .NET 程序**
// （`file` 判定 `Mono/.Net assembly`），理论上有 .NET 运行时的地方就能跑。
// 实测在手机上确实能跑通，但要先排掉**三个障碍** —— 每一步都是报错信息直接指路的：
//
// | # | 报错原文 | 原因 | 本脚本的做法 |
// |---|---|---|---|
// | 1 | `libhostpolicy.so is for EM_X86_64 (62) instead of EM_AARCH64 (183)` | deploy 里自带 **x86_64 的完整 .NET 运行时**，host 优先用它 | 把 14 个原生文件挪进 `_x64_native/` |
// | 2 | `was run as a self-contained app because … did not specify a framework` | `runtimeconfig.json` 用 `includedFrameworks`（自包含模式），host 坚持找 app-local 运行时 | 改写为 `"framework": {"name":"Microsoft.NETCore.App","version":"8.0.0"}` |
// | 3 | `Could not resolve CoreCLR path` | `deps.json` 把**整个 `runtimepack.Microsoft.NETCore.App.Runtime.linux-x64`** 声明成"应用自带资产"，摘掉原生文件后仍按它去找 | 从 `targets` / `libraries` / `dependencies` **三处**摘掉 runtimepack |
//
// 排掉之后轨迹（`COREHOST_TRACE=1`）会出现**决定性一行**：
// ```
// Processing TPA for deps entry [Microsoft.NETCore.App.Runtime.linux-bionic-arm64, 8.0.31, …]
// Launch host: /…/dotnet, app: /…/il2cpp-compile.dll, argc: 0, args:
// ```
// 即它解析的是**手机自己的运行时**（`linux-bionic-arm64`）并成功 `Launch`。
//
// ## 幂等
// 重复跑不会重复搬运/重复改写；已改过的会跳过并报告 `skip`。
//
// ## 用法
//   node tools/prepare-il2cpp-for-android.mjs <deploy 目录> [--check]
//   # deploy 目录 = <解出的 unity-tools>/out/Editor/Data/il2cpp/build/deploy
import fs from 'node:fs';
import path from 'node:path';

const args = process.argv.slice(2);
const checkOnly = args.includes('--check');
const deploy = args.find((a) => !a.startsWith('--'));
if (!deploy) {
  console.error('用法: node tools/prepare-il2cpp-for-android.mjs <deploy 目录> [--check]');
  process.exit(2);
}
if (!fs.existsSync(deploy)) { console.error('目录不存在: ' + deploy); process.exit(2); }

const log = (m) => console.log('  ' + m);
let changed = 0;

// ── ① 把 x86_64 原生运行时挪走 ────────────────────────────────
// 只挪 `.so` 与 `createdump`；**不动** `il2cpp` / `il2cpp-compile` 这两个 x86_64 apphost
// （它们是给 Windows/Linux-x64 直接双击用的，我们用 `dotnet <dll>` 绕开，留着无妨也不会被选到）。
const NATIVE = ['createdump'];
const isNative = (n) => n.endsWith('.so') || NATIVE.includes(n);
const x64Dir = path.join(deploy, '_x64_native');
const natives = fs.readdirSync(deploy).filter((n) => isNative(n) && fs.statSync(path.join(deploy, n)).isFile());
if (natives.length === 0) {
  log('① 原生 .so 已挪走（跳过）');
} else if (checkOnly) {
  log(`① 待挪 ${natives.length} 个原生文件（--check 未执行）`);
  changed++;
} else {
  fs.mkdirSync(x64Dir, { recursive: true });
  for (const n of natives) fs.renameSync(path.join(deploy, n), path.join(x64Dir, n));
  log(`① 已挪走 ${natives.length} 个 x86_64 原生文件 → _x64_native/`);
  changed++;
}

// ── ② runtimeconfig.json 改成框架依赖 ─────────────────────────
const rcPath = path.join(deploy, 'il2cpp-compile.runtimeconfig.json');
if (!fs.existsSync(rcPath)) { console.error('缺 il2cpp-compile.runtimeconfig.json'); process.exit(1); }
const rc = JSON.parse(fs.readFileSync(rcPath, 'utf8'));
const alreadyFrameworkDep = !!(rc.runtimeOptions && rc.runtimeOptions.framework);
if (alreadyFrameworkDep) {
  log('② runtimeconfig 已是框架依赖（跳过）');
} else if (checkOnly) {
  log('② runtimeconfig 仍是自包含模式（--check 未执行）');
  changed++;
} else {
  const inc = (rc.runtimeOptions?.includedFrameworks ?? [])[0] ?? { version: '8.0.4' };
  // 版本写 8.0.0：**下限**而非精确值 —— 手机的 8.0.31 能靠 roll-forward 命中。
  // 若照抄 includedFrameworks 里的 8.0.4，手机只有 8.0.x 时也能滚，但写 8.0.0 更稳。
  rc.runtimeOptions = {
    tfm: rc.runtimeOptions?.tfm ?? 'net8.0',
    framework: { name: 'Microsoft.NETCore.App', version: '8.0.0' },
    configProperties: rc.runtimeOptions?.configProperties ?? {},
  };
  if (!fs.existsSync(rcPath + '.orig-from-unity')) fs.copyFileSync(rcPath, rcPath + '.orig-from-unity');
  fs.writeFileSync(rcPath, JSON.stringify(rc, null, 2) + '\n');
  log(`② runtimeconfig 已改为框架依赖（原 includedFrameworks 为 ${inc.version}，备份 .orig-from-unity）`);
  changed++;
}

// ── ③ deps.json 摘掉 runtimepack ──────────────────────────────
const djPath = path.join(deploy, 'il2cpp-compile.deps.json');
if (!fs.existsSync(djPath)) { console.error('缺 il2cpp-compile.deps.json'); process.exit(1); }
const dj = JSON.parse(fs.readFileSync(djPath, 'utf8'));
const RP_PREFIX = 'runtimepack.Microsoft.NETCore.App.Runtime.';
let rpKeys = [];
for (const t of Object.keys(dj.targets ?? {})) for (const k of Object.keys(dj.targets[t])) if (k.startsWith(RP_PREFIX)) rpKeys.push([t, k]);
if (rpKeys.length === 0) {
  log('③ deps.json 已无 runtimepack（跳过）');
} else if (checkOnly) {
  log(`③ deps.json 仍有 ${rpKeys.length} 处 runtimepack 声明（--check 未执行）`);
  changed++;
} else {
  let n = 0;
  for (const [t, k] of rpKeys) {
    delete dj.targets[t][k]; n++;
    delete dj.libraries[k];
    // 主项目的 dependencies 里也可能引用（名字带/不带 .linux-x64 后缀）
    for (const proj of Object.keys(dj.targets[t])) {
      const d = dj.targets[t][proj]?.dependencies;
      if (!d) continue;
      for (const dk of Object.keys(d)) if (dk.startsWith(RP_PREFIX)) { delete d[dk]; n++; }
    }
  }
  if (!fs.existsSync(djPath + '.orig-from-unity')) fs.copyFileSync(djPath, djPath + '.orig-from-unity');
  fs.writeFileSync(djPath, JSON.stringify(dj, null, 2) + '\n');
  log(`③ 已从 deps.json 摘掉 ${n} 处 runtimepack 声明（备份 .orig-from-unity）`);
  changed++;
}

// ── 汇总 ──────────────────────────────────────────────────────
console.log();
if (changed === 0) console.log('✓ 已是 Android/aarch64 可跑形态（三处都无需改动）');
else if (checkOnly) { console.log(`✗ 有 ${changed} 处待处理（去掉 --check 即执行）`); process.exit(1); }
else console.log(`✓ 已改造完成（${changed} 处）`);

// ── 自检提示（真正跑起来的判据）──────────────────────────────
console.log();
console.log('  验证（要能看到与下面同形的行才算真的启动托管入口）：');
console.log('    DOTNET_ROOT=<.NET SDK 根> COREHOST_TRACE=1 \\');
console.log('      <DOTNET_ROOT>/dotnet il2cpp-compile.dll 2>&1 | grep -E "Launch host|TPA for deps entry"');
console.log('  期望看到：');
console.log('    Processing TPA for deps entry [Microsoft.NETCore.App.Runtime.linux-bionic-arm64, …]');
console.log('    Launch host: …/dotnet, app: …/il2cpp-compile.dll, argc: 0, args:');
console.log('  注：`argc: 0` 时它会**静默退 1** —— 那是"缺参数"的正常表现，');
console.log('      说明 CoreCLR 已解析成功、应用已启动；真要干活得喂它 Bee 响应文件。');
