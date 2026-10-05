#!/usr/bin/env node
/**
 * wire-map-vote-panel.mjs — 让地图板真正用上注册表（可投票选图）
 *
 * ## 为什么
 * 上一步加了 `config.maps` 注册表与 `GameBootstrap.SelectMap/DescribeMaps`，
 * 但**地图板还是写死的静态文字**（第 12 轮的投票面板里甚至写着"多地图数据尚未落地"）。
 * 注册表白建 = 假进度。本脚本把地图板接上真数据：
 * · 面板列出注册表里每张图（名称 + id + 是否已实现），当前图标 ★；
 * · 已实现的图**可点选中**（写进 `GameBootstrap.SelectMap`）；
 * · 未实现的图**明确拒绝并说明原因**（不许静默换图，也不许假装能选）。
 *
 * ## 交互方式（移动端）
 * 面板是 `ShowModal` 文本，无法直接做按钮列表 —— 故采用**编号选择**：
 * 面板列出「1) 疗养院 (asylum_v1) ★」「2) …（未实现）」，
 * 玩家点面板关闭后按对应数字键 1-9 选图（键盘通路第 10 轮已修为每帧生效）。
 * ⚠ 这是移动端"无鼠标列表"的折中；等 UI 收尾进到真按钮列表时再替换（已记入文档）。
 *
 * 用法：node tools/wire-map-vote-panel.mjs [--check]
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
const fail = (m) => { console.error('[map-ui] ✗ ' + m); process.exit(1); };
const sub = (from, to, what) => {
  const n = src.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  src = src.replace(from, to);
  log.push('  ✓ ' + what);
};

if (src.includes('SelectMapByNumber')) { console.log('[map-ui] 已接线，跳过'); process.exit(0); }

// ── ① 投票面板改为读注册表 ────────────────────────────────────────────
{
  const i = src.indexOf('        void OpenMapVotePanel()');
  if (i < 0) fail('未找到 OpenMapVotePanel');
  const brace = src.indexOf('{', i);
  if (brace < 0) fail('未找到 OpenMapVotePanel 左括号');
  let end = -1, depth = 0;
  for (let j = brace; j < src.length; j++) {
    if (src[j] === '{') depth++;
    else if (src[j] === '}') { depth--; if (depth === 0) { end = j; break; } }
  }
  if (end < 0) fail('未找到 OpenMapVotePanel 结束');
  const Q = String.fromCharCode(34);
  const body = [
    '        void OpenMapVotePanel()',
    '        {',
    '            var boot = GetComponent<GameBootstrap>();',
    '            var sb = new System.Text.StringBuilder();',
    '            sb.AppendLine(' + Q + '地图选择（投票）' + Q + ');',
    '            sb.AppendLine();',
    '            var list = Whisper.Gameplay.Config.GameConfig.Get(' + Q + 'maps.list' + Q + ') as System.Collections.Generic.List<object>;',
    '            if (list == null)',
    '            {',
    '                sb.AppendLine(' + Q + '地图注册表缺失（config.maps）—— 无法选图。' + Q + ');',
    '            }',
    '            else',
    '            {',
    '                int shown = 0;',
    '                for (int i = 0; i < list.Count && shown < 9; i++)',
    '                {',
    '                    var m = MiniJson.AsMap(list[i]);',
    '                    if (m == null) continue;',
    '                    shown++;',
    '                    var id = MiniJson.AsString(MiniJson.Get(m, ' + Q + 'id' + Q + '));',
    '                    var name = MiniJson.AsString(MiniJson.Get(m, ' + Q + 'name' + Q + '));',
    '                    var size = MiniJson.AsString(MiniJson.Get(m, ' + Q + 'size' + Q + '));',
    '                    int floors = MiniJson.AsInt(MiniJson.Get(m, ' + Q + 'floors' + Q + '));',
    '                    bool impl = MiniJson.AsBool(MiniJson.Get(m, ' + Q + 'implemented' + Q + '));',
    '                    bool cur = boot != null && boot.MapId == id;',
    '                    sb.Append(shown).Append(' + Q + ') ' + Q + ').Append(name)',
    '                      .Append(' + Q + '（' + Q + ').Append(id).Append(' + Q + ' · ' + Q + ').Append(size)',
    '                      .Append(' + Q + ' · ' + Q + ').Append(floors).Append(' + Q + '层）' + Q + ');',
    '                    if (cur) sb.Append(' + Q + ' ★当前' + Q + ');',
    '                    if (!impl) sb.Append(' + Q + '（未实现）' + Q + ');',
    '                    sb.AppendLine();',
    '                }',
    '                sb.AppendLine();',
    '                sb.AppendLine(' + Q + '按数字键 1-' + Q + ' + shown + ' + Q + ' 选图（关闭面板后按）' + Q + ');',
    '                sb.AppendLine(' + Q + '官方形态：多人投票，票数相同随机，另有「随机地图」。' + Q + ');',
    '            }',
    '            ShowModal(sb.ToString());',
    '        }',
    '        /// <summary>按编号选图（面板里列出的序号）。未实现的图明确拒绝，不静默换图。</summary>',
    '        void SelectMapByNumber(int n)',
    '        {',
    '            if (n < 1) return;',
    '            var boot = GetComponent<GameBootstrap>();',
    '            if (boot == null) { Note(' + Q + '组合根缺失：无法选图' + Q + '); return; }',
    '            var list = Whisper.Gameplay.Config.GameConfig.Get(' + Q + 'maps.list' + Q + ') as System.Collections.Generic.List<object>;',
    '            if (list == null) { Note(' + Q + '地图注册表缺失' + Q + '); return; }',
    '            int shown = 0;',
    '            for (int i = 0; i < list.Count; i++)',
    '            {',
    '                var m = MiniJson.AsMap(list[i]);',
    '                if (m == null) continue;',
    '                shown++;',
    '                if (shown != n) continue;',
    '                var id = MiniJson.AsString(MiniJson.Get(m, ' + Q + 'id' + Q + '));',
    '                bool impl = MiniJson.AsBool(MiniJson.Get(m, ' + Q + 'implemented' + Q + '));',
    '                if (!impl) { Note(' + Q + '该地图尚未实现：' + Q + ' + id + ' + Q + '（注册表里标了 implemented=false）' + Q + '); return; }',
    '                if (boot.SelectMap(id)) { Note(' + Q + '已选图：' + Q + ' + id + ' + Q + '（下一局生效）' + Q + '); }',
    '                else Note(' + Q + '选图失败：' + Q + ' + id);',
    '                return;',
    '            }',
    '        }',
  ].join('\n');
  src = src.slice(0, i) + body + src.slice(end + 1);
  log.push('  ✓ ① 投票面板读注册表 + 新增 SelectMapByNumber');
}

// ── ② 数字键选图（接进每帧键盘入口）──────────────────────────────────
{
  const Q = String.fromCharCode(34);
  const anchor = '            if (_boardMode && Input.GetKeyDown(KeyCode.J)) { StartJoinSession(); return; }';
  const n = src.split(anchor).length - 1;
  if (n !== 1) fail(`键盘锚点命中 ${n} 次（应为 1）`);
  const add = anchor + '\n' +
    '            // 数字键 1-9：选图（面板列出序号；移动端无鼠标列表时的折中，见 wire-map-vote-panel.mjs 注释）。\n' +
    '            for (int d = 1; d <= 9; d++)\n' +
    '            {\n' +
    '                if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + d - 1))) { SelectMapByNumber(d); return; }\n' +
    '            }';
  src = src.replace(anchor, add);
  log.push('  ✓ ② 数字键 1-9 选图');
}

console.log('[map-ui] 地图板接上注册表' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
if (!checkOnly) {
  const bak = FILE + '.bak-mapui';
  if (!fs.existsSync(bak)) fs.writeFileSync(bak, raw, 'utf8');
  fs.writeFileSync(FILE, src, 'utf8');
  console.log(`  已写回：${raw.length} → ${src.length} 字节`);
}
