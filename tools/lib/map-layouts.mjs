/**
 * map-layouts.mjs — 多地图布局表（`tools/gen-map.mjs` 的数据源）
 *
 * ## 为什么单列一个文件
 * 生成规则的**唯一实现**在 `tools/gen-map.mjs`（几何编译 + 自检）；本文件只放**数据**。
 * 这样加新地图 = 加一段数据，不会复制出第二套规则慢慢漂移。
 *
 * ## 坐标与门的写法（写布局前必读）
 * · `x0/x1/z0/z1`：**最小角点 → 最大角点**（米，XZ 平面）。`pos` 输出为最小角点（与灰盒一致）。
 * · `doors[].at`：门洞**中心沿该墙的绝对坐标**。
 *   `wall:'east'|'west'` 时 `at` 是 **z**；`wall:'north'|'south'` 时 `at` 是 **x**。
 * · `links`：`[门A引用, 门B引用, 门宽]`，引用形如 `房间id/门id`。
 *   **自检 G2 会强制**：两门必须相对、贴同一条共享墙、开口中心对齐（±0.02m）。
 *   门宽 = 走廊宽度，两端共用（避免两处各写一遍）。
 * · `zones`：光区分组（safe / pressure / high-risk）。
 * · `extraction`：撤离点（标准/深处房间 id）。
 * · **每张图的房间必须从第一个房间起全可达**（自检 G3 洪水填充）。
 *
 * 来源标注：asylum_v1 = design（本仓既有，由 gen-asylum-v1.mjs 产出，**不在此表内**）；
 * tanglewood_v1 = 参照官方最小图（6 Tanglewood Drive，单层住宅）的**规模**做的 design 布局
 * （官方原图几何不公开，故这里明确标 design，不冒充官方还原）。
 */

