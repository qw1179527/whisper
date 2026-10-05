// 核验 `Assets/Resources/Models/**/*.glb.bytes`：每个文件的真实性（魔数、长度、顶点数、三角面数）
//
// 为什么需要这个工具：Blender 导出**不报错也会产出空文件** ——
// 实测本轮"鬼怪眼球"里 `hiddenRender=true` 的那几件，导出日志**没有** "Primitives created"，
// 但 `export_scene.gltf` 依然返回成功。若只看"导出成功"，就会把空模型当资产交给游戏。
// 这正是本项目反复踩的"汇报与事实不符"：**必须从产出字节里读数**。
import fs from 'node:fs';
import path from 'node:path';

const ROOT = process.argv[2] || 'unity/Assets/Resources/Models';

/** 从 GLB 里读出 JSON 块并统计：mesh 数、primitive 数、accessor 声明的顶点/索引数。 */
function inspectGlb(buf) {
  if (buf.length < 20) return { ok: false, why: '文件过短' };
  const magic = buf.readUInt32LE(0);
  if (magic !== 0x46546c67) return { ok: false, why: '魔数不是 glTF' };
  const version = buf.readUInt32LE(4);
  const declared = buf.readUInt32LE(8);
  if (declared !== buf.length) return { ok: false, why: `声明长度 ${declared} != 实际 ${buf.length}` };

  // 第一个 chunk 必须是 JSON
  const chunkLen = buf.readUInt32LE(12);
  const chunkType = buf.readUInt32LE(16);
  if (chunkType !== 0x4e4f534a) return { ok: false, why: '第一个 chunk 不是 JSON' };
  let json;
  try {
    json = JSON.parse(buf.slice(20, 20 + chunkLen).toString('utf8'));
  } catch (e) {
    return { ok: false, why: 'JSON 块解析失败：' + e.message };
  }

  const meshes = json.meshes || [];
  const accessors = json.accessors || [];
  let vertices = 0, indices = 0, prims = 0;
  for (const m of meshes) {
    for (const p of m.primitives || []) {
      prims++;
      if (p.attributes && p.attributes.POSITION !== undefined) {
        vertices += accessors[p.attributes.POSITION]?.count || 0;
      }
      if (p.indices !== undefined) indices += accessors[p.indices]?.count || 0;
    }
  }
  return {
    ok: true, version,
    meshes: meshes.length, prims, vertices, triangles: Math.floor(indices / 3),
    materials: (json.materials || []).length,
    jsonBytes: chunkLen,
  };
}

function walk(dir) {
  const out = [];
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) out.push(...walk(p));
    else if (e.name.endsWith('.glb.bytes')) out.push(p);
  }
  return out;
}

if (!fs.existsSync(ROOT)) { console.error('目录不存在：' + ROOT); process.exit(2); }
const files = walk(ROOT).sort();
let bad = 0, empty = 0, totalV = 0, totalT = 0;

console.log(`[model-verify] ${ROOT} · 共 ${files.length} 个 .glb.bytes`);
for (const f of files) {
  const buf = fs.readFileSync(f);
  const r = inspectGlb(buf);
  const rel = path.relative(ROOT, f).replace(/\\/g, '/');
  if (!r.ok) { console.log(`  ✗ ${rel.padEnd(42)} ${r.why}`); bad++; continue; }
  if (r.vertices === 0) { console.log(`  ✗ ${rel.padEnd(42)} **空几何**（0 顶点，${r.meshes} mesh / ${r.prims} prim）`); empty++; continue; }
  totalV += r.vertices; totalT += r.triangles;
  console.log(`  ✓ ${rel.padEnd(42)} ${String(Math.round(buf.length / 1024)).padStart(4)}KB · ${r.vertices} 顶点 · ${r.triangles} 面 · ${r.materials} 材质`);
}

console.log(`\n  合计：${files.length - bad - empty} 个有效 · ${empty} 个空几何 · ${bad} 个损坏`);
console.log(`  顶点总数 ${totalV} · 三角面总数 ${totalT}`);
if (bad || empty) { console.log('  ✗ 存在无效模型（不能交给游戏）'); process.exit(1); }
console.log('  ✓ 全部模型有效');
