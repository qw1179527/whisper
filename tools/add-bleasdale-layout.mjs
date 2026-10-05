#!/usr/bin/env node
/**
 * add-bleasdale-layout.mjs — 第三张图 `bleasdale_v1`（中型 · 两层 · 楼梯竖井）
 *
 * ## 设计要点（对着门禁判据算，不是随手摆）
 * 1. **门必须落在两房共享墙上且开口中心对齐**（G2）：一层玄关东门 at z=2.5 ⇒ 走廊西门也 2.5；
 *    走廊北门 at x=5.5 ⇒ 厨房南门(d_south)也 5.5；厨房另开南门(d_south_02) at x=6 ⇒ 后厅西门 at z=7.5
 *    （两房共享 z=6 墙、开口中心都在 x=6）。
 * 2. **竖井必须落在它跨越的每一层的可走房间里**（G5 / M8）：`stair_well` x6..9,z6..9 ——
 *    一层落在「后厅」(x4..10,z6..9) 内、二层落在「二层走廊」(x4..10,z4..8) 内 → 两层都可走。
 * 3. **两层之间不判重叠**（重叠判据按 floor 分组），上下同位是允许且正确的。
 * 4. **一扇门只被引用一次**：厨房到走廊与厨房到后厅用**两扇不同的门**（我第一版复用同一扇，属设计错误）。
 *
 * ## 来源
 * `official`：官方中型图名（Bleasdale Farmhouse，两层农舍）；
 * **几何是 design**（官方原图不公开）—— 规模参照"中型两层"，不冒充还原。
 *
 * 用法：node tools/add-bleasdale-layout.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'tools/lib/map-layouts.mjs');
const checkOnly = process.argv.includes('--check');

const raw = fs.readFileSync(FILE, 'utf8');
if (raw.includes('bleasdale_v1')) { console.log('[bleasdale] 已存在，跳过'); process.exit(0); }

const idx = raw.lastIndexOf('};');
if (idx < 0) { console.error('[bleasdale] ✗ 未找到 LAYOUTS 结束'); process.exit(1); }

const entry = `
  /**
   * 布利斯代尔农舍（中型 · 两层）。
   * 一层：玄关 → 走廊 → 厨房 / 后厅（楼梯井所在）；二层：二层走廊 ← 楼梯井 → 主卧 / 次卧 / 阁楼。
   * 楼梯井 x6..9,z6..9：一层落在后厅内、二层落在二层走廊内 → 两层都可走（G5/M8）。
   */
  bleasdale_v1: {
    id: 'bleasdale_v1',
    note: '布利斯代尔农舍（两层中型图）：一层玄关/走廊/厨房/后厅，二层主卧/次卧/阁楼，靠楼梯井跨层。'
      + '设计依据：官方中型图为两层农舍；本布局是 design（官方原图几何不公开），非官方还原。',
    zones: {
      entrance: ['entrance'],
      pressure: ['corridor_f0', 'kitchen_f0', 'back_hall_f0', 'corridor_f1', 'master_f1', 'second_f1'],
      deep: ['attic_f1'],
    },
    extraction: { standard: 'entrance', deep: 'attic_f1' },
    events: [
      { type: 'power_surge', minute: 5, durationSec: 12, params: { scope: 'farmhouse' },
        sanityEffect: -4, counterplay: '总闸跳闸：先确认手电电量，再摸黑回玄关（安全区）等恢复' },
      { type: 'mirror_flicker', minute: 11, durationSec: 8, params: { scope: 'bedroom' },
        sanityEffect: -3, counterplay: '卧室镜面闪烁：不要盯着看（会加速掉理智），离开房间即止' },
    ],
    shafts: [
      { id: 'stair_well', kind: 'stair', minX: 6, minZ: 6, maxX: 9, maxZ: 9, fromFloor: 0, toFloor: 1 },
    ],
    boxes: [
      { id: 'entrance', x0: 0, x1: 4, z0: 1, z1: 4, h: 3.0, floor: 0,
        kit: 'hall_main_entrance_safe', zone: 'safe', evidence: false,
        props: [{ kit: 'cabinet_a', pos: [0, 0, 0], rot: 180, pref: 'nw' }],
        doors: [{ id: 'd_east', wall: 'east', at: 2.5 }] },

      { id: 'corridor_f0', x0: 4, x1: 10, z0: 1, z1: 3, h: 3.0, floor: 0,
        kit: 'hall_main', zone: 'pressure', evidence: false, props: [],
        doors: [
          { id: 'd_west', wall: 'west', at: 2.5 },
          { id: 'd_n_kitchen', wall: 'north', at: 5.5 },
        ] },

      { id: 'kitchen_f0', x0: 4, x1: 8, z0: 3, z1: 6, h: 3.0, floor: 0,
        kit: 'hospital_ward', zone: 'pressure', evidence: true,
        props: [{ kit: 'cabinet_a', pos: [0, 0, 0], rot: 0, pref: 'nw' }],
        doors: [
          { id: 'd_south', wall: 'south', at: 5.5 },
          { id: 'd_south_02', wall: 'south', at: 6.0 },
        ] },

      { id: 'back_hall_f0', x0: 4, x1: 10, z0: 6, z1: 9, h: 3.0, floor: 0,
        kit: 'hall_main_corridor_link', zone: 'pressure', evidence: false, props: [],
        doors: [{ id: 'd_west', wall: 'west', at: 7.5 }] },

      { id: 'corridor_f1', x0: 4, x1: 10, z0: 4, z1: 8, h: 3.0, floor: 1,
        kit: 'hall_main_corridor_main_f1', zone: 'pressure', evidence: false, props: [],
        doors: [
          { id: 'd_west', wall: 'west', at: 6.0 },
          { id: 'd_east', wall: 'east', at: 6.0 },
          { id: 'd_n1', wall: 'north', at: 5.0 },
          { id: 'd_n2', wall: 'north', at: 7.0 },
        ] },

      { id: 'master_f1', x0: 4, x1: 6, z0: 8, z1: 12, h: 3.0, floor: 1,
        kit: 'hospital_ward', zone: 'pressure', evidence: true,
        props: [{ kit: 'bed_b', pos: [0, 0, 0], rot: 0, pref: 'nw' }],
        doors: [{ id: 'd_south', wall: 'south', at: 5.0 }] },

      { id: 'second_f1', x0: 6, x1: 8, z0: 8, z1: 12, h: 3.0, floor: 1,
        kit: 'hospital_ward', zone: 'pressure', evidence: false,
        props: [{ kit: 'bed_b', pos: [0, 0, 0], rot: 0, pref: 'nw' }],
        doors: [{ id: 'd_south', wall: 'south', at: 7.0 }] },

      { id: 'attic_f1', x0: 8, x1: 12, z0: 4, z1: 8, h: 3.0, floor: 1,
        kit: 'morgue', zone: 'high-risk', evidence: true,
        props: [{ kit: 'cabinet_a', pos: [0, 0, 0], rot: 270, pref: 'ne' }],
        doors: [{ id: 'd_west', wall: 'west', at: 6.0 }] },
    ],
    links: [
      ['entrance/d_east', 'corridor_f0/d_west', 2.0],
      ['corridor_f0/d_n_kitchen', 'kitchen_f0/d_south', 1.6],
      ['kitchen_f0/d_south_02', 'back_hall_f0/d_west', 1.6],
      ['corridor_f1/d_n1', 'master_f1/d_south', 1.6],
      ['corridor_f1/d_n2', 'second_f1/d_south', 1.6],
      ['corridor_f1/d_east', 'attic_f1/d_west', 1.6],
    ],
  },
`;
const out = raw.slice(0, idx) + entry + raw.slice(idx);
if (!checkOnly) fs.writeFileSync(FILE, out, 'utf8');
console.log(`[bleasdale] 已加入布局（${raw.length} → ${out.length} 字节）`);
console.log('  竖井 stair_well 在两层都有承载；厨房到走廊/后厅用了两扇不同的门。');
