// 【本轮取舍得写清楚，不然下轮会有人"顺手打开"再踩一遍】
//
// 0.1.47~0.1.52 真机实测结论：
//   · 主界面 3D 场景本身渲染正常（关掉 PostFx 时画面明亮、走廊清晰）。
//   · 一旦开 PostFx 就整片黑，同时 HUD 报 `raw=0.00`（亮度读回恒为零）。
//   · 说明 `_CameraDepthNormalsTexture` 与 1x1 RFloat 读回在
//     "相机 OnRenderImage + 手工 Graphics.Blit 链" 里**拿不到有效数据**。
//     SSAO/SSGI 依赖前者、眼部适应依赖后者 —— 两个依赖都不可靠。
//
// 决定：**默认档位改为只启用"只依赖颜色"的效果**（辉光/暗角/颗粒）。
//   理由：这三个只需要 `_MainTex`，不可能因为深度/读回失效而黑屏；
//   而"画面能看"是用户当下最在意的事（他已反馈黑屏）。
//   SSAO/SSGI/眼部适应**不删**（代码与开关都在），但默认关，并在 spec 里登记为
//   "需要先解决深度纹理在 Blit 链中的绑定问题"才能开 —— 不假装做完了。
//
// 同时把辉光阈值按**本场景的真实亮度分布**下调：
//   实测画面均值远低于 0.62（那是我按"线性空间高光"想当然定的），
//   阈值过高 = 辉光永不触发 = "开了看不出差别"。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const log = [];

// ── ① 配置：三档统一关掉深度/读回依赖项 ──
{
  const P = path.join(ROOT, 'data/config.json');
  const cfg = JSON.parse(fs.readFileSync(P, 'utf8'));
  const tiers = cfg.render.tiers;
  for (const k of ['low', 'high', 'top']) {
    const t = tiers[k];
    t.ssao = false;
    t.ssgi = false;
    t.eyeAdaptation = false;
    t.volumetricLight = false;
    if (k === 'low') { t.bloom = false; t.bloomIntensity = 0; t.grain = false; t.grainAmount = 0; t.vignette = true; }
    if (k === 'high') { t.bloom = true; t.bloomIntensity = 0.55; t.grain = true; t.grainAmount = 0.04; t.vignette = true; }
    if (k === 'top') { t.bloom = true; t.bloomIntensity = 0.8; t.grain = true; t.grainAmount = 0.055; t.vignette = true; }
    t._note_disabled = "ssao/ssgi/eyeAdaptation/volumetricLight 默认关：它们在 OnRenderImage 的 Blit 链里拿不到深度/读回数据（真机 raw=0.00），开启会导致黑屏。见 docs/spec/supplement-2026-10-05-permanent.md 台账。";
  }
  fs.writeFileSync(P, JSON.stringify(cfg, null, 2) + '\n', 'utf8');
  log.push('  ✓ ① 三档改成只依赖颜色的效果');
}

// ── ② 着色器：辉光阈值改全局量（按场景亮度标定），并让深度分支真正可跳过 ──
{
  const P = path.join(ROOT, 'unity/Assets/Resources/Shaders/WhisperPostFx.shader');
  let s = fs.readFileSync(P, 'utf8');
  // 阈值升为全局量（原先硬编码 0.62，按线性空间想当然；本场景是 Gamma 暗场）
  s = s.replace('            float  _WhisperFxBloomThreshold;', '            float  _WhisperFxBloomThreshold;');
  // 深度相关分支加重入保护：AO/GI 强度为 0 时**整段不执行**（已由 if 保证），
  // 但还要保证"即使强度非 0、深度纹理无效"时也不会把画面乘黑。
  if (!s.includes('深度无效保护')) {
    s = s.replace('                    occ = 1.0 - (occ / 8.0) * _WhisperFxAo;\n                    // 只压暗，不提亮：避免"发光的地板"这种反物理结果\n                    col *= lerp(1.0, saturate(occ), 1.0);',
`                    occ = 1.0 - (occ / 8.0) * _WhisperFxAo;
                    // 只压暗，不提亮：避免"发光的地板"这种反物理结果
                    // 【深度无效保护】本工程的 PostFx 走 OnRenderImage + 手工 Blit，
                    // 实测 `_CameraDepthNormalsTexture` 在这里拿不到有效数据（HUD raw=0.00）。
                    // 深度无效时 occ 会退化成常量，把它当一个**已知无效**的结果使用是错的，
                    // 所以夹一个下限：遮蔽最多把画面压到 35%，不可能压成黑屏。
                    occ = clamp(occ, 0.35, 1.0);
                    col *= occ;`);
    log.push('  ✓ ② SSAO 加下限保护（最多压到 35%）');
  }
  fs.writeFileSync(P, s, 'utf8');
}

// ── ③ PostFx：阈值全局量 + 深度依赖项汇总诊断 ──
{
  const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/PostFx.cs');
  let s = fs.readFileSync(P, 'utf8');
  s = s.replace('            Shader.SetGlobalFloat("_WhisperFxBloomThreshold", 0.62f);   // Gamma 空间屏显阈值',
`            // 【标定依据】本场景是 Gamma 空间的暗场（实测画面均值远低于线性空间的高光量级）。
            // 阈值 0.62 是我按"线性空间 1.0 以上才算高光"想当然定的 → 辉光永不触发 = 开了看不出差别。
            // 改用 0.40：暗场里"比较亮的部分"就能泛光，同时不至于把整个画面糊成一团。
            Shader.SetGlobalFloat("_WhisperFxBloomThreshold", 0.40f);`);
  fs.writeFileSync(P, s, 'utf8');
  log.push('  ✓ ③ 辉光阈值 0.62 → 0.40（按暗场实际亮度标定）');
}

console.log(log.join('\n'));
