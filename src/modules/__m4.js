/* 低语计划 · 灰盒源树分模块产物（tools/split-modules.mjs 生成，勿手改）
 * 模块：__m4 → m4.js
 * 职责：Level DSL Builder（V9 §19.2：关卡即数据，运行时拼装，编辑器零参与） 输入：data/levels/*.json（房间矩形 + 门洞 + 道具 + 证据点 + 事件） 输出：编译后的几何契约（轴对齐墙段 / 门 / 套件实例 / 巡逻路点 / 连通图） 三个消费者： ① web/ 灰盒渲染器（2.5D 射线投射）与碰撞 ② 怪物导航（路点图 A*）与视线/隔音判定 ③ 内容工厂（新地图 = 一份新 JSON，边际成本 ≤40 小时，V9 §14 P1）
 * 来源：baseline/game-0.6.0.js 第 1360~1666 行（11126 字节，逐字节搬移）
 * 包装改写（唯一改动，可审计）：mod.exports → module.exports；__req("__mN") → require("__mN")
 * 依赖：无｜导出：WALL_SIDES, buildLevel, buildLevelFromData, buildRoomGraph, compileDoors, compileWalls, distanceFrom, doorConnections, findRoomPath, hasLineOfSight, installLevel, levelBounds, roomAt, roomCenter, roomPathToWaypoints, segmentsIntersect, spawnPointOf
 */
