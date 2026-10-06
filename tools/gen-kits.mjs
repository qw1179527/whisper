#!/usr/bin/env node
/**
 * gen-kits.mjs — 资产套件生成器 v2（V9 §19.1 C3「资产零导入」/ §19.3 CI 资产管线）
 *
 * ## 为什么自产而不是下载（v1 的理由，仍然成立）
 * 清单原本声明了 5 个 CC0 资产的路径，但文件一个都不存在 —— C3 当时只是声明。
 * 选「Blender 脚本化生成」：无版权风险、确定性可复现、无网络依赖。
 *
 * ## v2 改了什么（2026-10-04 · 问题单「11/11 房间没有天花板 · 楼板按房型整块摆不裁剪」）
 * v1 的配方是**写死的盒子表**：`hall_main` 的楼板恒为 16×3，于是被摆进 4×3 的入口区时悬挑 12m、
 * 摆进 18×3 的主廊时缺口 2m。根因不是数值抄错，而是**配方里没有房间尺寸这个变量**。
 * v2 把配方改成**房间类的函数**：`recipe(arch, W, H, D, doors)` ——
 *   · 楼板恒 = 房间尺寸（footprint 的唯一真源，直接被门禁 B2 拿 floor 节点对账）
 *   · 天花板按层高落在 H 处、四边内收 9cm（内收是为了不与墙共面，也顺带让
 *     LevelBuilder 的几何分类器把「不铺满房间」的那件归到天花板色）
 *   · 墙裙/顶角线/门套/窗套按**门洞位置**断开（门位来自关卡 DSL，见下）
 *   · 冷柜/病床/机柜改为带细节的低多边形家具（格栅、把手、脚轮、标签牌）
 *
 * ## 房间尺寸从哪来（V9 §19.2「关卡即数据」）
 * 生成器**读 `<root>/unity/Assets/Levels/*.json`**，把房间按 (kit, 尺寸, 门洞布局) 归类，
 * 再用同一套配方给每个类出套件。于是「楼板与房间不符」在生成期就不可能发生：
 * `--check` 会逐个房间核对「这个房间用的套件，它的楼板是不是正好是这个房间的 W×D」。
 *
 * ## 两种模式（--mode）
 *   canonical（默认）：仍只出清单里那 5 个套件 id，尺寸取**标称房间**（见 CANONICAL_ROOM）。
 *                      多尺寸共用一个 id 的房间会被如实列为「未适配」（不是静默放过）。
 *   variants        ：按房间类出套件（hall_main 之外再出 3 个 hall 变体），**每个房间都能逐一对上**。
 *                      ⚠ 需要清单扩条目，而 `native/csharp-verify/Program.cs:1847` 硬断言
 *                      `kitList.Count == 5`，所以该模式要等 native 侧放行（见交付说明）。
 *
 * ## 用法
 *   node tools/gen-kits.mjs                       # 生成到仓库（CC0/）+ 回写清单
 *   node tools/gen-kits.mjs --check               # 只校验「清单记录 ↔ 产物」一致（CI 用，不写盘）
 *   node tools/gen-kits.mjs --emit <dir>          # 只出到 <dir>（证据/评审用，不碰仓库）
 *   node tools/gen-kits.mjs --mode variants       # 按房间类出套件
 *   node tools/gen-kits.mjs --lint-only           # 只跑几何自检（不调 Blender）
 */
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const MANIFEST = path.join(ROOT, 'unity/Assets/Data/asset-manifest.json');

/**
 * 从**产出的 GLB 字节**里量顶点数，与产品 `GlbReader.VertexCount` 同口径
 * （= 所有 primitive 的 `accessors[POSITION].count` 求和）。
 *
 * 【为什么需要它】清单里的 `verticesByKit` 曾用配方里的"共享角点"计数，而 GLB 是
 * **平直着色拆点**后的顶点（Blender 把每盒 8 角点拆成 24）→ 实测倍数**精确 3.000**，
 * 清单写 `hall_main:56` 而真实是 `168`。当时没判红只是因为 `csharp-verify` 恰好只核对
 * 三角面数；任何人写一条"顶点对账"就会红 —— **已上膛的假账**（独立复核 B2 抓到）。
 * 现在改成量产出字节：清单这一列永远等于真实资产，不会再随"配方怎么算"漂移。
 */
function glbVertexCount(glbPath) {
  const b = fs.readFileSync(glbPath);
  if (b.readUInt32LE(0) !== 0x46546C67) throw new Error(`不是 GLB：${glbPath}`);
  const jsonLen = b.readUInt32LE(12);
  const gltf = JSON.parse(b.subarray(20, 20 + jsonLen).toString('utf8'));
  let n = 0;
  for (const mesh of gltf.meshes ?? [])
    for (const prim of mesh.primitives ?? []) {
      const acc = gltf.accessors?.[prim.attributes?.POSITION];
      if (acc) n += acc.count | 0;
    }
  return n;
}
const LEVELS_DIR = path.join(ROOT, 'unity/Assets/Levels');

const args = process.argv.slice(2);
const hasFlag = (f) => args.includes(f);
const argOf = (f) => { const i = args.indexOf(f); return i >= 0 ? args[i + 1] : null; };
const checkOnly = hasFlag('--check');
const lintOnly = hasFlag('--lint-only');
const emitDir = argOf('--emit');
const MODE = argOf('--mode') ?? process.env.WHISPER_KIT_MODE ?? 'variants';
if (!['canonical', 'variants'].includes(MODE)) { console.error(`[kits] 未知 --mode：${MODE}`); process.exit(2); }

/**
 * `--only <id>[,<id>…]`：只生成指定的套件。
 *
 * ## 为什么需要它（本机实测，2026-10-06）
 * 本机 Blender 跑在 proot 里，**一次生成太多套件会被 SIGKILL**（`vpid 1: terminated with signal 9`）。
 * 这不是内存不足（实测 MemAvailable 4.5 GB，22 个套件合计几百面），
 * 而是启动器头部注释写明的 **proot seccomp 与 Blender Python 初始化的冲突**：
 * 同一进程里初始化到某个量级就被杀，且**不报错、只给 signal 9**。
 * 「重跑就好」不是修法（那是碰运气）；把工作**切成小批**才是。
 *
 * 用法：`node tools/gen-kits.mjs --mode variants --only hall_main_hall_main,hall_main_corridor_f0`
 * 说明：`--only` **只影响"调 Blender 生成"这一步**；几何自检 / 房间适配 / 清单回写仍按**全量**计划算 ——
 * 否则分批生成会把清单写成"只有这几件"，把别处引用到的套件静默抹掉。
 */
const onlyIds = (argOf('--only') ?? '').split(',').map((s) => s.trim()).filter(Boolean);
const onlySet = onlyIds.length ? new Set(onlyIds) : null;

// ════════════════════════════════════════════════════════════════════════════
// ① 几何常量（与 LevelBuilder / LevelGeometry 同口径 —— 三处必须一致）
// ════════════════════════════════════════════════════════════════════════════
const WALL_T = 0.22;          // 墙厚（LevelBuilder.AddWallsWithDoorGaps 的 thickness）
const WALL_IN = WALL_T / 2;   // 墙内表面到房间边界（= 墙中线）的距离
const FLOOR_T = 0.10;         // 楼板厚（顶面 y=0，与 LevelGeometry 的碰撞地面同面）
const CEIL_T = 0.12;          // 天花板厚
const CEIL_INSET = 0.09;      // 天花板四边内收（避免与墙共面；也让分类器认出它不是楼板）
const CEIL_TOP_GAP = 0.005;   // 天花板顶面比墙顶低 5mm（避免与墙顶面共面 —— 共面重叠会闪）
const JOIN = 0.02;            // 相接部件的最小重叠：**绝不用 0 间隙贴合**（0 间隙 = 共面）
const SKIRT_H = 0.11;         // 墙裙高
const TRIM_D = 0.07;          // 墙裙/顶角线厚（凸出 + 嵌入）
const TRIM_PROUD = 0.03;      // 凸出墙内表面的量
const CORNICE_H = 0.12;       // 顶角线高
const CORNICE_PROUD = 0.05;
const TRIM_END_IN = 0.05;     // 线脚两端伸进邻墙（避免露缝；伸入即不会共面）
const TRIM_DOOR_GAP = 0.02;   // 线脚在门洞两侧的退让

