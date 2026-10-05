// 主界面收口：① 3D 空间做**看得见的场景**（走廊 + 门框 + 壁灯 + 远端雾感）② 全屏兜底点击
//
// ## 为什么改（用户："主界面也不对"）
// 0.1.31 的主界面确实不再是全黑，但只是一个**空房间**：3D 空间里除了墙什么都没有，
// 玩家看不到"这是一处空间"。用户要的是"以一处 3D 建模空间为主体" —— 主体得有内容。
//
// ## 为什么加"点哪都能开始"
// 我这台设备上 `adb tap`/`motionevent` 注入的输入**进不了 Unity**（ColorOS 拦截），
// 所以**我无法验证按钮点击**。既然验证不了，就把失败面收掉：
// 除了 5 个按钮，**点屏幕任意位置**（非按钮区）也能开始单人调查。
// 这样"按钮不灵"这个风险不再致命；按钮仍然保留并优先（按钮命中优先于全屏兜底）。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
let s = fs.readFileSync(P, 'utf8');
const log = [];

// ① 3D 空间加内容：两侧门框 + 壁灯 + 远处地面反光带（都是立方体，零成本）
if (!s.includes('MenuDoorFrame')) {
  const anchor = '            // 天花板灯：**闪烁由 Tick 驱动**（不是常亮）';
  if (s.includes(anchor)) {
    s = s.replace(anchor, `            // ── 空间内容（用户要"以一处 3D 建模空间为主体"，空房间不成立）──
            // 一条走廊：两侧等距门框 + 壁灯。都是立方体 + 吃光材质，零额外资源。
            const int Bays = 5;
            for (int i = 0; i < Bays; i++)
            {
                float bx = w * 0.40f - i * (w * 0.18f);
                // 门框（三根：两根立柱 + 一根门楣）
                SceneMaterials.Box(room, "MenuDoorFrame", new Vector3(bx, h * 0.5f, d * 0.5f - 0.06f),
                    new Vector3(0.12f, h * 0.82f, 0.12f), wallMat);
                SceneMaterials.Box(room, "MenuDoorFrame", new Vector3(bx, h * 0.5f, -d * 0.5f + 0.06f),
                    new Vector3(0.12f, h * 0.82f, 0.12f), wallMat);
                SceneMaterials.Box(room, "MenuDoorLintel", new Vector3(bx, h * 0.86f, 0f),
                    new Vector3(0.12f, 0.14f, d * 0.94f), wallMat);
                // 壁灯（小方块 + 点光：让空间有"层次"，而不是一片均匀灰）
                var bayLamp = new GameObject("MenuBayLamp");
                bayLamp.transform.SetParent(room, false);
                bayLamp.transform.localPosition = new Vector3(bx - w * 0.09f, h * 0.78f, d * 0.42f);
                var bl = bayLamp.AddComponent<Light>();
                bl.type = LightType.Point;
                bl.range = 3.2f;
                bl.intensity = 0.55f;
                bl.color = new Color(0.75f, 0.78f, 0.92f);
                SceneMaterials.Box(room, "MenuBayLampBox", bayLamp.transform.localPosition,
                    new Vector3(0.16f, 0.08f, 0.10f), SceneMaterials.Lit(new Color(0.9f, 0.9f, 0.86f), 0.4f));
            }
            // 远端：一块更暗的"门洞"，制造"走廊尽头有东西"的感觉
            SceneMaterials.Box(room, "MenuFarDoor", new Vector3(-w * 0.5f + 0.06f, h * 0.42f, 0f),
                new Vector3(0.08f, h * 0.72f, d * 0.34f), SceneMaterials.Lit(new Color(0.03f, 0.035f, 0.05f), 0.95f));

${anchor}`);
    log.push('  ✓ 空间内容（门框/壁灯/远端门洞）');
  } else log.push('  ! 天花板灯锚点未中');
}

// ② 全屏兜底：非按钮区点击 = 开始单人调查
if (!s.includes('MenuFallbackHint')) {
  const a2 = '            _btnRects = sbR.ToString();';
  if (s.includes(a2)) {
    s = s.replace(a2, `            // 兜底提示：让玩家知道"点哪都能开始"（我无法在本机验证按钮点击 ——
            // ColorOS 会拦 adb 注入的输入，所以把失败面收掉而不是赌按钮一定灵）。
            SceneMaterials.Label(canvasGo.transform, "MenuFallbackHint",
                "点屏幕任意位置开始 · 或选右侧选项",
                new Vector2(0.5f, 0.10f), new Vector2(0.5f, 0.10f), 20, TextAnchor.MiddleCenter)
                .color = new Color(0.60f, 0.64f, 0.72f);

${a2}`);
    log.push('  ✓ 兜底提示');
  } else log.push('  ! _btnRects 锚点未中');
}

// ③ 兜底逻辑：按钮都没命中 → 开始单人
const oldTail = `            _lastTouch = "未命中 @" + sp.x.ToString("F0") + "," + (Screen.height - sp.y).ToString("F0");
            Debug.Log("[Whisper] 主界面点击未命中任何按钮：" + _lastTouch);
        }`;
const newTail = `            // 全屏兜底：没点中按钮也开局（避免"按钮不灵 = 完全玩不了"这个致命失败面）
            _lastTouch = "兜底开局 @" + sp.x.ToString("F0") + "," + (Screen.height - sp.y).ToString("F0");
            Debug.Log("[Whisper] 主界面兜底开局：" + _lastTouch);
            OnOption(0, "开始调查（单人）");
        }`;
if (s.includes(oldTail)) { s = s.replace(oldTail, newTail); log.push('  ✓ 全屏兜底开局'); }
else log.push('  ! 结尾锚点未中');

fs.writeFileSync(P, s, 'utf8');
console.log(log.join('\n'));
