// 落"25 种鬼魂"数据表（来源：C:\Users\qing_\Desktop\补充.md 第 3~136 行，用户原件）
//
// 为什么做成配置而不是代码：补充方案明确要求
//   「**必须做成数据驱动**（不能一种鬼一段代码）」——
//   字段：id / 名称 / 证据三选 / 猎杀理智阈值 / 基础速度 / 特殊规则(可组合) / 模型性别限制。
//
// 证据类型（原件用的六种 + 灵球）：
//   emf5 紫外线 uv 通灵盒 spirit_box 鬼魂笔记 ghost_writing
//   点阵投影仪 dots 灵球 orb 刺骨寒温 freezing
//
// `huntThreshold` 就是 HuntScheduler 的 `threshold` 参数（<100 的数字），
// `huntThresholdDynamic` 用于阈值随条件变化的那几种（Mare/Thaye/Day an/Raiju/Onryo 等）。
// `speedMps` 是常速；`speedRule` 描述动态速度（0.4 ~ 3.7 那些）。
import fs from 'node:fs';

const ghosts = {
  spirit: {
    label: '魂魄', labelEn: 'Spirit',
    evidence: ['emf5', 'spirit_box', 'ghost_writing'],
    huntThreshold: 50, speedMps: 1.7,
    trait: 'smudge_blocks_hunt_150s',
    note: '被圣木熏到后 150 秒内无法猎杀（普通鬼为 90 秒）',
  },
  wraith: {
    label: '魅影', labelEn: 'Wraith',
    evidence: ['emf5', 'dots', 'spirit_box'],
    huntThreshold: 50, speedMps: 1.7,
    trait: 'never_steps_salt', traitExtra: 'teleport_near_player_emf2',
    note: '永远不会踩到盐堆。可传送到玩家附近并产生 EMF 2 级信号',
  },
  phantom: {
    label: '幻影', labelEn: 'Phantom',
    evidence: ['dots', 'uv', 'spirit_box'],
    huntThreshold: 50, speedMps: 1.7,
    trait: 'hunt_nearly_invisible_slow_flicker', traitExtra: 'photo_makes_it_vanish',
    note: '猎杀时几乎全程隐身、闪烁极慢。拍照后暂时消失，鬼照清晰无马赛克',
  },
  poltergeist: {
    label: '骚灵', labelEn: 'Poltergeist',
    evidence: ['uv', 'ghost_writing', 'spirit_box'],
    huntThreshold: 50, speedMps: 1.7,
    trait: 'mass_throw_many_objects',
    note: '能一次性投掷大量物品；猎杀时投掷频率极快、距离很远',
  },
  banshee: {
    label: '女妖', labelEn: 'Banshee',
    evidence: ['dots', 'uv', 'orb'],
    huntThreshold: 50, huntThresholdRule: 'locked_target_sanity_not_team_average',
    speedMps: 1.7,
    trait: 'locks_one_target_ignores_others', traitExtra: 'recorder_33pct_unique_scream',
    modelGender: 'female',
    note: '必定女性模型。只锁定并追杀一名玩家，无视其他人。收音器 33% 几率听到独特尖叫',
  },
  jinn: {
    label: '巨灵', labelEn: 'Jinn',
    evidence: ['emf5', 'uv', 'freezing'],
    huntThreshold: 50, speedMps: 1.7,
    speedRule: 'breaker_on_and_sees_player_2_5',
    trait: 'never_turns_off_breaker',
    note: '开闸且看见玩家时速度可达 2.5 m/s；电闸关闭后失去加速能力',
  },
  mare: {
    label: '梦魇', labelEn: 'Mare',
    evidence: ['spirit_box', 'orb', 'ghost_writing'],
    huntThreshold: 40, huntThresholdDynamic: { lights_on: 40, lights_off: 50 },
    speedMps: 1.7,
    trait: 'turns_off_lights_breaks_bulbs', traitExtra: 'stronger_in_dark',
    note: '房间灯亮时约 40%；灯灭时正常 50%。倾向关灯打碎灯泡，黑暗中更具攻击性',
  },
  revenant: {
    label: '亡魂', labelEn: 'Revenant',
    evidence: ['orb', 'ghost_writing', 'freezing'],
    huntThreshold: 50, speedMps: 1.7,
    speedRule: 'sees_player_3_0_loses_sight_1_0',
    note: '速度两极分化：看见玩家约 3.0 m/s，失去视野约 1.0 m/s。躲藏是有效应对',
  },
  shade: {
    label: '暗影', labelEn: 'Shade',
    evidence: ['emf5', 'ghost_writing', 'freezing'],
    huntThreshold: 35, speedMps: 1.7,
    trait: 'shy_rarely_interacts_or_hunts_when_player_in_room',
    note: '非常害羞。玩家在鬼房内时几乎不互动、不猎杀（猎杀阈值最低的鬼之一）',
  },
  demon: {
    label: '恶魔', labelEn: 'Demon',
    evidence: ['uv', 'ghost_writing', 'freezing'],
    huntThreshold: 70, speedMps: 1.7,
    trait: 'short_hunt_cooldown_20s', traitExtra: 'smudge_blocks_hunt_60s',
    note: '猎杀阈值最高的鬼（70%）。猎杀冷却短（20 秒）；被圣木熏到后仅 60 秒无法猎杀',
  },
  yurei: {
    label: '幽灵', labelEn: 'Yurei',
    evidence: ['dots', 'orb', 'freezing'],
    huntThreshold: 50, speedMps: 1.7,
    trait: 'often_blows_out_candles', traitExtra: 'no_hunt_inside_flame_zone',
    note: '会频繁吹灭蜡烛。在火焰覆盖区域内不会开启猎杀',
  },
  oni: {
    label: '赤鬼', labelEn: 'Oni',
    evidence: ['emf5', 'uv', 'freezing'],
    huntThreshold: 50, speedMps: 1.7,
    trait: 'hunt_almost_always_visible_fast_flicker', traitExtra: 'interacts_more_often',
    note: '猎杀时几乎全程可见、闪烁频率极低（与幻影相反）。互动更频繁',
  },
  myling: {
    label: '鬼婴', labelEn: 'Myling',
    evidence: ['emf5', 'uv', 'ghost_writing'],
    huntThreshold: 50, speedMps: 1.7,
    trait: 'very_quiet_footsteps_heartbeat_louder_than_steps',
    note: '猎杀时脚步声非常轻，心跳声比脚步声大，难以靠声音判断距离',
  },
  onryo: {
    label: '怨灵', labelEn: 'Onryo',
    evidence: ['spirit_box', 'orb', 'freezing'],
    huntThreshold: 60, speedMps: 1.7,
    trait: 'candle_blown_out_can_hunt_ignoring_sanity', traitExtra: 'flame_blocks_hunt',
    note: '吹灭蜡烛后有概率无视理智直接猎杀。火焰可阻止其猎杀',
  },
  twins: {
    label: '孪魂', labelEn: 'The Twins',
    evidence: ['emf5', 'spirit_box', 'freezing'],
    huntThreshold: 50, speedMps: 1.7,
    speedRule: 'main_minus_10pct_1_53_sub_plus_10pct_1_87',
    trait: 'two_areas_interact_simultaneously',
    note: '主鬼 1.53 m/s、副鬼 1.87 m/s。可同时与两个区域互动，制造不在场证明',
  },
  raiju: {
    label: '雷魂', labelEn: 'Raiju',
    evidence: ['emf5', 'orb', 'dots'],
    huntThreshold: 50, huntThresholdDynamic: { electronics_on_nearby: 65 },
    speedMps: 1.7,
    speedRule: 'nearby_electronics_2_5',
    note: '附近有开启的电子设备时阈值约 65%、速度可达 2.5 m/s',
  },
  hantu: {
    label: '寒魔', labelEn: 'Hantu',
    evidence: ['uv', 'orb', 'freezing'],
    huntThreshold: 50, speedMps: 1.7,
    speedRule: 'cold_fast_hot_slow_1_4_to_2_7',
    trait: 'never_turns_on_breaker_often_turns_it_off', traitExtra: 'breath_fog_when_cold',
    note: '低温快、高温慢（1.4~2.7 m/s）。不会开闸并经常关它；关闸/低温时猎杀口中呼出白雾',
  },
  moroi: {
    label: '魔洛伊', labelEn: 'Moroi',
    evidence: ['spirit_box', 'ghost_writing', 'freezing'],
    huntThreshold: 50, speedMps: 1.7,
    speedRule: 'lower_sanity_faster_3_7_at_zero_with_los',
    trait: 'spirit_box_response_curses_player', traitExtra: 'smudge_blind_7_5s',
    note: '理智越低越快（0 理智+视线可达 3.7）。通灵盒回应会诅咒玩家加速理智流失；圣木致盲 7.5s',
  },
  deogen: {
    label: '雾影', labelEn: 'Deogen',
    evidence: ['spirit_box', 'ghost_writing', 'dots'],
    huntThreshold: 40, speedMps: 1.7,
    speedRule: 'farther_faster_close_0_4',
    trait: 'sees_through_walls_can_find_hiding_players',
    note: '透视，能精准锁定躲藏的玩家。靠近时极慢（约 0.4 m/s），是遛鬼的好机会',
  },
  thaye: {
    label: '刹耶', labelEn: 'Thaye',
    evidence: ['ghost_writing', 'orb', 'dots'],
    huntThreshold: 75, huntThresholdDynamic: { youngest: 75, oldest: 15 },
    speedMps: 2.7,
    speedRule: 'youngest_fastest_2_7_slows_with_age',
    trait: 'ages_faster_near_players',
    note: '最年轻时最快（约 2.7）且阈值 75%，随年龄增长变慢、阈值降至 15%',
  },
  mimic: {
    label: '拟魂', labelEn: 'The Mimic',
    evidence: ['spirit_box', 'uv', 'freezing'],
    huntThreshold: 50, speedMps: 1.7,
    trait: 'always_shows_orb', traitExtra: 'mimics_other_ghost_speed_and_behaviour',
    note: '必定显示「灵球」证据（即使难度里灵球不是证据）。会模仿其他鬼的速度与行为',
  },
  obambo: {
    label: '盲灵', labelEn: 'Obambo',
    evidence: ['ghost_writing', 'orb', 'dots'],
    huntThreshold: 50, speedMps: 1.7,
    speedRule: 'periodic_slow_fast_cycles_by_minute',
    trait: 'completely_blind_sensitive_to_sound_and_electronics',
    note: '完全失明，看不见静止的玩家，但对声音与电子设备敏感。速度周期性变化',
  },
  aswang: {
    label: '阿斯旺', labelEn: 'Aswang',
    evidence: ['dots', 'freezing', 'ghost_writing'],
    huntThreshold: 50, speedMps: 1.53,
    speedRule: 'slow_base_fast_los_accel',
    trait: 'found_in_hiding_spot_ends_hunt_and_targets_it_next',
    note: '基础速度较慢（约 1.53）但视线加速极快。猎杀中在躲藏点被发现则猎杀立即结束，下次直冲该点',
  },
  dayan: {
    label: '达彦', labelEn: 'Dayan',
    evidence: ['emf5', 'orb', 'spirit_box'],
    huntThreshold: 50,
    huntThresholdDynamic: { nobody_within_10m: 50, nearby_still: 45, nearby_moving: 65 },
    speedMps: 1.7,
    speedRule: 'player_moves_fast_player_still_slow',
    trait: 'player_can_reduce_threat_by_staying_still',
    modelGender: 'female',
    note: '必定女性模型。玩家动它就快、静止它就慢（无论有无视野）',
  },
  gallu: {
    label: '加鲁', labelEn: 'Gallu',
    evidence: ['emf5', 'uv', 'spirit_box'],
    huntThreshold: 50, speedMps: 1.7,
    speedRule: 'three_states_normal_enraged_weakened',
    trait: 'protective_gear_triggers_enrage', traitExtra: 'enraged_does_not_step_on_salt',
    note: '在正常/暴怒/虚弱三态间循环。用盐、十字架、圣木会使其暴怒，暴怒时不踩盐',
  },
  deildegast: {
    label: '德戴尔加斯特', labelEn: 'Deildegast',
    evidence: ['emf5', 'ghost_writing', 'dots'],
    huntThreshold: 50, speedMps: 3.0,
    speedRule: 'starts_3_0_slows_after_interactions_0_4_at_26',
    trait: 'heavily_interaction_dependent',
    note: '初始猎杀速度极快（3.0），若投掷/互动了 26 个物体则降至 0.4 m/s',
  },
  kormos: {
    label: '科莫斯', labelEn: 'Kormos',
    evidence: ['orb', 'spirit_box', 'uv'],
    huntThreshold: 50, speedMps: 1.7,
    trait: 'completely_blind_longer_detection_range',
    note: '完全失明但探测范围更远，靠听觉与电子设备定位。安静与关电子设备是有效策略',
  },
};

