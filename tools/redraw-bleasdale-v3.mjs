#!/usr/bin/env node
/**
 * redraw-bleasdale-v3.mjs — 第三张图定稿：**一层与二层同位**，竖井落点自然成立
 *
 * ## 为什么前几版反复失败（一次说清）
 * 竖井要求「同一 XZ 矩形在两层都落在可走房间内」，而我把一层的可走区铺在 z1..8、二层铺在 z1..8
 * **但两层的房间切分不同** → 要么同层重叠（G1），要么竖井没有承载（G5），要么门跨到别的房间（G4）。
 * 根因是：**我在同时解两个耦合的约束**（竖井交集 + 同层不重叠），每次只改一个就崩另一个。
 *
 * ## 定稿做法：两层**同位镜像**
 * | 层 | 玄关/走廊 | 走廊 | 北侧房间 |
 * |---|---|---|---|
 * | 一层 f0 | entrance x0..4 z1..4 | corridor_f0 x4..12 z1..4 | kitchen_f0 x4..12 z4..8 |
 * | 二层 f1 | （无） | corridor_f1 x4..12 z1..4 | corridor2_f1 x4..6 z4..8；master_f1 x6..8 z4..8；attic_f1 x8..12 z4..8 |
 *
 * 两层共享 **z1..4 的走廊带** → 竖井取 x6..9, z2..3（两层都在走廊里）✓
 * 且每层内部房间按 z 或 x 互不重叠 ✓
 *
 * ## 门（每条都落在所属房间那条墙的范围内）
 * · entrance/d_east@z2.5 ↔ corridor_f0/d_west@z2.5（共享 x=4）
 * · corridor_f0/d_n@x=5.5 ↔ kitchen_f0/d_south@x=5.5（共享 z=4）
 * · corridor_f1/d_n1@x5.0 ↔ corridor2_f1/d_south@x5.0（z=4）
 * · corridor_f1/d_n2@x7.0 ↔ master_f1/d_south@x7.0（z=4）
 * · corridor_f1/d_n3@x10.0 ↔ attic_f1/d_south@x10.0（z=4）
 *
 * 用法：node tools/redraw-bleasdale-v3.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'tools/lib/map-layouts.mjs');
const checkOnly = process.argv.includes('--check');

const raw = fs.readFileSync(FILE, 'utf8');
const start = raw.indexOf('  bleasdale_v1: {');
if (start < 0) { console.error('[v3] ✗ 未找到 bleasdale_v1'); process.exit(1); }
let endMark = raw.indexOf('\n  },\n', start);
if (endMark < 0) { console.error('[v3] ✗ 未找到布局结束'); process.exit(1); }

const entry = `  bleasdale_v1: {
    id: 'bleasdale_v1',
    note: '布利斯代尔农舍（两层中型图）：两层同位——走廊带 z1..4 上下贯通，北侧一层厨房、二层起居/主卧/阁楼。'
      + '设计依据：官方中型图为两层农舍；本布局是 design（官方原图几何不公开），非官方还原。',
    zones: {
      entrance: ['entrance'],
      pressure: ['corridor_f0', 'kitchen_f0', 'corridor_f1', 'corridor2_f1', 'master_f1'],
      deep: ['attic_f1'],
    },
    extraction: { standard: 'entrance', deep: 'attic_f1' },
    events: [
      { type: 'power_surge', minute: 5, durationSec: 12, params: { scope: 'farmhouse' },
        sanityEffect: -4, counterplay: '总闸跳闸：先确认手电电量，再摸黑回玄关（安全区）等恢复' },
      { type: 'mirror_flicker', minute: 11, durationSec: 8, params: { scope: 'bedroom' },
        sanityEffect: -3, counterplay: '卧室镜面闪烁：不要盯着看（会加速掉理智），离开房间即止' },
    ],
    // 两层共享走廊带 z1..4 → 竖井 x6..9,z2..3 在两层都落在走廊内
    shafts: [
      { id: 'stair_well', kind: 'stair', minX: 6, minZ: 2, maxX: 9, maxZ: 3, fromFloor: 0, toFloor: 1 },
    ],
    boxes: [
      // ══ 一层 ══
      { id: 'entrance', x0: 0, x1: 4, z0: 1, z1: 4, h: 3.0, floor: 0,
        kit: 'hall_main_entrance_safe', zone: 'safe', evidence: false,
        props: [{ kit: 'cabinet_a', pos: [0, 0, 0], rot: 180, pref: 'nw' }],
        doors: [{ id: 'd_east', wall: 'east', at: 2.5 }] },

      { id: 'corridor_f0', x0: 4, x1: 12, z0: 1, z1: 4, h: 3.0, floor: 0,
        kit: 'hall_main', zone: 'pressure', evidence: false, props: [],
        doors: [
          { id: 'd_west', wall: 'west', at: 2.5 },     // ↔ entrance/d_east（共享 x=4）
          { id: 'd_n_kitchen', wall: 'north', at: 5.5 } // ↔ kitchen_f0/d_south（共享 z=4）
        ] },

      { id: 'kitchen_f0', x0: 4, x1: 12, z0: 4, z1: 8, h: 3.0, floor: 0,
        kit: 'hospital_ward', zone: 'pressure', evidence: true,
        props: [{ kit: 'cabinet_a', pos: [0, 0, 0], rot: 0, pref: 'nw' }],
        doors: [{ id: 'd_south', wall: 'south', at: 5.5 }] },

      // ══ 二层（走廊带与一层同位）══
      { id: 'corridor_f1', x0: 4, x1: 12, z0: 1, z1: 4, h: 3.0, floor: 1,
        kit: 'hall_main_corridor_main_f1', zone: 'pressure', evidence: false, props: [],
        doors: [
          { id: 'd_n1', wall: 'north', at: 5.0 },     // ↔ corridor2_f1
          { id: 'd_n2', wall: 'north', at: 7.0 },     // ↔ master_f1
          { id: 'd_n3', wall: 'north', at: 10.0 },    // ↔ attic_f1
        ] },

      { id: 'corridor2_f1', x0: 4, x1: 6, z0: 4, z1: 8, h: 3.0, floor: 1,
        kit: 'hall_main_corridor_link', zone: 'pressure', evidence: false, props: [],
        doors: [{ id: 'd_south', wall: 'south', at: 5.0 }] },

      { id: 'master_f1', x0: 6, x1: 8, z0: 4, z1: 8, h: 3.0, floor: 1,
        kit: 'hospital_ward', zone: 'pressure', evidence: true,
        props: [{ kit: 'bed_b', pos: [0, 0, 0], rot: 0, pref: 'nw' }],
        doors: [{ id: 'd_south', wall: 'south', at: 7.0 }] },

      { id: 'attic_f1', x0: 8, x1: 12, z0: 4, z1: 8, h: 3.0, floor: 1,
        kit: 'morgue', zone: 'high-risk', evidence: true,
        props: [{ kit: 'cabinet_a', pos: [0, 0, 0], rot: 270, pref: 'ne' }],
        doors: [{ id: 'd_south', wall: 'south', at: 10.0 }] },
    ],
    links: [
      ['entrance/d_east', 'corridor_f0/d_west', 2.0],
      ['corridor_f0/d_n_kitchen', 'kitchen_f0/d_south', 1.6],
      ['corridor_f1/d_n1', 'corridor2_f1/d_south', 1.6],
      ['corridor_f1/d_n2', 'master_f1/d_south', 1.6],
      ['corridor_f1/d_n3', 'attic_f1/d_south', 1.6],
    ],
  },`;

const out = raw.slice(0, start) + entry + raw.slice(endMark + 1);
if (!checkOnly) fs.writeFileSync(FILE, out, 'utf8');
console.log(`[v3] bleasdale_v1 定稿（${raw.length} → ${out.length} 字节）`);