/** 角色 → 材质基色（与 DesignTokens 的灰盒色一致）。运行时 LevelBuilder 会按分区另上色，
 *  这里的颜色只决定 GLB 自身的观感（编辑器里看模型时用）。 */
const ROLE_COLOR = {
  floor: [0.42, 0.40, 0.37, 1],
  structure: [0.35, 0.33, 0.31, 1],
  trim: [0.52, 0.49, 0.44, 1],
  soft: [0.60, 0.57, 0.52, 1],
  detail: [0.28, 0.27, 0.26, 1],
  light: [0.85, 0.80, 0.62, 1],
};

// ════════════════════════════════════════════════════════════════════════════
// ② 部件模型：一律用**轴对齐盒（AABB）**声明 min/max —— 于是「共面/穿模」可本机断言
// ════════════════════════════════════════════════════════════════════════════
function mkBox(name, x0, x1, y0, y1, z0, z1, role) {
  const min = [Math.min(x0, x1), Math.min(y0, y1), Math.min(z0, z1)];
  const max = [Math.max(x0, x1), Math.max(y0, y1), Math.max(z0, z1)];
  for (let i = 0; i < 3; i++) if (!(max[i] - min[i] > 1e-6)) throw new Error(`[kits] 部件 ${name} 第 ${i} 轴尺寸非正：${max[i] - min[i]}`);
  return { name, role, shape: 'box', min, max };
}
/** ⑧ 边形脚轮（轴沿 X）：AABB 由半径/半长推出，供自检使用 */
function mkWheel(name, cx, cy, cz, r, depth, role) {
  return {
    name, role, shape: 'wheel',
    at: [cx, cy, cz], radius: r, depth, rot: [0, Math.PI / 2, 0],
    min: [cx - depth / 2, cy - r, cz - r],
    max: [cx + depth / 2, cy + r, cz + r],
  };
}
const centerOf = (p) => [0, 1, 2].map((i) => (p.min[i] + p.max[i]) / 2);
const sizeOf = (p) => [0, 1, 2].map((i) => p.max[i] - p.min[i]);
const r3 = (v) => Math.round(v * 1000) / 1000;

// ════════════════════════════════════════════════════════════════════════════
// ③ 关卡 DSL：房间类（尺寸 + 门洞）是套件几何的真源
// ════════════════════════════════════════════════════════════════════════════
function readRooms() {
  const out = [];
  if (!fs.existsSync(LEVELS_DIR)) return out;
  for (const f of fs.readdirSync(LEVELS_DIR).filter((n) => n.endsWith('.json')).sort()) {
    const lvl = JSON.parse(fs.readFileSync(path.join(LEVELS_DIR, f), 'utf8'));
    for (const r of lvl.rooms ?? []) {
      out.push({
        level: lvl.levelId, id: r.id, kit: r.kit,
        W: r.size?.[0], H: r.size?.[1], D: r.size?.[2],
        doors: (r.doors ?? []).map((d) => ({ wall: d.wall, offsetM: d.offsetM ?? 0, widthM: d.widthM ?? 1.2 })),
      });
    }
  }
  return out;
}
const ROOMS = readRooms();

/** 每个套件 id 的「标称房间」：canonical 模式下该 id 就按这间房的尺寸出壳。
 *  为什么要显式写：hall_main 被 4 种尺寸用，必须挑一个并把其余**如实登记为未适配**。 */
const CANONICAL_ROOM = { hall_main: 'corridor_main', morgue: 'morgue_deep', hospital_ward: 'ward_01' };

/**
 * 「等效类 → 套件 id」的**显式登记**，优先于按房间 id 推导的名字。
 *
 * ## 为什么需要它（2026-10-06 实测踩到）
 * 变体 id 的默认规则是「代表房间沿用基础 id、其余用 `${base}_${roomId}`」。这条规则在
 * **两个不同地图的同尺寸房间属于同一个等效类**时会失效：
 * `tanglewood_v1/entrance` 与 `bleasdale_v1/entrance` 都是 4×3（门位也相同）→ 同一个 classKey，
 * 但 `roomId` 是 `entrance` → 推导出 `hall_main_entrance`；而三张图的 DSL 里写的、
 * 清单里已有的都是 **`hall_main_entrance_safe`**（既成 id，改它要动清单/镜像/门禁）。
 *
 * ⇒ 把「已经存在的 id」显式登记下来，而不是让命名规则去猜。
 * 键 = `基础套件/代表房间id`（代表房间由 gen-kits 自己选，选取规则见下方 variantIdFor）。
 */
const KIT_ID_OVERRIDES = {
  'hall_main/entrance_safe': 'hall_main_entrance_safe',
};

/**
 * 变体 id：给一个等效类定名。
 * 优先级：① 显式登记表 → ② 代表房间沿用基础 id → ③ `${base}_${代表房间id}`。
 * 「代表房间」= 该等效类按**固定顺序**（读入顺序）遇到的第一个房间 —— 顺序固定，
 * 所以同一份输入永远得到同一个名字（确定性是本文件的基本要求）。
 */
function variantIdFor(baseKit, cls) {
  const override = KIT_ID_OVERRIDES[`${baseKit}/${cls.roomId}`];
  if (override) return override;
  return cls.roomId === CANONICAL_ROOM[baseKit] ? baseKit : `${baseKit}_${cls.roomId}`;
}

/** 房间类键：尺寸 + 架构 + **会影响线脚的门洞布局**（例如太平间只给东西墙做墙裙，
 *  南北墙门位不同的两间房因此仍可共用一个套件）。 */
const TRIM_WALLS = { hall: ['north', 'south', 'east', 'west'], ward: ['north', 'south', 'east', 'west'], morgue: ['east', 'west'] };
function classKey(kit, arch, r) {
  const doors = (r.doors ?? [])
    .filter((d) => TRIM_WALLS[arch].includes(d.wall))
    .map((d) => `${d.wall}:${r3(d.offsetM)}+${r3(d.widthM)}`).sort().join(',');
  return `${kit}|${arch}|${r3(r.W)}x${r3(r.H)}x${r3(r.D)}|${doors}`;
}
function classOf(kit, arch, room) {
  return { kit, arch, W: room.W, H: room.H, D: room.D, doors: room.doors ?? [], roomId: room.id, key: classKey(kit, arch, room) };
}