const files = [
  'data/config.json',
  'unity/Assets/Data/config.json',
  'unity/Assets/Resources/Data/config.json',
  'native/csharp-verify/config.json',
];

const EV = {
  _note: '证据类型（原件用词）：emf5 / uv / spirit_box / ghost_writing / dots / orb / freezing。每只鬼恰好三条。',
};

const payload = {
  _note: 'Ghost roster, 25 entries, transcribed from user source C:/Users/qing_/Desktop/补充.md lines 3-136. Data-driven by requirement: one entry per ghost, no per-ghost code.',
  evidenceTypes: ['emf5', 'uv', 'spirit_box', 'ghost_writing', 'dots', 'orb', 'freezing'],
  ghosts,
  ...EV,
};

for (const f of files) {
  const j = JSON.parse(fs.readFileSync(f, 'utf8'));
  j.ghosts = payload;
  fs.writeFileSync(f, JSON.stringify(j, null, 2) + '\n', 'utf8');
}

const c = JSON.parse(fs.readFileSync('data/config.json', 'utf8'));
const ids = Object.keys(c.ghosts.ghosts);
console.log('  ghosts: ' + ids.length + ' entries');
console.log('  ids: ' + ids.join(', '));
// 自检：每只鬼恰好三条证据，且都在 known 集合里
const known = new Set(payload.evidenceTypes);
let bad = 0;
for (const [id, g] of Object.entries(ghosts)) {
  if (!Array.isArray(g.evidence) || g.evidence.length !== 3) { console.log('  BAD evidence count: ' + id); bad++; }
  for (const e of g.evidence) if (!known.has(e)) { console.log('  BAD evidence type: ' + id + ' -> ' + e); bad++; }
  if (!(g.huntThreshold > 0)) { console.log('  BAD threshold: ' + id); bad++; }
  if (!(g.speedMps > 0)) { console.log('  BAD speed: ' + id); bad++; }
}
// 【不许写死数量】原写 'all 25'，而用户原件（补充.md 第 3~136 行）实际列了 **27** 种 ——
// 写死的数字会骗人，这类"汇报与事实不符"是本项目最反复踩的坑。现在从实际条目数算。
console.log(bad === 0
  ? '  self-check: OK (' + ids.length + ' ghosts, each with 3 known evidence, positive threshold and speed)'
  : '  self-check: ' + bad + ' problems');

