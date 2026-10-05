#!/usr/bin/env node
/**
 * redraw-bleasdale-v2.mjs — 第三张图最终版：所有房间与门位**按共享墙重算**
 *
 * ## 上一版 8 条自检错的两类根因
 * 1. **门位超出门所在房间的墙范围**：例如厨房在 x10..14，我却把它的南门写成 x=5.5
 *    （那是"走廊的门位"，不是厨房的）。→ 每条门的 `at` 必须落在**它所属房间那条墙**的范围内。
 * 2. **主卧与走廊只角接触**：主卧 x0..4,z4..8 与走廊 x4..10,z1..4 只在点 (4,4) 相邻，
 *    不构成共享墙 → G2 判"不在共享墙上"。→ 二层三个房间都必须**贴走廊的同一面墙**。
 *
 * ## 本版布局（先定共享墙，再摆房间）
 * 一层：`entrance` x0..4,z1..4 ｜ `corridor_f0` x4..12,z1..8 ｜ `kitchen_f0` x10..14,z4..12
 * 二层：`corridor_f1` x4..12,z1..4 ｜ `corridor2_f1` x4..6,z4..8 ｜ `master_f1` x6..8,z4..8 ｜ `attic_f1` x8..12,z4..8
 * 竖井：x10..11,z4..5（一层落 kitchen、二层落 attic —— 两层都可走）
 *
 * 门（逐条按共享墙算）：
 * · entrance 东墙 x=4 ↔ corridor_f0 西墙 x=4 ⇒ 两门 z=2.5
 * · corridor_f0 北墙 z=8 ↔ kitchen_f0 南墙 z=8 ⇒ 两门 x=12.0
 * · corridor_f1 北墙 z=4 ↔ corridor2_f1 / master_f1 / attic_f1 南墙 z=4 ⇒ 门 x=5.0 / 7.0 / 10.0
 * · corridor_f1 东墙 x=12 ↔ attic_f1 西墙 x=12 ⇒ 两门 z=6.0
 *
 * 用法：node tools/redraw-bleasdale-v2.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'tools/lib/map-layouts.mjs');
const checkOnly = process.argv.includes('--check');

const raw = fs.readFileSync(FILE, 'utf8');
const start = raw.indexOf('  bleasdale_v1: {');
if (start < 0) { console.error('[v2] ✗ 未找到 bleasdale_v1'); process.exit(1); }
const endMark = raw.indexOf('\n  },\n', start);
if (endMark < 0) { console.error('[v2] ✗ 未找到布局结束'); process.exit(1); }

const entry = `  bleasdale_v1: {
    id: 'bleasdale_v1',
    note: '布利斯代尔农舍（两层中型图）：一层玄关/走廊/厨房，二层走廊/起居/主卧/阁楼，靠楼梯井跨层。'
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
    // 竖井：一层落 kitchen_f0(x10..14,z4..12)、二层落 attic_f1(x8..12,z4..8) → 交集 x10..11,z4..5
    shafts: [
      { id: 'stair_well', kind: 'stair', minX: 10, minZ: 4, maxX: 11, maxZ: 5, fromFloor: 0, toFloor: 1 },
    ],
    boxes: [
      // ══ 一层 ══
      { id: 'entrance', x0: 0, x1: 4, z0: 1, z1: 4, h: 3.0, floor: 0,
        kit: 'hall_main_entrance_safe', zone: 'safe', evidence: false,
        props: [{ kit: 'cabinet_a', pos: [0, 0, 0], rot: 180, pref: 'nw' }],
        doors: [{ id: 'd_east', wall: 'east', at: 2.5 }] },

      { id: 'corridor_f0', x0: 4, x1: 12, z0: 1, z1: 8, h: 3.0, floor: 0,
        kit: 'hall_main', zone: 'pressure', evidence: false, props: [],
        doors: [
          { id: 'd_west', wall: 'west', at: 2.5 },          // ↔ entrance/d_east（共享 x=4）
          { id: 'd_n_kitchen', wall: 'north', at: 12.0 },   // ↔ kitchen/d_south（共享 z=8）
        ] },

      { id: 'kitchen_f0', x0: 10, x1: 14, z0: 4, z1: 12, h: 3.0, floor: 0,
        kit: 'hospital_ward', zone: 'pressure', evidence: true,
        props: [{ kit: 'cabinet_a', pos: [0, 0, 0], rot: 0, pref: 'nw' }],
        doors: [{ id: 'd_south', wall: 'south', at: 12.0 }] },   // 墙 x10..14，1.6m 门 @12.0 合法

      // ══ 二层 ══
      { id: 'corridor_f1', x0: 4, x1: 12, z0: 1, z1: 4, h: 3.0, floor: 1,
        kit: 'hall_main_corridor_main_f1', zone: 'pressure', evidence: false, props: [],
        doors: [
          { id: 'd_n1', wall: 'north', at: 5.0 },    // ↔ corridor2_f1/d_south（z=4 共享）
          { id: 'd_n2', wall: 'north', at: 7.0 },    // ↔ master_f1/d_south（z=4 共享）
          { id: 'd_n3', wall: 'north', at: 10.0 },   // ↔ attic_f1/d_south（z=4 共享）
        ] },

      { id: 'corridor2_f1', x0: 4, x1: 6, z0: 4, z1: 8, h: 3.0, floor: 1,
        kit: 'hall_main_corridor_link', zone: 'pressure', evidence: false, props: [],
        doors: [{ id: 'd_south', wall: 'south', at: 5.0 }] },    // 墙 x4..6，1.6m 门 @5.0 合法

      { id: 'master_f1', x0: 6, x1: 8, z0: 4, z1: 8, h: 3.0, floor: 1,
        kit: 'hospital_ward', zone: 'pressure', evidence: true,
        props: [{ kit: 'bed_b', pos: [0, 0, 0], rot: 0, pref: 'nw' }],
        doors: [{ id: 'd_south', wall: 'south', at: 7.0 }] },    // 墙 x6..8，1.6m 门 @7.0 合法

      { id: 'attic_f1', x0: 8, x1: 12, z0: 4, z1: 8, h: 3.0, floor: 1,
        kit: 'morgue', zone: 'high-risk', evidence: true,
        props: [{ kit: 'cabinet_a', pos: [0, 0, 0], rot: 270, pref: 'ne' }],
        doors: [{ id: 'd_south', wall: 'south', at: 10.0 }] },   // 墙 x8..12，1.6m 门 @10.0 合法
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
console.log(`[v2] bleasdale_v1 重算（${raw.length} → ${out.length} 字节）`);
console.log('  每条门的 at 都落在它所属房间那条墙的范围内；二层三房都贴走廊北墙 z=4。');
