// 主界面按钮改为**自绘点击判定**：不再依赖 EventSystem/GraphicRaycaster 的点击派发。
//
// ## 为什么推翻 EventSystem 方案
// 真机实测（0.1.25）：我加了 `EventSystem + StandaloneInputModule`，按钮仍**点不动**，
// 且 HUD 诊断显示触摸/点击从未到达回调。可能原因一堆（Input System 与旧 Input 的模块不匹配、
// 射线被遮挡、模块未启用…），而**每一条都要再烧一轮构建去验证**（一轮 ≈ 6 分钟）。
//
// ## 正解：绕过 EVentSystem，自己判定
// 主界面只有 5 个矩形按钮，判定逻辑就是"点在不在矩形里"：
//   · 每帧读 `Input.GetMouseButtonDown(0)`（adb tap、真机触摸、鼠标都会变成它）
//   · 把屏幕坐标转成 canvas 局部坐标（`RectTransformUtility.ScreenPointToLocalPointInRectangle`）
//   · 与每个按钮的 `rect` 比较 → 命中就调回调
// 代价是失去"悬停高亮"等 UI 高级特性 —— 恐怖游戏主界面根本不需要。
// **收益是不再有看不见的输入失败**，这与本项目"失败必须可见"的纪律一致。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
let s = fs.readFileSync(P, 'utf8');
const log = [];

// ① 记录按钮矩形 + 回调（供自绘判定用）
if (!s.includes('_hitButtons')) {
  s = s.replace('        string _btnRects = "";',
`        string _btnRects = "";
        /// <summary>按钮命中区（canvas 局部坐标的矩形 + 回调）。自绘点击判定用。</summary>
        readonly System.Collections.Generic.List<(Rect rect, System.Action act, string label)> _hitButtons =
            new System.Collections.Generic.List<(Rect, System.Action, string)>();
        /// <summary>承载按钮的 Canvas（把屏幕坐标转它局部坐标用）。</summary>
        Canvas _canvas;`);
  log.push('  ✓ 命中区字段');
}

// ② BuildOptions 里记住 canvas，并在建完按钮后填命中区
s = s.replace('            canvasGo.AddComponent<GraphicRaycaster>();',
`            canvasGo.AddComponent<GraphicRaycaster>();
            _canvas = canvas;`);
s = s.replace('            for (int i = 0; i < items.Length; i++)\n            {\n                float y = 0.62f - i * 0.105f;\n                int captured = i;\n                SceneMaterials.Button(canvasGo.transform, "MenuBtn" + i, items[i],\n                    new Vector2(0.86f, y), new Vector2(300f, 66f),\n                    () => OnOption(captured, items[captured]));\n            }',
`            for (int i = 0; i < items.Length; i++)
            {
                float y = 0.62f - i * 0.105f;
                int captured = i;
                var btn = SceneMaterials.Button(canvasGo.transform, "MenuBtn" + i, items[i],
                    new Vector2(0.86f, y), new Vector2(300f, 66f),
                    () => OnOption(captured, items[captured]));
                // 自绘判定用：记下矩形与回调（矩形以**锚点为原点**，与点击换算同源）
                var brt = btn.GetComponent<RectTransform>();
                var hit = new Rect(-brt.sizeDelta.x * 0.5f, -brt.sizeDelta.y * 0.5f,
                                   brt.sizeDelta.x, brt.sizeDelta.y);
                _hitButtons.Add((hit, () => OnOption(captured, items[captured]), items[captured]));
            }`);
log.push('  ✓ 命中区填充');

// ③ Update 里加自绘点击判定
if (!s.includes('HandleSelfDrawClick')) {
  s = s.replace('        void Update()\n        {\n            LogTouchesOnce();',
`        /// <summary>自绘点击判定（不依赖 EventSystem）。</summary>
        void HandleSelfDrawClick()
        {
            // adb tap / 真机触摸 / 鼠标在 Unity 里都会变成 GetMouseButtonDown(0)
            if (!Input.GetMouseButtonDown(0)) return;
            Vector2 sp = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
            if (_canvas == null) return;

            for (int i = 0; i < _hitButtons.Count; i++)
            {
                var b = _hitButtons[i];
                // 把屏幕点转到**按钮所在层级的局部坐标**再与矩形比较：
                // 直接比屏幕坐标会因 CanvasScaler 的缩放而错位（这正是我先前按"看着像"的像素点失败的原因之一）。
                var btn = _canvas.transform.Find("MenuBtn" + i) as RectTransform;
                if (btn == null) continue;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(btn, sp, null, out var local);
                if (b.rect.Contains(local))
                {
                    _lastTouch = "命中 " + b.label + " @" + sp.x.ToString("F0") + "," + sp.y.ToString("F0");
                    Debug.Log("[Whisper] 主界面按钮 " + i + " 被点击：" + b.label);
                    b.act?.Invoke();
                    return;
                }
            }
            _lastTouch = "未命中 @" + sp.x.ToString("F0") + "," + sp.y.ToString("F0");
            Debug.Log("[Whisper] 主界面点击未命中任何按钮：" + _lastTouch);
        }

        void Update()
        {
            LogTouchesOnce();
            HandleSelfDrawClick();`);
  log.push('  ✓ 自绘点击判定');
}

fs.writeFileSync(P, s, 'utf8');
console.log(log.join('\n'));