// ════════════════════════════════════════════════════════════════════════════
// ④ 房间壳配方（参数化）：楼板 / 天花板 / 墙裙 / 顶角线
// ════════════════════════════════════════════════════════════════════════════
/** 沿墙分段：把墙长按门洞切段，返回**房间局部坐标**下的 [a,b] 区间 */
function runsFor(wall, cls) {
  const along = (wall === 'north' || wall === 'south') ? cls.W : cls.D;
  const half = along / 2;
  const gaps = (cls.doors ?? []).filter((d) => d.wall === wall)
    .map((d) => [d.offsetM - half, d.offsetM + d.widthM - half])
    .sort((a, b) => a[0] - b[0]);
  const runs = [];
  let cursor = -half;
  for (const [a, b] of gaps) {
    const lo = Math.max(-half, cursor), hi = Math.min(half, a);
    if (hi - lo > 0.02) {
      runs.push([lo + (lo <= -half + 1e-6 ? TRIM_END_IN : TRIM_DOOR_GAP), hi - TRIM_DOOR_GAP]);
    }
    cursor = Math.max(cursor, b);
  }
  if (half - cursor > 0.02) {
    runs.push([cursor + (cursor <= -half + 1e-6 ? TRIM_END_IN : TRIM_DOOR_GAP), half - TRIM_END_IN]);
  }
  return runs.filter(([a, b]) => b - a > 0.05);
}
/** 沿墙条带（墙裙/顶角线/腰线/门套通用）：proud 凸出内表面、embed 嵌入墙内 */
function wallStripAt(name, wall, cls, a, b, y0, y1, proud, depth, role) {
  const hx = cls.W / 2, hz = cls.D / 2, embed = depth - proud;
  if (wall === 'north') return mkBox(name, a, b, y0, y1, hz - WALL_IN - proud, hz - WALL_IN + embed, role);
  if (wall === 'south') return mkBox(name, a, b, y0, y1, -hz + WALL_IN - embed, -hz + WALL_IN + proud, role);
  if (wall === 'east') return mkBox(name, hx - WALL_IN - proud, hx - WALL_IN + embed, y0, y1, a, b, role);
  return mkBox(name, -hx + WALL_IN - embed, -hx + WALL_IN + proud, y0, y1, a, b, role);
}
function wallStrips(name, wall, cls, y0, y1, proud, depth, role) {
  const runs = runsFor(wall, cls);
  return runs.map(([a, b], i) => wallStripAt(runs.length > 1 ? `${name}${i}` : name, wall, cls, a, b, y0, y1, proud, depth, role));
}
/** 门套线脚：每个门洞两侧各一根门樘（凸出 5cm · 嵌入 5cm），顶部由顶角线充当门楣。
 *  门洞贴墙角时（offsetM=0 之类）那一侧不加门樘 —— 那面墙本身就是门樘。 */
function doorFrames(cls) {
  const out = [];
  for (const wall of TRIM_WALLS[cls.arch]) {
    const along = (wall === 'north' || wall === 'south') ? cls.W : cls.D;
    const half = along / 2;
    (cls.doors ?? []).filter((d) => d.wall === wall).forEach((d, di) => {
      const a = d.offsetM - half;
      [['a', a], ['b', a + d.widthM]].forEach(([tag, edge]) => {
        const e0 = Math.max(edge - 0.05, -half + TRIM_END_IN);
        const e1 = Math.min(edge + 0.05, half - TRIM_END_IN);
        if (e1 - e0 < 0.02) return;
        out.push(wallStripAt(`doorjamb_${wall[0]}${di}${tag}`, wall, cls, e0, e1,
          -0.01, cls.H - CEIL_T + JOIN, 0.05, 0.10, 'trim'));
      });
    });
  }
  return out;
}

/** 房间壳（所有房间套件共用）：楼板 + 天花板 + 墙裙 + 顶角线 */
function shellParts(cls) {
  const { W, H, D } = cls;
  const hx = W / 2, hz = D / 2;
  const p = [];
  // ① 楼板：**整房尺寸** —— footprint 的唯一真源（门禁 B2 拿 floor 节点的 XZ 对账）
  p.push(mkBox('floor', -hx, hx, -FLOOR_T, 0, -hz, hz, 'floor'));
  // ② 天花板：落在层高 H 处（顶面低 5mm 避免与墙顶共面），四边内收 9cm
  p.push(mkBox('ceiling', -hx + CEIL_INSET, hx - CEIL_INSET,
    H - CEIL_T, H - CEIL_TOP_GAP, -hz + CEIL_INSET, hz - CEIL_INSET, 'structure'));
  // ③ 墙裙：底边压进楼板 1cm（不与楼板顶面共面），门洞处断开
  for (const wall of TRIM_WALLS[cls.arch]) {
    p.push(...wallStrips(`skirt_${wall[0]}`, wall, cls, -0.01, SKIRT_H - 0.01, TRIM_PROUD, TRIM_D, 'trim'));
  }
  // ④ 顶角线：顶边压进天花板 6cm（不与吊顶底面共面），同样避开门洞
  const cTop = H - CEIL_T + 0.04 + JOIN;
  for (const wall of TRIM_WALLS[cls.arch]) {
    p.push(...wallStrips(`cornice_${wall[0]}`, wall, cls, cTop - CORNICE_H, cTop,
      CORNICE_PROUD, TRIM_D + 0.02, 'trim'));
  }
  // ⑤ 门套线脚（用户点名要的"门框线脚"）：只对**门位确定**的架构做 —— 太平间两个房间的南北门位
  //    不同（2.0m 全开 vs 0.1+1.8），共用一套件时门樘会错位，所以太平间不做门套。
  if (ARCH[cls.arch].doorFrames) p.push(...doorFrames(cls));
  return p;
}

// ════════════════════════════════════════════════════════════════════════════
// ⑤ 各架构的家具/装置
// ════════════════════════════════════════════════════════════════════════════
/** 天花板灯带（所有房间都有）：顶面压进天花板 4cm */
function lightPanel(x0, x1, z0, z1, cls, name = 'light_panel') {
  const { H } = cls;
  return mkBox(name, x0, x1, H - CEIL_T - 0.04, H - CEIL_T + 0.06, z0, z1, 'light');
}

