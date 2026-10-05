// 读 GLB 的**真实包围盒**（从 accessor 的 min/max 里读，不靠任何渲染或工具自报）。
//
// 为什么需要：我在对比图上折腾了 4 轮都判断不出"模型到底是站着还是躺着"，
// 而包围盒是**客观数字** —— X/Y/Z 三个跨度一比就知道朝向，不需要看图。
import fs from 'node:fs';

const files = process.argv.slice(2);
if (!files.length) { console.error('用法：node tools/glb-bbox.mjs <a.glb> [b.glb ...]'); process.exit(2); }

for (const f of files) {
  const buf = fs.readFileSync(f);
  const jl = buf.readUInt32LE(12);
  const g = JSON.parse(buf.subarray(20, 20 + jl).toString('utf8'));
  const acc = g.accessors || [];
  let mn = [Infinity, Infinity, Infinity], mx = [-Infinity, -Infinity, -Infinity];
  for (const m of g.meshes || []) for (const p of m.primitives) {
    const a = acc[p.attributes.POSITION];
    if (!a || !a.min || !a.max) continue;
    for (let i = 0; i < 3; i++) { mn[i] = Math.min(mn[i], a.min[i]); mx[i] = Math.max(mx[i], a.max[i]); }
  }
  const size = [mx[0] - mn[0], mx[1] - mn[1], mx[2] - mn[2]];
  const axis = size[1] > size[2] && size[1] > size[0] ? 'Y 最长 → **Y-up（glTF 标准）**'
             : size[2] > size[1] && size[2] > size[0] ? 'Z 最长 → **Z-up（Unity 约定）**'
             : 'X 最长 → 躺平/异常';
  const name = f.split(/[\\/]/).pop();
  console.log(`  ${name}`);
  console.log(`    min [${mn.map(v => v.toFixed(3)).join(', ')}]  max [${mx.map(v => v.toFixed(3)).join(', ')}]`);
  console.log(`    跨度 X=${size[0].toFixed(3)}  Y=${size[1].toFixed(3)}  Z=${size[2].toFixed(3)}  → ${axis}`);
}
