#!/usr/bin/env node
/**
 * redraw-bleasdale-2f.mjs — 重画第三张图的二层，使其与一层在 z 范围上有**交集**（竖井才有处可放）
 *
 * ## 前几轮为什么反复失败（根因，不是细节）
 * 竖井要求：**同一个 XZ 矩形在它跨越的每一层都落在某个可走房间内**（G5 / gate-model M8）。
 * 我前几版把一层的可走区放在 z1..7、二层放在 z4..8，**两层 z 交集只有 7..8 且被厨房占着**——
 * 于是不是"重叠"就是"竖井没有承载"，改来改去都在同一个死结里。
 *
 * ## 这一版的对齐（先算交集，再摆房间）
 * | 层 | 房间 | z 范围 |
 * |---|---|---|
 * | 一层 | 玄关 x0..4, z1..4；走廊 x4..10, **z1..8**；厨房 x10..14, **z8..12** | 1..12 |
 * | 二层 | 二层走廊 x4..10, **z1..4**；主卧 x0..4, z4..8；次卧 x4..7, z4..8；阁楼 x7..10, z4..8 | 1..8 |
 *
 * 竖井取 **两层走廊的 z 交集**：一层走廊 z1..8 ∩ 二层走廊 z1..4 = **z1..4** → 竖井 x6..9, z2..3。
 * 这样竖井在两层都落在**走廊**里（走廊天然是通行区，比放在卧室合理）。
 *
 * ## 门对齐（逐条按共享墙算）
 * · 玄关东墙 x=4(z1..4) ↔ 一层走廊西墙 x=4(z1..8) → 两门都取 **z=2.5**
 * · 一层走廊北墙 z=8 ↔ 厨房南墙 z=8 → 两门都取 **x=5.5**
 * · 二层走廊北墙 z=4 ↔ 主卧/次卧/阁楼的南墙 z=4 → 门取 **x=5.0 / 6.5 / 8.5**
 * · 二层走廊东墙 x=10 ↔ 阁楼西墙 x=10 → 两门都取 **z=6.0**
 *
 * 用法：node tools/redraw-bleasdale-2f.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'tools/lib/map-layouts.mjs');
const checkOnly = process.argv.includes('--check');

const raw = fs.readFileSync(FILE, 'utf8');
const start = raw.indexOf('  bleasdale_v1: {');
if (start < 0) { console.error('[redraw] ✗ 未找到 bleasdale_v1 布局'); process.exit(1); }
// 找到该布局的结束（下一个顶层 "  }," 且缩进为 2 空格的 '},'）
const endMark = raw.indexOf('\n  },\n', start);
if (endMark < 0) { console.error('[redraw] ✗ 未找到布局结束'); process.exit(1); }

const entry = `  bleasdale_v1: {
    id: 'bleasdale_v1',
    note: '布利斯代尔农舍（两层中型图）：一层玄关/走廊/厨房，二层走廊/主卧/次卧/阁楼，靠楼梯井跨层。'
      + '设计依据：官方中型图为两层农舍；本布局是 design（官方原图几何不公开），非官方还原。',
    zones: {
      entrance: ['entrance'],
      pressure: ['corridor_f0', 'kitchen_f0', 'corridor_f1', 'master_f1', 'second_f1'],
      deep: ['attic_f1'],
    },
    extraction: { standard: 'entrance', deep: 'attic_f1' },
    events: [
      { type: 'power_surge', minute: 5, durationSec: 12, params: { scope: 'farmhouse' },
        sanityEffect: -4, counterplay: '总闸跳闸：先确认手电电量，再摸黑回玄关（安全区）等恢复' },
      { type: 'mirror_flicker', minute: 11, durationSec: 8, params: { scope: 'bedroom' },
        sanityEffect: -3, counterplay: '卧室镜面闪烁：不要盯着看（会加速掉理智），离开房间即止' },
    ],
    // 竖井 = 两层走廊的 z 交集（z2..3 落在一层走廊 z1..8 与二层走廊 z1..4 之内）
    shafts: [
      { id: 'stair_well', kind: 'stair', minX: 6, minZ: 2, maxX: 9, maxZ: 3, fromFloor: 0, toFloor: 1 },
    ],
    boxes: [
      // ══ 一层 ══
      { id: 'entrance', x0: 0, x1: 4, z0: 1, z1: 4, h: 3.0, floor: 0,
        kit: 'hall_main_entrance_safe', zone: 'safe', evidence: false,
        props: [{ kit: 'cabinet_a', pos: [0, 0, 0], rot: 180, pref: 'nw' }],
        doors: [{ id: 'd_east', wall: 'east', at: 2.5 }] },

      { id: 'corridor_f0', x0: 4, x1: 10, z0: 1, z1: 8, h: 3.0, floor: 0,
        kit: 'hall_main', zone: 'pressure', evidence: false, props: [],
        doors: [
          { id: 'd_west', wall: 'west', at: 2.5 },
          { id: 'd_n_kitchen', wall: 'north', at: 5.5 },
        ] },

      { id: 'kitchen_f0', x0: 10, x1: 14, z0: 8, z1: 12, h: 3.0, floor: 0,
        kit: 'hospital_ward', zone: 'pressure', evidence: true,
        props: [{ kit: 'cabinet_a', pos: [0, 0, 0], rot: 0, pref: 'nw' }],
        doors: [{ id: 'd_south', wall: 'south', at: 5.5 }] },

      // ══ 二层 ══
      { id: 'corridor_f1', x0: 4, x1: 10, z0: 1, z1: 4, h: 3.0, floor: 1,
        kit: 'hall_main_corridor_main_f1', zone: 'pressure', evidence: false, props: [],
        doors: [
          { id: 'd_n1', wall: 'north', at: 5.0 },
          { id: 'd_n2', wall: 'north', at: 6.5 },
          { id: 'd_east', wall: 'east', at: 6.0 },
        ] },

      { id: 'master_f1', x0: 0, x1: 4, z0: 4, z1: 8, h: 3.0, floor: 1,
        kit: 'hospital_ward', zone: 'pressure', evidence: true,
        props: [{ kit: 'bed_b', pos: [0, 0, 0], rot: 0, pref: 'nw' }],
        doors: [{ id: 'd_south', wall: 'south', at: 5.0 }] },

      { id: 'second_f1', x0: 4, x1: 7, z0: 4, z1: 8, h: 3.0, floor: 1,
        kit: 'hospital_ward', zone: 'pressure', evidence: false,
        props: [{ kit: 'bed_b', pos: [0, 0, 0], rot: 0, pref: 'nw' }],
        doors: [{ id: 'd_south', wall: 'south', at: 6.5 }] },

      { id: 'attic_f1', x0: 7, x1: 10, z0: 4, z1: 8, h: 3.0, floor: 1,
        kit: 'morgue', zone: 'high-risk', evidence: true,
        props: [{ kit: 'cabinet_a', pos: [0, 0, 0], rot: 270, pref: 'ne' }],
        doors: [{ id: 'd_south', wall: 'south', at: 8.5 }] },
    ],
    links: [
      ['entrance/d_east', 'corridor_f0/d_west', 2.0],
      ['corridor_f0/d_n_kitchen', 'kitchen_f0/d_south', 1.6],
      ['corridor_f1/d_n1', 'master_f1/d_south', 1.6],
      ['corridor_f1/d_n2', 'second_f1/d_south', 1.6],
      ['corridor_f1/d_east', 'attic_f1/d_south', 1.6],
    ],
  },`;

const out = raw.slice(0, start) + entry + raw.slice(endMark + 1);
if (!checkOnly) fs.writeFileSync(FILE, out, 'utf8');
console.log(`[redraw] bleasdale_v1 二层重画（${raw.length} → ${out.length} 字节）`);
console.log('  竖井 x6..9,z2..3 落在两层走廊的交集内；各门按共享墙重新对齐。');