const ARCH = {
  /** 主走廊/连接廊：顶部两道纵梁 + 立柱（立柱按房间长轴均分，绝不出房间） */
  hall: {
    trimWalls: TRIM_WALLS.hall,
    doorFrames: true,
    decorate(cls) {
      const { W, H, D } = cls, hz = D / 2, iz = hz - WALL_IN;
      const p = [];
      p.push(lightPanel(-0.5, 0.5, -0.30, 0.30, cls));
      // 顶梁：沿长轴（X）两道，压在吊顶下方
      for (const z of [-iz + 0.22, iz - 0.22]) {
        p.push(mkBox(`ceil_beam_${z < 0 ? 's' : 'n'}`, -W / 2 + 0.06, W / 2 - 0.06,
          H - CEIL_T - 0.10, H - CEIL_T + JOIN, z - 0.09, z + 0.09, 'structure'));
      }
      // 壁柱：只给**长走廊**做（短房间塞柱子会显得又挤又假）；间距 ~4.5m，贴南北墙
      const n = W >= 6 ? Math.min(5, Math.max(2, Math.round(W / 4.5))) : 0;
      for (let i = 0; i < n; i++) {
        const x = -W / 2 + (W / (n + 1)) * (i + 1);
        for (const side of [-1, 1]) {
          const zi = side * iz;
          p.push(mkBox(`pilaster_${i}_${side < 0 ? 's' : 'n'}`,
            x - 0.16, x + 0.16, 0, H - CEIL_T + JOIN,
            Math.min(zi - side * 0.16, zi + side * 0.05), Math.max(zi - side * 0.16, zi + side * 0.05), 'structure'));
        }
      }
      // 墙面桥架/穿线管：贴两侧墙 2.3m 高的一条通长线管（分层感；嵌入式不共面）
      for (const side of [-1, 1]) {
        const zi = side * iz;
        p.push(mkBox(`conduit_${side < 0 ? 's' : 'n'}`, -W / 2 + 0.08, W / 2 - 0.08, 2.26, 2.36,
          Math.min(zi - side * 0.06, zi + side * 0.05), Math.max(zi - side * 0.06, zi + side * 0.05), 'detail'));
      }
      return p;
    },
  },
  /** 病房：两道顶梁 + 中央灯带 + 北墙窗（凸出门套 + 中梃 + 封板） */
  ward: {
    trimWalls: TRIM_WALLS.ward,
    doorFrames: true,
    decorate(cls) {
      const { W, H, D } = cls, hx = W / 2, iz = D / 2 - WALL_IN;
      const p = [];
      // 顶梁：沿进深方向两道（避开中央灯带）
      for (const x of [-0.72, 0.72]) {
        p.push(mkBox(`ceil_beam_${x < 0 ? 'w' : 'e'}`, x - 0.07, x + 0.07,
          H - CEIL_T - 0.10, H - CEIL_T + JOIN, -iz + 0.02, iz - 0.02, 'structure'));
      }
      p.push(lightPanel(-0.45, 0.45, -0.25, 0.25, cls));
      // 窗（北墙）：外框 1.40×1.10、凸出内表面 7cm、嵌入墙 4cm
      const yc = 1.85, fz0 = iz - 0.07, fz1 = iz + 0.04;
      const xo = 0.70, bar = 0.09;
      p.push(mkBox('window_top', -xo, xo, yc + 0.55 - bar, yc + 0.55, fz0, fz1, 'trim'));
      p.push(mkBox('window_bot', -xo, xo, yc - 0.55, yc - 0.55 + bar, fz0, fz1, 'trim'));
      p.push(mkBox('window_l', -xo, -xo + bar, yc - 0.55 + bar - JOIN, yc + 0.55 - bar + JOIN, fz0, fz1, 'trim'));
      p.push(mkBox('window_r', xo - bar, xo, yc - 0.55 + bar - JOIN, yc + 0.55 - bar + JOIN, fz0, fz1, 'trim'));
      p.push(mkBox('window_mullion', -0.03, 0.03, yc - 0.55 + bar - JOIN, yc + 0.55 - bar + JOIN, fz0 + 0.01, fz1 - 0.01, 'trim'));
      p.push(mkBox('window_board', -xo + bar - JOIN - 0.02, xo - bar + JOIN + 0.02,
        yc - 0.55 + bar - JOIN - 0.02, yc + 0.55 - bar + JOIN + 0.02, fz0 + 0.04, fz1 - 0.01, 'detail'));
      // 窗下暖气片（避难所/病房的标配；凸出墙面 8cm，带 4 片散热肋）
      p.push(mkBox('radiator_body', -0.60, 0.60, 0.18, 0.62, iz - 0.10, iz + 0.02, 'detail'));
      for (let i = 0; i < 4; i++) {
        const x = -0.45 + i * 0.30;
        p.push(mkBox(`radiator_fin_${i}`, x - 0.05, x + 0.05, 0.22, 0.58, iz - 0.13, iz - 0.05, 'detail'));
      }
      return p;
    },
  },
  /** 太平间：中央排水沟 + 格栅肋 + 两侧冷柜（三层抽屉 + 把手 + 通风格栅） */
  morgue: {
    trimWalls: TRIM_WALLS.morgue,
    doorFrames: false,
    decorate(cls) {
      const { W, H, D } = cls, hz = D / 2, iz = hz - WALL_IN, ix = W / 2 - WALL_IN;
      const p = [];
      p.push(lightPanel(-0.40, 0.40, -0.20, 0.20, cls));
      // 排水沟：贴在楼板上（凸出 1cm），沿进深方向
      p.push(mkBox('drain_channel', -0.14, 0.14, -0.04, 0.01, -iz + 0.04, iz - 0.04, 'detail'));
      for (const [i, z] of [[0, -0.90], [1, 0], [2, 0.90]]) {
        p.push(mkBox(`drain_rib_${i}`, -0.13, 0.13, -0.02, 0.02, z - 0.03, z + 0.03, 'detail'));
      }
      // 两侧冷柜：外沿距墙内表面 2cm（绝不穿墙），正面朝中央通道
      const RW = 0.43, CLEAR = 0.02;
      const rack = (side) => {
        const tag = side < 0 ? 'a' : 'b';                     // a=西侧 b=东侧
        const x_outer = side * (ix - CLEAR);
        const x0 = Math.min(x_outer, x_outer - side * RW), x1 = Math.max(x_outer, x_outer - side * RW);
        const face = side < 0 ? x1 : x0;                      // 正面所在平面
        const dir = -side;                                    // 正面凸出方向
        p.push(mkBox(`rack_${tag}_plinth`, Math.min(x0 + 0.02, x1 - 0.02), Math.max(x0 + 0.02, x1 - 0.02),
          -0.01, 0.07, -iz + 0.35, iz - 0.35, 'structure'));
        p.push(mkBox(`rack_${tag}_body`, x0, x1, 0.05, 1.74, -iz + 0.29, iz - 0.29, 'structure'));
        const bands = [[0.10, 0.60], [0.65, 1.15], [1.20, 1.70]];
        bands.forEach(([y0, y1], i) => {
          const f0 = face - dir * 0.01, f1 = face + dir * 0.05;   // 面板：压进柜体 1cm，凸出 5cm
          p.push(mkBox(`rack_${tag}_tray_${i}`, Math.min(f0, f1), Math.max(f0, f1), y0, y1, -iz + 0.33, iz - 0.33, 'trim'));
          const h0 = face + dir * 0.05, h1 = face + dir * 0.075;  // 把手：再凸出 2.5cm
          const hy = (y0 + y1) / 2;
          p.push(mkBox(`rack_${tag}_handle_${i}`, Math.min(h0, h1), Math.max(h0, h1), hy - 0.02, hy + 0.02, -0.30, 0.30, 'detail'));
          const v0 = face + dir * 0.05, v1 = face + dir * 0.068;  // 通风格栅：抽屉面板上缘一道
          p.push(mkBox(`rack_${tag}_vent_${i}`, Math.min(v0, v1), Math.max(v0, v1), y1 - 0.10, y1 - 0.04, -iz + 0.45, iz - 0.45, 'detail'));
        });
      };
      rack(-1); rack(1);
      return p;
    },
  },
};

// ════════════════════════════════════════════════════════════════════════════
// ⑥ 道具配方（固定尺寸；footprint 必须与清单逐位一致 —— 碰撞盒就是它）
// ════════════════════════════════════════════════════════════════════════════
const PROPS = {
  /** 病床：整体 0.90(宽) × 2.00(长) × 0.80(高)。床架/床垫/床头板/床尾板/护栏/四腿/四脚轮 */
  bed_b() {
    const p = [];
    p.push(mkBox('frame', -0.43, 0.43, 0.30, 0.40, -0.97, 0.97, 'structure'));
    p.push(mkBox('mattress', -0.41, 0.41, 0.38, 0.52, -0.90, 0.90, 'soft'));
    p.push(mkBox('headboard', -0.45, 0.45, 0.28, 0.80, -1.00, -0.94, 'trim'));
    p.push(mkBox('footboard', -0.45, 0.45, 0.28, 0.62, 0.94, 1.00, 'trim'));
    for (const s of [-1, 1]) {
      p.push(mkBox(`rail_${s < 0 ? 'l' : 'r'}`, s * 0.40, s * 0.45, 0.50, 0.64, -0.55, 0.55, 'trim'));
    }
    for (const [i, sx, sz] of [[0, -1, -1], [1, 1, -1], [2, -1, 1], [3, 1, 1]]) {
      p.push(mkBox(`leg_${'abcd'[i]}`, sx * 0.33, sx * 0.39, 0.05, 0.32, sz * 0.83, sz * 0.89, 'structure'));
      p.push(mkWheel(`caster_${'abcd'[i]}`, sx * 0.36, 0.05, sz * 0.86, 0.05, 0.03, 'detail'));
    }
    return p;
  },
  /** 档案柜：整体 0.80(宽) × 0.50(深) × 1.25(高)。踢脚/柜体/台面/三抽屉面板/把手/标签牌 */
  cabinet_a() {
    const p = [];
    p.push(mkBox('plinth', -0.36, 0.36, -0.01, 0.07, -0.21, 0.15, 'structure'));
    p.push(mkBox('body', -0.40, 0.40, 0.05, 1.21, -0.25, 0.19, 'structure'));
    p.push(mkBox('top', -0.40, 0.40, 1.19, 1.25, -0.25, 0.25, 'trim'));
    const bands = [[0.11, 0.45], [0.47, 0.81], [0.83, 1.17]];
    bands.forEach(([y0, y1], i) => {
      p.push(mkBox(`drawer_${i}`, -0.36, 0.36, y0, y1, 0.18, 0.23, 'trim'));
      const hy = (y0 + y1) / 2 - 0.06;
      p.push(mkBox(`handle_${i}`, -0.12, 0.12, hy - 0.02, hy + 0.02, 0.22, 0.25, 'detail'));
    });
    p.push(mkBox('label', -0.11, 0.11, 1.00, 1.06, 0.22, 0.25, 'detail'));
    return p;
  },
};

