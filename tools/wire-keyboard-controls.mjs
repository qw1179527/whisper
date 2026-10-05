#!/usr/bin/env node
/**
 * wire-keyboard-controls.mjs — 加"键盘主控"：H = 建房、J = 加入房间
 *
 * ## 为什么（这是被真机取证逼出来的设计）
 * 本会话在真机上反复验证「点菜单板纸片」这条路，撞到三层不确定：
 *   ① 纸片命中要先射线命中共用的菜单板拾取面，落点稍偏就落到**兜底开局**（`MenuScene:1298`，直接开单人对局）；
 *   ② `adb shell input tap` 的投递被 ColorOS 时通时不通；
 *   ③ 判定"是否被业务接住"只能靠 HUD 回执，而回执是小字，取证成本高。
 * 而**键盘事件（`adb shell input keyevent`）在这台设备上稳定可用**（空格进板、Esc 退出都验证过）。
 * 于是把"建房/加入"也接到键盘上：**一条 `keyevent` 就能确定性触发**，把"人机交互不确定性"与
 * "联机功能是否正确"彻底解耦 —— 这是可验证性的问题，不是玩法设计问题。
 *
 * ## 与官方行为的关系
 * 官方桌面版本来就用键盘（空格开菜单板、Esc 退出）。本工程是移动端，键盘入口是**调试与验证通道**，
 * 不改变触摸路径；菜单板纸片点击仍然照旧工作。
 *
 * ## 键位选择
 * · `H` = Host 建房（H for Host）· `J` = Join 加入（J for Join）
 * · 都要求**在操作视角（板模式）**下按，避免主界面误触直接开局。
 *
 * ## 纪律
 * `KeyCode` 离线桩里原本只有 `None/Escape/Space/E` → 需先补 `H`/`J`（本脚本一并补）。
 *
 * 用法：node tools/wire-keyboard-controls.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const checkOnly = process.argv.includes('--check');
const MENU = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
const STUB = path.join(ROOT, 'native/unity-stubs/UnityStubs.cs');

const log = [];
const fail = (m) => { console.error('[kbd-ctl] ✗ ' + m); process.exit(1); };

// ── ① 补 KeyCode.H / KeyCode.J（ASCII：H=104, J=106）─────────────────
{
  const raw = fs.readFileSync(STUB, 'utf8');
  const from = 'public enum KeyCode { None = 0, Escape = 27, Space = 32, E = 101 }';
  if (raw.includes('H = 104')) { log.push('  · KeyCode.H/J 已存在，跳过'); }
  else {
    const n = raw.split(from).length - 1;
    if (n !== 1) fail(`KeyCode 枚举锚点命中 ${n} 次（应为 1）`);
    const to = 'public enum KeyCode { None = 0, Escape = 27, Space = 32, E = 101, H = 104, J = 106 }';
    if (!checkOnly) fs.writeFileSync(STUB, raw.replace(from, to), 'utf8');
    log.push('  ✓ KeyCode 加 H=104 / J=106');
  }
}

// ── ② MenuScene：板模式下按 H/J 触发建房/加入 ─────────────────────────
{
  const raw = fs.readFileSync(MENU, 'utf8');
  if (raw.includes('KeyCode.H')) { console.log('[kbd-ctl] MenuScene 已接入，跳过'); process.exit(0); }
  const from = [
    '            // 键盘：空格进出（官方行为）。Esc 只用于"退出操作视角"。',
    '            if (Input.GetKeyDown(KeyCode.Space)) { ToggleBoardMode(); return true; }',
    '            if (Input.GetKeyDown(KeyCode.Escape) && _boardMode) { ToggleBoardMode(); return true; }',
  ].join('\n');
  const to = [
    '            // 键盘：空格进出（官方行为）。Esc 只用于"退出操作视角"。',
    '            if (Input.GetKeyDown(KeyCode.Space)) { ToggleBoardMode(); return true; }',
    '            if (Input.GetKeyDown(KeyCode.Escape) && _boardMode) { ToggleBoardMode(); return true; }',
    '',
    '            // ── 键盘主控（可验证性优先，见 wire-keyboard-controls.mjs 头注释）──',
    '            // 为什么需要：真机点击有不确定（纸片命中要射线命中共用拾取面，偏了就落兜底开局；',
    '            // adb tap 投递被 ColorOS 时通时不通），而 keyevent 稳定可用。',
    '            // 把"建房/加入"接到键盘上，就能用一条命令确定性验证联机功能本身。',
    '            // 限定在操作视角（_boardMode）下，避免主界面误触直接开局。',
    '            if (_boardMode && Input.GetKeyDown(KeyCode.H)) { StartHostSession(); return true; }',
    '            if (_boardMode && Input.GetKeyDown(KeyCode.J)) { StartJoinSession(); return true; }',
  ].join('\n');
  const n = raw.split(from).length - 1;
  if (n !== 1) fail(`MenuScene 键盘锚点命中 ${n} 次（应为 1）`);
  if (!checkOnly) fs.writeFileSync(MENU, raw.replace(from, to), 'utf8');
  log.push('  ✓ MenuScene 加 H=建房 / J=加入（仅板模式下生效）');
}

console.log('[kbd-ctl] 键盘主控' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
console.log('  提示：出包后可用 `adb shell input keyevent 36`(H) / `38`(J) 确定性触发');
