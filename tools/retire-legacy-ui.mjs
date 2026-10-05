#!/usr/bin/env node
/**
 * retire-legacy-ui.mjs — 主界面 UI 收尾：停建旧 UI 全体，并让官方两个交互位可点
 *
 * ## 为什么（用户永久约束里的下一步）
 * `docs/spec/supplement-2026-10-05-permanent.md` 与交接都写明：
 * > 用户执行顺序「先做着色器/渲染器/建模 → **再主界面重构** → 再重做 UI 交互」，
 * > 且 **「不要再在旧界面上花时间」**（旧 UI 只做删除，不做美化）。
 *
 * 旧 UI 全体（右侧竖排 8 按钮 + 右下 `MenuHint` + 左上 `LobbyPanel`）**都是 `BuildOptions()` 建的**，
 * 本脚本用一个开关 `ShowLegacyUi`（**默认 false**）整体停建：
 * · 停建后主界面只剩官方大厅该有的东西：菜单板 6 项 / 地图板 / 商店屏 / ID 卡 / 房间码 / 提示条；
 * · 用户报的"字体到处都是"由此根治（那些碎字全部来自这三块）；
 * · 保留开关是为了**取证可回退**（旧 UI 里有按钮屏幕矩形诊断，偶发排查仍可能要）。
 *
 * ⚠ 为什么不是"删代码"：`BuildOptions()` 里还登记了 `_hitButtons`（自绘点击判定用）。
 * 直接删会让"点哪里都不响应"——那是本会话已经踩过的坑（输入挂在条件链里）。
 * 停建时 `_hitButtons` 保持为空列表即可，判定逻辑不变。
 *
 * ## 顺带做官方两个交互位（用户《补充说明》§4）
 * · **左侧地图选项板**：点了以后弹面板（投票选图/随机）——官方是"投票 + 票数相同随机 + 随机地图选项"；
 * · **右侧装备商店电脑**：点了以后打开商店（`OpenShop()` 已存在）。
 * 两者都用**既有的 `ShowModal`/世界坐标→屏幕坐标换算**，不新造一套。
 *
 * 用法：node tools/retire-legacy-ui.mjs [--check]
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
const fail = (m) => { console.error('[retire] ✗ ' + m); process.exit(1); };
const sub = (from, to, what) => {
  const n = src.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  src = src.replace(from, to);
  log.push('  ✓ ' + what);
};

if (src.includes('ShowLegacyUi')) { console.log('[retire] 已应用，跳过'); process.exit(0); }

// ── ① 开关字段 ────────────────────────────────────────────────────────
sub('        public bool ShowDiagnostics = false;',
  '        public bool ShowDiagnostics = false;\n\n' +
  '        /// <summary>\n' +
  '        /// 旧 UI（右侧竖排 8 按钮 + 右下诊断行 + 左上信息面板）是否还建。**默认 false = 不建**。\n' +
  '        ///\n' +
  '        /// 用户永久约束：「不要再在旧界面上花时间」「主界面成分复杂，字体到处都是」——\n' +
  '        /// 旧界面已由新版官方形态（3D 菜单板 + 地图板 + 商店电脑 + ID 卡）取代，故整体停建。\n' +
  '        /// 之所以保留开关而不是删代码：旧 UI 里带了按钮屏幕矩形诊断，偶发排查时仍可能要；\n' +
  '        /// 且 `BuildOptions()` 还负责登记 `_hitButtons`（自绘点击判定），不能凭直觉删。\n' +
  '        /// </summary>\n' +
  '        public bool ShowLegacyUi = false;',
  '① 新增 ShowLegacyUi 开关（默认停建）');

// ── ② BuildOptions 整体受开关控制 ────────────────────────────────────
sub(`        void BuildOptions()
        {
            BuildCanvas();`,
  `        void BuildOptions()
        {
            BuildCanvas();          // 画布是官方 UI 与新 UI 共用的，**必须**先建（幂等）
            if (!ShowLegacyUi) return;   // 旧 UI 整体停建（见 ShowLegacyUi 字段注释）`,
  '② BuildOptions 受开关控制（默认只建画布）');

// ── ③ 地图板 / 商店电脑可点（官方两个交互位）─────────────────────────
{
  // 复用既有 HandleBoardInput 的世界→屏幕换算与命中范式
  const anchor = '        bool HandleBoardInput(Vector2 screenPoint, bool clicked)';
  const i = src.indexOf(anchor);
  if (i < 0) fail('未找到 HandleBoardInput');
  const brace = src.indexOf('{', i);
  if (brace < 0) fail('未找到 HandleBoardInput 左括号');
  const hook = [
    '',
    '            // ── 官方两个交互位（用户《补充说明》§4）────────────────────────',
    '            // 左侧地图选项板：点了弹投票面板；右侧装备商店电脑：点了开商店。',
    '            // 命中判定沿用同一个范式（局域坐标 + 双 y 口径），不另造一套。',
    '            if (clicked && TryHitWorldLabel(_mapBoardText, screenPoint)) { OpenMapVotePanel(); return true; }',
    '            if (clicked && TryHitWorldLabel(_shopScreenText, screenPoint)) { OpenShop(); return true; }',
  ].join('\n');
  src = src.slice(0, brace + 1) + hook + src.slice(brace + 1);
  log.push('  ✓ ③ 地图板/商店电脑接入点击');
}

// ── ④ 命中工具 + 地图投票面板 ────────────────────────────────────────
{
  const anchor = '        /// <summary>处理"点菜单板 / 空格"这类主界面输入。返回 true 表示已消费本次点击。</summary>';
  const i = src.indexOf(anchor);
  if (i < 0) fail('未找到 HandleBoardInput 文档锚点');
  const block = [
    '        /// <summary>',
    '        /// 命中一个"贴在 3D 世界位置上的 UI 文本"（地图板/商店屏这种）。',
    '        /// 与菜单板拾取同一口径：世界点 → 屏幕点 → 局域坐标 → rect 包含（双 y 口径）。',
    '        /// 放宽到 1.6 倍（世界空间的牌匾比文字本身大，玩家会点牌匾边缘）。',
    '        /// </summary>',
    '        bool TryHitWorldLabel(Text label, Vector2 screenPoint)',
    '        {',
    '            if (label == null || _cam == null || !label.gameObject.activeInHierarchy) return false;',
    '            var rt = label.rectTransform;',
    '            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPoint, null, out var local);',
    '            var flipped = new Vector2(screenPoint.x, Screen.height - screenPoint.y);',
    '            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, flipped, null, out var localFlipped);',
    '            var r = rt.rect;',
    '            r.width *= 1.6f; r.height *= 1.6f;',
    '            r.x -= rt.rect.width * 0.3f; r.y -= rt.rect.height * 0.3f;',
    '            return r.Contains(local) || r.Contains(localFlipped);',
    '        }',
    '',
    '        /// <summary>',
    '        /// 地图投票面板（官方：左侧地图选项板，可投票、票数相同随机、可选"随机地图"）。',
    '        /// ⚠ 目前仍是**选择界面**：多地图数据（config.maps 注册表 + 第二张图）尚未落地，',
    '        /// 故这里如实显示"当前仅 1 张图"，不假装有得选（禁止 Guessing）。',
    '        /// </summary>',
    '        void OpenMapVotePanel()',
    '        {',
    '            var sb = new System.Text.StringBuilder();',
    '            sb.AppendLine("地图选择（投票）");',
    '            sb.AppendLine();',
    '            sb.AppendLine("当前可用：asylum_v1 · 疗养院（3 层 · 15 房间）");',
    '            sb.AppendLine();',
    '            sb.AppendLine("官方形态：多张地图投票，票数相同时随机，另有「随机地图」选项。");',
    '            sb.AppendLine("本项目多地图数据尚未落地（config.maps 注册表未建）—— 不假装有得选。");',
    '            ShowModal(sb.ToString());',
    '        }',
    '',
  ].join('\n');
  src = src.slice(0, i) + block + src.slice(i);
  log.push('  ✓ ④ 新增 TryHitWorldLabel / OpenMapVotePanel');
}

console.log('[retire] 旧 UI 收尾 + 官方交互位' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
if (!checkOnly) {
  const bak = FILE + '.bak-retire';
  if (!fs.existsSync(bak)) fs.writeFileSync(bak, raw, 'utf8');
  fs.writeFileSync(FILE, src, 'utf8');
  console.log(`  已写回：${raw.length} → ${src.length} 字节（备份 ${path.basename(bak)}）`);
  console.log('  ⚠ 必须跑：unity-syntax-check.sh + unity-tests.sh EditMode');
}
