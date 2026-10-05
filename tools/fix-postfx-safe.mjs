// 【本轮取舍，必须写清楚，否则下轮会有人"顺手打开"再踩一遍】
//
// 0.1.47~0.1.52 真机实测结论：
//   · 主界面 3D 场景本身渲染正常（关掉 PostFx 时画面明亮、走廊清晰）。
//   · 一旦开 PostFx 就整片黑，同时 HUD 报 raw=0.00（亮度读回恒为零）。
//   · 说明深度法线纹理与 1x1 RFloat 读回在"相机 OnRenderImage + 手工 Graphics.Blit 链"
//     里**拿不到有效数据**。SSAO/SSGI 依赖前者、眼部适应依赖后者 —— 两个依赖都不可靠。
//
// 决定：默认档位只启用**只依赖颜色**的效果（辉光/暗角/颗粒）。
//   这三个只需要主纹理，不可能因为深度/读回失效而黑屏；而"画面能看"是用户当下最在意的事。
//   SSAO/SSGI/眼部适应**不删**（代码与开关都在），但默认关，
//   并在 spec 台账里登记为"需先解决深度纹理在 Blit 链中的绑定问题"——**不假装做完了**。
//
// 注意：本文件里**不能出现反引号**（它会截断 JS 模板字符串，已失败 11 次）。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const log = [];
const NL = '\n';

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
    if (k === 'top') { t.bloom = true; t.bloomIntensity = 0.80; t.grain = true; t.grainAmount = 0.055; t.vignette = true; }
    t._disabledWhy = 'ssao/ssgi/eyeAdaptation/volumetricLight 默认关：它们在 OnRenderImage 的 Blit 链里拿不到深度/读回数据（真机 raw=0.00），开启会黑屏。见 docs/spec/supplement-2026-10-05-permanent.md 台账。';
  }
  fs.writeFileSync(P, JSON.stringify(cfg, null, 2) + NL, 'utf8');
  log.push('  ✓ ① 三档改成只依赖颜色的效果（辉光/暗角/颗粒）');
}

// ── ② 着色器：SSAO 加下限保护（深度无效时不可能把画面压黑） ──
{
  const P = path.join(ROOT, 'unity/Assets/Resources/Shaders/WhisperPostFx.shader');
  let s = fs.readFileSync(P, 'utf8');
  const oldBlock = [
    '                    occ = 1.0 - (occ / 8.0) * _WhisperFxAo;',
    '                    // 只压暗，不提亮：避免"发光的地板"这种反物理结果',
    '                    col *= lerp(1.0, saturate(occ), 1.0);',
  ].join(NL);
  const newBlock = [
    '                    occ = 1.0 - (occ / 8.0) * _WhisperFxAo;',
    '                    // 只压暗，不提亮：避免"发光的地板"这种反物理结果。',
    '                    // 【深度无效保护】本工程 PostFx 走 OnRenderImage + 手工 Blit，',
    '                    // 实测深度法线纹理在这里拿不到有效数据（HUD 亮度读回 raw=0.00）。',
    '                    // 深度无效时 occ 会退化成常量 —— 把它当有效结果用是错的，',
    '                    // 所以夹下限 0.35：遮蔽最多压到 35%，数学上不可能变成黑屏。',
    '                    occ = clamp(occ, 0.35, 1.0);',
    '                    col *= occ;',
  ].join(NL);
  if (s.includes(oldBlock)) { s = s.replace(oldBlock, newBlock); log.push('  ✓ ② SSAO 下限保护（最多压到 35%）'); }
  else log.push('  ! ② SSAO 锚点未中（可能已改过）');
  fs.writeFileSync(P, s, 'utf8');
}

// ── ③ PostFx：辉光阈值按暗场重新标定 ──
{
  const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/PostFx.cs');
  let s = fs.readFileSync(P, 'utf8');
  const oldLine = '            Shader.SetGlobalFloat("_WhisperFxBloomThreshold", 0.62f);   // Gamma 空间屏显阈值';
  const newLines = [
    '            // 【阈值标定依据】本场景是 Gamma 空间的暗场（实测画面均值远低于线性空间的高光量级）。',
    '            // 原值 0.62 是照"线性空间 1.0 以上才算高光"定的 → 辉光永不触发 = 开了看不出差别。',
    '            // 改 0.40：暗场里"比较亮的部分"就泛光，又不至于把整个画面糊成一团。',
    '            Shader.SetGlobalFloat("_WhisperFxBloomThreshold", 0.40f);',
  ].join(NL);
  if (s.includes(oldLine)) { s = s.replace(oldLine, newLines); log.push('  ✓ ③ 辉光阈值 0.62 → 0.40'); }
  else log.push('  ! ③ 阈值行未命中');
  fs.writeFileSync(P, s, 'utf8');
}

console.log(log.join(NL));
