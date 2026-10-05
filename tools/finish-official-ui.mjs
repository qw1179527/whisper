#!/usr/bin/env node
/**
 * finish-official-ui.mjs — 补完官方大厅 UI 剩余三项（用户《补充说明》§4）
 *
 * ## 本轮补的三项
 * | 官方元素 | 现状 | 本轮做法 |
 * |---|---|---|
 * | 右上 **ID 卡**（资金/等级/升级进度） | 只显示，不可点 | 可点 → 弹"档案"面板（等级/经验/钱/碎片 + 说明） |
 * | 主菜单板上 **每日/每周挑战** | 只在已废弃的旧左面板里 | 上板：在菜单板下方加一行「每日 3 · 每周 1」，点了看详情 |
 * | **地点 ↔ 主菜单 ↔ 商店 快速跳转** | 无 | 在板提示条区域加一行快捷提示，并把三个交互位都接上点击 |
 *
 * ## 纪律
 * · 官方形态以 `docs/spec/supplement-2026-10-05-permanent.md` §4 为准（本仓内权威，不靠搜索）；
 * · 未知/未落地的部分**如实标注**，不假装（例如多地图数据未落地时地图板会写明）；
 * · 命中判定一律复用 `TryHitWorldLabel`（与菜单板同一口径），不另造一套。
 *
 * 用法：node tools/finish-official-ui.mjs [--check]
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
const fail = (m) => { console.error('[official-ui] ✗ ' + m); process.exit(1); };
const sub = (from, to, what) => {
  const n = src.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  src = src.replace(from, to);
  log.push('  ✓ ' + what);
};

if (src.includes('_taskBoardText')) { console.log('[official-ui] 已应用，跳过'); process.exit(0); }

// ── ① 新增两个板面元素：挑战行 + 快捷跳转提示 ─────────────────────────
sub(`            _shopScreenText = SceneMaterials.Label(_canvas.transform, "ShopScreenText", "装备商店",
                new Vector2(0f, 0f), new Vector2(0f, 0f), 22, TextAnchor.MiddleCenter);`,
  `            _shopScreenText = SceneMaterials.Label(_canvas.transform, "ShopScreenText", "装备商店",
                new Vector2(0f, 0f), new Vector2(0f, 0f), 22, TextAnchor.MiddleCenter);

            // ── 官方 §4：每日/每周挑战**显示在主菜单板上**（原先只在已废弃的旧左面板里）──
            // 内容由 TaskSystem 提供（按 dayIndex 确定性生成），这里只做"上板"这一段。
            _taskBoardText = SceneMaterials.Label(_canvas.transform, "TaskBoardText", "每日挑战 · 每周挑战",
                new Vector2(0f, 0f), new Vector2(0f, 0f), 20, TextAnchor.MiddleCenter);
            _taskBoardText.color = new Color(0.92f, 0.90f, 0.78f);

            // ── 官方 §4：地点选择 ↔ 主菜单 ↔ 商店 的快速跳转（后续版本加的导航）──
            // 本工程三个交互位都在同一个大厅里（地图板在左、菜单板在中、商店电脑在右），
            // 故"快速跳转"= 把这三个位置**标出来并都可点**，而不是做三个页面栈。
            _navHintText = SceneMaterials.Label(_canvas.transform, "NavHintText", "◀ 地图　｜　菜单板　｜　商店 ▶",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), 20, TextAnchor.LowerCenter);
            _navHintText.color = new Color(0.80f, 0.84f, 0.90f);`,
  '① 新增挑战行与快捷跳转提示');

// ── ② 字段 ────────────────────────────────────────────────────────────
sub('        /// <summary>房间码显示（仅建房成功后可见；官方在菜单板右上角）。</summary>',
  '        /// <summary>主菜单板上的每日/每周挑战行（官方 §4 要求显示在板上）。</summary>\n' +
  '        Text _taskBoardText;\n' +
  '        /// <summary>快速跳转提示（地点 ↔ 主菜单 ↔ 商店）。</summary>\n' +
  '        Text _navHintText;\n\n' +
  '        /// <summary>房间码显示（仅建房成功后可见；官方在菜单板右上角）。</summary>',
  '② 新增 _taskBoardText / _navHintText 字段');

// ── ③ ID 卡可点 → 档案面板 ────────────────────────────────────────────
{
  const anchor = '            if (clicked && TryHitWorldLabel(_shopScreenText, screenPoint)) { OpenShop(); return true; }';
  sub(anchor,
    anchor + '\n' +
    '            // 官方 §4：右上 ID 卡可点击查看详情（资金/等级/升级进度）。\n' +
    '            if (clicked && TryHitWorldLabel(_idCardText, screenPoint)) { OpenProfilePanel(); return true; }\n' +
    '            // 官方 §4：每日/每周挑战显示在菜单板上 → 点了看详情（复用既有 ShowTasks）。\n' +
    '            if (clicked && TryHitWorldLabel(_taskBoardText, screenPoint)) { ShowTasks(); return true; }',
    '③ ID 卡与挑战行接入点击');
}

// ── ④ 档案面板 ────────────────────────────────────────────────────────
{
  const anchor = '        void OpenMapVotePanel()';
  const i = src.indexOf(anchor);
  if (i < 0) fail('未找到 OpenMapVotePanel 锚点');
  const block = [
    '        /// <summary>',
    '        /// 档案面板（官方 §4：右上 ID 卡显示资金/等级/升级进度，点开看详情）。',
    '        /// 数值全部取自既有系统（Progression），这里不自己算、不缓存 —— 避免两套口径。',
    '        /// </summary>',
    '        void OpenProfilePanel()',
    '        {',
    '            var boot = GetComponent<GameBootstrap>();',
    '            var sb = new System.Text.StringBuilder();',
    '            sb.AppendLine("档案");',
    '            sb.AppendLine();',
    '            if (boot != null && boot.Progression != null) sb.AppendLine(boot.Progression.Describe());',
    '            else sb.AppendLine("（进度系统未就绪）");',
    '            sb.AppendLine();',
    '            sb.AppendLine("官方形态：等级 / 资金 / 升级进度，多人时每位玩家一张 ID 卡。");',
    '            sb.AppendLine("点任意处关闭");',
    '            ShowModal(sb.ToString());',
    '        }',
    '',
  ].join('\n');
  src = src.slice(0, i) + block + src.slice(i);
  log.push('  ✓ ④ 新增 OpenProfilePanel');
}

// ── ⑤ 每帧把挑战行挂到板下方、快捷提示挂到底部（跟随相机投影）────────
{
  const anchor = '        void UpdateBoardUi()';
  const i = src.indexOf(anchor);
  if (i < 0) fail('未找到 UpdateBoardUi');
  const brace = src.indexOf('{', i);
  if (brace < 0) fail('未找到 UpdateBoardUi 左括号');
  // 在方法体开头插入：让新元素跟随相机（与 BoardItem 同一套投影口径）
  const hook = [
    '',
    '            // 挑战行与快捷提示跟随相机投影（与 BoardItem 同一口径，避免另造一套坐标换算）。',
    '            if (_cam != null && _hall != null && _boardMode)',
    '            {',
    '                var b = _hall.MenuBoardPos;',
    '                if (_taskBoardText != null)',
    '                {',
    '                    var tp = _cam.WorldToScreenPoint(new Vector3(b.x, 0.95f, b.z));',
    '                    PlaceAtScreen(_taskBoardText, tp, 520f, 44f);',
    '                }',
    '                if (_navHintText != null)',
    '                {',
    '                    var np = _cam.WorldToScreenPoint(new Vector3(b.x, 0.45f, b.z));',
    '                    PlaceAtScreen(_navHintText, np, 720f, 44f);',
    '                }',
    '            }',
  ].join('\n');
  src = src.slice(0, brace + 1) + hook + src.slice(brace + 1);
  log.push('  ✓ ⑤ 挑战行/快捷提示跟随相机');
}

console.log('[official-ui] 补完官方 UI 剩余三项' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
if (!checkOnly) {
  const bak = FILE + '.bak-officialui';
  if (!fs.existsSync(bak)) fs.writeFileSync(bak, raw, 'utf8');
  fs.writeFileSync(FILE, src, 'utf8');
  console.log(`  已写回：${raw.length} → ${src.length} 字节（备份 ${path.basename(bak)}）`);
  console.log('  ⚠ 必须跑：unity-syntax-check.sh + unity-tests.sh EditMode');
}
