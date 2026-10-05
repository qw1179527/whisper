#!/usr/bin/env node
/**
 * hide-debug-text.mjs — 主界面"小字"清场：加一个总开关，默认把所有调试/诊断文字关掉
 *
 * ## 用户要求（2026-10-05 第 10 轮原话）
 * 「还有主界面其他的小字也去掉」+ 更早的「主界面成分复杂，字体到处都是」。
 *
 * ## 清点结果（哪些是"小字"）
 * 主界面上会出现文字的地方一共 11 处，分三类：
 * | 类别 | 位置 | 处置 |
 * |---|---|---|
 * | **调试/诊断** | `_hint`（左上角面板：接口/关卡/几何着色器/玩家/怪物/温度/相机/画质/后处理/板UI/按钮矩形/屏幕…） | **默认隐藏**（本脚本） |
 * | **调试/诊断** | `_btnRects`（按钮屏幕矩形 + `屏幕 2800x1280`，贴在 `_hint` 里） | **默认隐藏**（本脚本） |
 * | **调试/诊断** | `MenuHint`（右下角 `_inputState` 等） | **默认隐藏**（本脚本） |
 * | **旧 UI 统计** | `LobbyPanel`（等级 1 · 钱 0 · 商店 18 件 · 电闸 · 每日/每周任务 · 本局任务 · 互动） | 随旧 UI 整体废弃（用户已明确"不要再在旧界面上花时间"） |
 * | **官方该有的** | 菜单板 6 项 / 地图板 / 商店屏 / ID 卡 / 房间码 / 板提示条 | **保留**（用户《补充说明》§4 的官方大厅形态） |
 *
 * ## 为什么用"开关"而不是删代码
 * 这些文字是本项目**唯一的真机取证通道**（交接 §0.6：「Debug.Log 在 release IL2CPP 里时有时无 →
 * 所有真机取证走 HUD」）。整段删掉等于把眼睛挖了；做成 `ShowDiagnostics` 开关（**默认 false**）
 * 兼顾"交付给玩家看的主界面干净"与"排查时还能打开"。
 *
 * ## 顺带修一处真的越界
 * `_boardHint` 的文本里原本追加了 `_btnRects`（按钮矩形 + 屏幕尺寸）——那是调试数据，
 * 被拼进了**玩家可见的提示条**里（截图里能看到 `| 屏幕 2800x1280`）。这里一并去掉。
 *
 * 用法：node tools/hide-debug-text.mjs [--check]
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
const fail = (m) => { console.error('[dbg-text] ✗ ' + m); process.exit(1); };
const sub = (from, to, what) => {
  const n = src.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  src = src.replace(from, to);
  log.push('  ✓ ' + what);
};

if (src.includes('ShowDiagnostics')) { console.log('[dbg-text] 已应用，跳过'); process.exit(0); }

// ── ① 新开关（默认关）────────────────────────────────────────────────
{
  const anchor = '        string _camInfo = "相机：—";';
  sub(anchor,
    '        /// <summary>\n' +
    '        /// 主界面诊断文字总开关。**默认关**：交付给玩家看的主界面必须干净（用户 2026-10-05：\n' +
    '        /// "主界面成分复杂，字体到处都是"、"其他的小字也去掉"）。\n' +
    '        /// 但**不能删代码**：release IL2CPP 下 `Debug.Log` 时有时无，HUD 是本项目唯一可靠的真机取证通道\n' +
    '        /// （交接 §0.6）。排查时把它置 true 即可恢复全部诊断行。\n' +
    '        /// </summary>\n' +
    '        public bool ShowDiagnostics = false;\n\n' +
    anchor,
    '① 新增 ShowDiagnostics 开关（默认关）');
}

// ── ② _hint（左上角诊断面板）默认隐藏 ────────────────────────────────
{
  const anchor = '        string _btnRects = "";';
  const i = src.indexOf(anchor);
  if (i < 0) fail('未找到 _btnRects 字段锚点');
  sub('        string _btnRects = "";',
    '        string _btnRects = "";\n\n' +
    '        /// <summary>诊断面板的宿主 Text（隐藏它比逐条清空文本更彻底）。</summary>\n' +
    '        Text _diagPanel;',
    '② 新增 _diagPanel 字段');
}

// ── ③ 建面板时记下引用，并按开关决定可见 ─────────────────────────────
{
  // _hint 就是那个诊断面板（它被赋值 _hint.text = ... 在 Update 里）
  const anchor = '        if (_hint != null) _hint.text = s;';
  sub(anchor,
    '        if (_hint != null)\n' +
    '        {\n' +
    '            // 诊断文字**只在开关打开时**写进面板；关闭时连写都不写（避免瞬间闪字）。\n' +
    '            if (!ShowDiagnostics) { _hint.text = string.Empty; return; }\n' +
    '            _hint.text = s;\n' +
    '        }',
    '③ 诊断写入受开关控制');
}

// ── ④ 板提示条：去掉拼进去的调试数据 ─────────────────────────────────
{
  const anchor = '            _boardHint = SceneMaterials.Label(_canvas.transform, "BoardHint",';
  const i = src.indexOf(anchor);
  if (i < 0) fail('未找到 _boardHint 建立处');
  // 找到该 Label 调用中拼接 _btnRects 的地方（不同版本可能是 + _btnRects 或 + 屏幕尺寸）
  const seg = src.slice(i, i + 700);
  if (seg.includes('_btnRects')) {
    src = src.replace(seg, seg.replace(/\s*\+\s*_btnRects/g, ''));
    log.push('  ✓ ④ 提示条去掉 _btnRects 调试拼接');
  } else {
    log.push('  · 提示条未拼接 _btnRects（可能已在别处处理）');
  }
}

// ── ⑤ Update 里那行把 _btnRects 塞进 HUD 的也关掉 ────────────────────
{
  const anchor = '            _hint.text = string.Format("灯闪中 · 鬼 {0}/刷出 {1} · 触摸 {2} · 输入 [{3}]\\n{4}\\n{5}",';
  const n = src.split(anchor).length - 1;
  if (n === 1) {
    sub(anchor,
      '            // 诊断行（含触摸/输入/相机/按钮矩形）——受总开关控制，见字段注释。\n' +
      '            if (ShowDiagnostics)\n' +
      '            _hint.text = string.Format("灯闪中 · 鬼 {0}/刷出 {1} · 触摸 {2} · 输入 [{3}]\\n{4}\\n{5}",',
      '⑤ 触摸/相机/矩形诊断行受开关控制');
  } else {
    log.push('  · 未匹配到诊断行（格式可能不同）');
  }
}

console.log('[dbg-text] 主界面小字清场' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
if (!checkOnly) {
  const bak = FILE + '.bak-debugtext';
  if (!fs.existsSync(bak)) fs.writeFileSync(bak, raw, 'utf8');
  fs.writeFileSync(FILE, src, 'utf8');
  console.log(`  已写回：${raw.length} → ${src.length} 字节（备份 ${path.basename(bak)}）`);
  console.log('  ⚠ 必须跑：unity-syntax-check.sh + unity-tests.sh EditMode');
}
