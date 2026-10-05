// 修菜单板文字定位的**真正根因**：Canvas 缩放。
//
// 【事实链（0.1.67 的 HUD 诊断给出决定性证据）】
//   · HUD 上 `板UI:未建` → `_boardItems` 循环一次都没跑 → 说明 `BuildBoardUi()` 提前返回，
//     即 `_canvas == null`。
//   · 代码里 `_canvas` 只在 `BuildRoom` **里面的** `BuildCanvas` 赋值（MenuScene.cs:536），
//     而我在 `Build()` 里把 `BuildBoardUi()` 排在了 **`BuildRoom()` 之前**（因为 3D 场景换成了
//     `HallScene`，我顺手删掉了 `BuildRoom()` 调用）→ 画布根本没建，`_canvas` 永远是 null。
//   · 同时还有第二个坑：Canvas 的 `referenceResolution = 1920x1080`（MenuScene.cs:534），
//     而真机屏幕是 2800x1280 → **scaleFactor ≈ 1.458**。所以
//     `WorldToScreenPoint` 给的**屏幕像素**不能直接写进 `anchoredPosition`（那是画布单位），
//     必须除以 `canvas.scaleFactor`。否则坐标被放大 1.458 倍、文字移出屏幕 ——
//     这正是"位置看着对但一个字都看不到"的原因。
//
// 两个都修。**本文件不得出现反引号**。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
let s = fs.readFileSync(P, 'utf8');
const NL = '\n';
const log = [];

// ① BuildBoardUi 之前先把画布建出来（把 BuildCanvas 从 BuildRoom 里抽出来单独调）
//    事实：BuildCanvas 是 BuildRoom 内部的方法（MenuScene.cs:536 赋 _canvas）。
//    最稳的修法：在 BuildBoardUi 开头**显式确保画布存在** —— 调 BuildCanvas()。
if (!s.includes('// 确保画布已建')) {
  const anchor = '        void BuildBoardUi()\n        {\n            if (_canvas == null || _hall == null) return;';
  const repl = [
    '        void BuildBoardUi()',
    '        {',
    '            // 确保画布已建。',
    '            // 【为什么必须显式建】`_canvas` 只在 BuildCanvas() 里赋值，而 BuildCanvas 原先只被',
    '            // BuildRoom() 调用 —— 本轮 3D 场景换成 HallScene 后 BuildRoom() 不再被调用，',
    '            // 于是 _canvas 永远是 null，BuildBoardUi 静默返回（HUD 诊断 `板UI:未建` 抓到）。',
    '            // 这类"换了上游导致下游静默失效"的坑，修法不是挪调用顺序，而是**让依赖显式自足**：',
    '            // 谁需要画布，谁就先确保画布存在（BuildCanvas 自身是幂等的：它会先 Find 再建）。',
    '            if (_canvas == null) BuildCanvas();',
    '            if (_canvas == null || _hall == null) { _boardUiDiag = "板UI:画布未建"; return; }',
  ].join(NL);
  if (s.includes(anchor)) { s = s.replace(anchor, repl); log.push('  ✓ BuildBoardUi 先确保画布'); }
  else log.push('  ! BuildBoardUi 锚点未中');
}

// ② PlaceAtScreen：屏幕像素 → 画布单位（除以 scaleFactor）
const oldHelper = [
  '            rt.anchorMin = new Vector2(0f, 0f);',
  '            rt.anchorMax = new Vector2(0f, 0f);',
  '            rt.pivot = new Vector2(0.5f, 0.5f);',
  '            rt.sizeDelta = new Vector2(w, h);',
  '            rt.anchoredPosition = new Vector2(screenPoint.x, screenPoint.y);',
].join(NL);
const newHelper = [
  '            var rt = t.rectTransform;',
  '            rt.anchorMin = new Vector2(0f, 0f);',
  '            rt.anchorMax = new Vector2(0f, 0f);',
  '            rt.pivot = new Vector2(0.5f, 0.5f);',
  '            rt.sizeDelta = new Vector2(w, h);',
  '            // 【关键】屏幕像素 → 画布单位。',
  '            // Canvas 是 ScaleWithScreenSize + referenceResolution 1920x1080，真机 2800x1280',
  '            // → scaleFactor ≈ 1.458。`WorldToScreenPoint` 给的是**屏幕像素**，',
  '            // 而 `anchoredPosition` 是**画布单位** —— 不除 scaleFactor 就会被放大 1.458 倍移出屏幕。',
  '            float sf = _canvas != null ? _canvas.scaleFactor : 1f;',
  '            if (sf <= 0.0001f) sf = 1f;',
  '            rt.anchoredPosition = new Vector2(screenPoint.x / sf, screenPoint.y / sf);',
].join(NL);
if (s.includes(oldHelper)) {
  s = s.replace(oldHelper, newHelper);
  // 去掉原先 PlaceAtScreen 里重复声明 rt 的那一行
  s = s.replace('            t.enabled = true;\n            var rt = t.rectTransform;\n            var rt = t.rectTransform;',
                '            t.enabled = true;\n            var rt = t.rectTransform;');
  log.push('  ✓ PlaceAtScreen 除以 scaleFactor');
} else log.push('  ! PlaceAtScreen 锚点未中');

fs.writeFileSync(P, s, 'utf8');
console.log(log.join(NL));
