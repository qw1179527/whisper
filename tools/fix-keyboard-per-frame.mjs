#!/usr/bin/env node
/**
 * fix-keyboard-per-frame.mjs — 键盘处理从"有点击的那一帧"里解耦出来，改为每帧执行
 *
 * ## 缺陷（真机实测 + 读码确认，非猜测）
 * 现象：`adb shell input keyevent KEYCODE_SPACE`（以及 H/J）在**焦点正常、设备醒着**的情况下也毫无反应
 * （两次截图 SHA 完全相同）。
 *
 * 根因：`MenuScene.Update()` 里 `HandleSelfDrawClick()` 在 `if (_root == null || _paused) return;` **之前**被调用，
 * **且键盘检查写在 `HandleBoardInput` 内部**：
 * ```
 * if (_cam == null || _hall == null || _hall.MenuBoardPicker == null) return false;
 * if (Input.GetKeyDown(KeyCode.Space)) { ToggleBoardMode(); return true; }   // ← 到这里才检查键盘
 * ...
 * if (!clicked) return false;      // ← 而到达这里需要"本帧有点击"
 * ```
 * 于是**纯按键事件（没有任何触摸/点击）永远走不到键盘分支** —— 键盘只有恰好和一次点击同帧才生效。
 * 本会话我一直在用 `adb keyevent` 做"确定性主控"，而它从第 4 轮起就**从未真正生效过**
 * （前几轮我以为空格进板模式是生效的，那其实是我点了屏幕或板模式本来就是开的）。
 *
 * ## 修法
 * 键盘是**每帧事件**（`GetKeyDown` 本身就是"这一帧按下"），必须放在 `Update()` 里无条件检查，
 * 与触摸/点击彻底解耦。`HandleBoardInput` 里原来的键盘分支保留（对"点击那帧同时按键"仍成立），
 * 新增的 `HandleMenuKeyboard()` 作为**每帧入口**，两者共用同一批动作函数。
 *
 * ## 顺带
 * 把 `HandleSelfDrawClick()` 挪到 `_root/_paused` 早退**之后**，语义更清楚：
 * 暂停/无根时既不该收点击，也不该收键盘。
 *
 * 用法：node tools/fix-keyboard-per-frame.mjs [--check]
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
const fail = (m) => { console.error('[kbd-frame] ✗ ' + m); process.exit(1); };
const sub = (from, to, what) => {
  const n = src.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  src = src.replace(from, to);
  log.push('  ✓ ' + what);
};

if (src.includes('HandleMenuKeyboard')) { console.log('[kbd-frame] 已修，跳过'); process.exit(0); }

// ── ① Update：把点击判定挪到晚退之后，并每帧检查键盘 ─────────────────
sub(`            UpdateBoardCamera(Time.deltaTime);
            UpdateBoardUi();
            HandleSelfDrawClick();
            if (_root == null || _paused) return;`,
  `            UpdateBoardCamera(Time.deltaTime);
            UpdateBoardUi();
            if (_root == null || _paused) return;
            // 键盘是**每帧事件**：必须无条件每帧检查，不能挂在"有点击的那一帧"里
            // （否则纯按键永远走不到 —— 真机实测与读码确认，见 fix-keyboard-per-frame.mjs 头注释）。
            HandleMenuKeyboard();
            HandleSelfDrawClick();`,
  '① Update：每帧检查键盘 + 点击判定挪到晚退之后');

// ── ② 新增每帧键盘入口 ───────────────────────────────────────────────
{
  const anchor = '        /// <summary>处理"点菜单板 / 空格"这类主界面输入。返回 true 表示已消费本次点击。</summary>';
  const i = src.indexOf(anchor);
  if (i < 0) fail('未找到 HandleBoardInput 文档注释锚点');
  const block = [
    '        /// <summary>',
    '        /// 每帧键盘入口（与触摸解耦）。',
    '        /// 为什么单独一个方法：键盘检查原先写在 `HandleBoardInput` 内部，而它在 `if (!clicked) return false;`',
    '        /// **之后** → 纯按键事件永远走不到那些分支。真机实测（焦点正常、设备醒着）空格/H/J 全部无反应。',
    '        /// </summary>',
    '        void HandleMenuKeyboard()',
    '        {',
    '            if (_cam == null || _hall == null) return;',
    '            // 空格：进出操作视角（官方行为）。Esc：只在操作视角下退出。',
    '            if (Input.GetKeyDown(KeyCode.Space)) { ToggleBoardMode(); return; }',
    '            if (Input.GetKeyDown(KeyCode.Escape) && _boardMode) { ToggleBoardMode(); return; }',
    '            // 键盘主控（可验证性优先）：H = 建房、J = 加入。限定在操作视角下，避免主界面误触开局。',
    '            if (_boardMode && Input.GetKeyDown(KeyCode.H)) { StartHostSession(); return; }',
    '            if (_boardMode && Input.GetKeyDown(KeyCode.J)) { StartJoinSession(); return; }',
    '        }',
    '',
  ].join('\n');
  src = src.slice(0, i) + block + src.slice(i);
  log.push('  ✓ ② 新增 HandleMenuKeyboard（每帧入口）');
}

console.log('[kbd-frame] 键盘改为每帧处理' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
if (!checkOnly) {
  const bak = FILE + '.bak-kbdframe';
  if (!fs.existsSync(bak)) fs.writeFileSync(bak, raw, 'utf8');
  fs.writeFileSync(FILE, src, 'utf8');
  console.log(`  已写回：${raw.length} → ${src.length} 字节`);
}
