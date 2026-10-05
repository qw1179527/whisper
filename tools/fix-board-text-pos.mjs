// 修菜单板选项文字不可见。
//
// 【根因】`SceneMaterials.Label(...)` 建出的 Text 其 RectTransform **pivot = (0,0)（左下角）**，
// 而 `UpdateBoardUi` 里的 `WorldToScreenPoint` 给的是**屏幕像素中心**坐标，
// 直接写进 `anchoredPosition` → 文字的左下角被放到目标点上，整块文字向右上偏出可视区。
//
// 正确做法：把这些"跟随 3D 点"的文字 pivot 设为 (0.5,0.5)，并显式设置 anchor 为左下角，
// 这样 anchoredPosition 就与屏幕像素一一对应（本工程 Canvas 是 ScreenSpaceOverlay + ScaleWithScreenSize
// 参考分辨率 = 当前屏幕，所以画布单位 == 屏幕像素 —— 这一点在 MenuScene 里已有先例：
// 旧的按钮矩形诊断行就是按屏幕像素直接写的）。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
let s = fs.readFileSync(P, 'utf8');

/**
 * 给一个"跟随 3D 点"的 Text 做定位修正：anchor = (0,0)（屏幕左下），pivot = (0.5,0.5)（中心）。
 * 这样 `anchoredPosition = WorldToScreenPoint(...)` 就是"把文字中心放在那个屏幕像素上"。
 */
const helper = [
  '        /// <summary>',
  '        /// 把一个"跟随 3D 点"的文字摆到屏幕像素位置。',
  '        /// </summary>',
  '        /// <remarks>',
  '        /// 为什么需要这个助手：`SceneMaterials.Label` 建出的 Text 的 RectTransform **pivot 是 (0,0)**',
  '        /// 且 anchor 由调用方给。如果直接 `anchoredPosition = 屏幕像素中心`，文字的左下角会被放到那个点上，',
  '        /// 整块文字向右上偏出可视区 —— 0.1.65 真机就是"菜单板选项一个字都看不到"。',
  '        /// 本助手统一口径：**anchor=屏幕左下、pivot=中心** → anchoredPosition 就是"文字中心的屏幕像素"。',
  '        /// （本工程 Canvas 是 ScreenSpaceOverlay + ScaleWithScreenSize 且参考分辨率=当前屏幕，',
  '        ///   所以画布单位 == 屏幕像素。旧的按钮矩形诊断行就是按这个口径写的，有先例。）',
  '        /// </remarks>',
  '        static void PlaceAtScreen(Text t, Vector3 screenPoint, float w, float h)',
  '        {',
  '            if (t == null) return;',
  '            if (screenPoint.z <= 0f) { t.enabled = false; return; }   // 在相机背后',
  '            t.enabled = true;',
  '            var rt = t.rectTransform;',
  '            rt.anchorMin = new Vector2(0f, 0f);',
  '            rt.anchorMax = new Vector2(0f, 0f);',
  '            rt.pivot = new Vector2(0.5f, 0.5f);',
  '            rt.sizeDelta = new Vector2(w, h);',
  '            rt.anchoredPosition = new Vector2(screenPoint.x, screenPoint.y);',
  '        }',
  '',
].join('\n');

if (!s.includes('static void PlaceAtScreen')) {
  const anchor = '        /// <summary>把菜单板 UI 每帧贴到 3D 位置上（世界坐标 → 屏幕坐标）。</summary>';
  if (!s.includes(anchor)) { console.error('  ✗ 锚点未中'); process.exit(1); }
  s = s.replace(anchor, helper + anchor);
  console.log('  ✓ 插入 PlaceAtScreen 助手');
}

// 用助手替换原来的直接赋值
const oldItems = [
  '                var sp = _cam.WorldToScreenPoint(_hall.NoteCenter(i));',
  '                if (sp.z <= 0f) { t.enabled = false; continue; }   // 在相机背后',
  '                t.enabled = true;',
  '                t.rectTransform.anchoredPosition = new Vector2(sp.x, sp.y);',
  '                t.rectTransform.sizeDelta = new Vector2(460f, 96f);',
].join('\n');
const newItems = [
  '                PlaceAtScreen(t, _cam.WorldToScreenPoint(_hall.NoteCenter(i)), 470f, 100f);',
].join('\n');
if (s.includes(oldItems)) { s = s.replace(oldItems, newItems); console.log('  ✓ 选项文字改用 PlaceAtScreen'); }
else console.log('  ! 选项文字锚点未中');

const oldShop = [
  '                var sp = _cam.WorldToScreenPoint(new Vector3(_hall.WidthM * 0.30f, 1.28f, -_hall.LengthM * 0.5f + 1.5f));',
  '                if (sp.z > 0f) { _shopScreenText.enabled = true; _shopScreenText.rectTransform.anchoredPosition = new Vector2(sp.x, sp.y); }',
  '                else _shopScreenText.enabled = false;',
].join('\n');
const newShop = [
  '                PlaceAtScreen(_shopScreenText,',
  '                    _cam.WorldToScreenPoint(new Vector3(_hall.WidthM * 0.30f, 1.28f, -_hall.LengthM * 0.5f + 1.5f)), 380f, 90f);',
].join('\n');
if (s.includes(oldShop)) { s = s.replace(oldShop, newShop); console.log('  ✓ 商店屏文字改用 PlaceAtScreen'); }
else console.log('  ! 商店屏锚点未中');

// ID 卡：位置修正 + 文案
const oldId = s.match(/if \(_idCardText != null\)\r?\n\s*\{\r?\n\s*var sp = _cam\.WorldToScreenPoint\(new Vector3\(_hall\.WidthM \* 0\.5f - 2\.4f, 3\.75f, -_hall\.LengthM \* 0\.5f \+ 0\.5f\)\);[\s\S]*?\n            \}/);
if (oldId) {
  const newId = [
    '            if (_idCardText != null)',
    '            {',
    '                var p = _boot != null ? _boot.Progression : null;',
    '                _idCardText.text = p != null',
    '                    ? "ID\\n等级 " + p.Level + "\\n钱 " + p.Money + "\\n经验 " + (p.LevelProgress * 100f).ToString("F0") + "%"',
    '                    : "ID";',
    '                PlaceAtScreen(_idCardText,',
    '                    _cam.WorldToScreenPoint(new Vector3(_hall.WidthM * 0.5f - 2.4f, 3.75f, -_hall.LengthM * 0.5f + 0.5f)), 200f, 130f);',
    '            }',
  ].join('\n');
  s = s.replace(oldId[0], newId);
  console.log('  ✓ ID 卡改用 PlaceAtScreen');
} else console.log('  ! ID 卡锚点未中');

// 字号调大：真机 2800x1280 上 30 号字在纸上偏小
s = s.replace('new Vector2(0f, 0f), new Vector2(0f, 0f), 30, TextAnchor.MiddleCenter);\n                t.color = new Color(0.10f, 0.10f, 0.13f);   // 深字写在浅色纸片上（软木板风格）',
              'new Vector2(0f, 0f), new Vector2(0f, 0f), 42, TextAnchor.MiddleCenter);\n                t.color = new Color(0.08f, 0.08f, 0.11f);   // 深字写在浅色纸片上（软木板风格）');
console.log('  ✓ 选项字号 30 → 42');

fs.writeFileSync(P, s, 'utf8');