/** v1 的 hall_main 壳体（16×3 楼板 + 两道顶梁 + 4 立柱）。
 *  保留它是因为：hall_main 被 4 种尺寸的房间共用，在 canonical 模式下加天花板会与邻接 hall 房间
 *  的天花板**同 Y 共面重叠**（分区色还不同 → 闪）。逐房间出壳（--mode variants）才是它的正解。 */
function legacyHallParts() {
  const p = [];
  p.push(mkBox('floor', -8, 8, -FLOOR_T, 0, -1.5, 1.5, 'floor'));
  p.push(mkBox('beam_n', -8, 8, 2.90, 3.00, 1.10, 1.50, 'trim'));
  p.push(mkBox('beam_s', -8, 8, 2.90, 3.00, -1.50, -1.10, 'trim'));
  [-7.5, -2.5, 2.5, 7.5].forEach((x) => p.push(mkBox(`pillar_${x}`, x - 0.15, x + 0.15, 0, 3.0, -0.15, 0.15, 'structure')));
  return p;
}

// ════════════════════════════════════════════════════════════════════════════
// ⑦ 计划：套件清单（由房间类推出）
// ════════════════════════════════════════════════════════════════════════════
const ARCH_OF = { hall_main: 'hall', hospital_ward: 'ward', morgue: 'morgue' };
const KIT_META = {
  hall_main: { kind: 'room', file: 'kits/hall_main.glb', tags: ['level', 'asylum'], addressablesGroup: 'kits_rooms' },
  hospital_ward: { kind: 'room', file: 'kits/hospital_ward.glb', tags: ['level', 'asylum'], addressablesGroup: 'kits_rooms' },
  morgue: { kind: 'room', file: 'kits/morgue.glb', tags: ['level', 'asylum'], addressablesGroup: 'kits_rooms' },
  bed_b: { kind: 'prop', file: 'props/bed_b.glb', tags: ['prop'], addressablesGroup: 'kits_props' },
  cabinet_a: { kind: 'prop', file: 'props/cabinet_a.glb', tags: ['prop', 'evidence'], addressablesGroup: 'kits_props' },
};

/**
 * 变体 id（`hall_main_entrance_safe`）→ 基础套件 id（`hall_main`）：前缀匹配，取最长者。
 * 为什么需要：关卡 DSL 里房间引用的是**变体 id**（那是运行时 `KitMeshLibrary.GetParts` 的键），
 * 而配方/元数据（arch、tags、addressablesGroup）是按基础 id 组织的。
 */
function baseKitOf(kitId) {
  if (KIT_META[kitId]) return kitId;
  let best = null;
  for (const base of Object.keys(KIT_META)) {
    if (kitId.startsWith(base + '_') && (!best || base.length > best.length)) best = base;
  }
  return best;
}
/** DSL 里的 kit（可能是变体 id）→ 架构名（hall / ward / morgue） */
function archOfKit(kitId) { const b = baseKitOf(kitId); return b ? ARCH_OF[b] : null; }

function roomClassFor(roomId, kit) {
  const r = ROOMS.find((x) => x.id === roomId && x.kit === kit);
  if (!r) throw new Error(`[kits] 关卡里找不到房间 ${roomId}（kit=${kit}）`);
  return classOf(kit, ARCH_OF[kit], r);
}

/** 构建套件计划：id → { kind, file, parts, footprint, classKey, roomIds } */
function buildPlan() {
  const plan = [];
  if (MODE === 'canonical') {
    for (const id of ['hall_main', 'hospital_ward', 'morgue']) {
      const meta = KIT_META[id];
      if (id === 'hall_main') {
        plan.push({ id, kit: id, ...meta, footprint: [16, 3], parts: legacyHallParts(), cls: null });
        continue;
      }
      const cls = roomClassFor(CANONICAL_ROOM[id], id);
      plan.push({ id, kit: id, ...meta, footprint: [cls.W, cls.D], parts: [...shellParts(cls), ...ARCH[cls.arch].decorate(cls)], cls });
    }
  } else {
    // variants：每个 (基础 kit, 尺寸, 门洞布局) 类出一个套件；标称房间沿用原 id
    const classes = new Map();
    for (const r of ROOMS) {
      const base = baseKitOf(r.kit);
      const arch = base ? ARCH_OF[base] : null;
      if (!arch) continue;
      const k = classKey(base, arch, r);
      if (!classes.has(k)) classes.set(k, { cls: classOf(base, arch, r), roomIds: [] });
      classes.get(k).roomIds.push(r.id);
    }
    for (const [k, { cls, roomIds }] of classes) {
      const id = variantIdFor(cls.kit, cls);
      const meta = KIT_META[cls.kit];
      plan.push({
        id, kit: cls.kit, kind: meta.kind,
        file: meta.file.replace(`${cls.kit}.glb`, `${id}.glb`),
        tags: meta.tags, addressablesGroup: meta.addressablesGroup,
        footprint: [cls.W, cls.D],
        parts: [...shellParts(cls), ...ARCH[cls.arch].decorate(cls)],
        cls, roomIds, classKey: k,
      });
    }
  }
  for (const id of ['bed_b', 'cabinet_a']) {
    const meta = KIT_META[id];
    const parts = PROPS[id]();
    const fp = [Math.max(...parts.map((p) => p.max[0])) - Math.min(...parts.map((p) => p.min[0])),
      Math.max(...parts.map((p) => p.max[2])) - Math.min(...parts.map((p) => p.min[2]))];
    plan.push({ id, kit: id, ...meta, footprint: fp.map(r3), parts, cls: null });
  }
  // 顺序固定（清单 diff 可读）：房间套件按 id 排序，道具在后
  const rooms = plan.filter((k) => k.kind === 'room').sort((a, b) => a.id.localeCompare(b.id));
  const props = plan.filter((k) => k.kind === 'prop').sort((a, b) => a.id.localeCompare(b.id));
  return [...rooms, ...props];
}

