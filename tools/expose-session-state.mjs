#!/usr/bin/env node
/**
 * expose-session-state.mjs — 让"玩法层是否在跑"变成**可见事实**（v2：修 JS 转义）
 *
 * ## v1 为什么没跑起来
 * 我在 JS 模板串里嵌了**反引号**（`BootLog`）与**未转义的双引号** →
 * `SyntaxError: missing ) after argument list`。本会话第 N 次栽在"多层字符串"上。
 * 纪律（再次确认）：**凡补丁内容含引号/反引号，一律用数组 + join 拼行，不要塞进模板串。**
 *
 * ## 为什么必须做这一步
 * 上一步把 `GameSession` 实例化后，我往 `lines.AppendLine` 写了证据，但随后核实发现：
 * **`BootLog = lines.ToString()` 只在失败路径被赋值**（`GameBootstrap.cs:985` 在失败分支内），
 * 成功启动时 `lines` 根本不展示 —— **接线了，但没人能看见它在跑**。
 * 这正是本轮要防的"验证过 ≠ 在产品里"。
 *
 * ## 做法
 * 1. `SessionStatus`：一行可读状态（是否接线 / 已推进秒数 / 理智% 与档位 / 证据数 / 阶段）。
 * 2. 成功路径把 `SessionStatus` 追加进 `BootLog`（成功也留证据）。
 * 3. 每帧 Tick 后写进 HUD 顶部（`AppendStatus`）—— **真机取证硬证据**：
 *    理智数字若随时间变化，说明 Tick 真在跑。
 *
 * 用法：node tools/expose-session-state.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'unity/Assets/Scripts/Runtime/GameBootstrap.cs');
const checkOnly = process.argv.includes('--check');
const log = [];
const fail = (m) => { console.error('[sess-state] ✗ ' + m); process.exit(1); };
const Q = String.fromCharCode(34);   // 双引号：避免直接写进模板串

const raw = fs.readFileSync(FILE, 'utf8');
if (raw.includes('SessionStatus')) { console.log('[sess-state] 已存在，跳过'); process.exit(0); }
let s = raw;
const sub = (from, to, what) => {
  const n = s.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  s = s.replace(from, to);
  log.push('  ✓ ' + what);
};

// ── ① SessionStatus ───────────────────────────────────────────────────
{
  const anchor = '        /// <summary>配置读取器（与 Progression/Shop/TaskSystem 共用同一份，避免两套配置口径）。</summary>';
  const blk = [
    '        /// <summary>',
    '        /// 玩法层状态一行文本（**单一口径**：HUD / 诊断面板 / 将来的结算页都读它）。',
    '        /// 为什么必须有它：接线之后如果没人能看见它在跑，就等于没接线 —— 本项目已有',
    '        /// SendLocalPlayer 零调用者、UdpV6NetService 从未构造、MenuScene 键盘检查挂在条件链里',
    '        /// 从未生效这三条同类前科。判据：**理智数字随时间变化**，才证明 Tick 真被调用。',
    '        /// </summary>',
    '        public string SessionStatus',
    '        {',
    '            get',
    '            {',
    '                if (Session == null) return ' + Q + '玩法层：**未接线**（理智/猎杀/撤离不会推进）' + Q + ';',
    '                var so = Session.Outcome;',
    '                return ' + Q + '玩法层 已推进 ' + Q + ' + so.ElapsedSeconds.ToString(' + Q + 'F1' + Q + ') + ' + Q + 's' + Q,
    '                     + ' + Q + ' · 理智 ' + Q + ' + (Session.Sanity.Value * 100f).ToString(' + Q + 'F1' + Q + ') + ' + Q + '%' + Q,
    '                     + ' + Q + '（' + Q + ' + Session.Sanity.Band.Label + ' + Q + '）' + Q,
    '                     + ' + Q + ' · 证据 ' + Q + ' + so.EvidenceCollected + ' + Q + '/' + Q + ' + so.EvidenceTotal',
    '                     + ' + Q + ' · 阶段 ' + Q + ' + Session.Director.Stage',
    '                     + (so.Ended ? ' + Q + ' · 已结束' + Q + ' : ' + Q + Q + ');',
    '            }',
    '        }',
    '',
  ].join('\n');
  sub(anchor, blk + anchor, '① 新增 SessionStatus（单一口径）');
}

// ── ② 成功路径也留证据 ────────────────────────────────────────────────
sub('            InitProgressionSystems(lines);\n            BuildMenu(lines);',
  [
    '            InitProgressionSystems(lines);',
    '            BuildMenu(lines);',
    '            // 【可见性】BootLog 原先只在失败路径赋值 → 成功启动时 lines 没人看得到，',
    '            // "玩法层已接线"这类证据等于没留。这里在成功路径也把它固化下来。',
    '            lines.AppendLine(SessionStatus);',
  ].join('\n'),
  '② 成功路径把 SessionStatus 写进 BootLog');

// ── ③ 每帧写进 HUD（真机取证硬证据）──────────────────────────────────
sub('            Session.Tick(Time.deltaTime);',
  [
    '            Session.Tick(Time.deltaTime);',
    '            // 【真机取证通道】把玩法层状态写进 HUD 顶部那行 —— 理智数字若在变，',
    '            // 说明 Tick 真在跑（而不是只编译过）。release 下 Debug.Log 时有时无，HUD 才可靠。',
    '            AppendStatus(SessionStatus);',
  ].join('\n'),
  '③ 每帧把状态写进 HUD');

if (!checkOnly) {
  const bak = FILE + '.bak-sessstate';
  if (!fs.existsSync(bak)) fs.writeFileSync(bak, raw, 'utf8');
  fs.writeFileSync(FILE, s, 'utf8');
}
console.log('[sess-state] 玩法层状态可见化' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
