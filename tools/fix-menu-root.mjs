// 根治主界面的两个问题（用户：「点按钮还是没反应，主界面也不对」）。
//
// ## 问题 1：按钮点不动 —— 不再靠坐标系推理，改为**两种口径都判**
// 事实（读数得来，不是猜）：设备窗口 2800×1280，`adb tap y=790` 在游戏里被 `Input.mousePosition`
// 读成 y≈486。说明游戏看到的**视口高度**与 `Screen.height` 不是一回事（异形屏/切边）。
// 我先前用 `ScreenPointToLocalPointInRectangle(btn, sp, ...)` 单口径判定，于是只要口径差一点就全不命中。
// **正解**：判定时**两种 y 口径都试一遍**（原样 / 上下翻转），任一命中即算点击。
//   代价：理论上会多点中一个不存在的镜像位置 —— 但按钮彼此垂直分离，实际不会误触；
//   收益：不再依赖我推算的口径，**点得到就是点得到**。
//
// ## 问题 2：主界面 3D 空间全黑 —— Unlit 材质不吃灯
// 我用 `SceneMaterials.Make`（走 `Whisper/UnlitColor`）给墙面/地板，而 **Unlit 不响应任何灯光**，
// 所以即使加了方向光也全黑。正解：主界面房间里改用**吃光**的标准材质（`Standard` 回退）：
// 恐怖氛围靠"把灯调暗"，而不是靠"材质不吃光"。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
let s = fs.readFileSync(P, 'utf8');
const log = [];

// ── 修复 1：双向口径命中 ──
const oldHit = `                RectTransformUtility.ScreenPointToLocalPointInRectangle(btn, sp, null, out var local);
                if (b.rect.Contains(local))
                {`;
const newHit = `                // 两种 y 口径都试：设备实测 mousePosition 的 y 与 Screen.height 不同源，
                // 单口径判定只要差一点就全不命中（这就是"按钮点不动"的直接原因）。
                RectTransformUtility.ScreenPointToLocalPointInRectangle(btn, sp, null, out var local);
                var spFlipped = new Vector2(sp.x, Screen.height - sp.y);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(btn, spFlipped, null, out var localFlipped);
                bool hit = b.rect.Contains(local) || b.rect.Contains(localFlipped);
                if (hit)
                {`;
if (s.includes(oldHit)) { s = s.replace(oldHit, newHit); log.push('  ✓ 双向口径命中'); }
else log.push('  ! 命中判定锚点未中');

// ── 修复 2：房间材质改吃光 ──
const oldMat = `            Material floorMat = SceneMaterials.Make(Color.Lerp(SceneMaterials.RoomTint, Color.black, 0.45f), 0.92f);
            Material wallMat = SceneMaterials.Make(Color.Lerp(SceneMaterials.RoomTint, Color.black, 0.62f), 0.88f);`;
const newMat = `            // ⚠ 主界面房间必须用**吃光材质**：先前走 SceneMaterials.Make（= Whisper/UnlitColor）时
            // **Unlit 不响应任何灯光** → 即使加了方向光房间也全黑（用户："主界面也不对"）。
            // 恐怖氛围靠"把灯调暗"实现，不靠"材质不吃光"。
            Material floorMat = SceneMaterials.Lit(Color.Lerp(SceneMaterials.RoomTint, Color.black, 0.35f), 0.90f);
            Material wallMat = SceneMaterials.Lit(Color.Lerp(SceneMaterials.RoomTint, Color.black, 0.55f), 0.86f);`;
if (s.includes(oldMat)) { s = s.replace(oldMat, newMat); log.push('  ✓ 房间改吃光材质'); }
else log.push('  ! 房间材质锚点未中');

// ── 新增 SceneMaterials.Lit（吃光材质）──
if (!s.includes('public static Material Lit(')) {
  const anchor = '        /// <summary>自发光材质（rgb=颜色 · **a=强度**，与本项目 `_WhisperEmission` 的契约一致）。</summary>';
  if (s.includes(anchor)) {
    s = s.replace(anchor, `        /// <summary>**吃光**材质（响应场景灯光）。用于主界面房间这类"要能被灯照亮"的表面。</summary>
        /// <remarks>
        /// 为什么要单独一个方法：本项目默认材质走 \`Whisper/UnlitColor\`（**不吃光**，几何在暗场里
        /// 靠自发光可见）。但主界面房间需要"灯亮则亮、灯灭则暗"的观感，用 Unlit 会永远全黑 ——
        /// 我因此白烧了一轮构建（0.1.22~0.1.26 主界面 3D 空间都是黑的）。
        /// </remarks>
        public static Material Lit(Color c, float roughness)
        {
            string key = "L" + ColorKey(c) + "|" + roughness.ToString("F2");
            if (_cache.TryGetValue(key, out var hit) && hit != null) return hit;
            var sh = Shader.Find("Standard") ?? Shader.Find("Whisper/UnlitColor");
            var m = new Material(sh);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 1f - roughness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            _cache[key] = m;
            return m;
        }

${anchor}`);
    log.push('  ✓ SceneMaterials.Lit');
  } else log.push('  ! 自发光材质注释锚点未中');
}

// ── 修复 3：把房间亮度拉起来（灯调亮一档，保证"看得见"） ──
s = s.replace('        public float LightBase = 1.15f;', '        /// <summary>灯基准强度。1.15 → 2.2：Unlit 改吃光后必须整体提亮，否则夜空黑得看不出场景。</summary>\n        public float LightBase = 2.2f;');
s = s.replace('            keyLight.intensity = 0.55f;', '            keyLight.intensity = 1.05f;');
s = s.replace('            fill.intensity = 0.10f;', '            fill.intensity = 0.28f;');
log.push('  ✓ 亮度提升（灯 2.2 / 方向光 1.05 / 补光 0.28）');

fs.writeFileSync(P, s, 'utf8');
console.log(log.join('\n'));