// ════════════════════════════════════════════════════════════════════════════
// ⑧ 几何自检（本机可判定的部分 —— 直接对着复核者的三类问题）
// ════════════════════════════════════════════════════════════════════════════
const EPS = 1e-6;
function lintKit(kit) {
  const issues = [];
  const parts = kit.parts;
  // C1 楼板/占地件 = 声明 footprint（房间套件用 floor 节点；道具用整体包围盒）
  const floor = parts.find((p) => p.name === 'floor');
  if (kit.kind === 'room') {
    if (!floor) issues.push('缺少 floor 部件（footprint 的真源，门禁 B2 按节点名找它）');
    else {
      const s = sizeOf(floor);
      if (Math.abs(s[0] - kit.footprint[0]) > 1e-6 || Math.abs(s[2] - kit.footprint[1]) > 1e-6) {
        issues.push(`楼板 ${r3(s[0])}×${r3(s[2])} ≠ 清单 footprint ${kit.footprint[0]}×${kit.footprint[1]}`);
      }
    }
  } else {
    const bx = [Math.min(...parts.map((p) => p.min[0])), Math.max(...parts.map((p) => p.max[0]))];
    const bz = [Math.min(...parts.map((p) => p.min[2])), Math.max(...parts.map((p) => p.max[2]))];
    if (Math.abs((bx[1] - bx[0]) - kit.footprint[0]) > 0.05) issues.push(`道具整体 X=${r3(bx[1] - bx[0])} 与 footprint ${kit.footprint[0]} 差 > 0.05（门禁 B2 会红）`);
    if (Math.abs((bz[1] - bz[0]) - kit.footprint[1]) > 0.05) issues.push(`道具整体 Z=${r3(bz[1] - bz[0])} 与 footprint ${kit.footprint[1]} 差 > 0.05（门禁 B2 会红）`);
  }
  // C2 占地件必须躺平（gate-asset-bbox B3 的原意）：只对**房间套件的楼板**成立 ——
  //    道具的柜体本来就是竖的（档案柜 0.8×0.5×1.25），拿它判"竖立"是误判。
  for (const p of parts.filter((x) => x.name === 'floor')) {
    const s = sizeOf(p);
    if (s[1] > Math.max(s[0], s[2])) issues.push(`占地件 ${p.name} 竖立（Y=${r3(s[1])} > max(XZ)=${r3(Math.max(s[0], s[2]))}）`);
  }
  const isRoomKit = kit.kind === 'room' && kit.cls;
  // C3 穿墙：房间套件里除「楼板/线脚」外，任何部件都不得越过墙内表面
  if (isRoomKit) {
    const { W, D } = kit.cls, ix = W / 2 - WALL_IN, iz = D / 2 - WALL_IN;
    for (const p of parts) {
      if (p.name === 'floor') continue;
      const trim = p.role === 'trim' || /^window_/.test(p.name);
      const slack = trim ? 0.06 : WALL_IN;      // 线脚允许嵌入墙 6cm；其余最多到墙中线
      const ox = Math.max(0, Math.abs(p.min[0]) - ix - slack, Math.abs(p.max[0]) - ix - slack);
      const oz = Math.max(0, Math.abs(p.min[2]) - iz - slack, Math.abs(p.max[2]) - iz - slack);
      if (ox > EPS || oz > EPS) issues.push(`部件 ${p.name} 穿出墙内表面（Δx=${r3(ox)} Δz=${r3(oz)}）`);
      if (p.max[1] > kit.cls.H + EPS) issues.push(`部件 ${p.name} 高过层高（y=${r3(p.max[1])} > ${kit.cls.H}）`);
    }
  }
  // C4 共面重叠：**同向共面且面片有交叠** = z-fighting（背对背贴合不算，视觉无害）
  for (let i = 0; i < parts.length; i++) {
    for (let j = i + 1; j < parts.length; j++) {
      const A = parts[i], B = parts[j];
      const ov = [0, 1, 2].map((k) => Math.min(A.max[k], B.max[k]) - Math.max(A.min[k], B.min[k]));
      if (ov.some((v) => v < -EPS)) continue;                       // 不相接
      if (ov.every((v) => v > EPS)) continue;                       // 体积重叠（有意拼接）
      const axes = [0, 1, 2].filter((k) => ov[k] > EPS);
      if (axes.length !== 2) continue;                              // 边/点接触：无害
      const k = [0, 1, 2].find((q) => Math.abs(ov[q]) <= EPS);
      const sameFace = Math.abs(A.max[k] - B.max[k]) <= 1e-6 || Math.abs(A.min[k] - B.min[k]) <= 1e-6;
      if (sameFace) issues.push(`共面重叠（同向，会闪）：${A.name} ↔ ${B.name}（法向轴 ${'XYZ'[k]}）`);
    }
  }
  // C5 面数预算（移动端低多边形）
  const tris = parts.reduce((n, p) => n + (p.shape === 'wheel' ? 28 : 12), 0);
  if (tris > 900) issues.push(`三角面 ${tris} 超出低多边形预算（900）`);
  return { issues, tris };
}

/** 房间适配核对：每个房间用的套件，其壳尺寸是否正好等于该房间 */
function lintRoomFit(plan) {
  const byId = new Map(plan.map((k) => [k.id, k]));
  const byClass = new Map(plan.filter((k) => k.cls).map((k) => [k.classKey, k]));
  const rows = [];
  for (const r of ROOMS) {
    const base = baseKitOf(r.kit);
    const arch = base ? ARCH_OF[base] : null;
    if (!arch) { rows.push({ room: r.id, kit: r.kit, ok: false, why: '无配方（arch 未知）' }); continue; }
    const key = classKey(base, arch, r);
    if (MODE === 'variants') {
      // ★ 这一条是**关卡 DSL ↔ 套件清单**的耦合判据：房间必须引用"为它那一类出的那一个套件"。
      // 为什么必须是等号而不是"存在即可"：`hall_main` 也在清单里（它是主廊那一类），
      // 所以"只要 id 在清单里就算过"抓不到"DSL 退回旧 kit 名"这种回归 ——
      // 实测场景：有人重跑 `tools/gen-asylum-v1.mjs` 把 4 个 hall 房间写回 `hall_main`，
      // 于是 4 个房间又用上 18m 的壳。等号判据会在链条第 12 步立刻判红。
      const k = byClass.get(key);
      if (!k) rows.push({ room: r.id, kit: r.kit, ok: false, why: '没有为该类出套件' });
      else if (k.id !== r.kit) rows.push({ room: r.id, kit: r.kit, ok: false, why: `DSL 写的是 ${r.kit}，但该房间类应引用 ${k.id}（DSL 与套件清单脱钩）` });
      else rows.push({ room: r.id, kit: k.id, ok: true, why: '逐类匹配' });
    } else {
      const k = byId.get(r.kit);
      const fp = k?.footprint ?? [0, 0];
      const ok = Math.abs(fp[0] - r.W) <= 0.05 && Math.abs(fp[1] - r.D) <= 0.05;
      rows.push({ room: r.id, kit: r.kit, ok, why: ok ? '尺寸吻合' : `套件标称 ${fp[0]}×${fp[1]} vs 房间 ${r3(r.W)}×${r3(r.D)}` });
    }
  }
  return rows;
}

