#!/usr/bin/env node
/**
 * fix-run-in-background.mjs — 修「画面死住 + 点了没反应」的根因：应用失焦即停渲染
 *
 * ## 根因（真机日志证据，不是猜）
 * 复现时抓到（`adb logcat`，Unity 自己打印的）：
 * ```
 * [Unity] HasWindow = 1, HasFocus = 0
 * [Unity] Handle cmd APP_CMD_TERM_WINDOW(2)
 * [Unity] Handle cmd APP_CMD_STOP(15)
 * WindowManager: state from READY_TO_SHOW to NO_SURFACE; reason: destroySurface
 * ```
 * Android 下应用**失去焦点**时，Unity 默认（`runInBackground=false`）会**停止渲染**并释放 Surface。
 * 而本工程相机、UI、画布全部是**代码建的**（场景只有 Boot.unity），没有任何组件会在 Surface
 * 重建后自己接回来 → 表现就是用户报的「**画面死住**」；同时没焦点的窗口收不到注入输入 →
 * 「**点了没反应**」。两条症状同一个根因。
 *
 * ## 为什么在本项目里必须开 runInBackground
 * 1. 移动端玩家常切出去看消息再切回来；本工程没有"重新初始化"的入口，一停就回不来；
 * 2. 真机取证（本项目主力验证手段）依赖 `adb`，而 adb 注入期间窗口常处于失焦态 ——
 *    不开这个开关，取证本身就会制造"看起来坏了"的假象（本会话已被它误导一轮）。
 *
 * ## 两处修（构建期 + 运行期，缺一不可）
 * · 构建期：`BuildConfigurator` 设 `PlayerSettings.runInBackground = true`（进 ProjectSettings 落盘）；
 * · 运行期：`GameBootstrap` 启动时设 `Application.runInBackground = true`（防 ProjectSettings 被覆盖）。
 *
 * ## 纪律
 * `Application.runInBackground` 在离线桩里**没有** → 先补桩，否则语法门禁先红（本会话已多次如此）。
 *
 * 用法：node tools/fix-run-in-background.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const checkOnly = process.argv.includes('--check');
const log = [];
const fail = (m) => { console.error('[rib] ✗ ' + m); process.exit(1); };

function edit(rel, from, to, what) {
  const p = path.join(ROOT, rel);
  const s = fs.readFileSync(p, 'utf8');
  if (s.includes(to.trim().split('\n')[0]) && to.includes('runInBackground')) {
    // 幂等判断交给调用方，这里只在锚点唯一时替换
  }
  const n = s.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  if (!checkOnly) fs.writeFileSync(p, s.replace(from, to), 'utf8');
  log.push('  ✓ ' + what);
}

// ── ① 补桩：Application.runInBackground ───────────────────────────────
{
  const p = path.join(ROOT, 'native/unity-stubs/UnityStubs.cs');
  const s = fs.readFileSync(p, 'utf8');
  if (s.includes('runInBackground')) log.push('  · 桩里已有 runInBackground，跳过');
  else {
    const from = '        public static bool isEditor => false;';
    const to = '        public static bool isEditor => false;\n' +
      '        /// <summary>失焦时是否继续渲染（出处 ScriptReference/Application-runInBackground）。\n' +
      '        /// 移动端默认 false：失焦即停渲染并释放 Surface，而本工程相机/UI 全是代码建的、不会自建回来。</summary>\n' +
      '        public static bool runInBackground { get; set; }';
    const n = s.split(from).length - 1;
    if (n !== 1) fail(`Application 桩锚点命中 ${n} 次（应为 1）`);
    if (!checkOnly) fs.writeFileSync(p, s.replace(from, to), 'utf8');
    log.push('  ✓ 桩补 Application.runInBackground');
  }
}

// ── ② 构建期：PlayerSettings.runInBackground = true ───────────────────
{
  const rel = 'unity/Assets/Editor/BuildConfigurator.cs';
  const p = path.join(ROOT, rel);
  const s = fs.readFileSync(p, 'utf8');
  if (s.includes('PlayerSettings.runInBackground')) log.push('  · BuildConfigurator 已设，跳过');
  else {
    const from = '            PlayerSettings.useAnimatedAutorotation = false;';
    const n = s.split(from).length - 1;
    if (n !== 1) fail(`BuildConfigurator 锚点命中 ${n} 次（应为 1）`);
    const to = from + '\n' +
      '            // 【用户 2026-10-05 报"画面死住 + 点了没反应"】\n' +
      '            // 真机日志证据：失焦 → Unity APP_CMD_TERM_WINDOW → WindowManager destroySurface。\n' +
      '            // 移动端默认 runInBackground=false，失焦即停渲染；而本工程相机/UI 全是代码建的，\n' +
      '            // 没人会在 Surface 重建后接回来 → 画面停住且输入无人处理。\n' +
      '            // 同时 adb 取证期间窗口常失焦，不开它连验证都会制造假故障。\n' +
      '            PlayerSettings.runInBackground = true;';
    if (!checkOnly) fs.writeFileSync(p, s.replace(from, to), 'utf8');
    log.push('  ✓ BuildConfigurator 设 PlayerSettings.runInBackground = true');
  }
}

// ── ③ 运行期：Application.runInBackground = true ──────────────────────
{
  const rel = 'unity/Assets/Scripts/Runtime/GameBootstrap.cs';
  const p = path.join(ROOT, rel);
  const s = fs.readFileSync(p, 'utf8');
  if (s.includes('Application.runInBackground')) log.push('  · GameBootstrap 已设，跳过');
  else {
    const from = '        void Start() => Boot();';
    const n = s.split(from).length - 1;
    if (n !== 1) fail(`GameBootstrap Start 锚点命中 ${n} 次（应为 1）`);
    const to = '        void Start()\n' +
      '        {\n' +
      '            // 【防"失焦即停渲染"】移动端默认 false：失焦会停渲染并释放 Surface，\n' +
      '            // 而本工程相机/UI 全是代码建的、不会自建回来（真机日志：APP_CMD_TERM_WINDOW → destroySurface）。\n' +
      '            // 运行期也设一遍，防 ProjectSettings 被覆盖或换机后丢设置。\n' +
      '            Application.runInBackground = true;\n' +
      '            Boot();\n' +
      '        }';
    if (!checkOnly) fs.writeFileSync(p, s.replace(from, to), 'utf8');
    log.push('  ✓ GameBootstrap 运行期设 Application.runInBackground = true');
  }
}

console.log('[rib] 修「失焦即停渲染」' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
console.log('  ⚠ 必须跑：unity-syntax-check.sh + unity-tests.sh EditMode，然后出包真机复验');
