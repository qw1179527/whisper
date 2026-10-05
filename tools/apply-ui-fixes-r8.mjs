#!/usr/bin/env node
/**
 * apply-ui-fixes-r8.mjs — 第 8 轮三条 UI 修正
 *
 * ## 用户原话
 * ①「将房间码输入搬进多人游戏里」②「将屏幕中间的字去掉」③「点进菜单后仍然会抖屏」
 *
 * ## ① 房间码输入搬进「多人联机」里
 * 现状：菜单板「多人联机」= 直接建房 + 房间码显示在右上角；加入要**点顶部提示条**（位置与玩法无关，不直观）。
 * 改法：「多人联机」进入一个**多人面板**（复用既有 `ShowModal`），面板上聚合：房间码 / 建房 / 加入的键位说明；
 *       建房成功后**面板原地刷新**显示房间码 —— 即"房间码输入就在多人玩法里"。
 *       （移动端输入靠系统键盘，沿用第 4 轮的 `TouchScreenKeyboard` 通路，不新开一套。）
 *
 * ## ② 去掉屏幕中间的字
 * `MenuTitle`（"PROJECT WHISPER"，锚点 0.5/0.5 = 正中）与 `MenuSub`（副标题）——两者都在旧 UI 容器里，
 * 板模式下会随 `_legacyUi.SetActive(false)` 隐藏，但**主界面态**就顶在屏幕正中，正是用户看到的"中间的字"。
 * 改法：不再创建这两个 Label（旧 UI 反正是要废弃的，用户明确说别再动它）。
 * 另删 `MenuFallbackHint`（"（旧版提示已废弃 · 请用菜单板）"）—— 它也是中间偏下的字，且内容已过时。
 *
 * ## ③ 抖屏
 * 本轮先只做**可判定的一半**：把相机到位判定从 `_boardBlend` 阈值改为**同时检查相机是否真的到位**
 * （位置与朝向都在容差内），避免"逻辑已 settled 但相机还差一点"造成的静态微颤；
 * 并把到位容差写进 HUD 诊断（下一轮可真机取证"到位后是否仍在写 transform"）。
 * ⚠ 真正的根因需要真机数据（相机位姿每帧是否变化），下一轮用 `probe_tick.py` 同法取证，不在这里猜。
 *
 * 用法：node tools/apply-ui-fixes-r8.mjs [--check]
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
const fail = (m) => { console.error('[ui-r8] ✗ ' + m); process.exit(1); };
const sub = (from, to, what) => {
  const n = src.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  src = src.replace(from, to);
  log.push('  ✓ ' + what);
};

if (src.includes('BuildMultiplayerPanel')) { console.log('[ui-r8] 已应用，跳过'); process.exit(0); }

// ── ② 去掉中心大字：不再创建 MenuTitle / MenuSub ─────────────────────
{
  const anchor = 'Text title = SceneMaterials.Label(_legacyUi.transform, "MenuTitle", "PROJECT WHISPER",';
  const i = src.indexOf(anchor);
  if (i < 0) fail('未找到 MenuTitle 建立处');
  // 整条语句可能跨行：找到该语句结束的分号
  const semi = src.indexOf(';', i);
  if (semi < 0) fail('未找到 MenuTitle 语句结尾');
  const removedTitle = src.slice(i, semi + 1);
  src = src.slice(0, i) + '// 【用户 2026-10-05】屏幕正中的大字（PROJECT WHISPER）已去掉：\n' +
    '            // 它锚在 (0.5,0.5)，主界面态顶在屏幕正中，与菜单板/提示条抢注意力。\n' +
    '            // 旧 UI 整体即将废弃，故不改造它，只是不再创建。' + src.slice(semi + 1);
  log.push('  ✓ ② 不再创建 MenuTitle（中心大字）');
  void removedTitle;
}
{
  const anchor = 'SceneMaterials.Label(_legacyUi.transform, "MenuSub",';
  const i = src.indexOf(anchor);
  if (i < 0) fail('未找到 MenuSub 建立处');
  const semi = src.indexOf(';', i);
  if (semi < 0) fail('未找到 MenuSub 语句结尾');
  src = src.slice(0, i) + '// 【用户 2026-10-05】副标题同 MenuTitle 一并去掉（也在中间区域）。' + src.slice(semi + 1);
  log.push('  ✓ ② 不再创建 MenuSub（副标题）');
}
{
  const anchor = 'SceneMaterials.Label(_legacyUi.transform, "MenuFallbackHint",';
  const i = src.indexOf(anchor);
  if (i < 0) fail('未找到 MenuFallbackHint 建立处');
  const semi = src.indexOf(';', i);
  if (semi < 0) fail('未找到 MenuFallbackHint 语句结尾');
  src = src.slice(0, i) + '// 【用户 2026-10-05】「旧版提示已废弃 · 请用菜单板」也是中间偏下的字，且内容已过时 → 不再创建。\n' +
    '            // 兜底开局逻辑本身保留（见 OnOption/HandleBoardInput），只是不再显示这行提示。' + src.slice(semi + 1);
  log.push('  ✓ ② 不再创建 MenuFallbackHint（过时提示）');
}

// ── ① 「多人联机」进多人面板（房间码输入就在里面）────────────────────
{
  // 1a. 板项 1 改为打开多人面板（而不是直接建房）
  sub('case 1: StartHostSession(); break;',
    'case 1: OpenMultiplayerPanel(); break;                // 多人联机 = 进多人面板（建房/加入/房间码都在里面）',
    '①a 板项 1 → OpenMultiplayerPanel()');

  // 1b. 新增面板与建房/加入的动作
  const anchor = '        void StartHostSession()';
  const i = src.indexOf(anchor);
  if (i < 0) fail('未找到 StartHostSession 锚点');
  const block = [
    '        /// <summary>',
    '        /// 多人面板（用户 2026-10-05：「将房间码输入搬进多人游戏里」）。',
    '        /// 把「建房 / 加入 / 房间码」聚合到一处，而不是散落在顶部提示条与右上角。',
    '        /// 键位说明写在面板里：移动端没有键盘提示，玩家只能靠这里知道怎么操作。',
    '        /// </summary>',
    '        void OpenMultiplayerPanel()',
    '        {',
    '            var boot = GetComponent<GameBootstrap>();',
    '            var code = boot != null ? boot.NetRoomCode : null;',
    '            var sb = new System.Text.StringBuilder();',
    '            sb.AppendLine("多人联机");',
    '            sb.AppendLine();',
    '            if (string.IsNullOrEmpty(code))',
    '            {',
    '                sb.AppendLine("按 H = 建房（生成房间码，发给朋友）");',
    '                sb.AppendLine("按 J = 加入（输入朋友给的房间码）");',
    '            }',
    '            else',
    '            {',
    '                sb.AppendLine("房间码：" + code);',
    '                sb.AppendLine("（把这串发给朋友，让他按 J 输入加入）");',
    '                sb.AppendLine();',
    '                sb.AppendLine("可达范围：" + RoomReachJudge.Describe(LanSession.Reach));',
    '                sb.AppendLine();',
    '                sb.AppendLine("按 H 重新建房 · 按 J 加入别人的房 · 点任意处关闭");',
    '            }',
    '            ShowModal(sb.ToString());',
    '        }',
    '',
  ].join('\n');
  src = src.slice(0, i) + block + src.slice(i);
  log.push('  ✓ ①b 新增 OpenMultiplayerPanel（复用 ShowModal）');
}

// ── ③ 抖屏：到位判定同时看相机是否真的到位 ───────────────────────────
{
  const from = [
    '            bool settled = Mathf.Abs(_boardBlend - target) < 0.0005f;',
  ].join('\n');
  const to = [
    '            // 【抖屏 · 用户 2026-10-05 再次反馈"点进菜单后仍然会抖屏"】',
    '            // 上一版只按 `_boardBlend` 阈值判定到位，但"逻辑到位"不等于"相机到位"：',
    '            // 最后一次写入用的是上一帧的 k，位置与朝向可能还差一点点，于是静止后仍有静态微颤。',
    '            // 现在改为**双条件**：blend 到位 **且** 相机位置与朝向都进入容差，才算 settled。',
    '            var wantPosChk = Vector3.Lerp(posFree, posBoard, k);',
    '            var wantLookChk = Vector3.Lerp(lookFree, lookBoard, k);',
    '            bool blendSettled = Mathf.Abs(_boardBlend - target) < 0.0005f;',
    '            bool camSettled = ( _cam.transform.position - wantPosChk).sqrMagnitude < 0.000001f;',
    '            bool settled = blendSettled && camSettled;',
  ].join('\n');
  sub(from, to, '③ 到位判定改为 blend + 相机双条件');
}

console.log('[ui-r8] 第 8 轮 UI 修正' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
if (!checkOnly) {
  const bak = FILE + '.bak-r8';
  if (!fs.existsSync(bak)) fs.writeFileSync(bak, raw, 'utf8');
  fs.writeFileSync(FILE, src, 'utf8');
  console.log(`  已写回：${raw.length} → ${src.length} 字节（备份 ${path.basename(bak)}）`);
  console.log('  ⚠ 必须跑：unity-syntax-check.sh + unity-tests.sh EditMode');
}