// ════════════════════════════════════════════════════════════════════════════
// ⑨ Blender 脚本（确定性：工厂设置 + 固定顺序 + export_yup=False）
// ════════════════════════════════════════════════════════════════════════════
function blenderScript(kits) {
  const lines = [];
  lines.push('import bpy, json, os, math');
  lines.push('ROLE = json.loads(' + JSON.stringify(JSON.stringify(ROLE_COLOR)) + ')');
  lines.push('KITS = json.loads(' + JSON.stringify(JSON.stringify(kits)) + ')');
  lines.push(String.raw`
def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def mat(name, rgba):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = rgba
        if "Roughness" in bsdf.inputs: bsdf.inputs["Roughness"].default_value = 0.85
        if "Metallic" in bsdf.inputs: bsdf.inputs["Metallic"].default_value = 0.0
    return m

report = {}
for kit in KITS:
    clear()
    mats = {}
    for part in kit["parts"]:
        role = part["role"]
        if role not in mats:
            mats[role] = mat("role_" + role, ROLE[role])
        if part.get("shape") == "wheel":
            bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=part["radius"], depth=part["depth"],
                                                location=part["at"], rotation=part["rot"])
        else:
            bpy.ops.mesh.primitive_cube_add(size=1.0, location=part["at"])
            ob = bpy.context.active_object
            ob.scale = (part["size"][0], part["size"][1], part["size"][2])
            bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        ob = bpy.context.active_object
        ob.name = part["name"]
        ob.data.materials.append(mats[role])
    out = kit["out"]
    os.makedirs(os.path.dirname(out), exist_ok=True)
    # ⚠ 必须 export_yup=False：Blender 默认按 Y-up 导出会把 Z-up 旋转 90°，
    # 于是"地面 Z 向深度"被搬到 glTF 的 Y 上 —— footprint 会变成 [宽, 高]，
    # 碰撞盒与可见模型相差 1.4m（独立复核 F3 实测）。关卡是 XZ 平面布局，保留 Z-up 才自洽。
    bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', use_selection=False, export_yup=False)
    tris = 0; verts = 0; objs = 0
    for ob in bpy.data.objects:
        if ob.type == 'MESH':
            ob.data.calc_loop_triangles()
            tris += len(ob.data.loop_triangles); verts += len(ob.data.vertices); objs += 1
    report[kit["id"]] = {"tris": tris, "verts": verts, "objects": objs, "bytes": os.path.getsize(out)}
print("KIT_REPORT=" + json.dumps(report))
`);
  return lines.join('\n');
}

/** Blender 可执行：PATH 里没有（本机装在 Program Files），所以显式探测绝对路径 */
function findBlender() {
  const cands = [
    process.env.BLENDER,
    'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe',
    'C:/Program Files/Blender Foundation/Blender 5.0/blender.exe',
  ].filter(Boolean);
  for (const c of cands) if (fs.existsSync(c)) return c;
  return 'blender';   // 交给 PATH，失败时报可读错误
}

function toBlenderKit(kit, outFile) {
  return {
    id: kit.id, out: outFile,
    parts: kit.parts.map((p) => (p.shape === 'wheel'
      ? { name: p.name, role: p.role, shape: 'wheel', at: p.at.map(r3), radius: p.radius, depth: p.depth, rot: p.rot }
      : { name: p.name, role: p.role, size: sizeOf(p).map(r3), at: centerOf(p).map(r3) })),
  };
}

// ════════════════════════════════════════════════════════════════════════════
// ⑩ 主流程
// ════════════════════════════════════════════════════════════════════════════
const manifest = JSON.parse(fs.readFileSync(MANIFEST, 'utf8'));
const SRC_ROOT = path.join(ROOT, 'unity', manifest.sourceRoot ?? 'Assets/ThirdParty/CC0');
const plan = buildPlan();

// ── `--list`：只打印计划（每行一个 `id<TAB>file`）后退出 ──
// 为什么需要：本机 Blender 会被 proot SIGKILL（见 onlySet 注释），生成必须**分批**；
// 而分批的前提是能先拿到**权威的**计划 id 清单 —— 不能靠人从适配输出里抄（那会漏/会漂）。
// 也让 CI 与 shell 脚本能用同一条命令自查"计划里到底有哪些套件"。
if (hasFlag('--list')) {
  for (const k of plan) console.log(`${k.id}\t${k.file}`);
  process.exit(0);
}

// ── 几何自检（每次运行都跑；--lint-only 时到此为止）──
console.log(`[kits] 几何自检 · 模式 ${MODE} · ${plan.length} 个套件`);
let lintFails = 0;
const lintReport = [];
for (const kit of plan) {
  const { issues, tris } = lintKit(kit);
  lintReport.push({ id: kit.id, tris, parts: kit.parts.length, footprint: kit.footprint, issues });
  if (issues.length) { lintFails += issues.length; console.log(`  ✗ ${kit.id}`); issues.forEach((s) => console.log(`      - ${s}`)); }
}
const fit = lintRoomFit(plan);
const unfit = fit.filter((r) => !r.ok);
console.log(`  ${lintFails === 0 ? '✓' : '✗'} 套件自检：${plan.length} 个 · 部件 ${plan.reduce((n, k) => n + k.parts.length, 0)} 个 · 问题 ${lintFails}`);
console.log(`  房间适配：${fit.length - unfit.length}/${fit.length} 个房间的套件与房间尺寸吻合`);
for (const r of unfit) console.log(`      · 未适配 ${r.room}（kit ${r.kit}）：${r.why}`);

if (lintOnly) process.exit(lintFails ? 1 : 0);
// variants 模式下"房间未适配"是**硬失败**：那意味着关卡 DSL 与套件配方脱钩，
// 生成出来的清单一定配不上关卡（宁可不生成，也不要产出配不上的资产）。
if (MODE === 'variants' && unfit.length) {
  console.error(`[kits] ✗ variants 模式下有 ${unfit.length} 个房间的套件对不上 —— 先修关卡 DSL 或配方，再生成`);
  process.exit(1);
}
if (lintFails) { console.error('[kits] 几何自检未通过 —— 不生成（宁可不出资产，也不出穿模/闪烁的资产）'); process.exit(1); }

if (checkOnly) {
  // 只校验：产物存在 + 清单记录的 sha256/bytes 与产物一致
  const problems = [];
  for (const k of manifest.kits) {
    const f = path.join(SRC_ROOT, k.file);
    if (!fs.existsSync(f)) { problems.push(`缺产物：${k.file}`); continue; }
    const buf = fs.readFileSync(f);
    const sha = createHash('sha256').update(buf).digest('hex');
    if (!k.sha256) problems.push(`${k.id} 清单缺 sha256`);
    else if (k.sha256 !== sha) problems.push(`${k.id} sha256 不一致（清单 ${k.sha256.slice(0, 12)} vs 实际 ${sha.slice(0, 12)}）`);
    if (k.bytes && k.bytes !== buf.length) problems.push(`${k.id} 字节数不一致（清单 ${k.bytes} vs 实际 ${buf.length}）`);
  }
  console.log(`[kits] 校验 ${manifest.kits.length} 个套件产物`);
  if (problems.length) { for (const p of problems) console.log('  ✗ ' + p); console.log(`  结果：${problems.length} 个问题 ✗`); process.exit(1); }
  console.log('  结果：产物与清单记录逐项一致 ✓');
  process.exit(0);
}

// ── 生成 ──
const outRoot = emitDir ? path.resolve(ROOT, emitDir) : SRC_ROOT;
// `--only`：只把被选中的套件交给 Blender（见 onlySet 的注释：仅为绕开 proot SIGKILL，不改变计划口径）
const toGenerate = onlySet ? plan.filter((k) => onlySet.has(k.id)) : plan;
if (onlySet)
{
  const missing = [...onlySet].filter((id) => !plan.some((k) => k.id === id));
  if (missing.length) { console.error(`[kits] --only 里有计划中不存在的套件：${missing.join(', ')}`); process.exit(2); }
}
const kitsForBlender = toGenerate.map((k) => toBlenderKit(k, path.join(outRoot, k.file)));
const script = blenderScript(kitsForBlender);
const scriptPath = path.join(ROOT, 'tmp/dbg/gen-kits.py');
fs.mkdirSync(path.dirname(scriptPath), { recursive: true });
fs.writeFileSync(scriptPath, script, 'utf8');

const blender = findBlender();
console.log(`[kits] 用 Blender 生成 ${toGenerate.length} 个套件 → ${path.relative(ROOT, outRoot) || '.'}（${blender}）`
  + (onlySet ? `（--only 分批：计划共 ${plan.length} 个）` : ''));
