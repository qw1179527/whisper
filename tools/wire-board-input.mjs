// 把菜单板输入/相机过渡/UI 贴位接进 Update（承 wire-board-ui）。
// **本文件不得出现反引号**。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
let s = fs.readFileSync(P, 'utf8');
const log = [];

// ① 每帧：菜单板 UI 贴位 + 相机过渡（排在点击判定之前，保证拾取用的是本帧的相机）
s = s.replace('            LogTouchesOnce();\n            HandleSelfDrawClick();',
`            LogTouchesOnce();
            // 先更新相机与 UI 位置，再做点击判定 —— 否则拾取射线用的是上一帧的相机（过渡期间会点偏）
            UpdateBoardCamera(Time.deltaTime);
            UpdateBoardUi();
            HandleSelfDrawClick();`);
log.push('  ✓ Update 调相机/UI');

// ② HandleSelfDrawClick：在算完 sp 之后，先给菜单板一次机会
s = s.replace('            if (clicked) _lastTouch = via + " " + sp.x.ToString("F0") + "," + sp.y.ToString("F0");\n            _inputState = via.Length > 0 ? via : "无输入";\n            if (!clicked) return;\n            if (_canvas == null) return;',
`            if (clicked) _lastTouch = via + " " + sp.x.ToString("F0") + "," + sp.y.ToString("F0");
            _inputState = via.Length > 0 ? via : "无输入";

            // 【主界面 UI 重构】先让**菜单板 3D 拾取**处理点击（官方：点菜单板进入操作视角）。
            // 它返回 true = 已消费（点在板上 / 按了空格），此时不要再落到旧的竖排按钮上。
            // 注意：空格是在**没有点击**时也要处理的，所以这一句必须在 clicked 判断之前。
            if (HandleBoardInput(sp, clicked)) return;

            if (!clicked) return;
            if (_canvas == null) return;`);
log.push('  ✓ 点击判定前置菜单板拾取');

fs.writeFileSync(P, s, 'utf8');
console.log(log.join('\n'));
