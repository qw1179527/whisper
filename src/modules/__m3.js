/* 低语计划 · 灰盒源树分模块产物（tools/split-modules.mjs 生成，勿手改）
 * 模块：__m3 → m3.js
 * 职责：三怪行为状态机（V9 §7：三段式 巡逻 → 调查 → 追逐 → 回归，失联 12 秒回归） 这是 Host 权威端的确定性状态机：同一条刺激序列 + 同一初态 → 同一状态轨迹（可回归测试）。 本文件不碰 Unity、不碰网络，只吃"每 tick 的输入"吐"每 tick 的输出"。
 * 来源：baseline/game-0.6.0.js 第 1138~1360 行（8457 字节，逐字节搬移）
 * 包装改写（唯一改动，可审计）：mod.exports → module.exports；__req("__mN") → require("__mN")
 * 依赖：__m0, __m2｜导出：MonsterBrain, STATES, axisClear, finalRageActive
 */
'use strict';

    /**
     * 三怪行为状态机（V9 §7：三段式 巡逻 → 调查 → 追逐 → 回归，失联 12 秒回归）
     *
     * 这是 Host 权威端的确定性状态机：同一条刺激序列 + 同一初态 → 同一状态轨迹（可回归测试）。
     * 本文件不碰 Unity、不碰网络，只吃"每 tick 的输入"吐"每 tick 的输出"。
     */
    var __ns0 = require("__m0");
    var cfg = __ns0.cfg;
    var __ns1 = require("__m2");
    var canHear = __ns1.canHear;
    
    const STATES = ['patrol', 'investigate', 'chase', 'return'];
    
    /** 同轴直线是否无墙（怪物寻路用，避免"穿墙位移"） */
    function axisClear(grid, a, b) {
      if (a.z === b.z) {
        for (let x = Math.min(a.x, b.x); x <= Math.max(a.x, b.x); x++) if (grid[Math.floor(a.z)] && grid[Math.floor(a.z)][x] === "#") return false;
        return true;
      }
      if (a.x === b.x) {
        for (let z = Math.min(a.z, b.z); z <= Math.max(a.z, b.z); z++) if (grid[z] && grid[z][Math.floor(a.x)] === "#") return false;
        return true;
      }
      return false;
    }
    
    class MonsterBrain {
      /**
       * @param {string} monsterId stitcher | whisperer | coroner
       * @param {{position:{x:number,z:number}, patrolPoints?:Array<{x:number,z:number}>}} opts
       */
      constructor(monsterId, opts = {}) {
        const m = cfg(`monsters.${monsterId}`);
        if (!m) throw new Error(`unknown monster: ${monsterId}`);
        this.id = monsterId;
        this.model = m;
        this.behavior = cfg('monsterBehavior');
        this.speedMps = m.speedMps;
        // 追击倍率：调用方可按怪覆盖（per-monster），否则用 V9 §7 表7-2 的全局值 1.6
        this.chaseSpeedScale = opts.chaseSpeedScale ?? this.behavior.chaseSpeedScale ?? 1.6;
        this.position = { ...(opts.position ?? { x: 0, z: 0 }) };
        this.patrolPoints = opts.patrolPoints ?? [{ x: 0, z: 0 }];
        this.patrolIndex = 0;
        this.state = 'patrol';
        this.target = null;              // 调查目标点
        this.lastStimulus = null;        // 最后一次听见的刺激
        this.lastHeardTick = null;       // 最后一次听见的 tick（用于失联计时）
        this.tick = 0;
        this.stateEnteredTick = 0;
        this.history = [];               // 状态迁移轨迹（测试与调试面板用）
        this.perceptionBonus = 0;        // 由最恐惧的玩家理智档注入（0.2 = +20%）
        this.aggroLockUntil = null;      // 挑衅者人格：持续吸引仇恨 5 秒
        /* PATCH 002: monster-unstick */
        // 停滞检测（见 _moveToward）：记录到目标的历史最近距离与停滞起始 tick
        this._nav = { side: 1, lastSidestepTick: -1e9, lastPos: null, stuckTicks: 0 };
      }
    
      _enter(state) {
        if (this.state !== state) {
          this.history.push({ tick: this.tick, from: this.state, to: state });
          this.state = state;
          this.stateEnteredTick = this.tick;
        }
      }
    
      /**
       * 注入一条刺激（Host 权威端已按可听性筛过）。
       *
       * 语义纪律（首版把这条搞错，直接导致"没说话也被抓"）：
       *  听见 → **调查**声源；只有**看见**（sight）才进追击。
       *  唯一例外是挑衅者人格的仇恨锁定（那是显式给它的代价）。
       *  绝不能让"一声音 = 被锁定"，否则任何语音都等于死亡，核心机制就没法玩了。
       */
      onStimulus(stim, tick = this.tick) {
        this.lastStimulus = stim;
        this.lastHeardTick = tick;
        this.target = { ...stim.position };
        if (this.aggroLockUntil != null && tick < this.aggroLockUntil) {
          this._enter('chase');
          return;
        }
        if (this.state !== 'chase') this._enter('investigate');
      }
    
      /** 挑衅者人格：锁定仇恨 5 秒 */
      applyAggroLock(tick) {
        const taunter = cfg('personaPacks.taunter');
        const lock = taunter.traits.includes('aggro_lock_5s') ? 5 : 0;
        if (lock > 0) this.aggroLockUntil = tick + lock * cfg('network.tickRate', 60);
      }
    
      /**
       * 推进一个 tick。
       * @param {{tick?:number, seenPlayer?:boolean, playerPos?:{x:number,z:number}}} input
       * @returns {{state:string, position:{x:number,z:number}, speed:number, movedTo:object|null, arrived:boolean}}
       */
      step(input = {}) {
        this.tick = input.tick ?? this.tick + 1;
        const dt = 1 / cfg('network.tickRate', 60);
        let movedTo = null;
        let arrived = false;
    
        switch (this.state) {
          case 'patrol': {
            const p = this.patrolPoints[this.patrolIndex % this.patrolPoints.length];
            movedTo = this._moveToward(p, this.speedMps * dt);
            if (this._distance(this.position, p) <= this.behavior.investigateArriveRadiusM) {
              this.patrolIndex++;
              arrived = true;
            }
            break;
          }
          case 'investigate': {
            if (!this.target) { this._enter('patrol'); break; }
            movedTo = this._moveToward(this.target, this.speedMps * dt);
            if (this._distance(this.position, this.target) <= this.behavior.investigateArriveRadiusM) {
              this.target = null;
              arrived = true;
              this._enter('return'); // 到达声源后短暂停留再回归巡逻（回归态承担"停留"语义）
            }
            break;
          }
          case 'chase': {
            if (input.seenPlayer && input.playerPos) this.target = { ...input.playerPos };
            if (this.target) movedTo = this._moveToward(this.target, this.speedMps * this.chaseSpeedScale * dt);
            else if (input.playerPos) movedTo = this._moveToward(input.playerPos, this.speedMps * this.chaseSpeedScale * dt);
            break;
          }
          case 'return': {
            movedTo = this._moveToward(this.patrolPoints[this.patrolIndex % this.patrolPoints.length], this.speedMps * dt);
            if (this._distance(this.position, this.patrolPoints[this.patrolIndex % this.patrolPoints.length]) <= this.behavior.investigateArriveRadiusM) {
              this.patrolIndex++;
              this._enter('patrol');
            }
            break;
          }
          default:
            throw new Error(`unknown state: ${this.state}`);
        }
    
        // 失联 12 秒 → 回归巡逻（V9 §7）
        if ((this.state === 'chase' || this.state === 'investigate') && this.lastHeardTick != null) {
          const lostTicks = this.behavior.lostContactSeconds * cfg('network.tickRate', 60);
          if (this.tick - this.lastHeardTick > lostTicks) {
            this.lastStimulus = null;
            this.target = null;
            this._enter('return');
          }
        }
    
        return {
          state: this.state,
          position: { x: round3(this.position.x), z: round3(this.position.z) },
          speed: this.state === 'chase' ? this.speedMps * this.chaseSpeedScale : this.speedMps,
          movedTo,
          arrived,
        };
      }
    
      /** 反应链：把"这一 tick 的所有刺激"交给该怪，返回是否听见 */
      reactTo(stimuli, tick = this.tick) {
        const heard = [];
        for (const s of stimuli) {
          const r = canHear(this.id, s, this.position, { perceptionBonus: this.perceptionBonus });
          if (r.audible) {
            heard.push({ stimulus: s, detection: r });
            this.onStimulus(s, tick);
          }
        }
        return heard;
      }
    
      /**
       * 朝目标移动一步。
       * **必须**优先走注入的 mover：那是与玩家共用的碰撞解析（含子步进与墙体阻挡）。
       * 没有 mover 时才退化为直线移动（仅用于纯逻辑测试）。
       */
      _moveToward(target, maxStep) {
        /* PATCH 002: monster-unstick */
        // ── 绕行策略（按实测教训定的顺序）──
        // 教训：第一版一遇到"停滞"（含缓慢但持续的接近）就垂直推离目标，
        // 结果怪在离门洞 1.5m 处被反复推歪、来回震荡，反而永远到不了。
        // 实测：只要一路直推，40 帧即可走到门洞 0.135m 以内 —— 所以**默认永远先直推**。
        //
        // 真正需要绕行的情形是"直推完全无效"（分离轴滑行把其中一个轴卡死，位移≈0）。
        // 且绕行只短暂执行，随后立刻回到直推（避免把"接近"误判成"卡住"）。
        const nav = (this._nav = this._nav ?? { side: 1, lastSidestepTick: -1e9, lastPos: null, stuckTicks: 0 });
        const dtTick = cfg('network.tickRate', 60);
        const tX = target.x - this.position.x, tZ = target.z - this.position.z;
        const tD = Math.hypot(tX, tZ) || 1;
        if (nav.lastPos) {
          const movedSince = Math.hypot(this.position.x - nav.lastPos.x, this.position.z - nav.lastPos.z);
          if (movedSince < maxStep * 0.15) nav.stuckTicks++; else nav.stuckTicks = 0;
        }
        nav.lastPos = { x: this.position.x, z: this.position.z };

        // 连续 0.25s 位移不足 → 判定真卡住，执行一次绕行（之后立刻回到直推）
        const reallyStuck = nav.stuckTicks > 0.25 * dtTick && tD > 0.5;
        const cooling = this.tick - nav.lastSidestepTick < 0.6 * dtTick;
        if (reallyStuck && !cooling && typeof this.mover === 'function') {
          nav.stuckTicks = 0;
          const s = nav.side;
          const ux = tX / tD, uz = tZ / tD;
          const px = -uz, pz = ux;
          const dirs = [
            [ux * 0.35 + px * 0.94 * s, uz * 0.35 + pz * 0.94 * s],
            [ux * 0.35 - px * 0.94 * s, uz * 0.35 - pz * 0.94 * s],
            [px * s, pz * s],
            [-ux, -uz],
          ];
          for (const [dx, dz] of dirs) {
            const len = Math.hypot(dx, dz) || 1;
            const res = this.mover({ x: this.position.x, z: this.position.z },
              { x: this.position.x + (dx / len) * maxStep * 10, z: this.position.z + (dz / len) * maxStep * 10 }, maxStep);
            if (!res || !Number.isFinite(res.x) || !Number.isFinite(res.z)) continue;
            const moved = Math.hypot(res.x - this.position.x, res.z - this.position.z);
            if (moved > maxStep * 0.5) {
              this.position.x = res.x; this.position.z = res.z;
              nav.lastSidestepTick = this.tick;
              return { x: round3(this.position.x), z: round3(this.position.z), blocked: false, sidestep: true };
            }
          }
          nav.side = -nav.side;
          nav.lastSidestepTick = this.tick;
        }
        if (typeof this.mover === 'function') {
          const res = this.mover({ x: this.position.x, z: this.position.z }, target, maxStep);
          if (res && Number.isFinite(res.x) && Number.isFinite(res.z)) {
            this.position.x = res.x;
            this.position.z = res.z;
            return { x: round3(this.position.x), z: round3(this.position.z), blocked: !!res.blocked };
          }
        }
        const dx = target.x - this.position.x;
        const dz = target.z - this.position.z;
        const dist = Math.hypot(dx, dz);
        if (dist <= maxStep || dist === 0) {
          this.position.x = target.x;
          this.position.z = target.z;
          return { ...this.position };
        }
        this.position.x += (dx / dist) * maxStep;
        this.position.z += (dz / dist) * maxStep;
        return { x: round3(this.position.x), z: round3(this.position.z) };
      }
    
      _distance(a, b) {
        return Math.hypot(a.x - b.x, a.z - b.z);
      }
    
      snapshot() {
        return {
          id: this.id,
          state: this.state,
          position: { x: round3(this.position.x), z: round3(this.position.z) },
          patrolIndex: this.patrolIndex,
          lastHeardTick: this.lastHeardTick,
          history: this.history.slice(-8),
        };
      }
    }
    
    const round3 = (v) => Math.round(v * 1000) / 1000;
    
    /** 终局狂暴：撤离倒计时 60 秒内触发（V9 §7） */
    function finalRageActive(secondsToExtraction) {
      return secondsToExtraction <= cfg('monsterBehavior.finalRageWindowBeforeExtractionSec');
    }
    
    module.exports = { axisClear, finalRageActive, MonsterBrain, STATES };
