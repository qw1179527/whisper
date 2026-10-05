// 给菜单板 UI 加**可见诊断**（HUD 上直接显示：建了几个、投影到了哪个屏幕坐标）。
//
// 为什么这么做：0.1.65/66 两个包都在追"选项文字看不到"，而我在真机上无法单步调试。
// 与其再猜一轮（改颜色/字号/锚点各出一版），不如把**过程量**打到 HUD：
//   `板UI n=6 [i0 1420,905] [i5 ...]` —— 一眼就能分出是"没建出来"还是"位置不对"。
// 这是本项目反复验证有效的做法（release 下 Debug.Log 不可靠，HUD 才是取证通道）。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
let s = fs.readFileSync(P, 'utf8');
const log = [];

// ① 诊断字段
if (!s.includes('_boardUiDiag')) {
  s = s.replace('        /// <summary>操作视角下的提示行。</summary>\n        Text _boardHint;',
`        /// <summary>操作视角下的提示行。</summary>
        Text _boardHint;
        /// <summary>菜单板 UI 的诊断串（走 HUD —— release 下 Debug.Log 不可靠）。</summary>
        string _boardUiDiag = "板UI:未建";`);
  log.push('  ✓ 诊断字段');
}

// ② 在 UpdateBoardUi 里记录
s = s.replace('                PlaceAtScreen(t, _cam.WorldToScreenPoint(_hall.NoteCenter(i)), 470f, 100f);',
`                var spp = _cam.WorldToScreenPoint(_hall.NoteCenter(i));
                PlaceAtScreen(t, spp, 470f, 100f);
                if (i == 0 || i == 5)
                    _boardUiDiag = "板UI n=" + _boardItems.Count
                        + " i" + i + " 世界(" + _hall.NoteCenter(i).x.ToString("F1") + ","
                        + _hall.NoteCenter(i).y.ToString("F1") + ","
                        + _hall.NoteCenter(i).z.ToString("F1") + ")→屏("
                        + spp.x.ToString("F0") + "," + spp.y.ToString("F0") + ",z" + spp.z.ToString("F0") + ")"
                        + " on=" + (t.enabled ? 1 : 0);`);
log.push('  ✓ UpdateBoardUi 记录诊断');

// ③ 诊断串进常驻 HUD 行（跟相机信息放一起）
s = s.replace('            _camInfo = cam + "\\n" + fx + "\\n" + q + "\\n" + hall;',
              '            _camInfo = cam + "\\n" + fx + "\\n" + q + "\\n" + hall + "\\n" + _boardUiDiag;');
log.push('  ✓ 诊断进 HUD');

fs.writeFileSync(P, s, 'utf8');
console.log(log.join('\n'));
