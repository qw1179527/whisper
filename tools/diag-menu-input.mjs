// 输入诊断：主界面里记录 ① 游戏收到的每一次触摸 ② 每个按钮的实际屏幕矩形。
//
// 为什么需要：0.1.23 加了 EventSystem 后按钮**依然点不动**，而日志里连按钮回调都没有。
// 只靠截图分不清"触摸没到游戏"、"到了但没命中按钮"、"命中了但回调没跑"。
// 这三件事各有一个确定的观测量，全部打出来就能一次定位。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
let s = fs.readFileSync(P, 'utf8');
const log = [];

// ① 触摸诊断（限流：每次触摸一条，且最多打 40 条，避免刷屏）
if (!s.includes('_touchLogs')) {
  const anchor = '        void Update()\n        {\n            if (_root == null || _paused) return;';
  if (s.includes(anchor)) {
    s = s.replace(anchor, `        /// <summary>已打印的触摸条数（限流，避免真机日志被刷爆）。</summary>
        int _touchLogs;
        /// <summary>按钮的屏幕矩形缓存（诊断用）。</summary>
        readonly System.Collections.Generic.List<RectTransform> _buttons = new System.Collections.Generic.List<RectTransform>();

        /// <summary>诊断：把游戏**实际收到**的触摸打出来。
        /// 这一条能区分"触摸没到游戏"（日志空）与"到了但没命中按钮"（日志有坐标但回调不跑）。</summary>
        void LogTouchesOnce()
        {
            if (_touchLogs >= 40) return;
            int n = Input.touchCount;
            for (int i = 0; i < n && _touchLogs < 40; i++)
            {
                var t = Input.GetTouch(i);
                if (t.phase != TouchPhase.Began) continue;
                _touchLogs++;
                Debug.Log("[Whisper] 触摸到达 游戏内坐标 " + t.position.x + "," + t.position.y
                          + " (屏幕 " + Screen.width + "x" + Screen.height + ")");
            }
        }

        void Update()
        {
            LogTouchesOnce();
            if (_root == null || _paused) return;`);
    log.push('  ✓ 触摸诊断');
  } else log.push('  ! Update 锚点未中');
}

// ② 按钮矩形诊断（在 BuildOptions 末尾打一次）
if (!s.includes('按钮矩形')) {
  const a2 = '            _hint = hint;\n        }';
  if (s.includes(a2)) {
    s = s.replace(a2, `            _hint = hint;

            // 诊断：把每个按钮的**实际屏幕矩形**打出来（诊断"点得到/点不到"用）。
            // 我先前只按"看着像"的像素坐标去点，没有客观依据；这条日志把它变成数字。
            for (int i = 0; i < canvasGo.transform.childCount; i++)
            {
                var ch = canvasGo.transform.GetChild(i);
                var rt = ch as RectTransform;
                if (rt == null) continue;
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                Debug.Log("[Whisper] 按钮矩形 " + ch.name
                          + " 左下 " + corners[0].x.ToString("F0") + "," + corners[0].y.ToString("F0")
                          + " 右上 " + corners[2].x.ToString("F0") + "," + corners[2].y.ToString("F0"));
            }
        }`);
    log.push('  ✓ 按钮矩形诊断');
  } else log.push('  ! _hint 锚点未中');
}

fs.writeFileSync(P, s, 'utf8');
console.log(log.join('\n'));