export const LAYOUTS = {
  tanglewood_v1: {
    id: 'tanglewood_v1',
    note: '坦格尔伍德街 6 号（单层小图）：玄关 → 客厅走廊 → 厨房/卫生间/卧室 + 车库。'
      + '设计依据：官方最小图为单层住宅（房间数远少于疗养院）；本布局是 design，非官方几何还原。',
    zones: {
      entrance: ['entrance'],
      pressure: ['hall_main', 'kitchen', 'bath', 'bedroom'],
      deep: ['garage'],
    },
    extraction: { standard: 'entrance', deep: 'garage' },
    // 事件形状照抄真源 asylum_v1.json 的 events[]（内置 6 型之一 + 反制手段）：
    //   { type, minute, durationSec, params, sanityEffect, counterplay }
    // ⚠ counterplay 不是可选装饰：V9 §30.2 要求每个事件都有**明确反制手段**，门禁会查。
    events: [
      { type: 'blackout', minute: 6, durationSec: 10, params: { scope: 'house' },
        sanityEffect: -3, counterplay: '手电筒照地面确认出口；黑暗持续掉理智，回玄关（安全区）可恢复' },
      { type: 'door_lock_shift', minute: 12, durationSec: 8, params: { scope: 'bedroom' },
        sanityEffect: -2, counterplay: '卧室门短暂锁死：先做别的房间取证，或从卫生间绕行' },
    ],
    boxes: [
      // ── 玄关（安全区）：门在东墙，通往客厅走廊 ──
      { id: 'entrance', x0: 0, x1: 4, z0: 0, z1: 3, h: 3.0, floor: 0,
        kit: 'hall_main_entrance_safe', zone: 'safe', evidence: false,
        props: [{ kit: 'cabinet_a', pos: [0, 0, 0], rot: 180, pref: 'nw' }],
        doors: [{ id: 'd_east', wall: 'east', at: 1.5 }] },

      // ── 客厅走廊（主干，东西向）：西接玄关、北接厨房、东接车库 ──
      // kit 必须是**变体 id**（= `gen-kits.mjs --mode variants` 按「房间类」出的那一个）。
      // 写通用名 `hall_main` 会拿到 18×3 的壳 → 摆进 7×3 的客厅就是**悬挑 11m / 缺口 2m**，
      // 而且 `gen-kits.mjs` 的适配检查会在 variants 模式下**硬失败**（22 个变体一个都出不来）。
      // 变体 id 规则见 gen-kits.mjs:477 `isCanonical ? cls.kit : \`${cls.kit}_${cls.roomId}\``。
      { id: 'hall_main', x0: 4, x1: 11, z0: 0, z1: 3, h: 3.0, floor: 0,
        kit: 'hall_main_hall_main', zone: 'pressure', evidence: false, props: [],
        doors: [
          { id: 'd_west', wall: 'west', at: 1.5 },
          { id: 'd_n_kitchen', wall: 'north', at: 5.5 },
          { id: 'd_east', wall: 'east', at: 1.5 },
        ] },

      // ── 厨房：南门对齐 hall_main 的北门（共享墙 z=3，中心 x=5.5）──
      { id: 'kitchen', x0: 4, x1: 7, z0: 3, z1: 6, h: 3.0, floor: 0,
        kit: 'hospital_ward_kitchen', zone: 'pressure', evidence: true,
        props: [{ kit: 'cabinet_a', pos: [0, 0, 0], rot: 0, pref: 'nw' }],
        doors: [
          { id: 'd_south', wall: 'south', at: 5.5 },
          // 东门：与卫生间西门共享 x=7 的墙（两房 z 范围都是 3..6，中心 z=4.5）
          { id: 'd_east', wall: 'east', at: 4.5 },
        ] },

      // ── 卫生间与卧室（南北排布）：两者共享墙 z=6，中心 x=8.5 对齐 ──
      { id: 'bath', x0: 7, x1: 10, z0: 3, z1: 6, h: 3.0, floor: 0,
        kit: 'hospital_ward_bath', zone: 'pressure', evidence: false, props: [],
        doors: [
          { id: 'd_west', wall: 'west', at: 4.5 },
          { id: 'd_north', wall: 'north', at: 8.5 },
        ] },
      { id: 'bedroom', x0: 7, x1: 10, z0: 6, z1: 10, h: 3.0, floor: 0,
        kit: 'hospital_ward_bedroom', zone: 'pressure', evidence: true,
        props: [{ kit: 'bed_b', pos: [0, 0, 0], rot: 0, pref: 'nw' }],
        doors: [{ id: 'd_south', wall: 'south', at: 8.5 }] },

      // ── 车库（深处）：西门对齐 hall_main 的东墙（x=11，中心 z=1.5）──
      { id: 'garage', x0: 11, x1: 15, z0: 0, z1: 4, h: 3.2, floor: 0,
        kit: 'morgue_garage', zone: 'high-risk', evidence: false, props: [],
        doors: [{ id: 'd_west', wall: 'west', at: 1.5 }] },
    ],
    links: [
      ['entrance/d_east', 'hall_main/d_west', 2.0],
      ['hall_main/d_n_kitchen', 'kitchen/d_south', 1.6],
      ['kitchen/d_east', 'bath/d_west', 1.2],
      ['bath/d_north', 'bedroom/d_south', 1.6],
      ['hall_main/d_east', 'garage/d_west', 2.0],
    ],
  },

  /**
   * 布利斯代尔农舍（中型 · 两层）。
   * 一层：玄关 → 走廊 → 厨房 / 后厅（楼梯井所在）；二层：二层走廊 ← 楼梯井 → 主卧 / 次卧 / 阁楼。
   * 楼梯井 x6..9,z6..9：一层落在后厅内、二层落在二层走廊内 → 两层都可走（G5/M8）。
   */
  bleasdale_v1: {
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
        kit: 'hall_main_corridor_f0', zone: 'pressure', evidence: false, props: [],
        doors: [
          { id: 'd_west', wall: 'west', at: 2.5 },     // ↔ entrance/d_east（共享 x=4）
          { id: 'd_n_kitchen', wall: 'north', at: 5.5 } // ↔ kitchen_f0/d_south（共享 z=4）
        ] },

      { id: 'kitchen_f0', x0: 4, x1: 12, z0: 4, z1: 8, h: 3.0, floor: 0,
        kit: 'hospital_ward_kitchen_f0', zone: 'pressure', evidence: true,
        props: [{ kit: 'cabinet_a', pos: [0, 0, 0], rot: 0, pref: 'nw' }],
        doors: [{ id: 'd_south', wall: 'south', at: 5.5 }] },

      // ══ 二层（走廊带与一层同位）══
      { id: 'corridor_f1', x0: 4, x1: 12, z0: 1, z1: 4, h: 3.0, floor: 1,
        kit: 'hall_main_corridor_f1', zone: 'pressure', evidence: false, props: [],
        doors: [
          { id: 'd_n1', wall: 'north', at: 5.0 },     // ↔ corridor2_f1
          { id: 'd_n2', wall: 'north', at: 7.0 },     // ↔ master_f1
          { id: 'd_n3', wall: 'north', at: 10.0 },    // ↔ attic_f1
        ] },

      { id: 'corridor2_f1', x0: 4, x1: 6, z0: 4, z1: 8, h: 3.0, floor: 1,
        kit: 'hall_main_corridor2_f1', zone: 'pressure', evidence: false, props: [],
        doors: [{ id: 'd_south', wall: 'south', at: 5.0 }] },

      { id: 'master_f1', x0: 6, x1: 8, z0: 4, z1: 8, h: 3.0, floor: 1,
        kit: 'hospital_ward_master_f1', zone: 'pressure', evidence: true,
        props: [{ kit: 'bed_b', pos: [0, 0, 0], rot: 0, pref: 'nw' }],
        doors: [{ id: 'd_south', wall: 'south', at: 7.0 }] },

      { id: 'attic_f1', x0: 8, x1: 12, z0: 4, z1: 8, h: 3.0, floor: 1,
        kit: 'morgue_attic_f1', zone: 'high-risk', evidence: true,
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
  },
};

/*
 * 自检验证记录（2026-10-05）：
 * 本表的 links 最初**故意**含一条坏引用（kitchen/d_south2_placeholder），用于验证
 * `tools/gen-map.mjs` 的 G2 自检真的会拦 —— 实测 exit=1 且未产出文件。
 * 随后改为真实共享墙上的门对：kitchen/d_east ↔ bath/d_west（共享 x=7 墙，中心 z=4.5，开口对齐）。
 */
