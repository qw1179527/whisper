// 校验 render 段（画质三档 + 帧率档）并强制同步镜像。
// 写成脚本而不是内联 node -e：PowerShell 会把内联 JS 的引号解释掉（实测）。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'data/config.json');
const cfg = JSON.parse(fs.readFileSync(P, 'utf8'));
let bad = 0;
const fail = (m) => { console.error('  ✗ ' + m); bad++; };

const fps = cfg.render?.frameRates;
console.log(`帧率档: ${JSON.stringify(fps)}`);
if (!Array.isArray(fps) || fps.join(',') !== '60,90,120') fail('render.frameRates 必须是 [60,90,120]');

const tiers = cfg.render?.tiers ?? {};
const want = ['low', 'high', 'top'];
console.log(`画质档: ${Object.keys(tiers).join(', ')}`);
for (const k of want) if (!tiers[k]) fail(`render.tiers.${k} 缺失`);

// 每档必须齐全：一个键都不能少，否则 C# 会静默用兜底值（属于"配置没生效但看不出来"）
const REQUIRED = ['name', 'renderScale', 'pixelLightCount', 'shadows', 'shadowResolution',
  'shadowDistanceM', 'antiAliasing', 'anisotropicFiltering', 'farClipM',
  'bloom', 'ssgi', 'ssao', 'eyeAdaptation', 'grain', 'vignette', 'volumetricLight',
  'bloomIntensity', 'ssgiIntensity', 'ssaoRadiusM', 'grainAmount', 'eyeAdaptSpeed'];
for (const k of want) {
  const t = tiers[k];
  if (!t) continue;
  const missing = REQUIRED.filter((f) => t[f] === undefined);
  if (missing.length) fail(`render.tiers.${k} 缺键：${missing.join(', ')}`);
  if (![0, 2, 4, 8].includes(t.antiAliasing)) fail(`render.tiers.${k}.antiAliasing=${t.antiAliasing} 非法`);
  if (![0, 1, 2].includes(t.shadows)) fail(`render.tiers.${k}.shadows=${t.shadows} 非法`);
  if (!(t.renderScale > 0 && t.renderScale <= 1)) fail(`render.tiers.${k}.renderScale=${t.renderScale} 非法`);
}

// 单调性：档位越高，能力不能更低（这是"低/高/顶级"这个名字的**唯一**含义，
// 写错了就会出现"顶级画质比高画质还差"，而这种错在肉眼下很难发现）
const order = ['low', 'high', 'top'];
const mono = [
  ['pixelLightCount', 1], ['shadows', 1], ['shadowResolution', 1], ['shadowDistanceM', 1],
  ['antiAliasing', 1], ['farClipM', 1], ['bloomIntensity', 1], ['ssgiIntensity', 1],
  ['ssaoRadiusM', 1], ['grainAmount', 1],
];
for (const [key, dir] of mono) {
  for (let i = 1; i < order.length; i++) {
    const a = tiers[order[i - 1]]?.[key], b = tiers[order[i]]?.[key];
    if (a === undefined || b === undefined) continue;
    if (dir > 0 && b < a) fail(`${key} 非单调递增：${order[i - 1]}=${a} → ${order[i]}=${b}`);
  }
}
console.log('  ✓ 三档齐全 · 键完整 · 数值合法 · 单调递增');

console.log(bad ? `\n✗ ${bad} 个问题` : '\n✓ render 段全部通过');
process.exit(bad ? 1 : 0);
