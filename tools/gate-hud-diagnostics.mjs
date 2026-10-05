#!/usr/bin/env node
/**
 * gate-hud-diagnostics.mjs — 修「左上角整块小字仍在」（用户：**去除所有小字，不留字体**）
 *
 * ## 为什么上一版没生效
 * 第 10 轮我在 **`MenuScene`** 上加了 `ShowDiagnostics` 开关，但它管的是 `MenuScene` 自己的诊断面板；
 * 真机上左上角那一大块（`Project Whisper · 运行中 / Tick … / 接口：… / 关卡 … / 几何着色器 … /
 * 玩家 / 怪物 / 本局任务 / 温度 / 玩法层 …`）是 **`GameBootstrap.Update()` 每 0.5 秒写 `_status.text`**
 * 渲染的 —— 那条路径**完全没有开关**。0.1.80 真机截图逐字可见，坐实了这一点。
 *
 * ## 修法：开关放到**真正渲染它的地方**
 * · `GameBootstrap.ShowDiagnostics`（**默认 false**）；
 * · HUD 刷新块整体受它控制：关掉时**连写入都不做**，并把 `_status` 的 GameObject 直接 `SetActive(false)`
 *   （比只清空文本彻底 —— 空 Text 仍会占位、仍可能带描边/阴影残影）。
 * · 保留开关而不是删代码：本项目的真机取证**只能走 HUD**（release IL2CPP 下 `Debug.Log` 时有时无，
 *   交接 §0.6 已记），排查时一个字段即可恢复。
 *
 * ## 为什么"不留字体"要单独做（而不是只清文本）
 * 用户原话是「**不留字体**」——即屏幕上不该出现任何调试性文字，包括空壳控件。
 * 故这里用 `SetActive(false)`，把整个诊断容器关掉。
 *
 * 用法：node tools/gate-hud-diagnostics.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'unity/Assets/Scripts/Runtime/GameBootstrap.cs');
const checkOnly = process.argv.includes('--check');
const log = [];
const fail = (m) => { console.error('[hud-gate] ✗ ' + m); process.exit(1); };

const raw = fs.readFileSync(FILE, 'utf8');
if (raw.includes('ShowDiagnostics')) { console.log('[hud-gate] 已应用，跳过'); process.exit(0); }
let s = raw;
const sub = (from, to, what) => {
  const n = s.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  s = s.replace(from, to);
  log.push('  ✓ ' + what);
};

// ── ① 字段：默认关 ────────────────────────────────────────────────────
sub('        Text _status;',
  [
    '        Text _status;',
    '',
    '        /// <summary>',
    '        /// HUD 诊断文字总开关。**默认 false = 不留任何字体**（用户 2026-10-05：「去除所有小字，不留字体」）。',
    '        ///',
    '        /// 为什么必须放在**这里**而不是 MenuScene：左上角那一大块（Tick/接口/关卡/几何着色器/',
    '        /// 玩家/怪物/本局任务/温度/玩法层…）是**本类 Update() 每 0.5s 写 `_status.text`** 渲染的；',
    '        /// 第 10 轮我把开关加在 MenuScene 上，管不到这条路径 —— 0.1.80 真机截图里整块小字照样在。',
    '        ///',
    '        /// 为什么用 SetActive(false) 而不只是清空文本：用户要的是"**不留字体**"（屏幕上不该有调试文字，',
    '        /// 含空壳控件）；且空 Text 仍占位、仍可能留描边残影。',
    '        ///',
    '        /// 为什么保留开关而不是删代码：本项目真机取证**只能走 HUD**（release IL2CPP 下 Debug.Log 时有时无，',
    '        /// 交接 §0.6 已记）。排查时把此字段置 true 即可恢复全部诊断行。',
    '        /// </summary>',
    '        public bool ShowDiagnostics = false;',
  ].join('\n'),
  '① 新增 GameBootstrap.ShowDiagnostics（默认关）');

// ── ② HUD 刷新块整体受开关控制 ────────────────────────────────────────
sub('            if (Time.unscaledTime < _nextHudRefresh) return;\n            _nextHudRefresh = Time.unscaledTime + 0.5f;\n            _status.text = string.Format(',
  [
    '            if (Time.unscaledTime < _nextHudRefresh) return;',
    '            _nextHudRefresh = Time.unscaledTime + 0.5f;',
    '            // 【用户要求：去除所有小字，不留字体】关闭时**连写入都不做**，并把容器整个隐藏。',
    '            // 这比"清空文本"彻底：空 Text 仍占位、仍可能留描边/阴影残影。',
    '            if (!ShowDiagnostics)',
    '            {',
    '                if (_status != null && _status.gameObject.activeSelf) _status.gameObject.SetActive(false);',
    '                return;',
    '            }',
    '            if (_status != null && !_status.gameObject.activeSelf) _status.gameObject.SetActive(true);',
    '            _status.text = string.Format(',
  ].join('\n'),
  '② HUD 刷新块受开关控制（关掉时隐藏容器）');

if (!checkOnly) {
  const bak = FILE + '.bak-hudgate';
  if (!fs.existsSync(bak)) fs.writeFileSync(bak, raw, 'utf8');
  fs.writeFileSync(FILE, s, 'utf8');
}
console.log('[hud-gate] HUD 诊断总开关' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
