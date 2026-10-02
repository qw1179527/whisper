/* 低语计划 · 灰盒源树分模块产物（tools/split-modules.mjs 生成，勿手改）
 * 模块：__m2 → m2.js
 * 职责：声纹 → 听觉索敌（V9 §6 表6-1 + §7 表7-2） 判定模型（配置表驱动，代码不含数值）： 可听 ⟺ 刺激强度 ≥ 该怪有效的听觉阈值 且 距离 ≤ 该刺激的感知半径（globalBroadcast 刺激不受半径限制） 有效阈值 = monsters[].hearingThreshold × (1 − 恐惧加成) × 人格定位修正 设计后果（必须保持，否则"声纹即风险"退化）： 低语者阈值 10 < 耳语强度 10×人格乘子 → 耳语它也听得见（安静时几乎失明，只靠听觉） 缝匠阈值 30 → 听不见耳语，听得见正常说话/脚步 → 教学怪的压力来自"别跑别喊" 收殓人阈值 55 → 只对喊叫、砸窗、崩溃尖叫有反应，主体靠视野
 * 来源：baseline/game-0.6.0.js 第 999~1138 行（5666 字节，逐字节搬移）
 * 包装改写（唯一改动，可审计）：mod.exports → module.exports；__req("__mN") → require("__mN")
 * 依赖：__m0｜导出：MONSTER_IDS, canHear, effectiveHearingThreshold, hearStimulus, makeStimulus, makeVoiceStimulus, movementStimulusKey, stimulusIntensity, stimulusRadiusTable
 */
