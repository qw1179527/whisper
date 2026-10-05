// 给 `tools/whisper-model.mjs` 加 **UV 展开**（导出前）。
//
// ## 为什么这是关键修复
// 实测：网格 9.9k 顶点 → **GLB 里 83k 顶点（≈10×）**。
// 根因不是细分太多，而是**网格没有 UV 层** —— glTF 导出器在无 UV 时必须为**每个面角**复制顶点
// （它无法用 UV 索引复用），于是顶点数按"面角数"爆炸。
// 加 `<UVMap>` 后顶点可复用，顶点数应回落到 ≈ 网格顶点数。
//
// 顺带：有 UV 之后才能上贴图/贴花，这也是后续"不要纯色方体"的前提。
import fs from 'node:fs';

const P = 'tools/whisper-model.mjs';
let s = fs.readFileSync(P, 'utf8');

// 在"烘焙修改器 → 合并"之后、"整体缩放"之前插入 UV 展开
const anchor = `bpy.ops.object.join()
j = bpy.context.view_layer.objects.active
j.name = 'GEO-' + TAG + '_' + ARGS['bodyName']`;

const inject = `bpy.ops.object.join()
j = bpy.context.view_layer.objects.active
j.name = 'GEO-' + TAG + '_' + ARGS['bodyName']

# ── UV 展开（**导出前必做**）────────────────────────────────────────────────
# 没有 UV 时 glTF 导出器会为每个面角复制顶点 → 实测 9.9k 网格顶点变成 83k GLB 顶点（≈10×）。
# 有了 UV 层，导出器可以按 UV 索引复用顶点，顶点数回落到 ≈ 网格顶点数。
# 用 smart_project：对程序化网格足够，且不需要人工标记接缝。
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)
bpy.ops.object.mode_set(mode='OBJECT')`;

if (s.includes(anchor)) { s = s.replace(anchor, inject); console.log('  ✓ UV 展开已插入'); }
else console.log('  ! 锚点未找到');

fs.writeFileSync(P, s, 'utf8');
