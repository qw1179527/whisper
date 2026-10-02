/* 低语计划 · 灰盒源树分模块产物（tools/split-modules.mjs 生成，勿手改）
 * 模块：__m5 → m5.js
 * 职责：关卡几何编译（渲染与碰撞共用，浏览器/Node 双端零依赖） 关键取舍：**不逐格画墙**。30×30 网格里若每格都画立方体，仅外墙就有上千个面， 移动端 WebGL 立刻掉帧（V9 §14 P2 的"性能过程阈值"正是为拦这类实现）。 做法：把同向相邻的墙格合并成"条带"，内部被房间包围的墙格直接剔除。
 * 来源：baseline/game-0.6.0.js 第 1666~1908 行（8839 字节，逐字节搬移）
 * 包装改写（唯一改动，可审计）：mod.exports → module.exports；__req("__mN") → require("__mN")
 * 依赖：无｜导出：KIT_FOOTPRINT, buildWallBoxes, doorBlockers, floorQuad, levelBoundsOf, makeCollider, makeRoomCollider, mergeStrips, propBoxes, roomWalls, wallCells
 */
'use strict';

    /**
     * 关卡几何编译（渲染与碰撞共用，浏览器/Node 双端零依赖）
     *
     * 关键取舍：**不逐格画墙**。30×30 网格里若每格都画立方体，仅外墙就有上千个面，
     * 移动端 WebGL 立刻掉帧（V9 §14 P2 的"性能过程阈值"正是为拦这类实现）。
     * 做法：把同向相邻的墙格合并成"条带"，内部被房间包围的墙格直接剔除。
     */
    
    const WALL_H = 3.2;
    
    /**
     * 由**房间矩形 + 门格**生成整面墙（推荐的权威路径）。
     *
     * 为什么不用"逐格挖墙 + 合并条带"（首版做法，用户反馈"咋还有一堆条形墙呢"）：
     *  · 逐格合并只在**横向**生效，竖直墙永远合不了 → 65 个单格条带，视觉上就是一堆断续短墙；
     *  · 每格独立生成盒体还会在相邻条的接缝处共面重叠，产生 z-fighting 条纹。
     * 房间本身是矩形，因此一面墙就是**一段连续 span**，只需按门格把它切成若干段——
     * 段数从数百降到几十，墙是整面的，碰撞盒也不再碎。
     */
    function roomWalls(rooms, doorCells, opts = {}) {
      const thickness = opts.thickness ?? 0.22;
      const height = opts.height ?? 3.2;
      const isDoor = (x, z) => doorCells.has(x + ',' + z);
      const spans = [];
    
      /**
       * 墙线位置 = **格边界**（rect 是格索引：第 x 格覆盖世界 [x, x+1)）。
       *   北墙 z = z0 − 0.5，南墙 z = z1 − 0.5，西墙 x = x0 − 0.5，东墙 x = x1 − 0.5。
       * 门格是"落在两空间之间的那一格"，即与房间相邻的那一格：
       *   北墙看 (t, z0−1)、南墙看 (t, z1)、西墙看 (x0−1, t)、东墙看 (x1, t)。
       * 首版用墙线坐标去当格索引查门（19.5、20.5 之类），永远查不到，
       * 于是门格被当成实心墙——这正是"怪卡在门口"和"墙互相穿透看着像一堆条形墙"的根因。
       */
      for (const room of rooms) {
        const { x0, x1, z0, z1 } = room.rect;
        /* PATCH 005: door-clearance */
        // 门洞余量：与门格相邻的墙段端部各让出 doorMargin，否则胶囊半径会把 1m 门洞吃成 0.32m
        // （实测可用窗口只剩 3cm，怪贴门框磨而进不去）。
        const doorMargin = Math.max(0, opts.doorMargin ?? 0.34);
        const cut = (orientation, line, from, to, doorCoord) => {
          const doorAt = (t) => isDoor(
            orientation === 'h' ? t : doorCoord,
            orientation === 'h' ? doorCoord : t,
          );
          let runStart = null;
          const flush = (endT, startT) => {
            // 仅当该段端部紧邻门格时才收缩
            const shrinkStart = startT > from && doorAt(startT - 1) ? doorMargin : 0;
            const shrinkEnd = doorAt(endT) ? doorMargin : 0;
            const a = startT + shrinkStart;
            const b = endT - shrinkEnd;
            if (b - a > 0.02) spans.push(mkSpan(orientation, line, a, b));
          };
          for (let t = from; t <= to; t++) {
            const cellIsDoor = t < to && doorAt(t);
            if (cellIsDoor) {
              if (runStart !== null) { flush(t, runStart); runStart = null; }
            } else if (runStart === null) {
              runStart = t;
            }
          }
          if (runStart !== null) flush(to, runStart);
        };
    
        cut('h', z0 - 0.5, x0, x1, z0 - 1);   // 北墙：门格在 z0-1 那一行
        cut('h', z1 - 0.5, x0, x1, z1);       // 南墙：门格在 z1 那一行
        cut('v', x0 - 0.5, z0, z1, x0 - 1);   // 西墙：门格在 x0-1 那一列
        cut('v', x1 - 0.5, z0, z1, x1);       // 东墙：门格在 x1 那一列
      }
      return spans;
    
      function mkSpan(orientation, line, a, b) {
        const len = b - a;
        if (orientation === 'h') {
          return { orientation, len, cx: (a + b) / 2, cz: line, sx: len, sz: thickness, height };
        }
        return { orientation, len, cx: line, cz: (a + b) / 2, sx: thickness, sz: len, height };
      }
    }
    
    /** 内表面剔除：四邻至少一侧是房间地板、且至少一侧不是（即真正暴露在外的墙格） */
    function wallCells(grid) {
      const H = grid.length;
      const W = grid[0].length;
      const isFloor = (x, z) => {
        if (x < 0 || z < 0 || x >= W || z >= H) return false;
        return grid[z][x] !== '#';
      };
      const out = [];
      for (let z = 0; z < H; z++) {
        for (let x = 0; x < W; x++) {
          if (grid[z][x] !== '#') continue;
          const n = [isFloor(x, z - 1), isFloor(x, z + 1), isFloor(x - 1, z), isFloor(x + 1, z)].filter(Boolean).length;
          if (n > 0) out.push({ x, z });
        }
      }
      return out;
    }
    
    /** 合并为条带：先横向连续、再纵向连续 */
    function mergeStrips(cells) {
      const key = (x, z) => x + ',' + z;
      const pool = new Set(cells.map((c) => key(c.x, c.z)));
      const strips = [];
      // 横向
      for (const c of [...cells].sort((a, b) => (a.z - b.z) || (a.x - b.x))) {
        if (!pool.has(key(c.x, c.z))) continue;
        let len = 1;
        while (pool.has(key(c.x + len, c.z))) { pool.delete(key(c.x + len, c.z)); len++; }
        pool.delete(key(c.x, c.z));
        strips.push({ axis: 'x', x0: c.x, z0: c.z, len });
      }
      return strips;
    }
    
    /**
     * 墙条带 → 盒体列表（轴对齐，含 0.26m 厚度）。
     * 每个盒体给出 {cx, cz, sx, sz} 供碰撞与实例化复用。
     */
    function buildWallBoxes(grid, thickness = 0.26) {
      return mergeStrips(wallCells(grid)).map((s) => ({
        x0: s.x0, z0: s.z0, x1: s.x0 + s.len, z1: s.z0 + 1,
        cx: s.x0 + s.len / 2, cz: s.z0 + 0.5,
        sx: s.len, sz: 1,
        h: WALL_H,
        thickness,
      }));
    }
    
    /** 道具 AABB（玩家/怪物碰撞用；渲染另走 GLB 实例） */
    function propBoxes(level) {
      const out = [];
      for (const room of level.rooms) {
        for (const p of room.props ?? []) {
          const wx = room.rect.x0 + p.pos[0];
          const wz = room.rect.z0 + p.pos[2];
          const [sx, sz] = KIT_FOOTPRINT[p.kit] ?? [0.8, 0.6];
          out.push({ kind: p.kit, cx: wx, cz: wz, sx, sz, x0: wx - sx / 2, x1: wx + sx / 2, z0: wz - sz / 2, z1: wz + sz / 2 });
        }
      }
      return out;
    }
    
    /** 套件footprint（米）：与 Blender 套件尺寸一致 */
    const KIT_FOOTPRINT = {
      bed_b: [2.05, 0.95],
      locker_a: [0.92, 0.55],
      cabinet_a: [0.85, 0.45],
      desk_a: [1.3, 0.65],
      gurney_a: [1.95, 0.7],
    };
    
    /** 门格集合（关着的门挡路；锁死门永远挡路） */
    function doorBlockers(level) {
      return (level.doors ?? [])
        .filter((d) => d.locked)
        .map((d) => ({ kind: 'door_locked', cx: d.pos.x, cz: d.pos.z, sx: 1, sz: 1, x0: d.cell[0], x1: d.cell[0] + 1, z0: d.cell[1], z1: d.cell[1] + 1, doorId: d.id }));
    }
    
    /** 玩家胶囊半径内的可站立判定（含房间边界与道具） */
    function makeCollider(level, radius = 0.34) {
      const boxes = [...propBoxes(level), ...doorBlockers(level)];
      const W = level.gridSize.w;
      const H = level.gridSize.h;
      const grid = level.grid;
    
      const passable = (x, z) => {
        const gx = Math.floor(x);
        const gz = Math.floor(z);
        if (gx < 0 || gz < 0 || gx >= W || gz >= H) return false;
        if (grid[gz][gx] === '#') return false;
        return true;
      };
    
      /**
       * 分离轴滑动 + **子步进**。
       * 子步进不是可选优化：单帧位移若一次判定，5m 的位移会被判成"终点合法"而穿过整面墙
       * （首轮实测：从出生点向西 5m 直接穿墙落到门厅）。每步不超过 0.25m。
       */
      const MAX_STEP = 0.25;
      return function resolve(from, delta) {
        let { x, z } = from;
        const steps = Math.max(1, Math.ceil(Math.max(Math.abs(delta.x), Math.abs(delta.z)) / MAX_STEP));
        const dx = delta.x / steps;
        const dz = delta.z / steps;
        const blocked = (px, pz) =>
          !passable(px, pz) || boxes.some((b) => px + radius > b.x0 && px - radius < b.x1 && pz + radius > b.z0 && pz - radius < b.z1);
        for (let i = 0; i < steps; i++) {
          const nx = x + dx;
          if (!blocked(nx + Math.sign(dx) * radius, z)) x = nx;
          const nz = z + dz;
          if (!blocked(x, nz + Math.sign(dz) * radius)) z = nz;
        }
        return { x, z };
      };
    }
    
    /** 供渲染：地板/天花大平面（整层一张，而不是逐格） */
    function floorQuad(level) {
      const { x0, z0, x1, z1 } = levelBoundsOf(level);
      return { x0, z0, x1, z1 };
    }
    
    function levelBoundsOf(level) {
      const rs = level.rooms.map((r) => r.rect);
      return {
        x0: Math.min(...rs.map((r) => r.x0)),
        z0: Math.min(...rs.map((r) => r.z0)),
        x1: Math.max(...rs.map((r) => r.x1)),
        z1: Math.max(...rs.map((r) => r.z1)),
      };
    }
    
    /**
     * 按墙 span 构建碰撞器（与渲染共用同一份几何，避免看得见的墙不挡人）。
     * 首版用逐格墙格生成碰撞盒，且怪物根本没有接碰撞解析——两处叠加就是"怪贴我身上"。
     */
    function makeRoomCollider(level, radius = 0.34, opts = {}) {
      const doorCells = new Set((level.doors ?? []).map((d) => d.cell.join(',')));
      const spans = roomWalls(level.rooms, doorCells, { thickness: opts.thickness ?? 0.22 });
      const boxes = spans.map((sp) => ({
        x0: sp.cx - sp.sx / 2, x1: sp.cx + sp.sx / 2,
        z0: sp.cz - sp.sz / 2, z1: sp.cz + sp.sz / 2,
        kind: 'wall',
      }));
      for (const d of level.doors ?? []) {
        if (!d.locked) continue;
        boxes.push({ x0: d.cell[0], x1: d.cell[0] + 1, z0: d.cell[1], z1: d.cell[1] + 1, kind: 'door_locked' });
      }
      for (const b of propBoxes(level)) boxes.push(b);
      const W = level.gridSize.w;
      const H = level.gridSize.h;
      const MAX_STEP = 0.25;
      const blocked = (x, z) =>
        !(x > 0.3 && z > 0.3 && x < W - 0.3 && z < H - 0.3) ||
        boxes.some((b) => x + radius > b.x0 && x - radius < b.x1 && z + radius > b.z0 && z - radius < b.z1);
      return function resolve(from, delta) {
        let x = from.x;
        let z = from.z;
        const steps = Math.max(1, Math.ceil(Math.max(Math.abs(delta.x), Math.abs(delta.z)) / MAX_STEP));
        const dx = delta.x / steps;
        const dz = delta.z / steps;
        for (let i = 0; i < steps; i++) {
          const nx = x + dx;
          if (!blocked(nx + Math.sign(dx) * radius, z)) x = nx;
          const nz = z + dz;
          if (!blocked(x, nz + Math.sign(dz) * radius)) z = nz;
        }
        return { x, z };
      };
    }
    
    module.exports = { roomWalls, wallCells, mergeStrips, buildWallBoxes, propBoxes, doorBlockers, makeCollider, floorQuad, levelBoundsOf, makeRoomCollider, KIT_FOOTPRINT };