'use strict';

    /**
     * 声纹 → 听觉索敌（V9 §6 表6-1 + §7 表7-2）
     *
     * 判定模型（配置表驱动，代码不含数值）：
     *  可听 ⟺ 刺激强度 ≥ 该怪有效的听觉阈值
     *        且 距离 ≤ 该刺激的感知半径（globalBroadcast 刺激不受半径限制）
     *  有效阈值 = monsters[].hearingThreshold × (1 − 恐惧加成) × 人格定位修正
     *
     * 设计后果（必须保持，否则"声纹即风险"退化）：
     *  低语者阈值 10 < 耳语强度 10×人格乘子 → 耳语它也听得见（安静时几乎失明，只靠听觉）
     *  缝匠阈值 30 → 听不见耳语，听得见正常说话/脚步 → 教学怪的压力来自"别跑别喊"
     *  收殓人阈值 55 → 只对喊叫、砸窗、崩溃尖叫有反应，主体靠视野
     */
    var __ns0 = require("__m0");
    var cfg = __ns0.cfg,
        stimulusSource = __ns0.stimulusSource;
    
    const MONSTER_IDS = ['stitcher', 'whisperer', 'coroner'];
    
    /** 刺激构造：来源 key → 事件（位置、强度、类型、玩家、时间） */
    function makeStimulus(sourceKey, opts = {}) {
      const src = stimulusSource(sourceKey);
      const intensity = opts.intensity ?? src.intensity;
      return {
        id: opts.id ?? `${sourceKey}:${opts.tick ?? 0}:${opts.playerId ?? 'x'}`,
        sourceKey,
        type: src.type,
        label: src.label,
        intensity,
        radiusM: opts.radiusM ?? src.radiusM,
        globalBroadcast: opts.globalBroadcast ?? src.globalBroadcast,
        playerId: opts.playerId ?? 'p0',
        position: opts.position ?? { x: 0, y: 0, z: 0 },
        tick: opts.tick ?? 0,
        personaId: opts.personaId ?? null,
        mute: !!opts.mute,
      };
    }
    
    /** 由声纹分档直接构造刺激（语音链路专用） */
    function makeVoiceStimulus(bandResult, opts = {}) {
      const key = { whisper: 'voice_whisper', normal: 'voice_normal', shout: 'voice_shout' }[bandResult.band];
      if (!key) return null; // indistinguishable / null → 不产生刺激（判定不成立就不该惊动怪物）
      const src = stimulusSource(key);
      let intensity = src.intensity;
      if (opts.personaId) {
        const p = cfg(`personaPacks.${opts.personaId}`);
        intensity = opts.mute ? p.fixedIntensity : Math.round(intensity * (p.stimulusMultiplier ?? 1));
      }
      return makeStimulus(key, { ...opts, intensity });
    }
    
    /** 该怪在当前情境下的有效听觉阈值 */
    function effectiveHearingThreshold(monsterId, ctx = {}) {
      const m = cfg(`monsters.${monsterId}`);
      if (!m) throw new Error(`unknown monster: ${monsterId}`);
      let t = m.hearingThreshold;
      // 玩家处于"恐惧"及以上理智档时怪物感知 +20%（V9 §7 表7-1）
      const bonus = ctx.perceptionBonus ?? 0;
      t *= 1 - bonus;
      // 溺水者人格：怪物 3 秒内对声源定位精度 -30%（表现为"听得到但找不准"）
      if (ctx.localizationPenalty) t /= 1 - ctx.localizationPenalty;
      return t;
    }
    
    /**
     * 单怪对单刺激的可听判定。
     * @returns {{audible:boolean, distanceM:number, effectiveThreshold:number, marginDbLike:number, reason?:string}}
     */
    function canHear(monsterId, stim, monsterPos, ctx = {}) {
      const m = cfg(`monsters.${monsterId}`);
      const dx = stim.position.x - monsterPos.x;
      const dz = stim.position.z - monsterPos.z;
      const distanceM = Math.sqrt(dx * dx + dz * dz);
      const threshold = effectiveHearingThreshold(monsterId, ctx);
    
      if (stim.intensity < threshold) {
        return { audible: false, distanceM, effectiveThreshold: threshold, margin: stim.intensity - threshold, reason: 'intensity_below_threshold' };
      }
      if (!stim.globalBroadcast && stim.radiusM != null) {
        // 半径即"这一声传多远"；超出半径则衰减到不可辨
        const attenuationDb = 20 * Math.log10(Math.max(distanceM, 1) / Math.max(stim.radiusM, 1));
        const effective = stim.intensity + attenuationDb; // attenuationDb ≤ 0
        if (distanceM > stim.radiusM) {
          return {
            audible: false,
            distanceM,
            effectiveThreshold: threshold,
            margin: effective - threshold,
            attenuationDb: -round4(-attenuationDb),
            reason: 'out_of_radius',
          };
        }
        return { audible: true, distanceM, effectiveThreshold: threshold, margin: effective - threshold, attenuationDb: -round4(-attenuationDb) };
      }
      return { audible: true, distanceM, effectiveThreshold: threshold, margin: stim.intensity - threshold, attenuationDb: 0 };
    }
    
    /** 全体怪对一条刺激的可听结果（Host 权威端每 tick 调用一次） */
    function hearStimulus(stim, monsterStates, opts = {}) {
      const out = {};
      for (const id of Object.keys(monsterStates)) {
        const st = monsterStates[id];
        const r = canHear(id, stim, st.position, {
          perceptionBonus: opts.perceptionBonus ?? st.perceptionBonus ?? 0,
          localizationPenalty: opts.localizationPenalty ?? 0,
        });
        out[id] = r;
      }
      return out;
    }
    
    /** 声纹强度 → 实际发出刺激的强度（供 UI 与埋点展示"这一声有多响"） */
    function stimulusIntensity(sourceKey, personaId = null, mute = false) {
      const src = stimulusSource(sourceKey);
      if (!personaId) return src.intensity;
      const p = cfg(`personaPacks.${personaId}`);
      return mute ? p.fixedIntensity : Math.round(src.intensity * (p.stimulusMultiplier ?? 1));
    }
    
    /** 玩家行为 → 刺激源 key 查表（脚步形态由移动状态决定） */
    function movementStimulusKey({ running = false, crouching = false } = {}) {
      if (running) return 'run_footstep';
      if (crouching) return 'crouch_footstep';
      return 'walk_footstep';
    }
    
    /** 半径表速查（用于 UI 显示"这一声传多远"与内容工厂校验） */
    function stimulusRadiusTable() {
      return Object.fromEntries(
        Object.entries(cfg('stimulusSources')).map(([k, v]) => [k, { intensity: v.intensity, radiusM: v.radiusM, globalBroadcast: !!v.globalBroadcast, label: v.label }]),
      );
    }
    
    const round4 = (v) => Math.round(v * 1e4) / 1e4;
    
    module.exports = { makeStimulus, makeVoiceStimulus, effectiveHearingThreshold, canHear, hearStimulus, stimulusIntensity, movementStimulusKey, stimulusRadiusTable, MONSTER_IDS };