let report = {};
try {
  const out = execFileSync(blender, ['-b', '--factory-startup', '--python', scriptPath], {
    encoding: 'utf8', cwd: ROOT, stdio: 'pipe', timeout: 900000, maxBuffer: 32 * 1024 * 1024,
  });
  const m = out.match(/KIT_REPORT=(\{.*\})/);
  if (m) report = JSON.parse(m[1]);
  else { console.error('[kits] Blender 未输出 KIT_REPORT'); console.error(String(out).slice(-1200)); process.exit(1); }
} catch (e) {
  console.error('[kits] Blender 执行失败：');
  console.error(String(e.stdout ?? '').slice(-1500));
  console.error(String(e.stderr ?? '').slice(-800));
  process.exit(1);
}

// ── 汇总（sha256 / bytes / 三角面），回写清单 ──
// `--only` 分批时：**未在本次生成范围内**的套件沿用清单里已有的记录（不重算、不报错）——
// 但产物文件必须仍在，否则说明有人删了资产却留着清单条目，那要判红。
const prevById = new Map((manifest.kits ?? []).map((k) => [k.id, k]));
const result = [];
let keptFromManifest = 0;
const notYetGenerated = [];
for (const k of plan) {
  if (onlySet && !onlySet.has(k.id)) {
    const prev = prevById.get(k.id);
    const pf = path.join(SRC_ROOT, k.file);
    // 三种情况要分清（本项目最贵的教训就是"把不同的事混成一件"）：
    //   ① 清单有记录 + 产物在 → 沿用（正常的分批状态）
    //   ② 清单无记录 + 产物在 → 沿用不了记录，但产物是真的 → 登记为"待补记录"、**不中断**
    //   ③ 清单无记录 + 产物不在 → **分批过程中的正常状态**（这一批还没轮到它）
    //      → 也登记为"待补"，**绝不中断**。中断会让这一批已经生成好的文件白做
    //        （实测踩过：首批 4 个生成成功，却因后面的 id 还没记录而整批退出，清单一行没写）。
    // 关键不变量：**写进清单的每一条都必须在磁盘上真的存在** —— 这由下面 mkEntry 里的
    // `fs.existsSync` 保证，所以"待补"不会变成"清单谎报有资产"。
    if (prev && fs.existsSync(pf)) { result.push(prev); keptFromManifest++; continue; }
    notYetGenerated.push(k.id);
    continue;
  }
  const f = path.join(outRoot, k.file);
  if (!fs.existsSync(f)) { console.error(`[kits] ✗ 未生成：${k.file}`); process.exit(1); }
  const buf = fs.readFileSync(f);
  const sha = createHash('sha256').update(buf).digest('hex');
  const r = report[k.id] ?? {};
  const bmin = [0, 1, 2].map((i) => Math.min(...k.parts.map((q) => q.min[i])));
  const bmax = [0, 1, 2].map((i) => Math.max(...k.parts.map((q) => q.max[i])));
  result.push({
    id: k.id, kind: k.kind, file: k.file, tags: k.tags, addressablesGroup: k.addressablesGroup,
    sha256: sha, bytes: buf.length, triangles: r.tris ?? 0, vertices: r.verts ?? 0, objects: r.objects ?? k.parts.length,
    footprint: k.footprint, bbox: [0, 1, 2].map((i) => r3(bmax[i] - bmin[i])),
    // Blender 的网格顶点数（方块 8）/ 导出 GLB 后的顶点数（平面着色会按面拆点：方块 24 · 八边轮 48）
    blenderVertices: r.verts ?? 0,
    glbVertices: k.parts.reduce((n, q) => n + (q.shape === 'wheel' ? 48 : 24), 0),
    parts: k.parts.map((q) => ({ name: q.name, role: q.role, size: sizeOf(q).map(r3) })),
    resPath: `Kits/${k.id}.glb`,
    generator: 'tools/gen-kits.mjs（Blender 脚本化生成 · 按房间类参数化 · 确定性体块）',
  });
  console.log(`  ✓ ${k.id.padEnd(26)} ${String(r.verts ?? '?').padStart(5)} 顶点 · ${String(r.tris ?? '?').padStart(4)} 面 · ${(buf.length / 1024).toFixed(1)} KB · footprint ${k.footprint[0]}×${k.footprint[1]} · sha ${sha.slice(0, 12)}`);
}
if (keptFromManifest) console.log(`  · 本次沿用清单既有记录 ${keptFromManifest} 个（--only 分批）`);
if (notYetGenerated.length) {
  // 如实登记 + 明确后续动作，而不是静默丢条目（丢条目 = 那些房间在运行时静默退回程序化体块）
  console.log(`  ⚠ 以下 ${notYetGenerated.length} 个套件产物已在磁盘、但本次未纳入清单（请再跑一次完整生成补齐记录）：`);
  console.log(`      ${notYetGenerated.join(', ')}`);
}

if (emitDir) {
  const rep = { mode: MODE, blender, generatedAt: new Date().toISOString(), kits: result, lint: lintReport, roomFit: fit, rooms: ROOMS };
  fs.writeFileSync(path.join(outRoot, 'kit-plan.json'), JSON.stringify(rep, null, 2) + '\n', 'utf8');
  console.log(`[kits] 证据写入 ${path.relative(ROOT, path.join(outRoot, 'kit-plan.json'))}`);
  process.exit(unfit.length && MODE === 'variants' ? 1 : 0);
}

// 清单是资产的唯一入口：kits[] 由配方推出（footprint 也由配方推出，不再手抄）
manifest.kits = result.map(({ parts, vertices, objects, ...k }) => ({
  id: k.id, kind: k.kind, file: k.file, tags: k.tags, addressablesGroup: k.addressablesGroup,
  sha256: k.sha256, bytes: k.bytes, triangles: k.triangles, generator: k.generator,
  footprint: k.footprint, resPath: k.resPath,
}));
manifest.kitsUpdatedBy = 'tools/gen-kits.mjs';
// `verticesByKit`：**从产出字节里量**，不用配方里的角点数。
//
// 【2026-10-04 修·独立复核 B2 抓到】原实现用 `k.vertices`（配方里"共享角点"的计数），
// 而 GLB accessor 的 POSITION 顶点是**平直着色拆点后**的数量 —— 实测倍数**精确 3.000**
// （Blender 把每个盒子的 8 个角点拆成 24 个顶点）。于是清单写 `hall_main:56`、真实 GLB 是 `168`。
// 当时没判红，只是因为 `csharp-verify` 恰好只核对**三角面数**（那一列是对的）；
// 任何人写一条"顶点对账"就会红 —— 属于**已上膛的假账**。
// 现在改成解析刚写出的 GLB，与产品 `GlbReader.VertexCount` 同口径（accessor POSITION count 求和）。
manifest.verticesByKit = Object.fromEntries(result.map((k) => [k.id, glbVertexCount(path.join(SRC_ROOT, k.file))]));
fs.writeFileSync(MANIFEST, JSON.stringify(manifest, null, 2) + '\n', 'utf8');
// 同步到 Resources（运行时经 Resources.Load 读清单）—— 与 v1 同口径；
// 另一处镜像（native/micprobe/res/raw/asset_manifest.json）由 gen-kit-resources.mjs 与 data-mirror.mjs 负责。
const resMirror = path.join(ROOT, 'unity/Assets/Resources/Data/asset-manifest.json');
if (fs.existsSync(path.dirname(resMirror))) {
  const text = JSON.stringify(manifest, null, 2) + '\n';
  if (!fs.existsSync(resMirror) || fs.readFileSync(resMirror, 'utf8') !== text) {
    fs.writeFileSync(resMirror, text, 'utf8');
    console.log('  写回 unity/Assets/Resources/Data/asset-manifest.json（运行时读这份）');
  }
}
console.log(`[kits] 清单已回写（${manifest.kits.length} 个套件）`);
if (unfit.length) console.log(`[kits] ⚠ canonical 模式下仍有 ${unfit.length} 个房间未适配（逐类变体待 native 侧放行 --mode variants）`);
