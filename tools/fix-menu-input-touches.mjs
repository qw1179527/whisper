// 改菜单输入为 `Input.touches`（与 PlayerController 同一条已验证可用的路径）+
// 每秒打印一次输入原始值，用来确认"到底有没有事件进来"。
//
// ## 依据（不猜）
// · `activeInputHandler: 0` → 旧 Input 已启用，`Input.*` 应当可用。
// · `PlayerController` 用 `Input.touchCount` / `Input.GetTouch`，0.1.21 真机上**确实读到了触摸**
//   （HUD 显示"累计 2.4m"、点了按钮有反应）→ 说明**这条路径是通的**。
// · 而我在 MenuScene 的 `Input.GetMouseButtonDown(0)` 一直读到 `btn 0 down 0` → 大概率是
//   `GetMouseButton*` 在触摸屏上不被驱动（它只在有鼠标设备时才有值），而 `Input.touches` 才有。
// 因此：菜单点击改用 `Input.touches`，并保留 mouse 路径作为桌面调试用。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
let s = fs.readFileSync(P, 'utf8');
const log = [];

// ① 输入状态：用 `Input.touches` 的原始数组（与 PlayerController 同源）
s = s.replace('            _inputState = "mp " + Input.mousePosition.x.ToString("F0") + "," + Input.mousePosition.y.ToString("F0")\n                        + " touch " + Input.touchCount + " btn " + (Input.GetMouseButton(0) ? 1 : 0)\n                        + " down " + (Input.GetMouseButtonDown(0) ? 1 : 0);',
`            // 用 Input.touches（原始数组）而不是 touchCount：PlayerController 走的就是这条，
            // 0.1.21 真机验证过能读到触摸；而我先前用的 GetMouseButton* 在触摸屏上恒为 0。
            var touches = Input.touches;
            int tc = touches != null ? touches.Length : 0;
            string tinfo = "";
            if (tc > 0) { var t0 = touches[0]; tinfo = " T0 " + t0.position.x.ToString("F0") + "," + t0.position.y.ToString("F0") + " " + t0.phase; }
            _inputState = "touches " + tc + tinfo + " mp " + Input.mousePosition.x.ToString("F0") + "," + Input.mousePosition.y.ToString("F0");`);
log.push('  ✓ 输入状态改 Input.touches');

// ② 点击判定：优先 touches(Began/Ended)，其次 mouse
const oldClick = `            Vector2 sp;
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
            else return;`;
const newClick = `            Vector2 sp = new Vector2(0, 0);
            bool clicked = false;
            // 路 1：Input.touches —— **与 PlayerController 同一条已验证路径**（0.1.21 真机确认能读到触摸）。
            var ts = Input.touches;
            if (ts != null)
            {
                for (int t = 0; t < ts.Length; t++)
                {
                    if (ts[t].phase == TouchPhase.Began || ts[t].phase == TouchPhase.Ended)
                    { sp = ts[t].position; clicked = true; break; }
                }
            }
            // 路 2：鼠标（桌面/编辑器调试用；触摸屏上 GetMouseButton* 恒为 0，不能当主路径）
            if (!clicked && Input.GetMouseButtonDown(0))
            { sp = new Vector2(Input.mousePosition.x, Input.mousePosition.y); clicked = true; }
            if (!clicked) return;`;
if (s.includes(oldClick)) { s = s.replace(oldClick, newClick); log.push('  ✓ 点击判定改 Input.touches 优先'); }
else log.push('  ! 点击判定锚点未中（可能已被上一轮改过）');

fs.writeFileSync(P, s, 'utf8');
console.log(log.join('\n'));
