#!/usr/bin/env node
/**
 * wire-player-views.mjs — 把 PlayerViews 接进组合根（"看得见彼此"的装配）
 *
 * ## 缺陷（与 SendLocalPlayer 无人调用同源）
 * `Runtime/` 里只有 `MonsterViews`（怪），**没有任何代码渲染远端玩家**；
 * 于是即使联机握手成功，屏幕上也看不见队友 —— "可开黑"的验收核心项缺失。
 * 新写的 `PlayerViews` 同时补上行（发送本机位姿）与下行（画远端身体），本脚本负责把它建起来。
 *
 * ## 位置选择
 * 建在 `TrySpawnPlayer` 里（`_player` 就在那创建），并挂在 **Player 根对象**上 ——
 * 与 `PlayerBody` 同一宿主，生命周期一致，不会出现"玩家没了视图还在"。
 * 同时把 HUD 摘要写进启动行，便于真机截图取证（本项目纪律：诊断信息必须能被截屏读到）。
 *
 * 用法：node tools/wire-player-views.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'unity/Assets/Scripts/Runtime/GameBootstrap.cs');
const checkOnly = process.argv.includes('--check');

const raw = fs.readFileSync(FILE, 'utf8');
let src = raw;
const log = [];
const fail = (m) => { console.error('[wire-pv] ✗ ' + m); process.exit(1); };
const sub = (from, to, what) => {
  const n = src.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  src = src.replace(from, to);
  log.push('  ✓ ' + what);
};

if (src.includes('PlayerViews')) { console.log('[wire-pv] 已接入，跳过'); process.exit(0); }

// ── ① 在 PlayerBody 之后建 PlayerViews ─────────────────────────────────
sub(`                var body = playerGo.AddComponent<PlayerBody>();
                body.Build(playerGo.transform);
                _playerBody = body;
                lines.AppendLine(body.Describe());`,
  `                var body = playerGo.AddComponent<PlayerBody>();
                body.Build(playerGo.transform);
                _playerBody = body;
                lines.AppendLine(body.Describe());

                // 联机玩家视图：上行发本机位姿 + 把远端玩家画出来。
                // 建在 Player 根对象上（与身体同宿主，生命周期一致）。
                // 没有它时联机两端各断一半：我动了对面收不到，对面动了我看不见。
                var views = playerGo.AddComponent<PlayerViews>();
                views.Initialize(_player);
                _playerViews = views;
                lines.AppendLine(views.Describe());`,
  '① 在 TrySpawnPlayer 里创建 PlayerViews');

// ── ② 字段与公开访问器 ────────────────────────────────────────────────
{
  const anchor = '        public void StartMatch()';
  const i = src.indexOf(anchor);
  if (i < 0) fail('未找到 StartMatch 锚点');
  const field = [
    '        /// <summary>联机玩家视图（上行发位姿 + 远端可见）。见 PlayerViews 的类注释。</summary>',
    '        PlayerViews _playerViews;',
    '',
    '        /// <summary>HUD/取证用：联机视图摘要（发了多少帧、看到几个远端）。</summary>',
    '        public string DescribePlayerViews() => _playerViews != null ? _playerViews.Describe() : "联机视图：未建";',
    '',
    '        /// <summary>理智系统可把 0..1 理智写进来，随位姿一起上报（联机同步用）。</summary>',
    '        public void ReportSanity01(float sanity01)',
    '        {',
    '            if (_playerViews != null) _playerViews.SetSanity01(sanity01);',
    '        }',
    '',
  ].join('\n');
  src = src.slice(0, i) + field + src.slice(i);
  log.push('  ✓ ② 新增 _playerViews 字段与访问器');
}

console.log('[wire-pv] 接进 PlayerViews' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
if (!checkOnly) {
  fs.writeFileSync(FILE, src, 'utf8');
  console.log(`  已写回：${raw.length} → ${src.length} 字节`);
  console.log('  ⚠ 必须跑：bash tools/unity-syntax-check.sh 与 bash tools/unity-tests.sh EditMode');
}
