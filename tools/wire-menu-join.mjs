#!/usr/bin/env node
/**
 * wire-menu-join.mjs — 菜单「加入房间」：唤起系统键盘输入房间码 → 直连主机
 *
 * ## 为什么
 * 用户要求「以跨地区联机为主」。零信令路线下在**跨地区加入的唯一入口就是房间码**
 * （朋友把码发给你，你输进去）。此前只有建房路径，没有加入入口 —— 那一半是断的。
 *
 * ## 实现要点（每条都对着既有的坑写的）
 * · **系统键盘**（`TouchScreenKeyboard`，桩已补）：房间码是 12/31 位 base32，
 *   自绘小键盘既丑又难点；且玩家多半是**从聊天软件复制**，系统键盘支持粘贴。
 * · **大小写不敏感**：聊天软件复制来的码大小写不可控，`RoomCode` 用大写字母表解码，
 *   故输入后统一 `ToUpperInvariant()`（这一条已在 `LanSession.TryJoin` 内做，这里保持一致）。
 * · **只在移动端弹键盘**（`Application.isMobilePlatform`）：编辑器/CI 弹键盘会把自动化卡住。
 * · **失败必须可读**：`LanSession.TryJoin` 返回的 `reason` 直接显示给玩家，
 *   不写"加入失败"四个字了事 —— 沉默失败是本项目最贵的调试成本。
 * · **意图要在开局前设**：`NetIntent = Join` + `NetJoinCode = 码`，再 `StartMatch()`，
 *   由 `GameBootstrap.ApplyNetIntentAtMatchStart()` 装配真联机（与建房同一条路径）。
 *
 * ## 交互入口（不占菜单板位）
 * `HallScene` 只有 6 个板位（`_noteCenter = new Vector3[6]`），而官方板面正是 6 项，
 * 故「加入房间」**不抢板位**，做成：**点顶部的操作提示条**（`_boardHint`）即进入加入流程。
 * 提示条文案同步改成可发现的形式。
 *
 * 用法：node tools/wire-menu-join.mjs [--check]
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
const fail = (m) => { console.error('[join] ✗ ' + m); process.exit(1); };
const sub = (from, to, what) => {
  const n = src.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  src = src.replace(from, to);
  log.push('  ✓ ' + what);
};

if (src.includes('StartJoinSession')) { console.log('[join] 已接入，跳过'); process.exit(0); }

// ── ① 加入流程 + 键盘读取（每帧轮询 active，关闭后提交）────────────────
{
  const anchor = '        void StartHostSession()';
  const i = src.indexOf(anchor);
  if (i < 0) fail('未找到 StartHostSession 锚点（先跑 wire-menu-host.mjs）');
  const Q = String.fromCharCode(34);
  const block = [
    '        /// <summary>系统键盘句柄（加入房间时用；null = 未在输入）。</summary>',
    '        TouchScreenKeyboard _joinKeyboard;',
    '        /// <summary>最近一次加入尝试的结果（HUD/取证可读）。</summary>',
    '        string _joinStatus = ' + Q + '-' + Q + ';',
    '',
    '        /// <summary>',
    '        /// 加入房间：唤起系统键盘让玩家粘贴/输入房间码。',
    '        /// 只在移动端弹键盘（编辑器/CI 弹键盘会卡住自动化）。',
    '        /// </summary>',
    '        void StartJoinSession()',
    '        {',
    '            if (Application.isMobilePlatform)',
    '            {',
    '                // ASCIICapable：房间码是 base32 大写字母+数字；autocorrect=false 避免系统改字',
    '                _joinKeyboard = TouchScreenKeyboard.Open(' + Q + Q + ', TouchScreenKeyboardType.ASCIICapable,',
    '                    false, false, false, false, ' + Q + '输入朋友给你的房间码' + Q + ');',
    '                _joinStatus = ' + Q + '等待输入房间码…' + Q + ';',
    '            }',
    '            else',
    '            {',
    '                Note(' + Q + '加入房间需要输入房间码（约 12 或 31 位，大写字母+数字）。' + Q + ');',
    '            }',
    '            SetStatusLine(_joinStatus);',
    '        }',
    '',
    '        /// <summary>键盘关闭后提交房间码：设意图 → 开局（装配在 ApplyNetIntentAtMatchStart 里做）。</summary>',
    '        void CommitJoin(string code)',
    '        {',
    '            var boot = GetComponent<GameBootstrap>();',
    '            if (boot == null) { SetStatusLine(' + Q + '组合根缺失：无法加入' + Q + '); return; }',
    '            var trimmed = (code ?? string.Empty).Trim().ToUpperInvariant();',
    '            if (trimmed.Length == 0) { SetStatusLine(' + Q + '没有输入房间码' + Q + '); return; }',
    '            boot.NetIntent = NetIntentKind.Join;',
    '            boot.NetJoinCode = trimmed;',
    '            StartMatch();',
    '            // 真实结果由 ApplyNetIntentAtMatchStart 写进 NetStatus（含失败原因与可达范围）',
    '            _joinStatus = ' + Q + '加入 ' + Q + ' + trimmed + ' + Q + '：' + Q + ' + (boot.NetStatus ?? ' + Q + '（无说明）' + Q + ');',
    '            ShowRoomCode(boot);',
    '        }',
    '',
    '        /// <summary>把一行状态写到板提示条（真机截图即可取证）。</summary>',
    '        void SetStatusLine(string s)',
    '        {',
    '            if (_boardHint != null && !string.IsNullOrEmpty(s)) _boardHint.text = s;',
    '        }',
    '',
    '        /// <summary>每帧轮询键盘：关闭（active=false）时提交。放在 Update 里调。</summary>',
    '        void PumpJoinKeyboard()',
    '        {',
    '            if (_joinKeyboard == null) return;',
    '            if (_joinKeyboard.active) return;                       // 仍在输入',
    '            var code = _joinKeyboard.text;                          // 关闭瞬间取内容',
    '            _joinKeyboard = null;',
    '            if (string.IsNullOrEmpty(code)) { SetStatusLine(' + Q + '已取消加入' + Q + '); return; }',
    '            CommitJoin(code);',
    '        }',
    '',
  ].join('\n');
  src = src.slice(0, i) + block + src.slice(i);
  log.push('  ✓ ① 新增 StartJoinSession / CommitJoin / PumpJoinKeyboard');
}

// ── ② Update 里轮询键盘 ────────────────────────────────────────────────
{
  const anchor = '        void Update()';
  const i = src.indexOf(anchor);
  if (i < 0) fail('未找到 MenuScene.Update');
  const brace = src.indexOf('{', i);
  if (brace < 0) fail('未找到 Update 的左大括号');
  src = src.slice(0, brace + 1) + '\n            PumpJoinKeyboard();   // 加入房间：键盘关闭即提交' + src.slice(brace + 1);
  log.push('  ✓ ② Update 里轮询键盘');
}

// ── ③ 提示条做成可点：进入加入流程 ────────────────────────────────────
{
  const anchor = '        bool HandleBoardInput(Vector2 screenPoint, bool clicked)';
  const i = src.indexOf(anchor);
  if (i < 0) fail('未找到 HandleBoardInput');
  const brace = src.indexOf('{', i);
  if (brace < 0) fail('未找到 HandleBoardInput 左大括号');
  const hook = [
    '',
    '            // 点击顶部提示条 = 加入房间（不占 6 个板位：官方板面正是 6 项，加第 7 项会挤掉一项）。',
    '            if (clicked && _boardHint != null && _boardHint.gameObject.activeInHierarchy',
    '                && _joinKeyboard == null)',
    '            {',
    '                var rt = _boardHint.rectTransform;',
    '                var c = RectTransformUtility.WorldToScreenPoint(null, rt.position);',
    '                var half = rt.sizeDelta * 0.5f;',
    '                var d = new Vector2(screenPoint.x - c.x, screenPoint.y - c.y);',
    '                if (Mathf.Abs(d.x) <= half.x && Mathf.Abs(d.y) <= half.y) { StartJoinSession(); return true; }',
    '            }',
  ].join('\n');
  src = src.slice(0, brace + 1) + hook + src.slice(brace + 1);
  log.push('  ✓ ③ 提示条点击 → 加入房间');
}

// ── ④ 提示条文案：让它看起来可点 ─────────────────────────────────────
sub('"按 空格 或 点击菜单板 进入操作界面",',
  '"按 空格 或 点击菜单板 进入操作界面　｜　点这里 = 输入房间码加入",',
  '④ 提示条文案加"加入"入口');

console.log('[join] 菜单加入房间流程' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
if (!checkOnly) {
  const bak = FILE + '.bak-join';
  if (!fs.existsSync(bak)) fs.writeFileSync(bak, raw, 'utf8');
  fs.writeFileSync(FILE, src, 'utf8');
  console.log(`  已写回：${raw.length} → ${src.length} 字节（备份 ${path.basename(bak)}）`);
}
