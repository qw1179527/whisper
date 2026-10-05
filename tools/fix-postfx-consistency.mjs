// 0.1.50 真机读数：`后处理：开 曝光 0.35 亮度 0.000` —— 曝光贴在 clamp 下限。
// 两条修复：
//   ① **一致性 bug**：PostFx 用 `if (t.Bloom)` 决定是否 Blit 亮部，而 push 到着色器的是
//      `t.Bloom ? t.BloomIntensity : 0`。只要 BloomIntensity==0（低画质档就是这样），
//      就会出现"跑了全部 Blit、但着色器认为辉光关"的不一致状态 —— 白花开销且状态难推理。
//      正确判据只有一个：**强度 > 0**。
//   ② **亮度读回不可信**：`ReadPixels` 读 RFloat 在部分 Android 后端返回 0 →
//      适应算法朝"提亮"一路逼近（全黑画面 → 目标远高于当前）。修法：
//      读回值先做**有效性判断**（<=0 或非有限数一律视为"未知"），未知时保持上一次的值并
//      让曝光**回到中性 1.0**（而不是继续漂）。
//   ③ 合成 pass 加**直通分支**：所有效果都关时直接返回源色，用于二分定位"黑屏是不是合成造成的"。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const log = [];

// ── ① PostFx：判据统一为"强度 > 0" ──
{
  const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/PostFx.cs');
  let s = fs.readFileSync(P, 'utf8');
  const before = s;
  s = s.replace('            // ① 亮部 → 1/4 分辨率\n            if (t.Bloom)',
                '            // ① 亮部 → 1/4 分辨率\n            // ⚠ 判据必须与 PushGlobals 一致（那边是 t.BloomIntensity）：\n            //   曾用 t.Bloom，于是"强度 0 但开关 true"会跑完整套 Blit 却让着色器当它是关的。\n            bool bloomOn = t.Bloom && t.BloomIntensity > 0.001f;\n            if (bloomOn)');
  if (s !== before) log.push('  ✓ ① 辉光判据统一');
  else log.push('  ! ① 辉光判据锚点未中');

  const oldEye = `            float v = tex.GetPixel(0, 0).r;
            Destroy(tex);

            _logLuma = Mathf.Lerp(_logLuma, v, Mathf.Clamp01(dt * 4f));   // 读回本身有噪声，先平滑`;
  const newEye = `            float v = tex.GetPixel(0, 0).r;
            Destroy(tex);

            // 【0.1.50 真机教训】RFloat 的 ReadPixels 在部分 Android 后端返回 0/垃圾 →
            // 适应算法会朝"提亮"一路逼近（全黑画面 → 目标亮度远高于当前），曝光贴在 clamp 下限，
            // 而画面本来就暗，于是"越适应越黑"。
            // 正解：读回值先验有效性 —— 无效时**不更新**亮度的平滑值，
            // 并把曝光按中性回落。读不到数据时最安全的动作是"什么都不做"。
            if (!(v > -30f && v < 30f))
            {
                RawLogLuma = 0f;
                _exposure = Mathf.Lerp(_exposure, 1f, Mathf.Clamp01(dt * 2f));
                return;
            }
            _logLuma = Mathf.Lerp(_logLuma, v, Mathf.Clamp01(dt * 4f));   // 读回本身有噪声，先平滑`;
  if (s.includes(oldEye)) { s = s.replace(oldEye, newEye); log.push('  ✓ ② 亮度读回有效性判断'); }
  else log.push('  ! ② 亮度读回锚点未中');

  fs.writeFileSync(P, s, 'utf8');
}

// ── ③ 着色器：合成 pass 加直通分支 ──
{
  const P = path.join(ROOT, 'unity/Assets/Resources/Shaders/WhisperPostFx.shader');
  let s = fs.readFileSync(P, 'utf8');
  if (!s.includes('全部效果都关')) {
    s = s.replace('                float3 col = tex2D(_MainTex, i.uv).rgb;',
`                float3 col = tex2D(_MainTex, i.uv).rgb;

                // 【0.1.50】直通分支：所有效果都关时**原样返回**。
                // 加它的目的有两个，都很实在：
                //   ① 二分定位 —— 黑屏到底出在"合成"还是"某个效果"？有这条就能一眼分开；
                //   ② 正确性 —— 全关时不该有任何一次多余的采样/乘法（省电，且避免"关了还变样"）。
                if (_WhisperFxBloom < 0.001 && _WhisperFxAo < 0.001 && _WhisperFxGi < 0.001
                    && _WhisperFxVolumetric < 0.001 && _WhisperFxEye < 0.001
                    && _WhisperFxGrain < 0.001 && _WhisperFxVignette < 0.001)
                    return fixed4(col, 1.0);`);
    log.push('  ✓ ③ 合成直通分支');
  } else log.push('  · ③ 直通分支已存在');

  // 曝光只在眼部适应开启时参与（否则关掉它却仍在乘，会让"关眼适应"其实没关）
  s = s.replace('                col *= max(_WhisperFxExposure, 0.05);',
`                // 曝光只在眼部适应**开启**时参与：否则"关了眼适应"画面仍被曝光系数改变，
                // 玩家会看到"选项没生效"（这类"关了还在起作用"是最难被发现的 bug 之一）。
                if (_WhisperFxEye > 0.001) col *= clamp(_WhisperFxExposure, 0.05, 4.0);`);
  log.push('  ✓ ③ 曝光受眼适应开关约束');
  fs.writeFileSync(P, s, 'utf8');
}

console.log(log.join('\n'));