'use strict';

    /**
     * Level DSL Builder（V9 §19.2：关卡即数据，运行时拼装，编辑器零参与）
     *
     * 输入：data/levels/*.json（房间矩形 + 门洞 + 道具 + 证据点 + 事件）
     * 输出：编译后的几何契约（轴对齐墙段 / 门 / 套件实例 / 巡逻路点 / 连通图）
     *
     * 三个消费者：
     *  ① web/ 灰盒渲染器（2.5D 射线投射）与碰撞
     *  ② 怪物导航（路点图 A*）与视线/隔音判定
     *  ③ 内容工厂（新地图 = 一份新 JSON，边际成本 ≤40 小时，V9 §14 P1）
     */
    const WALL_SIDES = ['north', 'south', 'west', 'east'];
    
    /**
     * 关卡数据注入点。
     * core/ **不读文件系统**——Node 侧由 tools/node-config.mjs 读取 JSON，浏览器/APK 由页面内联。
     * 打包器第一次运行就暴露过 core 依赖 node:fs 的问题，这条纪律必须保住。
     */
    function installLevel(level) {
      return level;
    }
    
    /** 由注入的数据编译关卡（Node 与浏览器同一条路径） */
    function buildLevelFromData(dsl) {
      return buildLevel(dsl.id, null, dsl);
    }
    
    /** AABB 房间矩形 */
    function rect(room) {
      /* PATCH 001: door-cell-support */
      // 已编译房间直接带 rect（buildLevel 会给每个房间补 rect）；只有原始 DSL 才需要由 pos/size 推导。
      if (room.rect && typeof room.rect.x0 === 'number') return room.rect;
      const [w, , d] = room.size;
      const [x, , z] = room.pos;
      return { x0: x, z0: z, x1: x + w, z1: z + d, w, d };
    }
    
    /**
     * 墙段编译：把房间矩形拆成 4 条边，再按门洞切出缺口。
     * door: { id, wall, offsetM, widthM, locked }  offsetM 从该边起点算起。
     */
    function compileWalls(room) {
      const r = rect(room);
      const doorsOn = (wall) => (room.doors ?? []).filter((d) => d.wall === wall);
      const segs = [];
    
      const cut = (wall, ax, az, bx, bz, length) => {
        const ds = doorsOn(wall)
          .map((d) => {
            const w = d.widthM ?? 1.2;
            const start = Math.max(0, Math.min(length - w, d.offsetM));
            return { ...d, widthM: w, start, end: start + w };
          })
          .sort((a, b) => a.start - b.start);
        const ux = (bx - ax) / (length || 1);
        const uz = (bz - az) / (length || 1);
        let cursor = 0;
        const pointAt = (t) => ({ x: round3(ax + ux * t), z: round3(az + uz * t) });
        for (const d of ds) {
          if (d.start > cursor) segs.push({ room: room.id, wall, a: pointAt(cursor), b: pointAt(d.start) });
          const c = { ...pointAt(d.start), t: d.start };
          const e = { ...pointAt(d.end), t: d.end };
          segs.push({ room: room.id, wall, a: { x: c.x, z: c.z }, b: { x: e.x, z: e.z }, kind: 'door_gap', doorId: d.id });
          cursor = d.end;
        }
        if (cursor < length) segs.push({ room: room.id, wall, a: pointAt(cursor), b: pointAt(length) });
      };
    
      cut('north', r.x0, r.z0, r.x1, r.z0, r.w); // 北：x 递增
      cut('south', r.x0, r.z1, r.x1, r.z1, r.w); // 南：x 递增
      cut('west', r.x0, r.z0, r.x0, r.z1, r.d); // 西：z 递增
      cut('east', r.x1, r.z0, r.x1, r.z1, r.d); // 东：z 递增
      return segs;
    }
    
    /** 单入口点（门洞中心，含朝向法线，用于门状态与视线遮挡） */
    function compileDoors(level) {
      const out = [];
      for (const room of level.rooms) {
        const r = rect(room);
        for (const d of room.doors ?? []) {
          /* PATCH 001: door-cell-support */
          // 门有两种写法：格子形式 {id, cell}（关卡 DSL 实际用法）与墙面形式 {wall, offsetM, widthM}。
          // 只认后者时，格子门会落进 else 分支被摆到东墙且 z=NaN —— 这正是"房间图零边、怪直线撞墙"的根因。
          let w = d.widthM ?? 1.2;
          let pos;
          let normal;
          let wall = d.wall;
          if (Array.isArray(d.cell)) {
            const [cx, cz] = d.cell;
            pos = { x: cx + 0.5, z: cz + 0.5 };
            // 门格本身是 1×1；用「格中心相对房间矩形的位置」判断贴在哪面墙上
            if (Math.abs(pos.x - r.x0) <= 0.6) { wall = 'west'; normal = { x: -1, z: 0 }; }
            else if (Math.abs(pos.x - r.x1) <= 0.6) { wall = 'east'; normal = { x: 1, z: 0 }; }
            else if (Math.abs(pos.z - r.z0) <= 0.6) { wall = 'north'; normal = { x: 0, z: -1 }; }
            else if (Math.abs(pos.z - r.z1) <= 0.6) { wall = 'south'; normal = { x: 0, z: 1 }; }
            else { wall = null; normal = null; }
            w = 1.0;   // 格子门洞口宽 = 1 格
          } else {
            const mid = d.offsetM + w / 2;
            if (d.wall === 'north') { pos = { x: r.x0 + mid, z: r.z0 }; normal = { x: 0, z: -1 }; }
            else if (d.wall === 'south') { pos = { x: r.x0 + mid, z: r.z1 }; normal = { x: 0, z: 1 }; }
            else if (d.wall === 'west') { pos = { x: r.x0, z: r.z0 + mid }; normal = { x: -1, z: 0 }; }
            else { pos = { x: r.x1, z: r.z0 + mid }; normal = { x: 1, z: 0 }; }
          }
          // 位置不可解（NaN / 无法判断贴墙）→ 跳过：宁可少一扇门，也不要一扇位置是 NaN 的门
          if (!normal || !Number.isFinite(pos.x) || !Number.isFinite(pos.z)) continue;
          out.push({
            id: d.id,
            room: room.id,
            wall: wall,
            cell: Array.isArray(d.cell) ? d.cell : null,
            widthM: w,
            locked: !!d.locked,
            blocksSight: true,           // 门体遮挡视线
            blocksSound: false,          // 关着的门不隔音（恐怖设计：门挡视线不挡声音）
            open: false,
            pos: { x: round3(pos.x), z: round3(pos.z) },
            normal,
            connectsTo: d.connectsTo ?? null,
          });
        }
      }
      return out;
    }
    
    /** 距离场房间（用于"证据点是否够深""撤离点是否够远"的量化校验） */
    function roomCenter(level, roomId) {
      const room = level.rooms.find((r) => r.id === roomId);
      if (!room) throw new Error(`unknown room: ${roomId}`);
      const r = rect(room);
      return { x: round3((r.x0 + r.x1) / 2), z: round3((r.z0 + r.z1) / 2), rect: r };
    }
    
    function roomAt(level, x, z) {
      for (const room of level.rooms) {
        const r = rect(room);
        if (x >= r.x0 && x <= r.x1 && z >= r.z0 && z <= r.z1) return { id: room.id, zone: room.zone, rect: r, meta: room };
      }
      return null;
    }
    
    /** 门 → 连接的两个空间（按门两侧各探 0.6m 判定），用于连通图 */
    function doorConnections(level, door) {
      const probe = (sign) => roomAt(level, door.pos.x + door.normal.x * sign * 0.6, door.pos.z + door.normal.z * sign * 0.6);
      const a = probe(+1);
      const b = probe(-1);
      return [a?.id ?? null, b?.id ?? null];
    }
    
    /** 连通图：节点=房间，边=门 */
    function buildRoomGraph(level) {
      const nodes = Object.fromEntries(level.rooms.map((r) => [r.id, { id: r.id, zone: r.zone, doors: [] }]));
      const links = [];
      for (const door of compileDoors(level)) {
        const [a, b] = doorConnections(level, door);
        if (!a || !b || a === b) continue;
        nodes[a].doors.push({ to: b, door: door.id });
        nodes[b].doors.push({ to: a, door: door.id });
        links.push({ a, b, door: door.id, locked: door.locked });
      }
      return { nodes, links };
    }
    
    /** 关卡编译入口（数据必须由调用方注入；core 不读文件系统） */
    function buildLevel(levelId, dir, injected) {
      const dsl = injected;
      if (!dsl) throw new Error('buildLevel 需要注入关卡数据（Node 侧用 tools/node-config.mjs 读取）: ' + levelId);
      const walls = dsl.rooms.flatMap(compileWalls);
      const doors = compileDoors(dsl);
      const graph = buildRoomGraph(dsl);
    
      const kitIds = new Set();
      const props = [];
      for (const room of dsl.rooms) {
        for (const p of room.props ?? []) {
          const c = roomCenter(dsl, room.id);
          kitIds.add(p.kit);
          props.push({
            room: room.id,
            kit: p.kit,
            pos: { x: round3(c.rect.x0 + p.pos[0]), z: round3(c.rect.z0 + p.pos[2]) },
            rotY: p.rot ?? 0,
            blocksMovement: p.blocksMovement ?? false,
            stimulus: p.stimulus ?? null,
          });
        }
      }
    
      const evidencePoints = dsl.rooms
        .filter((r) => r.evidencePoint)
        .map((r) => {
          const c = roomCenter(dsl, r.id);
          return { room: r.id, zone: r.zone, pos: { x: c.x, z: c.z }, depthM: round3(distanceFrom(c, spawnPointOf(dsl))) };
        });
    
      return {
        id: dsl.id,
        name: dsl.name,
        meta: dsl.meta ?? {},
        bounds: levelBounds(dsl),
        rooms: dsl.rooms.map((r) => ({ ...r, rect: rect(r) })),
        zones: dsl.zones ?? {},
        walls,
        doors,
        props,
        kitIds: [...kitIds],
        evidencePoints,
        spawns: dsl.spawns ?? {},
        extractionPoints: dsl.extractionPoints ?? [],
        patrolRoutes: dsl.patrolRoutes ?? {},
        events: dsl.events ?? [],
        graph,
        stats: {
          roomCount: dsl.rooms.length,
          wallSegments: walls.length,
          doorCount: doors.length,
          propCount: props.length,
          evidenceCount: evidencePoints.length,
          kitCount: kitIds.size,
        },
      };
    }
    
    function spawnPointOf(dsl) {
      const s = dsl.spawns?.players?.[0] ?? { x: 0, z: 0 };
      return { x: s.x, z: s.z };
    }
    
    function levelBounds(dsl) {
      const rs = dsl.rooms.map(rect);
      return {
        x0: Math.min(...rs.map((r) => r.x0)),
        z0: Math.min(...rs.map((r) => r.z0)),
        x1: Math.max(...rs.map((r) => r.x1)),
        z1: Math.max(...rs.map((r) => r.z1)),
      };
    }
    
    function distanceFrom(a, b) {
      return Math.hypot(a.x - b.x, a.z - b.z);
    }
    
    /**
     * 房间级 A*（怪物导航基线：路点图，非网格）。
     * 低端机同屏寻路代理 ≤2（V9 §7），故用房间图而非逐格 A* 是刻意的性能决策。
     */
    function findRoomPath(level, fromRoomId, toRoomId) {
      const g = level.graph.nodes;
      if (!g[fromRoomId] || !g[toRoomId]) return null;
      if (fromRoomId === toRoomId) return [fromRoomId];
      const open = [{ id: fromRoomId, g: 0, f: 0 }];
      const came = new Map();
      const best = new Map([[fromRoomId, 0]]);
      const h = (id) => distanceFrom(roomCenter(level, id), roomCenter(level, toRoomId));
      while (open.length) {
        open.sort((a, b) => a.f - b.f);
        const cur = open.shift();
        if (cur.id === toRoomId) {
          const path = [cur.id];
          let p = cur.id;
          while (came.has(p)) { p = came.get(p); path.unshift(p); }
          return path;
        }
        for (const e of g[cur.id].doors) {
          const ng = cur.g + 1;
          if (best.has(e.to) && best.get(e.to) <= ng) continue;
          best.set(e.to, ng);
          came.set(e.to, cur.id);
          open.push({ id: e.to, g: ng, f: ng + h(e.to) });
        }
      }
      return null;
    }
    
    /** 路径 → 世界坐标路点序列（经门中心，供实际移动使用） */
    function roomPathToWaypoints(level, path) {
      /* PATCH 004: door-choice-on-path */
      if (!path) return [];
      const pts = [];
      const ca = (id) => roomCenter(level, id);
      // 点到线段的垂距（用于判断哪扇门"在这条路上"）
      const perpDist = (p, s, e) => {
        const vx = e.x - s.x, vz = e.z - s.z;
        const len2 = vx * vx + vz * vz;
        if (len2 === 0) return Math.hypot(p.x - s.x, p.z - s.z);
        let t = ((p.x - s.x) * vx + (p.z - s.z) * vz) / len2;
        t = Math.max(0, Math.min(1, t));
        return Math.hypot(p.x - (s.x + vx * t), p.z - (s.z + vz * t));
      };
      for (let i = 0; i < path.length - 1; i++) {
        const [a, b] = [path[i], path[i + 1]];
        // 同两房之间可能有多扇门：取「门中心离两房中心连线最近」的那扇。
        // 原来的 .find() 取第一条匹配 —— 实测把怪送去走廊另一端（lobby 的门在 6.5，却选到 14.5）。
        const cands = level.graph.links.filter((l) => (l.a === a && l.b === b) || (l.a === b && l.b === a));
        const sa = ca(a), sb = ca(b);
        let bestDoor = null, bestD = Infinity;
        for (const l of cands) {
          const door = level.doors.find((d) => d.id === l.door);
          if (!door) continue;
          const d = perpDist(door.pos, sa, sb);
          if (d < bestD) { bestD = d; bestDoor = door; }
        }
        if (bestDoor) pts.push({ x: bestDoor.pos.x, z: bestDoor.pos.z });
      }
      pts.push(roomCenter(level, path[path.length - 1]));
      return pts;
    }
    
    /**
     * 视线判定：轴对齐墙段 + 关门遮挡（供"低语者安静时几乎失明"等语义使用）。
     * 采用分段-线段相交判定，纯 2D、零依赖、确定性。
     */
    function hasLineOfSight(level, a, b, opts = {}) {
      const segs = level.walls.filter((w) => w.kind !== 'door_gap');
      for (const s of segs) if (segmentsIntersect(a, b, s.a, s.b)) return false;
      for (const door of level.doors) {
        if (!door.open && door.blocksSight && pointNearSegment(door.pos, a, b, door.widthM / 2)) return false;
      }
      return true;
    }
    
    function cross(o, a, b) {
      return (a.x - o.x) * (b.z - o.z) - (a.z - o.z) * (b.x - o.x);
    }
    function segmentsIntersect(p1, p2, p3, p4) {
      const d1 = cross(p3, p4, p1);
      const d2 = cross(p3, p4, p2);
      const d3 = cross(p1, p2, p3);
      const d4 = cross(p1, p2, p4);
      if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
      return false;
    }
    function pointNearSegment(p, a, b, tol) {
      const l2 = (b.x - a.x) ** 2 + (b.z - a.z) ** 2;
      if (l2 === 0) return Math.hypot(p.x - a.x, p.z - a.z) <= tol;
      let t = ((p.x - a.x) * (b.x - a.x) + (p.z - a.z) * (b.z - a.z)) / l2;
      t = Math.max(0, Math.min(1, t));
      return Math.hypot(p.x - (a.x + t * (b.x - a.x)), p.z - (a.z + t * (b.z - a.z))) <= tol;
    }
    
    const round3 = (v) => Math.round(v * 1000) / 1000;
    
    module.exports = { installLevel, buildLevelFromData, compileWalls, compileDoors, roomCenter, roomAt, doorConnections, buildRoomGraph, buildLevel, spawnPointOf, levelBounds, distanceFrom, findRoomPath, roomPathToWaypoints, hasLineOfSight, segmentsIntersect, WALL_SIDES };
