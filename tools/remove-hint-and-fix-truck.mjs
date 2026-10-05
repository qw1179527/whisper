#!/usr/bin/env node
/**
 * remove-hint-and-fix-truck.mjs — 用户第 2、3 条
 *
 * ## 用户原话
 * 「2. 我说过去除所有小字，**不留字体**」「3. 谁家好人把车放在这里，建模还这么丑」
 *
 * ## 处理一：菜单板提示条（最后一条中央小字）→ 删掉
 * `MenuScene` 里的 `_boardHint`（`按 空格 或 点击菜单板 进入操作界面　｜　点这里 = 输入房间码加入`）
 * 是屏幕上最后一块常驻文字，位于**正中央**（`anchoredPosition = (0, -240)`）。
 * 用户要"不留字体" → 不再创建它。
 * ⚠ **连带影响**：它同时是"点这里 = 输入房间码加入"的**点击热区**（`HandleBoardInput` 里判它）。
 * 删除后点击加入的入口消失 —— 但加入现在已由**多人面板**承担（`OpenMultiplayerPanel`：
 * 「多人联机」→ 按 J 加入），功能不丢，只是不再靠"去点一行字"。
 *
 * ## 处理二：货车摆放（官方大厅形态）
 * 用户说"谁家好人把车放在这里" —— 我原先把它放在 `x = -WidthM * 0.30f, z = -LengthM * 0.08f`，
 * 即**大厅中偏左、靠近菜单板那面墙**，正好挡住主视野。
 *
 * 官方大厅（用户《补充说明》§4：工业风两层仓库；§8：货车是大厅里的移动基地）里，货车停在
 * **仓库一侧的卷帘门位**、车尾朝向玩家活动区，玩家出生在货车后方、面向菜单板。
 * 故本脚本改为：
 * · 沿**右后侧**停放（x 偏右、z 靠后墙），把车头朝 +Z 改为朝 **-Z**（车头对后墙，车尾朝场内）；
 * · 抬离菜单板所在的前墙，不再挡主视野。
 *
 * 用法：node tools/remove-hint-and-fix-truck.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const checkOnly = process.argv.includes('--check');
const log = [];
const fail = (m) => { console.error('[hint-truck] ✗ ' + m); process.exit(1); };

function edit(rel, pairs, tag) {
  const p = path.join(ROOT, rel);
  let s = fs.readFileSync(p, 'utf8');
  for (const [from, to] of pairs) {
    const n = s.split(from).length - 1;
    if (n !== 1) fail(`${rel}：锚点命中 ${n} 次（应为 1）→ ${from.slice(0, 60)}`);
    s = s.replace(from, to);
  }
  if (!checkOnly) fs.writeFileSync(p, s, 'utf8');
  log.push('  ✓ ' + tag);
}

// ── ① 不再创建板提示条 ────────────────────────────────────────────────
edit('unity/Assets/Scripts/Runtime/MenuScene.cs', [[
  '            _boardHint = SceneMaterials.Label(_canvas.transform, "BoardHint",',
  [
    '            // 【用户 2026-10-05：「去除所有小字，不留字体」】',
    '            // 原先这里建 `BoardHint`（"按 空格 或 点击菜单板 进入操作界面　｜　点这里 = 输入房间码加入"），',
    '            // 它位于屏幕**正中央**（anchoredPosition 0,-240），是最后一块常驻文字 → 不再创建。',
    '            // ⚠ 连带影响：它同时是"点击加入"的热区；删除后加入改由**多人面板**承担',
    '            //（「多人联机」→ 按 J 加入，见 OpenMultiplayerPanel），功能不丢，只是不再"去点一行字"。',
    '            _boardHint = null;   // 明确置空：HandleBoardInput 里对它有 null 检查',
    '            /* 原实现（保留备查，勿删注释）：',
    '            _boardHint = SceneMaterials.Label(_canvas.transform, "BoardHint",',
  ].join('\n'),
]], '① 不再创建板提示条（连同其点击热区）');
// 收尾：把原实现整段注释掉
edit('unity/Assets/Scripts/Runtime/MenuScene.cs', [[
  '            _boardHint.rectTransform.anchoredPosition = new Vector2(0f, -240f);',
  '            _boardHint.rectTransform.anchoredPosition = new Vector2(0f, -240f);\n            */',
]], '①b 闭合注释块');

// ── ② 货车移到仓库侧后方（车头朝后墙，车尾朝场内）────────────────────
edit('unity/Assets/Scripts/Runtime/HallScene.cs', [[
  '            float tx = -WidthM * 0.30f;\n            float tz = -LengthM * 0.08f;\n            Truck = new TruckScene(_root, new Vector3(tx, 0f, tz), 90f, bodyMat, metalMat, glassMat);',
  [
    '            // 【用户 2026-10-05：「谁家好人把车放在这里」】',
    '            // 原先放在 x=-0.30W、z=-0.08L —— 大厅中偏左、紧邻菜单板那面前墙，正好挡住主视野。',
    '            // 官方大厅（《补充说明》§4 工业风两层仓库 + §8 货车是移动基地）里，货车停在',
    '            // **仓库一侧的卷帘门位**、车尾朝玩家活动区。故改为：沿右后侧停放、车头朝后墙（-Z），',
    '            // 车尾朝场内 —— 玩家出生在货车后方、面向菜单板，货车成为"身后的基地"而不是"眼前的路障"。',
    '            float tx = WidthM * 0.30f;              // 右侧（商店电脑一侧），避开菜单板主视野',
    '            float tz = LengthM * 0.22f;             // 靠后墙（+Z 侧），前墙（-Z）留给菜单板',
    '            Truck = new TruckScene(_root, new Vector3(tx, 0f, tz), 180f, bodyMat, metalMat, glassMat);',
  ].join('\n'),
]], '② 货车移到仓库右后侧、车头朝后墙');

console.log('[hint-truck] 删提示条 + 挪货车' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
console.log('  ⚠ 货车外观"丑"需要重做建模，属独立一轮（见 docs/modeling-redo-plan.md）');
