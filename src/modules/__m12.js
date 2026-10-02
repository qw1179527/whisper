/* 低语计划 · 灰盒源树分模块产物（tools/split-modules.mjs 生成，勿手改）
 * 模块：__m12 → m12.js
 * 职责：灰盒可玩端主循环：把已实现并测试过的四块接起来 —— ① Level DSL 关卡（13 房间 / 已编译校验） ② 声纹判定链（个人校准 + 分段归一化相对电平） ③ 声纹 → 听觉索敌（三怪阈值梯度） ④ 三怪三段式状态机 玩家侧：手电电量、理智、证据、撤离。V9 §19.5 三层调参在此体现：数值全部走 config。
 * 来源：baseline/game-0.6.0.js 第 2834~3434 行（28712 字节，逐字节搬移）
 * 包装改写（唯一改动，可审计）：mod.exports → module.exports；__req("__mN") → require("__mN")
 * 依赖：__m1, __m2, __m3, __m4, __m5, __m6, __m7, __m8, __m9, __m10, __m11｜导出：startGame
 */
'use strict';

    /**
     * 灰盒可玩端主循环：把已实现并测试过的四块接起来 ——
     *   ① Level DSL 关卡（13 房间 / 已编译校验）
     *   ② 声纹判定链（个人校准 + 分段归一化相对电平）
     *   ③ 声纹 → 听觉索敌（三怪阈值梯度）
     *   ④ 三怪三段式状态机
     * 玩家侧：手电电量、理智、证据、撤离。V9 §19.5 三层调参在此体现：数值全部走 config。
     */
    var __ns0 = require("__m1");
    var VoiceCalibrator = __ns0.VoiceCalibrator,
        VoiceBandClassifier = __ns0.VoiceBandClassifier,
        BAND_IDS = __ns0.BAND_IDS,
        bandToStimulus = __ns0.bandToStimulus;
    var __ns1 = require("__m2");
    var makeStimulus = __ns1.makeStimulus,
        movementStimulusKey = __ns1.movementStimulusKey,
        canHear = __ns1.canHear;
    var __ns2 = require("__m3");
    var MonsterBrain = __ns2.MonsterBrain;
    var __ns3 = require("__m4");
    var findRoomPath = __ns3.findRoomPath,
        roomPathToWaypoints = __ns3.roomPathToWaypoints;
    var __ns4 = require("__m5");
    var makeRoomCollider = __ns4.makeRoomCollider;
    var __ns5 = require("__m6");
    var createRenderer = __ns5.createRenderer,
        makeCamera = __ns5.makeCamera;
    var __ns6 = require("__m7");
    var parseGLB = __ns6.parseGLB;
    var __ns7 = require("__m8");
    var buildStructure = __ns7.buildStructure,
        kitInstances = __ns7.kitInstances,
        bakeInstances = __ns7.bakeInstances,
        bakeMonster = __ns7.bakeMonster;
    var __ns8 = require("__m9");
    var createInput = __ns8.createInput;
    var __ns9 = require("__m10");
    var createVoiceInput = __ns9.createVoiceInput;
    var __ns10 = require("__m11");
    var createHud = __ns10.createHud;
    
    const MONSTERS = ['stitcher', 'whisperer', 'coroner'];
    
    /** 单调时钟：声纹稳定门/冷却用（不要直接用 Date.now，设备时钟会被调整） */
    const nowMs = () => (typeof performance !== 'undefined' && performance.now ? performance.now() : Date.now());
    const FOG = { color: [0.043, 0.043, 0.047], near: 1.5, far: 13.0 };
    
    async function startGame(config, tokens) {
      // 设计 Token 来自 data/design-tokens.json，必须挂在 config 上：帧循环与 HUD 都从
      // config.designTokens 取。首版把 tokens 当独立参数传入、却仍读 config.designTokens，
      // 结果第一帧就抛 TypeError，在浏览器里表现为"点开始没反应"。
      config.designTokens = tokens;
      const boot = (msg) => {
        const el = document.getElementById('bootMsg');
        if (el) el.textContent = msg;
        window.__WHISPER_BOOT = msg;
      };
      boot('初始化渲染器…');
      const canvas = document.getElementById('gl');
      const renderer = createRenderer(canvas);
      const cam = makeCamera();
      const input = createInput(canvas, document.getElementById('hud'));
      const hud = createHud(config, tokens);
      boot('请求麦克风权限…');
      const voice = await createVoiceInput(config);
      if (voice.state.status === 'denied') hud.log('未获得麦克风：已进入无麦模式（人格包固定强度）');
    
      boot('加载关卡与套件…');
      // 关卡：APK 资产形态由 index.html 内联（零请求）；灰盒服务形态走 /api（支持改 JSON 刷新即生效）
      const level = globalThis.__WHISPER_LEVEL__
        ?? await (await fetch('/api/levels/' + config.levelId)).json();
      // 相对路径：两种形态下都指向站点根的 assets/（WebView 资产源根 = https://appassets.androidplatform.net/）
      const glbRes = await fetch('./assets/whisper-kits.glb');
      if (!glbRes.ok) throw new Error('套件加载失败：HTTP ' + glbRes.status);
      const glbBuf = await glbRes.arrayBuffer();
      const glb = parseGLB(glbBuf);
    
      const structure = buildStructure(level);
      const structureDraws = renderer.uploadMerged(structure.verts, structure.indices, structure.colorRanges, 'structure');
      const kitBaked = bakeInstances(glb, kitInstances(level));
      const kitDraws = renderer.uploadMerged(kitBaked.verts, kitBaked.indices, kitBaked.colorRanges, 'kits');
      const monsterDraws = {};
      for (const id of MONSTERS) {
        const b = bakeMonster(glb, id);
        monsterDraws[id] = { partCount: b.partCount, draws: renderer.uploadRanges(b.verts, b.indices, b.colorRanges, 'mon_' + id) };
      }
    
      // 玩家与怪物共用同一个碰撞器（同一份墙 span）
      const resolve = makeRoomCollider(level, 0.34);
    
      /**
       * 视线判定（含遮挡）：按 0.35m 步长沿线段采样，撞到墙格或**关闭的门**即视为看不见。
       * 首版只用距离判可见（pdist < 12 && sightRange >= pdist），于是怪物能隔墙"看见"玩家——
       * 实测：保护期一结束，巡逻怪走过门厅门口就把站在安全区里的玩家锁定。
       */
      const gridW = level.gridSize.w;
      const gridH = level.gridSize.h;
      const closedDoorCells = new Set((level.doors ?? []).filter((d) => d.locked).map((d) => d.cell.join(',')));
      /** 直线可达（无墙阻挡）：与 hasSight 同源，但用于导航而不是视觉 */
      function directPathClear(a, b) {
        const dx = b.x - a.x;
        const dz = b.z - a.z;
        const dist = Math.hypot(dx, dz);
        const steps = Math.max(1, Math.ceil(dist / 0.3));
        for (let i = 1; i <= steps; i++) {
          const t = i / steps;
          const x = Math.floor(a.x + dx * t);
          const z = Math.floor(a.z + dz * t);
          if (x < 0 || z < 0 || x >= gridW || z >= gridH) return false;
          if (level.grid[z][x] === '#') return false;
        }
        return true;
      }
    
      function hasSight(a, b) {
        const dx = b.x - a.x;
        const dz = b.z - a.z;
        const dist = Math.hypot(dx, dz);
        const steps = Math.max(1, Math.ceil(dist / 0.35));
        for (let i = 1; i < steps; i++) {
          const t = i / steps;
          const x = Math.floor(a.x + dx * t);
          const z = Math.floor(a.z + dz * t);
          if (x < 0 || z < 0 || x >= gridW || z >= gridH) return false;
          if (level.grid[z][x] === '#') return false;
          if (closedDoorCells.has(x + ',' + z)) return false;
        }
        return true;
      }
      const state = {
        pos: { ...level.spawns.players[0] },
        prevPos: { ...level.spawns.players[0] },
        yaw: 0, pitch: 0,
        sanity: config.sanity.max,
        battery: config.items.flashlight.batterySeconds,
        evidence: 0,
        elapsed: 0,
        lastStimulusAt: 0,
        stimulusLog: [],
        caught: false,
        extracted: null,
        monsterStates: {},
        debug: { fps: 0, frameMs: 0, drawCalls: 0, triangles: 0, levels: level.id, voice: null },
      };
    
      // 可交互物落点来自套件实例（必须在 state 定义之后挂载）
      {
        const kitList = kitInstances(level);
        state.pickupSpawns = kitList.filter((k) => k.pickup).map((k, i) => ({ key: k.pickup + ':' + i, kind: k.pickup, x: k.x, z: k.z }));
        state.breakerSpawns = kitList.filter((k) => k.breaker).map((k) => ({ zone: k.breaker, x: k.x, z: k.z }));
      }
    
      for (const id of MONSTERS) {
        const route = level.patrolRoutes[id]?.[0] ?? { x: 2, z: 2, room: 'corridor_main' };
        const brain = new MonsterBrain(id, { position: { x: route.x, z: route.z }, patrolPoints: [{ x: route.x, z: route.z }], chaseSpeedScale: config.monsters[id].chaseSpeedScale });
        // 怪物移动必须走同一套碰撞解析，否则会穿墙（实测：直线奔向出生点，1.7 秒贴脸）
        brain.mover = (from, to, step) => {
          const dx = to.x - from.x;
          const dz = to.z - from.z;
          const d = Math.hypot(dx, dz) || 1;
          const want = { x: (dx / d) * step, z: (dz / d) * step };
          const got = resolve(from, want);
          const moved = Math.hypot(got.x - from.x, got.z - from.z);
          return { ...got, blocked: moved < step * 0.5 };
        };
        state.monsterStates[id] = { brain, waypoints: [], wpIndex: 0, lastPathAt: -99 };
      }
    
      // ── 声纹校准（V9 §6：三步采样 + 环境底噪，写入第 0 局教学）──
      const calibrator = new VoiceCalibrator();
      const classifierBundle = { classifier: null };
      const saved = localStorage.getItem('whisper.anchors.v1');
      if (saved) {
        try {
          const anchors = JSON.parse(saved);
          calibrator.anchors = anchors;
          calibrator.done = true;
          classifierBundle.classifier = new VoiceBandClassifier({ anchors });
        } catch { /* 损坏则重新校准 */ }
      }
    
      const calibration = { phase: calibrator.done ? 'done' : 'ambient', framesInPhase: 0, phaseFrames: config.voiceCalibration.sampleMs / config.voiceCalibration.frameMs };
      state.calibration = calibration;
      hud.setPhase(calibration.phase, calibrator);
    
      async function beginCalibration() {
        calibration.phase = 'ambient';
        calibration.framesInPhase = 0;
        calibrator.reset();
      }
    
      window.addEventListener('whisper:recalibrate', () => {
        localStorage.removeItem('whisper.anchors.v1');
        classifierBundle.classifier = null;
        beginCalibration();
      });
      window.addEventListener('whisper:skip-calibration', () => {
        if (!calibrator.done) { state.calibrationSkipped = true; calibration.phase = 'done'; }
      });
    
      let last = performance.now();
      let running = true;
    
      function stepCalibration(frameDb) {
        if (calibration.phase === 'done') return;
        if (state.calibrationSkipped) { calibration.phase = 'done'; hud.setPhase('done', calibrator); return; }
        if (calibration.phase === 'ambient') {
          calibrator.pushAmbientFrame(frameDb);
          calibration.framesInPhase++;
          if (calibration.framesInPhase > 30) { calibration.phase = 'whisper'; calibration.framesInPhase = 0; calibrator.startPrompt('whisper'); }
        } else {
          calibrator.pushFrame(frameDb);
          calibration.framesInPhase++;
          if (calibration.framesInPhase >= calibration.phaseFrames) {
            calibrator.endPrompt();
            const order = ['whisper', 'normal', 'shout'];
            const idx = order.indexOf(calibration.phase);
            if (idx < order.length - 1) {
              calibration.phase = order[idx + 1];
              calibration.framesInPhase = 0;
              calibrator.startPrompt(calibration.phase);
            } else {
              const anchors = calibrator.anchors;
              localStorage.setItem('whisper.anchors.v1', JSON.stringify(anchors));
              classifierBundle.classifier = new VoiceBandClassifier({ anchors });
              const v = calibrator.validate();
              hud.log(v.ok ? '校准完成：三档区分度合格' : '校准完成但存在问题：' + v.problems.join('；'));
              calibration.phase = 'done';
            }
          }
        }
        hud.setPhase(calibration.phase, calibrator);
      }
    
      function emitStimulus(stim) {
        state.stimulusLog.push({ t: state.elapsed, stim });
        if (state.stimulusLog.length > 40) state.stimulusLog.shift();
        state.lastStimulusAt = state.elapsed;
        for (const id of MONSTERS) {
          const ms = state.monsterStates[id];
          const det = canHear(id, stim, ms.brain.position, { perceptionBonus: ms.brain.perceptionBonus });
          if (det.audible) {
            ms.brain.onStimulus(stim, Math.round(state.elapsed * config.network.tickRate));
            ms.lastPathAt = -99; // 强制重算路径
            hud.flashHeard(id, det);
          }
        }
      }
    
      function updateMonsters(dt, seen) {
        const tick = Math.round(state.elapsed * config.network.tickRate);
        for (const id of MONSTERS) {
          const ms = state.monsterStates[id];
          const brain = ms.brain;
          // 路径重算（房间级 A*，低频：低端机寻路代理 ≤2，V9 §7）
          if (state.elapsed - ms.lastPathAt > 1.2) {
            ms.lastPathAt = state.elapsed;
            const from = roomAt(level, brain.position.x, brain.position.z);
            const playerRoom = roomAt(level, state.pos.x, state.pos.z);
            // 巡逻终点不得（多数情况下）选中玩家所在房间：否则"没被发现的巡逻怪"会一路走到玩家脸上，
            // 玩家完全不知道为什么被抓（实测：保护期一结束三只怪同时入追击，距离 2.8~8m）。
            const target = brain.state === 'patrol'
              ? pickPatrolRoom(level, playerRoom, config.monsterBehavior.patrolAvoidPlayerRoomChance ?? 0.7, {
                  level,
                  excludeEntrance: state.elapsed <= (config.level.startGraceSeconds ?? 20),
                })
              : playerRoom;
            if (from && target && from !== target) {
              const path = findRoomPath(level, from, target);
              ms.waypoints = roomPathToWaypoints(level, path ?? [from]);
              ms.wpIndex = 0;
            } else ms.waypoints = [];
          }
          if (ms.waypoints.length) {
            const wp = ms.waypoints[ms.wpIndex];
            if (Math.hypot(wp.x - brain.position.x, wp.z - brain.position.z) < 0.6) ms.wpIndex = Math.min(ms.wpIndex + 1, ms.waypoints.length - 1);
            brain.patrolPoints = [ms.waypoints[ms.wpIndex]];
          }
          const pdist = Math.hypot(brain.position.x - state.pos.x, brain.position.z - state.pos.z);
          // 「看见」必须同时满足：手电亮着、在视野距离内、且**真正没有遮挡**
          // 灯光被切断的区域：怪物视觉距离减半（"声音诱饵 + 灯光限制"是收殓人的克制手段）
          const playerRoomZone = roomAt(level, state.pos.x, state.pos.z);
          const zoneDark = playerRoomZone && state.lightsOff?.has(level.rooms.find((r) => r.id === playerRoomZone)?.zone);
          const sightRange = brain.model.sightRangeM * (zoneDark ? 0.5 : 1);
          const seenNow = seen && pdist <= 12 && hasSight(brain.position, state.pos);
          const graceDone = state.elapsed > (config.level.startGraceSeconds ?? 12);
          if (graceDone && seenNow && sightRange >= pdist) {
            // 看见 → 追击，并持续更新"最后已知位置"
            brain.target = { x: state.pos.x, z: state.pos.z };
            brain.lastSeenAt = state.elapsed;
            brain.lastHeardTick = tick;
            brain._enter('chase');
          } else if (brain.state === 'chase') {
            // **看不见了**：去最后已知位置搜索，而不是继续贴着你。
            // 用户反馈"听见我后就贴我身上了"——首版一旦进入追击，目标会永远跟着玩家坐标，
            // 于是断掉视线也甩不掉。现在改成：超过 lostSightSeconds 未见，就锁定最后位置，
            // 到了那里搜索一段时间再回归巡逻（这正是恐怖游戏"躲进柜子等它走开"的前提）。
            const lostFor = state.elapsed - (brain.lastSeenAt ?? -1e9);
            if (lostFor > (config.monsterBehavior.loseSightSeconds ?? 3)) {
              brain.target = brain.target ?? { x: state.pos.x, z: state.pos.z };
              brain._enter('investigate');
              brain.lastHeardTick = tick;   // 重置失联计时，给它搜索时间
            }
          }
          brain.perceptionBonus = state.sanity < 50 ? (config.sanity.bands.find((b) => b.id === 'fear')?.effects.monsterPerceptionBonus ?? 0) : 0;
          // 导航纪律：直线被墙挡住时必须绕行。
          // 之前怪物永远朝目标走直线，接上碰撞后就直接卡在墙上（实测病区→走廊只走 1.8m 就停），
          // 而接碰撞之前它会**穿墙**直扑玩家——两种表现用户都报过（"贴我身上"）。
          const goal = brain.state === 'chase'
            ? { x: state.pos.x, z: state.pos.z }
            : (brain.target ?? brain.patrolPoints[0] ?? brain.position);
          if (!directPathClear(brain.position, goal)) {
            const goalRoom = roomAt(level, goal.x, goal.z);
            const hereRoom = roomAt(level, brain.position.x, brain.position.z);
            if (goalRoom && hereRoom) {
              const now = nowMs();
              if (now - (ms.navAt ?? -1e9) > 600) {
                ms.navAt = now;
                const path = findRoomPath(level, hereRoom, goalRoom);
                ms.waypoints = roomPathToWaypoints(level, path ?? [hereRoom]);
                ms.wpIndex = 0;
              }
              const wp = ms.waypoints[ms.wpIndex];
              if (wp && Math.hypot(wp.x - brain.position.x, wp.z - brain.position.z) < 0.7) {
                ms.wpIndex = Math.min(ms.wpIndex + 1, ms.waypoints.length - 1);
              }
              if (ms.waypoints.length) brain.patrolPoints = [ms.waypoints[ms.wpIndex]];
            }
          } else {
            brain.patrolPoints = [goal];
          }
          brain.step({ tick, seenPlayer: seenNow, playerPos: { x: state.pos.x, z: state.pos.z } });
          state.monsterStates[id] = ms;
          // 抓住判定放到 updateMonsters 之外统一做（含开局保护期）
        }
      }
    
      function frame(now) {
        if (!running) return;
        const dt = Math.min(0.05, (now - last) / 1000);
        last = now;
        const t0 = performance.now();
    
        const inp = input.consume();
        // 验证钩子：允许自动化校验脚本注入输入（tools/verify-playable.mjs 用），
        // 生产路径永不设置 __forceForward/__forceStrafe，等价于无操作。
        if (state.__forceForward) inp.forward = 1;
        if (state.__forceStrafe) inp.strafe = 1;
        if (state.__forceRun) inp.run = true;
        const db = voice.readDb();
        stepCalibration(db);
    
        // 视角
        const sens = config.designTokens.touch.lookSensitivityDefault;
        state.yaw += inp.look.dx * (window.innerWidth > 900 ? sens * 2.2 : sens * 6);
        state.pitch = Math.max(-1.1, Math.min(1.1, state.pitch - inp.look.dy * sens * (window.innerWidth > 900 ? 2.2 : 6)));
    
        // 移动（含子步进碰撞）
        const s = config.player;
        const speed = inp.crouch ? s.crouchSpeedMps : inp.run ? s.runSpeedMps : s.walkSpeedMps;
        const sin = Math.sin(state.yaw), cos = Math.cos(state.yaw);
        const dir = { x: (inp.strafe * cos + inp.forward * sin) * speed * dt, z: (inp.strafe * sin - inp.forward * cos) * speed * dt };
        state.prevPos = { ...state.pos };
        const moved = resolve(state.pos, dir);
        const realDist = Math.hypot(moved.x - state.pos.x, moved.z - state.pos.z);
        state.pos = moved;
        state.elapsed += dt;
    
        // 手电与理智
        const torchOn = state.battery > 0;
        if (torchOn) state.battery = Math.max(0, state.battery - dt);
        state.sanity = Math.max(0, state.sanity + (state.sanity < config.sanity.max ? config.sanity.recover.extractionSafeZonePerSec * 0.25 * dt : 0));
        if (!torchOn) state.sanity = Math.max(0, state.sanity + config.sanity.drain.darknessPerSec * dt);
        // 区域照明被切断：额外理智流失（V9 §7 的"黑暗"语义）
        if ((state.lightsOff?.size ?? 0) > 0) state.sanity = Math.max(0, state.sanity + config.sanity.drain.darknessPerSec * 1.5 * dt);
    
        // 脚步刺激（按移动形态节流）
        if (realDist > 0.01 && state.elapsed - (state.moveNoiseAt ?? -1) > (inp.run ? 0.34 : inp.crouch ? 0.9 : 0.55)) {
          state.moveNoiseAt = state.elapsed;
          emitStimulus(makeStimulus(movementStimulusKey({ running: inp.run, crouching: inp.crouch }), {
            position: { x: state.pos.x, z: state.pos.z }, playerId: 'p1', tick: Math.round(state.elapsed * config.network.tickRate),
          }));
        }
    
        // 语音刺激
        const cls = classifierBundle.classifier;
        let voiceInfo = null;
        if (cls && calibration.phase === 'done') {
          const r = cls.push(db);
          voiceInfo = r;
          const mute = false;
          // 声纹稳定门：连续 holdFrames 帧同档才承认一次发声；
          // 单帧变化会被"碰到手机的气流/转动时的摩擦声"触发（用户实测反馈）。
          const vc = config.voiceCalibration;
          const holdFrames = vc.emitHoldFrames ?? 8;
          const cooldownMs = vc.emitCooldownMs ?? 700;
          const st = state.debug.voiceGate = state.debug.voiceGate ?? { band: null, frames: 0, lastEmitMs: -1e9 };
          if (r.band === st.band) st.frames++;
          else { st.band = r.band; st.frames = 1; }
          const stable = st.frames >= holdFrames;
          const cooled = nowMs() - st.lastEmitMs >= cooldownMs;
          const emittable = !!r.band && r.band !== 'indistinguishable' && stable && cooled;
          state.debug.voiceGate.stable = stable;
          state.debug.voiceGate.frames = st.frames;
          if (emittable) {
            st.lastEmitMs = nowMs();
            st.emittedBand = r.band;
            const stim = bandToStimulus(r.band, { personaId: 'recruit', mute });
            if (!stim) { state.debug.voice = r; } else {
            state.debug.voiceStats.stimuli++;
            emitStimulus(makeStimulus(stim.sourceKey, {
              position: { x: state.pos.x, z: state.pos.z }, playerId: 'p1', intensity: stim.intensity,
              tick: Math.round(state.elapsed * config.network.tickRate),
            }));
            }
          }
          // 声纹调试面板数据（HUD 显示）：分档、相对电平、SNR、以及累计刺激数——
          // "没说话也被抓"这类反馈必须能一眼看出是声纹误判还是巡逻撞上。
          state.debug.voiceStats = state.debug.voiceStats ?? { stimuli: 0, lastBand: null, lastRel: null, lastSnr: null };
          state.debug.voiceStats.lastBand = r.band;
          state.debug.voiceStats.lastRel = r.relPeakNorm;
          state.debug.voiceStats.lastSnr = r.snrPeakDb ?? null;
          state.debug.voice = r;
        } else if (cls) {
          state.debug.voice = cls.push(db);
        }
    
        updateMonsters(dt, torchOn);
        // 接触判定：**扣理智**而非即死。
        //  ① 超出开局保护期；② 该怪处于追击态；③ 距离 < 0.9m；④ 不在接触无敌期内。
        // 即死设计会把"巡逻怪擦身而过"变成必然秒杀（实测保护期一结束就结束对局），
        // 改为扣 35 点理智 + 4 秒无敌期：遭遇依然致命，但玩家有反应余地；理智归零才结束。
        if (state.elapsed > (config.level.startGraceSeconds ?? 12) && state.elapsed > (state.contactGraceUntil ?? -1)) {
          for (const id of MONSTERS) {
            const b = state.monsterStates[id].brain;
            const d = Math.hypot(b.position.x - state.pos.x, b.position.z - state.pos.z);
            if (b.state === 'chase' && d < 0.9) {
              state.sanity = Math.max(0, state.sanity - (config.monsterBehavior.contactSanityLoss ?? 35));
              state.contactGraceUntil = state.elapsed + (config.monsterBehavior.contactGraceSeconds ?? 4);
              // 接触后把怪**推开**：否则它在无敌期内会一直贴着你，无敌一结束就是不可避免的二次抓
              // （实测日志：t=37.4 接触，t=41.4 与 45.4 两次都是 d=0.00 的贴脸抓）。
              // 世界观上也成立：它撕了一下、你尖叫着挣脱，双方短暂拉开。
              {
                const pushDist = config.monsterBehavior.contactPushBackM ?? 6;
                const ax = b.position.x - state.pos.x;
                const az = b.position.z - state.pos.z;
                const ad = Math.hypot(ax, az) || 1;
                // 沿"远离玩家"的方向找落点，逐点回退到可站立处（避免推进墙里）
                for (let step = pushDist; step >= 1; step -= 1) {
                  const nx = state.pos.x + (ax / ad) * step;
                  const nz = state.pos.z + (az / ad) * step;
                  const gx = Math.floor(nx);
                  const gz = Math.floor(nz);
                  if (gx > 0 && gz > 0 && gx < gridW && gz < gridH && level.grid[gz][gx] !== '#') {
                    b.position = { x: nx, z: nz };
                    b.target = null;
                    // 被撞开后该怪失去目标转为调查：否则它会在无敌期内走回来再次贴脸
                    b._enter('investigate');
                    b.lastHeardTick = null;
                    break;
                  }
                }
              }
              state.contacts = (state.contacts ?? 0) + 1;
              if (state.sanity <= 0) { state.caught = true; state.sanity = 0; }
              state.catchLog = state.catchLog ?? [];
              state.catchLog.push({ t: +state.elapsed.toFixed(1), id, d: +d.toFixed(2) });
              hud.log('被' + b.model.label + '抓到手：理智 -' + (config.monsterBehavior.contactSanityLoss ?? 35) + '（' + Math.round(state.sanity) + '）');
              break;
            }
          }
        }
    
        // 拾取物（手电电池 / 信号弹 / 镇静剂）：靠近即拾取，HUD 记录
        for (const inst of state.pickupSpawns ?? []) {
          if (state.taken?.has(inst.key)) continue;
          if (Math.hypot(inst.x - state.pos.x, inst.z - state.pos.z) < 1.0) {
            state.taken = state.taken ?? new Set();
            state.taken.add(inst.key);
            if (inst.kind === 'battery') { state.battery = Math.min(config.items.flashlight.batterySeconds, state.battery + 60); hud.log('拾取手电电池：电量 +60s'); }
            else if (inst.kind === 'flare') { state.flares = (state.flares ?? 0) + 1; hud.log('拾取信号弹 ×1（共 ' + state.flares + '）'); }
            else if (inst.kind === 'sedative') { state.sanitatives = (state.sanitatives ?? 0) + 1; hud.log('拾取镇静剂 ×1（共 ' + state.sanitatives + '）'); }
          }
        }
    
        // 配电箱：靠近按 E 切换该区灯光（灯光熄灭会加速理智流失、也降低怪物视觉）
        for (const b of state.breakerSpawns ?? []) {
          if (Math.hypot(b.x - state.pos.x, b.z - state.pos.z) < 1.6) {
            state.nearBreaker = b.zone;
            if (state.usePressed) {
              state.lightsOff = state.lightsOff ?? new Set();
              if (state.lightsOff.has(b.zone)) { state.lightsOff.delete(b.zone); hud.log('配电箱：' + b.zone + ' 区照明恢复'); }
              else { state.lightsOff.add(b.zone); hud.log('配电箱：' + b.zone + ' 区照明切断（怪物视觉下降，你的理智也在下降）'); }
              state.usePressed = false;
            }
          } else if (state.nearBreaker === b.zone) state.nearBreaker = null;
        }
    
        // 证据拾取
        for (const e of level.evidencePoints) {
          if (state.collected?.has(e.id)) continue;
          if (Math.hypot(e.pos.x - state.pos.x, e.pos.z - state.pos.z) < 0.9) {
            state.collected = state.collected ?? new Set();
            state.collected.add(e.id);
            state.evidence++;
            hud.log('拾取证据 ' + e.id + '（' + state.evidence + '/' + level.evidencePoints.length + '）');
          }
        }
        // 撤离
        for (const ep of level.extractionPoints) {
          if (state.evidence < level.evidencePoints.length) continue;
          if (Math.hypot(ep.pos.x - state.pos.x, ep.pos.z - state.pos.z) < 1.4) {
            state.extracted = ep;
            running = false;
          }
        }
        if (state.sanity <= 0 || state.caught) running = false;
    
        // 渲染
        const w = canvas.width, h = canvas.height;
        renderer.beginFrame(w, h, FOG);
        const proj = cam.projection(78, w / h, 0.08, 60);
        const view = cam.view(state.pos.x, 1.62, state.pos.z, state.yaw, state.pitch);
        const vp = cam.multiply(proj, view);
        const fwd = { x: Math.sin(state.yaw) * Math.cos(state.pitch), y: Math.sin(state.pitch), z: -Math.cos(state.yaw) * Math.cos(state.pitch) };
        const light = { pos: { x: state.pos.x, y: 1.5, z: state.pos.z }, dir: fwd, range: torchOn ? 11 : 1.4, cosOuter: Math.cos(0.52) };
    
        // 两遍渲染：
        //  ① 无光基底遍（极暗、不写深度）：让手电照不到的地方仍有可辨的轮廓，
        //     避免"黑到看不见墙和地板"（用户反馈过地板/天花看不见）；
        //  ② 手电光照遍：叠加锥形光，是本作氛围的主力。
        const darkLight = { pos: { x: 0, y: -10, z: 0 }, dir: { x: 0, y: -1, z: 0 }, range: 0.01, cosOuter: -2 };
        for (const d of structureDraws) renderer.draw({ ...d, base: 0.055 }, vp, state.pos, darkLight, { depthWrite: false });
        for (const d of kitDraws) renderer.draw({ ...d, base: 0.05 }, vp, state.pos, darkLight, { depthWrite: false });
        for (const d of structureDraws) renderer.draw({ ...d, base: 0.02 }, vp, state.pos, light);
        for (const d of kitDraws) renderer.draw({ ...d, base: 0.02 }, vp, state.pos, light);
        for (const id of MONSTERS) {
          const ms = state.monsterStates[id];
          const origin = { x: ms.brain.position.x, z: ms.brain.position.z };
          for (const d of monsterDraws[id].draws) {
            renderer.draw({ ...d, base: 0.04 }, vp, origin, { pos: origin, dir: { x: 0, y: 1, z: 0 }, range: 0.01, cosOuter: -2 }, { depthWrite: false });
          }
          for (const d of monsterDraws[id].draws) renderer.draw({ ...d, base: 0.02 }, vp, origin, light);
        }
    
        state.debug.frameMs = performance.now() - t0;
        state.debug.drawCalls = renderer.stats.drawCalls;
        state.debug.triangles = renderer.stats.triangles;
        state.debug.fps = state.debug.fps * 0.9 + (1 / dt) * 0.1;
    
        hud.update({
          state, level, voiceInfo, monsterStates: state.monsterStates,
          calibration,
          onReorder: () => { localStorage.removeItem('whisper.anchors.v1'); classifyReset(); },
          classifier: cls,
        });
        window.__WHISPER_STATE = state;
    
        if (running) {
          state.framesRun = (state.framesRun ?? 0) + 1;
          requestAnimationFrame(frame);
        }
        else hud.endScreen(state, level);
      }
    
      function classifyReset() {
        classifierBundle.classifier = null;
        beginCalibration();
      }
    
      requestAnimationFrame(frame);
      return { state, stop: () => { running = false; } };
    }
    
    function roomAt(level, x, z) {
      return level.rooms.find((r) => x >= r.rect.x0 && x < r.rect.x1 && z >= r.rect.z0 && z < r.rect.z1)?.id ?? null;
    }
    function randomRoom(level) {
      return level.rooms[Math.floor(Math.random() * level.rooms.length)].id;
    }
    
    /**
     * 巡逻终点选择：以给定概率避开玩家所在房间。
     * 100% 避开会让世界"太安全"，0% 则保证巡逻怪迟早贴到玩家身上——所以做成概率参数（默认 0.7）。
     */
    function pickPatrolRoom(level, playerRoom, avoidChance, opts = {}) {
      const safeIds = new Set((level.rooms ?? []).filter((r) => r.lightZone === 'safe').map((r) => r.id));
      const all = level.rooms.map((r) => r.id);
      // 保护期内排除入口区：开局这几秒玩家要看 UI、做校准，不该被巡逻怪堵在门厅里
      const banned = new Set([playerRoom]);
      if (opts.excludeEntrance) for (const id of safeIds) banned.add(id);
      const pool = all.filter((id) => !banned.has(id));
      if (pool.length && Math.random() < avoidChance) return pool[Math.floor(Math.random() * pool.length)];
      const fallback = all.filter((id) => !(opts.excludeEntrance && safeIds.has(id)));
      return (fallback.length ? fallback : all)[Math.floor(Math.random() * (fallback.length ? fallback.length : all.length))];
    }
    
    module.exports = { startGame };
