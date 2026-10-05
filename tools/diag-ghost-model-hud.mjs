// 把模型加载状态打到 HUD，并把主界面鬼放近一档（便于判读模型是否正确）。
// 本文件**不得出现反引号**（会截断 JS 模板，已失败 9 次）。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
let s = fs.readFileSync(P, 'utf8');
const log = [];

// ① 记录每次建鬼用的模型名与失败原因，接到 HUD
if (!s.includes('_ghostModelInfo')) {
  s = s.replace('        string _inputState = "";',
`        string _inputState = "";
        /// <summary>最近一次建鬼用的模型名（诊断用；release 下 Debug.Log 会被剥离，只能走 HUD）。</summary>
        string _ghostModelInfo = "未建";`);
  s = s.replace('            if (body == null)\n            {\n                try { body = ModelLibrary.InstantiateWhole("ghost", go.transform); }\n                catch (System.Exception e) { Debug.LogWarning("[Whisper] 旧鬼模型也失败：" + e.Message); }\n            }',
`            if (body == null)
            {
                _ghostModelInfo = (string.IsNullOrEmpty(picked) ? "池空" : picked + " 加载失败:" + ModelLibrary.LastProblem) + " → 回退旧模型";
                try { body = ModelLibrary.InstantiateWhole("ghost", go.transform); }
                catch (System.Exception e) { Debug.LogWarning("[Whisper] 旧鬼模型也失败：" + e.Message); }
            }
            else _ghostModelInfo = picked;`);
  log.push('  ✓ 模型名进 HUD');
}

// ② HUD 行带上模型名
s = s.replace('                _hint.text = string.Format("灯闪中 · 鬼 {0}/刷出 {1} · 触摸 {2} · 输入 [{3}]\\n按钮 {4}",\n                    GhostCount, GhostSpawned, _lastTouch, _inputState, _btnRects);',
`                _hint.text = string.Format("灯闪中 · 鬼 {0}/刷出 {1} · 模型 {2} · 触摸 {3} · 输入 [{4}]\\n按钮 {5}",
                    GhostCount, GhostSpawned, _ghostModelInfo, _lastTouch, _inputState, _btnRects);`);
log.push('  ✓ HUD 带模型名');

// ③ 鬼放近一档（原来 5.5~10.5m，在 26m 走廊里显得很小；改 4.0~7.0m 便于看清）
s = s.replace('        public float GhostMinDistM = 5.5f;', '        public float GhostMinDistM = 4.0f;');
s = s.replace('        public float GhostMaxDistM = 10.5f;', '        public float GhostMaxDistM = 7.5f;');
log.push('  ✓ 鬼距离 4.0~7.5m');

fs.writeFileSync(P, s, 'utf8');
console.log(log.join('\n'));
