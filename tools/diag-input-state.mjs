// 把**输入状态**实时打到 HUD，并给点击判定加 `Input.touches` 兜底。
//
// 事实链（都是读出来的，不是猜的）：
//   · 0.1.27 屏幕 2800x1280 ✓ · MenuBtn0 矩形 [2258,519 - 2558,453]（左上口径）✓
//   · 点 (2408,486) 落在该矩形内，但 HUD 的命中回显**没变** → 点击判定根本没跑或没读到点击
// 因此下一步不是继续调坐标，而是**确认游戏到底收到了什么输入**：mousePosition、touchCount、
// GetMouseButton(0)。这三条一打出来，问题就只剩一个可能。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
let s = fs.readFileSync(P, 'utf8');
const log = [];

// ① 实时输入状态
if (!s.includes('_inputState')) {
  s = s.replace('        string _lastTouch = "无";',
`        string _lastTouch = "无";
        /// <summary>实时输入状态（真机取证的唯一可靠通道 —— release 下 Debug.Log 会被剥离）。</summary>
        string _inputState = "";`);
  s = s.replace('        void Update()\n        {\n            LogTouchesOnce();',
`        void Update()
        {
            // 实时输入状态：mousePosition 与 touchCount 是判断"输入有没有到游戏"的唯一客观依据。
            _inputState = "mp " + Input.mousePosition.x.ToString("F0") + "," + Input.mousePosition.y.ToString("F0")
                        + " touch " + Input.touchCount + " btn " + (Input.GetMouseButton(0) ? 1 : 0)
                        + " down " + (Input.GetMouseButtonDown(0) ? 1 : 0);
            LogTouchesOnce();`);
  log.push('  ✓ 实时输入状态');
}

// ② HUD 那一行加上输入状态
s = s.replace('                _hint.text = string.Format("灯闪中 · 鬼 {0}/刷出 {1} · 触摸 {2}\\n按钮 {3}",\n                    GhostCount, GhostSpawned, _lastTouch, _btnRects);',
`                _hint.text = string.Format("灯闪中 · 鬼 {0}/刷出 {1} · 触摸 {2} · 输入 [{3}]\\n按钮 {4}",
                    GhostCount, GhostSpawned, _lastTouch, _inputState, _btnRects);`);
log.push('  ✓ HUD 带输入状态');

// ③ 触摸兜底：`Input.GetMouseButtonDown` 在某些设备/Input System 模式下拿不到触摸，
//    但 `Input.GetTouch(...).phase == Began` 一定拿得到 —— 两条路都走。
if (!s.includes('Input.touchCount > 0')) {
  s = s.replace('            if (!Input.GetMouseButtonDown(0)) return;\n            Vector2 sp = new Vector2(Input.mousePosition.x, Input.mousePosition.y);',
`            Vector2 sp;
            bool clicked = false;
            // 路 1：鼠标/触摸（多数情况下够用）
            if (Input.GetMouseButtonDown(0)) { sp = new Vector2(Input.mousePosition.x, Input.mousePosition.y); clicked = true; }
            // 路 2：触摸兜底。GetMouseButtonDown 在部分设备/Input System 模式下拿不到触摸，
            //       而 Input.GetTouch(...).phase 一定拿得到 —— 这很可能就是"点击完全没反应"的原因。
            else if (Input.touchCount > 0)
            {
                sp = new Vector2(0, 0);
                for (int t = 0; t < Input.touchCount; t++)
                {
                    var tc = Input.GetTouch(t);
                    if (tc.phase == TouchPhase.Began || tc.phase == TouchPhase.Ended) { sp = tc.position; clicked = true; break; }
                }
            }
            else return;`);
  log.push('  ✓ 触摸兜底');
}

fs.writeFileSync(P, s, 'utf8');
console.log(log.join('\n'));
