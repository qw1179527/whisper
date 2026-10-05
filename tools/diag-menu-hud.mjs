// 把主界面的诊断从 `Debug.Log` 改到 **HUD 文字**。
//
// 为什么改：0.1.21 的 `Debug.Log` 在 logcat 里能看到，0.1.24 同样代码却**一条都没有** ——
// release IL2CPP 下 `Debug.Log` 是否保留不可靠（Unity 在非 Development 构建里会剥离日志调用）。
// 结论：**真机取证不能依赖 logcat**，必须走"屏幕上能看到的通道"。
// 本工程 HUD 是代码构建的，主界面又本来就有左下角提示行 → 直接用它显示诊断。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
let s = fs.readFileSync(P, 'utf8');
const log = [];

// ① 按钮矩形：写到 hint（屏幕可见），不再只依赖 Debug.Log
if (!s.includes('_btnRects')) {
  s = s.replace('        Text _hint;',
`        Text _hint;
        /// <summary>按钮屏幕矩形摘要（诊断用；走 HUD 而不是 Debug.Log，见文件头的剥离说明）。</summary>
        string _btnRects = "";
        /// <summary>最近一次触摸（诊断用，同样走 HUD）。</summary>
        string _lastTouch = "无";`);
  log.push('  ✓ 诊断字段');
}

const rectBlock = `
            // 诊断：把每个按钮的**实际屏幕矩形**汇总成一行（走 HUD，release 下也可读）。
            // 我先前只按"看着像"的像素坐标去点，没有客观依据；这条把它变成数字。
            var sbR = new System.Text.StringBuilder();
            for (int i = 0; i < canvasGo.transform.childCount; i++)
            {
                var ch = canvasGo.transform.GetChild(i);
                var rt = ch as RectTransform;
                if (rt == null) continue;
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                sbR.Append(ch.name).Append('[')
                   .Append(corners[0].x.ToString("F0")).Append(',').Append(corners[0].y.ToString("F0"))
                   .Append('-').Append(corners[2].x.ToString("F0")).Append(',').Append(corners[2].y.ToString("F0"))
                   .Append("] ");
            }
            _btnRects = sbR.ToString();
            Debug.Log("[Whisper] 按钮矩形 " + _btnRects);
`;
if (s.includes('            _hint = hint;')) {
  s = s.replace('            _hint = hint;', '            _hint = hint;' + rectBlock);
  log.push('  ✓ 按钮矩形 → HUD');
} else log.push('  ! _hint 锚点未中');

// ② 触摸诊断改写到 HUD（保留 Debug.Log 作为"有则更好"）
s = s.replace('                _touchLogs++;\n                Debug.Log("[Whisper] 触摸到达 游戏内坐标 " + t.position.x + "," + t.position.y\n                          + " (屏幕 " + Screen.width + "x" + Screen.height + ")");',
`                _touchLogs++;
                _lastTouch = t.position.x.ToString("F0") + "," + t.position.y.ToString("F0")
                           + " / " + Screen.width + "x" + Screen.height;
                Debug.Log("[Whisper] 触摸到达 " + _lastTouch);`);

// ③ HUD 那一行把诊断带上（原来只显示鬼怪计数）
s = s.replace('                _hint.text = string.Format("灯光闪烁中 · 在场鬼怪 {0} · 累计刷出 {1}", GhostCount, GhostSpawned);',
`                // 把诊断与玩法信息**都**放到这一行：release 下这是唯一可靠的取证通道。
                _hint.text = string.Format("灯闪中 · 鬼 {0}/刷出 {1} · 触摸 {2}\\n按钮 {3}",
                    GhostCount, GhostSpawned, _lastTouch, _btnRects);`);
log.push('  ✓ 触摸 + 矩形 → HUD 行');

fs.writeFileSync(P, s, 'utf8');
console.log(log.join('\n'));
