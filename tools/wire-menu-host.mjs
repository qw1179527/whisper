#!/usr/bin/env node
/**
 * wire-menu-host.mjs — 菜单板「多人联机」真的建房，并显示房间码
 *
 * ## 缺陷
 * 上一轮把真联机服务接进了组合根（GameBootstrap），但**菜单没有入口改意图**：
 * `MenuScene.ActivateBoardItem` 的 `case 1` 走旧的 `OnOption(5, …)`，它不动 `NetIntent`
 * → 意图永远 Solo → 真机表现是"点了多人联机却仍是单机"。
 * 且建房后**那串码没有地方显示**（官方显示在菜单板右上角），玩家无从转发给朋友。
 *
 * ## 本脚本做四件事（都不需要新 API，故可立刻过双门禁）
 * 1. `case 1` → `StartHostSession()`：设 `NetIntent = Host` → `StartMatch()` → 显示房间码；
 * 2. 新增 `StartHostSession()` / `ShowRoomCode()`；
 * 3. 新增常驻文字 `_roomCodeText`（右上、默认隐藏）；
 * 4. 板模式显隐时**保留**房间码（否则退出操作视角就丢了要转发的码）。
 *
 * ## 为什么"加入"不在这里
 * 加入要输入 12/31 位房间码 → 需移动端系统键盘 `TouchScreenKeyboard`，
 * 而离线桩里**没有这个类型**（实测 grep 无命中），先加会让语法门禁红。故加入单独一轮。
 *
 * ## 写法纪律（本轮踩坑）
 * 锚点**不要包含注释里的中文引号** —— 我第一版把 `"创建房间"` 的引号写成 ASCII 双引号，
 * 导致脚本自身语法错。锚点一律取短、唯一、不含引号的片段。
 *
 * 用法：node tools/wire-menu-host.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
const checkOnly = process.argv.includes('--check');

const raw = fs.readFileSync(FILE, 'utf8');
let src = raw;
const log = [];
const fail = (m) => { console.error('[wire-menu] ✗ ' + m); process.exit(1); };
const sub = (from, to, what) => {
  const n = src.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  src = src.replace(from, to);
  log.push('  ✓ ' + what);
};

if (src.includes('_roomCodeText')) { console.log('[wire-menu] 已接入，跳过'); process.exit(0); }

// ── ① 板项 1 改为真的建房（锚点短、唯一、无引号）──────────────────────
sub('case 1: OnOption(5, BoardItemTitles[1]); break;',
  'case 1: StartHostSession(); break;                // 多人联机 = 建房（真起 UDP 监听）',
  '① 板项 1 改为 StartHostSession()');

// ── ② 建房与显示房间码 ────────────────────────────────────────────────
{
  const anchor = '        void StartMatch()';
  const i = src.indexOf(anchor);
  if (i < 0) fail('未找到 StartMatch 锚点');
  const Q = String.fromCharCode(34);
  const block = [
    '        /// <summary>',
    '        /// 建房（菜单板「多人联机」）：设意图 → 开局 → 显示房间码。',
    '        /// 顺序关键：`NetIntent` 必须在 `StartMatch()` **之前**设好，',
    '        /// 因为 `GameBootstrap.OnMenuStartRequested()` 会按当时的意图装配联机服务。',
    '        /// </summary>',
    '        void StartHostSession()',
    '        {',
    '            var boot = GetComponent<GameBootstrap>();',
    '            if (boot == null) { Note("组合根缺失：无法建房"); return; }',
    '            boot.NetIntent = NetIntentKind.Host;',
    '            StartMatch();',
    '            ShowRoomCode(boot);',
    '        }',
    '',
    '        /// <summary>',
    '        /// 把房间码显示在菜单板右上（官方形态）。',
    '        /// 失败要说清**为什么**（没有局域网地址 / 端口被占）——',
    '        /// 沉默的「建房按钮点了没反应」是本项目最贵的调试成本。',
    '        /// </summary>',
    '        void ShowRoomCode(GameBootstrap boot)',
    '        {',
    '            if (_roomCodeText == null) return;',
    '            var code = boot.NetRoomCode;',
    '            if (string.IsNullOrEmpty(code))',
    '            {',
    '                _roomCodeText.text = ' + Q + '建房未成功\\n' + Q + ' + (boot.NetStatus ?? ' + Q + '（无说明）' + Q + ');',
    '                _roomCodeText.color = new Color(1f, 0.72f, 0.6f);',
    '            }',
    '            else',
    '            {',
    '                _roomCodeText.text = ' + Q + '房间码\\n' + Q + ' + code + ' + Q + '\\n（发给朋友，让他输入这个码加入）' + Q + ';',
    '                _roomCodeText.color = new Color(0.75f, 1f, 0.8f);',
    '            }',
    '            _roomCodeText.gameObject.SetActive(true);',
    '            Note(_roomCodeText.text);',
    '        }',
    '',
  ].join('\n');
  src = src.slice(0, i) + block + src.slice(i);
  log.push('  ✓ ② 新增 StartHostSession / ShowRoomCode');
}

// ── ③ 房间码文字字段 ──────────────────────────────────────────────────
{
  const anchor = 'readonly System.Collections.Generic.List<Text> _boardItems = new System.Collections.Generic.List<Text>();';
  sub(anchor,
    anchor + '\n\n        /// <summary>房间码显示（仅建房成功后可见；官方在菜单板右上角）。</summary>\n        Text _roomCodeText;',
    '③ 新增 _roomCodeText 字段');
}

// ── ④ 建立 _roomCodeText（紧跟 IdCardText 之后）────────────────────────
{
  const anchor = '_idCardText.color';
  const i = src.indexOf(anchor);
  if (i < 0) fail('未找到 IdCardText 建立处');
  let lineEnd = -1;
  for (let j = i; j < src.length; j++) { if (src[j] === '\n') { lineEnd = j; break; } }
  if (lineEnd < 0) fail('未找到 IdCardText 语句结尾');
  const Q = String.fromCharCode(34);
  const insert = [
    '',
    '            // 房间码：建房成功后显示（默认隐藏，避免单人时占屏）。位置比 ID 卡低一档。',
    '            _roomCodeText = SceneMaterials.Label(_canvas.transform, ' + Q + 'RoomCodeText' + Q + ', ' + Q + Q + ',',
    '                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -230f), 30, TextAnchor.UpperRight);',
    '            _roomCodeText.gameObject.SetActive(false);',
  ].join('\n');
  src = src.slice(0, lineEnd) + insert + src.slice(lineEnd);
  log.push('  ✓ ④ 建 _roomCodeText（右上、默认隐藏）');
}

// ── ⑤ 板显隐：空文本才隐藏（有码则保留）──────────────────────────────
{
  const anchor = 'foreach (var t in _boardItems) if (t != null) t.gameObject.SetActive(show);';
  sub(anchor,
    anchor + '\n' +
    '            // 房间码一旦有内容就**不随板隐藏**：玩家退出菜单板后要把码转发给朋友。\n' +
    '            if (_roomCodeText != null && !show && string.IsNullOrEmpty(_roomCodeText.text)) _roomCodeText.gameObject.SetActive(false);',
    '⑤ 板显隐时保留房间码');
}

console.log('[wire-menu] 菜单板建房入口 + 房间码显示' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
if (!checkOnly) {
  const bak = FILE + '.bak-menu-host';
  if (!fs.existsSync(bak)) fs.writeFileSync(bak, raw, 'utf8');
  fs.writeFileSync(FILE, src, 'utf8');
  console.log(`  已写回：${raw.length} → ${src.length} 字节`);
  console.log('  ⚠ 必须跑：bash tools/unity-syntax-check.sh 与 bash tools/unity-tests.sh EditMode');
}
