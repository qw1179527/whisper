/* 低语计划 · 灰盒源树分模块产物（tools/split-modules.mjs 生成，勿手改）
 * 模块：__m8 → m8.js
 * 职责：世界装配：Level DSL + Blender 套件 + 共享几何 → 可渲染网格与碰撞体。 V9 §19.1 C4「烘焙零编辑器」：没有 NavMesh 烘焙、没有 lightmap，全部运行时生成。
 * 来源：baseline/game-0.6.0.js 第 2222~2427 行（9517 字节，逐字节搬移）
 * 包装改写（唯一改动，可审计）：mod.exports → module.exports；__req("__mN") → require("__mN")
 * 依赖：__m5, __m7｜导出：WALL_COLORS, bakeInstances, bakeMonster, buildStructure, kitInstances
 */
'use strict';

    /**
     * 世界装配：Level DSL + Blender 套件 + 共享几何 → 可渲染网格与碰撞体。
     * V9 §19.1 C4「烘焙零编辑器」：没有 NavMesh 烘焙、没有 lightmap，全部运行时生成。
     */
    var __ns0 = require("__m7");
    var parseGLB = __ns0.parseGLB;
    var __ns1 = require("__m5");
    var roomWalls = __ns1.roomWalls,
        propBoxes = __ns1.propBoxes,
        KIT_FOOTPRINT = __ns1.KIT_FOOTPRINT;
    
    const WALL_COLORS = {
      mat_bone: [0.847, 0.812, 0.733],
      mat_soot: [0.055, 0.051, 0.047],
      mat_ink: [0.102, 0.102, 0.102],
      mat_rust: [0.431, 0.290, 0.184],
      mat_mold: [0.361, 0.549, 0.431],
      mat_blood: [0.545, 0.118, 0.118],
      mat_paper: [0.941, 0.902, 0.824],
    };
    
    /** 由关卡网格生成墙/地板/天花板的大网格（顶点色，避免材质切换） */
    function buildStructure(level) {
      const grid = level.grid;
      const W = level.gridSize.w;
      const H = level.gridSize.h;
      // 按房间矩形生成整面墙（含门洞），不再逐格拼条带——
      // 逐格方案只在横向合并，竖直墙永远碎成单格，视觉上就是一堆断续短墙。
      const doorCells = new Set((level.doors ?? []).map((d) => d.cell.join(',')));
      const wallSpans = roomWalls(level.rooms, doorCells, { thickness: 0.22, height: 3.2 });
      const verts = [];
      const idx = [];
    
      const quad = (a, b, c, d, n) => {
        const base = verts.length / 6;
        for (const p of [a, b, c, d]) verts.push(p[0], p[1], p[2], n[0], n[1], n[2]);
        idx.push(base, base + 1, base + 2, base, base + 2, base + 3);
      };
      const boxes = (cx, cz, sx, sz, y0, y1, colorIdx) => {
        const x0 = cx - sx / 2, x1 = cx + sx / 2, z0 = cz - sz / 2, z1 = cz + sz / 2;
        // 四个侧面
        quad([x0, y0, z0], [x1, y0, z0], [x1, y1, z0], [x0, y1, z0], [0, 0, -1]);
        quad([x1, y0, z1], [x0, y0, z1], [x0, y1, z1], [x1, y1, z1], [0, 0, 1]);
        quad([x0, y0, z1], [x0, y0, z0], [x0, y1, z0], [x0, y1, z1], [-1, 0, 0]);
        quad([x1, y0, z0], [x1, y0, z1], [x1, y1, z1], [x1, y1, z0], [1, 0, 0]);
        return base0(colorIdx);
      };
      const colorRanges = [];
      const base0 = (c) => { colorRanges[c] = colorRanges[c] ?? [verts.length / 6, 0]; return c; };
    
      // 墙条带（按 y 从地到顶）
      const before = verts.length / 6;
      for (const sp of wallSpans) {
        const hx = sp.cx - sp.sx / 2, hx2 = sp.cx + sp.sx / 2;
        const hz = sp.cz - sp.sz / 2, hz2 = sp.cz + sp.sz / 2;
        const h = sp.height;
        quad([hx, 0, hz], [hx2, 0, hz], [hx2, h, hz], [hx, h, hz], [0, 0, -1]);
        quad([hx2, 0, hz2], [hx, 0, hz2], [hx, h, hz2], [hx2, h, hz2], [0, 0, 1]);
        quad([hx, 0, hz2], [hx, 0, hz], [hx, h, hz], [hx, h, hz2], [-1, 0, 0]);
        quad([hx2, 0, hz], [hx2, 0, hz2], [hx2, h, hz2], [hx2, h, hz], [1, 0, 0]);
      }
      colorRanges.push({ name: 'walls', start: before, count: verts.length / 6 - before, color: WALL_COLORS.mat_bone });
    
      /**
       * 地板与天花：按关卡产物里的 floorRects **逐块**生成，而不是一张覆盖全图的 30×30 大平面。
       * 首版用单张跨 30m 的四边形，两个后果（用户反馈"天花板与地板没有"）：
       *  ① 大平面盖在实心岩体上（可走区只占网格一小部分）；
       *  ② 近平面裁剪下整片消失——抬头/低头看时地面就没了。
       * 逐块还有个硬收益：每块与房间一一对应，证据点/线路布置有落点。
       */
      const rects = level.floorRects ?? [{ x0: 0, z0: 0, x1: W, z1: H }];
      const fBefore = verts.length / 6;
      for (const r of rects) {
        quad([r.x0, 0, r.z0], [r.x0, 0, r.z1], [r.x1, 0, r.z1], [r.x1, 0, r.z0], [0, 1, 0]);
      }
      colorRanges.push({ name: 'floor', start: fBefore, count: verts.length / 6 - fBefore, color: WALL_COLORS.mat_soot });
    
      const cBefore = verts.length / 6;
      for (const r of rects) {
        quad([r.x0, 3.2, r.z0], [r.x1, 3.2, r.z0], [r.x1, 3.2, r.z1], [r.x0, 3.2, r.z1], [0, -1, 0]);
      }
      colorRanges.push({ name: 'ceiling', start: cBefore, count: verts.length / 6 - cBefore, color: WALL_COLORS.mat_ink });
    
      return {
        verts: new Float32Array(verts),
        indices: new Uint16Array(idx),
        colorRanges,
        wallSpans,
        spanCount: wallSpans.length,
      };
    }
    
    /** 套件实例放置（位置来自关卡 JSON 的 room.props） */
    function kitInstances(level) {
      const out = [];
      for (const room of level.rooms) {
        for (const p of room.props ?? []) {
          out.push({
            kit: p.kit,
            x: room.rect.x0 + p.pos[0],
            z: room.rect.z0 + p.pos[2],
            rot: ((p.rot ?? 0) * Math.PI) / 180,
          });
        }
      }
      for (const e of level.evidencePoints) out.push({ kit: 'evidence_case', x: e.pos.x, z: e.pos.z, rot: 0, evidenceId: e.id });
      for (const e of level.extractionPoints) out.push({ kit: e.id === 'deep' ? 'extract_deep' : 'extract_standard', x: e.pos.x, z: e.pos.z, rot: 0, extractionId: e.id });
      // 门：门格中心即门洞中心；绕 Y 轴旋转让门框横向贴合门洞。
      // 门框由 jamb_l/jamb_r/lintel 三件构成（总宽 1.0m，与门格同宽），
      // 首版是一个 1.0×0.26×2.35 的实心块，塞进门洞后从正面看就是一根柱子，把门堵死。
      for (const d of level.doors) {
        const rot = d.orientation === 'ns' ? 0 : Math.PI / 2;
        const variant = d.locked ? 'door_locked' : 'door_frame';
        out.push({ kit: variant + ':jamb_l', x: d.pos.x, z: d.pos.z, rot, doorId: d.id });
        out.push({ kit: variant + ':jamb_r', x: d.pos.x, z: d.pos.z, rot, doorId: d.id });
        if (!d.locked) {
          out.push({ kit: 'door_frame:lintel', x: d.pos.x, z: d.pos.z, rot, doorId: d.id });
          out.push({ kit: 'door_frame:panel', x: d.pos.x, z: d.pos.z, rot, doorId: d.id });
          out.push({ kit: 'door_frame:handle', x: d.pos.x, z: d.pos.z, rot, doorId: d.id });
        } else {
          out.push({ kit: 'door_locked:bar', x: d.pos.x, z: d.pos.z, rot, doorId: d.id });
        }
      }
    
      // 线索与可交互物：证据点带发光柱（远处可见），拾取物按固定种子分布
      const rand = (() => { let s = 424242; return () => ((s = (s * 1664525 + 1013904223) >>> 0) / 4294967296); })();
      for (const e of level.evidencePoints) {
        out.push({ kit: 'evidence_case', x: e.pos.x, z: e.pos.z, rot: 0, evidenceId: e.id });
        out.push({ kit: 'evidence_beacon', x: e.pos.x, z: e.pos.z, rot: 0, evidenceId: e.id, beacon: true });
      }
      const PICKUPS = ['battery_pickup', 'flare_pickup', 'sedative_pickup'];
      let idx = 0;
      for (const room of level.rooms) {
        if (room.lightZone === 'safe') continue;              // 安全区不放补给
        if (room.props.length === 0) continue;
        const kind = PICKUPS[idx++ % PICKUPS.length];
        const anchor = room.props[0];
        out.push({
          kit: kind,
          x: room.rect.x0 + anchor.pos[0] + 0.9,
          z: room.rect.z0 + anchor.pos[2] - 0.9,
          rot: 0,
          pickup: kind.replace('_pickup', ''),
        });
      }
      // 配电箱：每个区一台（切换灯光）
      for (const room of level.rooms.filter((r) => r.id === 'corridor_main' || r.id === 'ward_hall' || r.id === 'morgue')) {
        out.push({ kit: 'breaker_light', x: room.rect.x0 + 1.5, z: room.rect.z0 + 1.5, rot: 0, breaker: room.zone });
      }
      return out;
    }
    
    /** 把 GLB 套件网格按实例变换烘焙进一个静态缓冲（一次 draw call 画完所有道具） */
    function bakeInstances(glb, instances) {
      const verts = [];
      const idx = [];
      const colorRanges = [];
      const missing = new Set();
      for (const inst of instances) {
        // 套件名支持子件选择：'door_frame:jamb_l' → GLB 节点 'kit_door_frame_jamb_l'
        // （门由门柱/上槛/门扇/把手四件构成，不能再当单个实心块）
        const [base, part] = String(inst.kit).split(':');
        const node = part
          ? (glb.byName['kit_' + base + '_' + part] ?? glb.byName['kit_door_' + part])
          : (glb.byName['kit_' + base] ?? glb.byName['kit_' + base + '_a']);
        if (!node) { missing.add(inst.kit); continue; }
        const cos = Math.cos(inst.rot), sin = Math.sin(inst.rot);
        for (const prim of node.primitives) {
          const base = verts.length / 6;
          for (let i = 0; i < prim.verts.length; i += 6) {
            const x = prim.verts[i], y = prim.verts[i + 1], z = prim.verts[i + 2];
            const nx = prim.verts[i + 3], ny = prim.verts[i + 4], nz = prim.verts[i + 5];
            verts.push(inst.x + x * cos - z * sin, y, inst.z + x * sin + z * cos,
                       nx * cos - nz * sin, ny, nx * sin + nz * cos);
          }
          for (const v of prim.indices) idx.push(base + v);
          colorRanges.push({ name: prim.materialName, start: base, count: prim.verts.length / 6, color: prim.color.slice(0, 3) });
        }
      }
      return { verts: new Float32Array(verts), indices: new Uint16Array(idx), colorRanges, missing: [...missing] };
    }
    
    /** 怪体烘焙：把三怪的部件合成一个可变换的网格（每帧按位置重放） */
    function bakeMonster(glb, monsterId) {
      const prefix = { stitcher: 'kit_m_stitcher', whisperer: 'kit_m_whisperer', coroner: 'kit_m_coroner' }[monsterId];
      const parts = glb.nodes.filter((n) => n.name.startsWith(prefix));
      const verts = [];
      const idx = [];
      const colorRanges = [];
      for (const node of parts) {
        for (const prim of node.primitives) {
          const base = verts.length / 6;
          for (let i = 0; i < prim.verts.length; i += 6) {
            verts.push(prim.verts[i], prim.verts[i + 1], prim.verts[i + 2], prim.verts[i + 3], prim.verts[i + 4], prim.verts[i + 5]);
          }
          for (const v of prim.indices) idx.push(base + v);
          colorRanges.push({ name: prim.materialName, start: base, count: prim.verts.length / 6, color: prim.color.slice(0, 3) });
        }
      }
      return { verts: new Float32Array(verts), indices: new Uint16Array(idx), colorRanges, partCount: parts.length };
    }
    
    module.exports = { buildStructure, kitInstances, bakeInstances, bakeMonster, WALL_COLORS };
