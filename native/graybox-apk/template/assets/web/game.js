/* 低语计划 · 灰盒单文件包（tools/bundle-web.mjs 生成，勿手改） */
(function () {
  "use strict";
  var __tables = {}, __cache = {};
  function __req(id) {
    if (__cache[id]) return __cache[id];
    // 关键：缓存必须存"工厂产出的 exports"，而不是先塞一个空壳。
    // 工厂内部是 `mod.exports = { ... }` 重新赋值，若缓存空壳，第二次 __req 会拿到空对象
    // （实测：geometry 被 world 二次导入时 buildWallBoxes undefined）。
    var mod = { exports: {} };
    __tables[id](mod);
    __cache[id] = mod.exports;
    return mod.exports;
  }
  __tables["__m0"] = function (mod) {
    var __CFG = {
      "meta": {
        "source": "V9.0 §6 表6-1 / §7 表7-1·7-2 / §8 表8-1 / 附录A（V5 数值基线）",
        "frozenAt": "2026-10-02",
        "note": "V9 为终稿冻结版；本文件是运行时唯一数值真相源，代码内不得硬编码数值（V9 §19.5 三层调参：改数值不碰代码）"
      },
      "stimulusSources": {
        "voice_whisper": {
          "intensity": 10,
          "radiusM": 4,
          "type": "voice",
          "label": "耳语"
        },
        "voice_normal": {
          "intensity": 35,
          "radiusM": 12,
          "type": "voice",
          "label": "正常说话"
        },
        "voice_shout": {
          "intensity": 80,
          "radiusM": 25,
          "type": "voice",
          "label": "喊叫"
        },
        "run_footstep": {
          "intensity": 52,
          "radiusM": 15,
          "type": "footstep",
          "label": "奔跑脚步"
        },
        "crouch_footstep": {
          "intensity": 8,
          "radiusM": 3,
          "type": "footstep",
          "label": "蹲行脚步"
        },
        "walk_footstep": {
          "intensity": 8,
          "radiusM": 8,
          "type": "footstep",
          "label": "行走脚步"
        },
        "radio_ptt": {
          "intensity": 60,
          "radiusM": null,
          "type": "device",
          "label": "对讲机PTT",
          "globalBroadcast": true
        },
        "prop_open": {
          "intensity": 30,
          "radiusM": 10,
          "type": "prop",
          "label": "开柜"
        },
        "prop_break": {
          "intensity": 70,
          "radiusM": 20,
          "type": "prop",
          "label": "砸窗"
        },
        "recorder_play": {
          "intensity": 55,
          "radiusM": 16,
          "type": "prop",
          "label": "录音笔回放"
        },
        "sanity_scream": {
          "intensity": 100,
          "radiusM": null,
          "type": "voice",
          "label": "崩溃尖叫",
          "globalBroadcast": true
        },
        "_balanceNote": "跑动强度 52：低于收殓人阈值 55（它只认喊叫/砸窗），高于缝匠 30 与低语者 10。原值 45 在部分随机布局下引不来怪，出现\"跑动 5/5 都安全\"的过度削弱。"
      },
      "voiceCalibration": {
        "algorithm": "personal-baseline-relative-level",
        "replaces": "绝对 dBFS 硬阈值 -45/-25dB（V6 §12.2，已废弃：机型 AGC/增益差异可达 10~25dB）",
        "frameMs": 50,
        "prompts": [
          "whisper",
          "normal",
          "shout"
        ],
        "samplesPerPrompt": 3,
        "sampleMs": 2600,
        "anchorStatistic": "median_of_speech_frames_p50",
        "minAnchorSnrDb": 6,
        "noiseFloorWindowMs": 10000,
        "noiseFloorPercentile": 10,
        "noiseFloorFastWindowMs": 2000,
        "noiseFloorFastPercentile": 5,
        "noiseFloorMaxDb": -30,
        "levelWindowMs": 700,
        "levelStatistic": "p90",
        "hysteresisDb": 3,
        "normalizeByDynamicRange": true,
        "clampCeil": 1.5,
        "peakBoost": 1.15,
        "minRuntimeSnrDb": 8,
        "boundaryPolicy": "piecewise-personal-range",
        "tieBreak": "nearest-band-then-higher",
        "recalibration": "settings-page-and-first-run-tutorial",
        "targets": {
          "crossDeviceAgreement": 0.9,
          "reportedAt": "M0 验收硬指标（V9 §6）"
        },
        "emitHoldFrames": 8,
        "emitCooldownMs": 700,
        "emitGateNote": "单帧分档变化就发刺激会把\"碰到手机/移动时的气流\"当成说话（用户报告\"转一下视觉就听到了\"）：改为连续稳定 8 帧才承认，并要求 700ms 冷却。",
        "minAnchorSeparationDb": 6,
        "calibrationNote": "采样 2.6s/档：首版 1.2s 太短，人还没开口就采完，锚点被环境音拉平，结果轻声说话直接顶到 shout。另要求三档锚点间距 ≥6dB，低于此值提示重新校准。"
      },
      "personaPacks": {
        "recruit": {
          "label": "新兵",
          "stimulusMultiplier": 1,
          "fixedIntensity": 35,
          "traits": []
        },
        "leader": {
          "label": "领队",
          "stimulusMultiplier": 0.85,
          "fixedIntensity": 30,
          "traits": [
            "ally_move_noise_-20pct_within_2m"
          ]
        },
        "taunter": {
          "label": "挑衅者",
          "stimulusMultiplier": 1.25,
          "fixedIntensity": 44,
          "traits": [
            "aggro_lock_5s"
          ]
        },
        "drowned": {
          "label": "溺水者",
          "stimulusMultiplier": 1,
          "fixedIntensity": 35,
          "traits": [
            "localization_accuracy_-30pct_3s"
          ],
          "frequencyScale": 0.6
        },
        "guide": {
          "label": "引路人",
          "stimulusMultiplier": 1,
          "fixedIntensity": 35,
          "traits": [
            "reveal_nearest_evidence_cost_sanity_5"
          ]
        }
      },
      "sanity": {
        "max": 100,
        "bands": [
          {
            "id": "composed",
            "min": 80,
            "max": 100,
            "label": "镇定",
            "effects": {}
          },
          {
            "id": "uneasy",
            "min": 50,
            "max": 79,
            "label": "不安",
            "effects": {
              "ambientGain": 1.35,
              "flashlightJitterDeg": 1.2
            }
          },
          {
            "id": "fear",
            "min": 25,
            "max": 49,
            "label": "恐惧",
            "effects": {
              "tinnitus": true,
              "edgeNoise": 0.35,
              "monsterPerceptionBonus": 0.2
            }
          },
          {
            "id": "brink",
            "min": 1,
            "max": 24,
            "label": "崩溃边缘",
            "effects": {
              "auditoryHallucination": true,
              "moveSpeedScale": 0.85
            }
          },
          {
            "id": "collapse",
            "min": 0,
            "max": 0,
            "label": "崩溃",
            "effects": {
              "screamSeconds": 3,
              "screamStimulus": "sanity_scream",
              "restoreTo": 25
            }
          }
        ],
        "drain": {
          "lookAtMonsterPerSec": -8,
          "darknessPerSec": -1,
          "allyDeath": -15,
          "jumpscare": -10
        },
        "recover": {
          "sedative": 25,
          "extractionSafeZonePerSec": 2
        }
      },
      "monsters": {
        "stitcher": {
          "label": "缝匠",
          "role": "教学怪（压力型）",
          "speedMps": 3.6,
          "chaseSpeedScale": 1.6,
          "hearing": "medium",
          "hearingThreshold": 30,
          "sightRangeM": 14,
          "canOpenDoors": false,
          "ignoresLockers": false,
          "counterplay": "关门可迟滞；躲柜静止 + 控制音量"
        },
        "whisperer": {
          "label": "低语者",
          "role": "语音猎手（核心怪）",
          "speedMps": 4.2,
          "chaseSpeedScale": 1.6,
          "hearing": "very_high",
          "hearingThreshold": 10,
          "sightRangeM": 6,
          "canOpenDoors": true,
          "ignoresLockers": false,
          "counterplay": "对语音声纹极敏感；安静时几乎失明；快捷喊话诱饵引开"
        },
        "coroner": {
          "label": "收殓人",
          "role": "终局压迫（高压怪）",
          "speedMps": 3.2,
          "chaseSpeedScale": 1.6,
          "hearing": "low",
          "hearingThreshold": 55,
          "sightRangeM": 20,
          "canOpenDoors": true,
          "ignoresLockers": true,
          "counterplay": "视野感知为主、无视柜子；声音诱饵 + 灯光限制"
        },
        "_speedNote": "追击速度 = speedMps × 1.6（V9 §7 表7-2，唯一真相源为 monsterBehavior.chaseSpeedScale）。缝匠 5.8 / 低语者 6.7 / 收殓人 5.1 m/s；玩家跑速 5.6 —— 只有低语者（语音猎手）比你快，其余可以甩掉。"
      },
      "monsterBehavior": {
        "states": [
          "patrol",
          "investigate",
          "chase",
          "return"
        ],
        "lostContactSeconds": 12,
        "investigateArriveRadiusM": 1.5,
        "investigateLingerSeconds": 4,
        "finalRageWindowBeforeExtractionSec": 60,
        "maxConcurrentPathAgents": 2,
        "hostPositionTolerance": 1.2,
        "contactSanityLoss": 35,
        "contactGraceSeconds": 4,
        "patrolAvoidPlayerRoomChance": 0.7,
        "contactNote": "接触惩罚而非即死：巡逻中的怪可能与你擦身而过，碰到就结束对局会让\"没说话也被抓\"变成必然。改为扣理智 + 4 秒无敌期，理智归零才结束；这样遭遇仍然致命，但玩家有反应余地。",
        "patrolAvoidEntranceDuringGrace": true,
        "contactPushBackM": 6,
        "loseSightSeconds": 3,
        "_loseSightNote": "看不见玩家 3 秒后，追击转为前往最后已知位置搜索（而不是继续跟随玩家坐标）。首版没有这条，导致断掉视线也甩不掉——用户反馈原话是听见我后就贴我身上了。"
      },
      "level": {
        "matchSeconds": [
          600,
          900
        ],
        "extraction": {
          "standardPoint": {
            "rewardScale": 1,
            "safe": true
          },
          "deepPoint": {
            "rewardScale": 1.3,
            "safe": false
          }
        },
        "dynamicEventsPerMatch": [
          2,
          3
        ],
        "eventPool": [
          "blackout",
          "door_lock_shift",
          "radio_static",
          "mirror_flicker",
          "power_surge",
          "child_laughter"
        ],
        "startGraceSeconds": 20,
        "graceNote": "开局保护期：期间不判定接触、怪物不因\"看见\"入追击，且巡逻不进入入口区（给玩家看清 UI 与校准的时间）"
      },
      "items": {
        "flashlight": {
          "batterySeconds": 120,
          "monsterPerceptionBonusM": 3
        },
        "recorder": {
          "recordSeconds": 20,
          "producesStimulusOnRecordAndPlay": true
        },
        "camera": {
          "stunSeconds": 2,
          "usesPerMonsterPerMatch": 1
        },
        "sedative": {
          "sanityRestore": 25,
          "glassStimulus": "prop_break",
          "castSeconds": 2
        },
        "flare": {
          "safeZoneSeconds": 15
        }
      },
      "economy": {
        "currency": "残响碎片",
        "formula": {
          "evidence": 200,
          "survivingAlly": 150,
          "efficiencyUnder10min": 100,
          "deepExtractionScale": 1.3
        },
        "redLine": "碎片不可付费购买"
      },
      "death": {
        "ghost": {
          "speedScale": 0.4,
          "whisperInterferenceCooldownSec": 30
        },
        "allDeadEvidenceLoss": 0.5
      },
      "player": {
        "walkSpeedMps": 3.5,
        "runSpeedMps": 5.6,
        "crouchSpeedMps": 1.6,
        "sanityDrainFromDarknessPerSec": -1,
        "note": "V9 §7：移动与噪音形态由 movementStimulusKey 映射到刺激源；速度必须配置化，代码不得硬编码"
      },
      "network": {
        "tickRate": 60,
        "batchEveryTicks": 3,
        "snapshotBytes": 2800,
        "reconnectWindowSec": 90,
        "reconnectSnapshotTtlSec": 120,
        "bandwidth": {
          "downKbps": 12,
          "upKbps": 6
        },
        "hostMigration": {
          "rttThresholdMs": 200,
          "sustainedSeconds": 10,
          "maskSeconds": [
            2,
            3
          ]
        },
        "transformSendHz": 10,
        "disconnectRateGate": 0.05
      },
      "capacity": {
        "ccuHardCap": 100,
        "roomsOfFour": 25,
        "dauBaseline": 2000,
        "mauBaseline": 40000,
        "trafficQuotaTbPerMonth": 0.3,
        "ccuWarnRooms": 20,
        "ccuFullRooms": 25,
        "plusPack": {
          "priceCnyPerMonth": 55,
          "extraCcu": 100
        },
        "noQueuePromise": "满房不得承诺「立即建房」；券 = 插队权重 + 次日优先（V8 R3）",
        "regionsFirstLaunch": 2
      },
      "performanceGates": {
        "apkMaxMb": 200,
        "aabInstallTimeMaxMb": 150,
        "coldStartMaxSecMiddleTier": 8,
        "pssMaxMb": 600,
        "uiFrameCostMaxMs": 2,
        "drawCallMax": 120,
        "dynamicLightsMax": 4,
        "lowTierFpsMin": 30,
        "crashRateMax": 0.01
      },
      "northStar": {
        "firstMatchCompletionRate": 0.85,
        "playAgainRate": null,
        "voiceOrWheelActivationRate": 0.3,
        "fourPlayerRoomRate": null,
        "lowTierCrashRate": 0.01,
        "fearSurveyM0": 3.5,
        "touchPassRateM0": 0.7,
        "muteFearSurvey": 3.3,
        "muteWinRateDelta": 0.15,
        "d1Retention": 0.25,
        "reopen24h": 0.5
      }
    };
    var __TOK = {
      "meta": {
        "source": "V9 §11 前端设计系统（V6 §17~§23 浓缩）",
        "note": "设计 Token 的直接落为代码（V9 §19.1 C2）：UI 零编辑器，Token 与代码一一对应"
      },
      "color": {
        "paper":     "#F0E6D2",
        "ink":       "#1A1A1A",
        "blood":     "#8B1E1E",
        "mold":      "#5C8C6E",
        "bone":      "#D8CFBB",
        "soot":      "#0E0D0C",
        "rust":      "#6E4A2F",
        "signal":    "#C9A227",
        "danger":    "#C0392B",
        "safe":      "#4E7A5A",
        "hud":       "rgba(240,230,210,0.86)",
        "hudDim":    "rgba(240,230,210,0.42)"
      },
      "font": {
        "family": "'Courier New', 'Noto Serif SC', monospace",
        "title": { "sizePx": 30, "weight": 700, "letterSpacing": "0.14em" },
        "hud":   { "sizePx": 15, "weight": 600, "letterSpacing": "0.10em" },
        "body":  { "sizePx": 14, "weight": 400, "letterSpacing": "0.02em" },
        "diegetic": { "sizePx": 13, "weight": 400, "letterSpacing": "0.06em" }
      },
      "spacing": { "xs": 4, "sm": 8, "md": 16, "lg": 24, "xl": 40 },
      "radius": { "none": 0, "sm": 2, "md": 4 },
      "layers": {
        "L0_fatal":     { "readSeconds": 0.1, "examples": ["怪物仇恨闪烁", "撤离倒计时"] },
        "L1_tactical":  { "readSeconds": 0.5, "examples": ["队友方位点", "理智环", "轮盘"] },
        "L2_status":    { "examples": ["手电电量", "道具栏", "证据计数"] },
        "L3_diegetic":  { "examples": ["环境沉浸层"] }
      },
      "motion": {
        "uiFadeMs": 180,
        "hudPulseMs": 900,
        "damageFlashMs": 220,
        "blackoutFadeMs": 600,
        "monsterStingerMs": 1400,
        "reduceMotionRespect": true
      },
      "touch": {
        "minHitTargetPx": 44,
        "lookSensitivityDefault": 0.0032,
        "deadZonePx": 8,
        "stickRadiusPx": 68
      },
      "accessibility": {
        "contrastMinRatio": 4.5,
        "subtitleForAllVoiceLines": true,
        "colorblindSafeIcons": true,
        "hapticsToggle": true
      }
    };
    function installConfig(c, t) { if (c) __CFG = c; if (t) __TOK = t; return __CFG; }
    function loadConfig() {
      var inline = globalThis.__WHISPER_BOOTSTRAP__;
      if (inline && inline.config) { __CFG = inline.config; if (inline.designTokens) __TOK = inline.designTokens; }
      return __CFG;
    }
    function cfg(path, fallback) {
      var conf = loadConfig();
      if (!conf) return fallback;
      var parts = String(path).split(".");
      var cur = conf;
      for (var i = 0; i < parts.length; i++) {
        if (cur == null || typeof cur !== "object" || !(parts[i] in cur)) return fallback;
        cur = cur[parts[i]];
      }
      return cur;
    }
    function stimulusSource(key) {
      var t = cfg("stimulusSources");
      var s = t[key];
      if (!s) throw new Error("unknown stimulus source: " + key);
      var out = { key: key, globalBroadcast: false };
      for (var k in s) out[k] = s[k];
      return out;
    }
    function designTokens() {
      var inline = globalThis.__WHISPER_BOOTSTRAP__;
      if (inline && inline.designTokens) __TOK = inline.designTokens;
      if (!__TOK) throw new Error("设计 Token 未加载");
      return __TOK;
    }
    function resetConfig() {}
    mod.exports = { installConfig: installConfig, loadConfig: loadConfig, cfg: cfg, stimulusSource: stimulusSource, designTokens: designTokens, resetConfig: resetConfig };
  };
  __tables["__m1"] = function (mod) {
    /**
     * 声纹判定链（V9 §6 / V8 N2 整改：个人校准 + 相对电平）
     *
     * 已废弃（禁止复活）：V6 §12.2 的绝对 dBFS 硬阈值（-45/-25dB）。
     * 原因：不同机型麦克风增益 / AGC 策略 / 握持姿势差异可达 10~25dB——
     *       同一句话在 A 机是"耳语"，在 B 机可能是"喊叫"，核心机制公平性被摧毁。
     *
     * 本模块是**纯函数 + 确定性状态机**，零依赖、零 DOM：浏览器侧只负责把
     * AnalyserNode 的 dBFS 帧喂进来（见 web/src/audio/voice-input.js），
     * C# 侧（unity/）必须通过同一批一致性向量 data/vectors/voice-classification.json。
     */
    var __ns0 = __req("__m0");
    var cfg = __ns0.cfg;
    
    const BAND_IDS = ['whisper', 'normal', 'shout'];
    /** 分档 → 声纹刺激源 key（强度/半径取 data/config.json 的 stimulusSources） */
    const BAND_SOURCE_KEY = {
      whisper: 'voice_whisper',
      normal: 'voice_normal',
      shout: 'voice_shout',
    };
    
    /** dBFS 下限（静音底），避免 -Infinity 参与运算 */
    const SILENCE_DBFS = -100;
    
    const clamp = (v, lo, hi) => (v < lo ? lo : v > hi ? hi : v);
    const round4 = (v) => Math.round(v * 1e4) / 1e4;
    
    /** 中位数 */
    function median(xs) {
      if (xs.length === 0) return SILENCE_DBFS;
      const s = [...xs].sort((a, b) => a - b);
      const m = s.length >> 1;
      return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2;
    }
    
    /** 线性插值百分位（p ∈ [0,1]） */
    function percentile(xs, p) {
      if (xs.length === 0) return SILENCE_DBFS;
      const s = [...xs].sort((a, b) => a - b);
      if (s.length === 1) return s[0];
      const idx = clamp(p, 0, 1) * (s.length - 1);
      const lo = Math.floor(idx);
      const hi = Math.ceil(idx);
      if (lo === hi) return s[lo];
      return s[lo] + (s[hi] - s[lo]) * (idx - lo);
    }
    
    /**
     * 锚点统计：anchorStatistic = median_of_speech_frames_p50。
     *
     * 为什么不是 max / 不是 top-decile：某档采样期间本就有大量静默帧（人耳语前会停顿、
     * 会换气），拿 max 取到的是爆音与偶发峰值；拿 top-10% 取到的是"最响的那几帧"，
     * 会把耳语锚点拉低 4~6dB，进而把正常说话误判成喊叫（首轮测试实测到的真实缺陷）。
     * 正确做法：先按锚点无关的规则裁掉静默帧（低于整段中位数 −3dB 的帧），
     * 再取剩余语音帧的中位数——它代表"这一档的稳态电平"。
     */
    function anchorFromFrames(frames) {
      if (!frames || frames.length === 0) return SILENCE_DBFS;
      const valid = frames.filter((f) => Number.isFinite(f) && f > SILENCE_DBFS);
      if (valid.length === 0) return SILENCE_DBFS;
      const med = median(valid);
      const speech = valid.filter((f) => f >= med - 3);
      return median(speech.length ? speech : valid);
    }
    
    /** 固定容量环形缓冲（零 GC 抖动，符合 §26 性能约束"每帧零分配"精神） */
    class Ring {
      constructor(capacity) {
        this.capacity = capacity;
        this.buf = new Float32Array(capacity);
        this.n = 0;
        this.head = 0;
      }
      push(v) {
        this.buf[this.head] = v;
        this.head = (this.head + 1) % this.capacity;
        if (this.n < this.capacity) this.n++;
      }
      /** 返回按时间序的浅拷贝数组（仅统计时调用，非每帧） */
      values() {
        const out = new Array(this.n);
        for (let i = 0; i < this.n; i++) {
          const idx = (this.head - this.n + i + this.capacity * 2) % this.capacity;
          out[i] = this.buf[idx];
        }
        return out;
      }
      get last() {
        return this.n === 0 ? undefined : this.buf[(this.head - 1 + this.capacity) % this.capacity];
      }
      clear() {
        this.n = 0;
        this.head = 0;
      }
    }
    
    function framesFor(ms) {
      const frameMs = cfg('voiceCalibration.frameMs', 50);
      return Math.max(1, Math.round(ms / frameMs));
    }
    
    /**
     * 个人基线校准器：三步采样（耳语 / 正常 / 喊叫）→ 该设备三档锚点。
     * 第 0 局教学内完成，持久化到本地（settings 页可重新校准）。
     */
    class VoiceCalibrator {
      constructor(opts = {}) {
        const c = cfg('voiceCalibration', {});
        this.samplesPerPrompt = opts.samplesPerPrompt ?? c.samplesPerPrompt ?? 3;
        this.sampleFrames = framesFor(opts.sampleMs ?? c.sampleMs ?? 2000);
        this.prompts = c.prompts ?? ['whisper', 'normal', 'shout'];
        this.reset();
      }
    
      /**
       * 喂入一帧环境音（第 0 局教学的第 0 步：先静默 1.5 秒采环境底噪）。
       * 环境底噪与"玩家说话多响"无关，必须在采样语音之前独立采集。
       */
      pushAmbientFrame(dbfs) {
        if (!this.ambient) this.ambient = [];
        this.ambient.push(Number.isFinite(dbfs) ? dbfs : SILENCE_DBFS);
        return { ambientFrames: this.ambient.length };
      }
    
      ambientFloor(percentilePct = 60) {
        if (!this.ambient || this.ambient.length === 0) return null;
        return round4(percentile(this.ambient, percentilePct / 100));
      }
    
      reset() {
        this.current = null;
        this.collected = new Map();
        this.ambient = [];
        this.done = false;
        this.anchors = null;
      }
    
      /** 开始某档采样：'whisper' | 'normal' | 'shout' */
      startPrompt(promptId) {
        if (!this.prompts.includes(promptId)) throw new Error(`unknown prompt: ${promptId}`);
        this.current = promptId;
        if (!this.collected.has(promptId)) this.collected.set(promptId, []);
      }
    
      /**
       * 喂入一帧 dBFS。返回 {promptId, progress, complete}
       * 未处于采样态时返回 {ignored:true}
       */
      pushFrame(dbfs) {
        if (!this.current) return { ignored: true };
        const promptId = this.current;
        const arr = this.collected.get(promptId);
        arr.push(Number.isFinite(dbfs) ? dbfs : SILENCE_DBFS);
        const progress = clamp(arr.length / this.sampleFrames, 0, 1);
        const complete = arr.length >= this.sampleFrames;
        if (complete) {
          this.current = null;
          this.done = this.prompts.every((p) => (this.collected.get(p)?.length ?? 0) > 0);
          if (this.done) this.anchors = this.buildAnchors();
        }
        return { promptId, progress: round4(progress), complete };
      }
    
      /** 显式结束当前档（提前结束也接受，取已采到的帧） */
      endPrompt() {
        this.current = null;
        this.done = this.prompts.every((p) => (this.collected.get(p)?.length ?? 0) > 0);
        if (this.done) this.anchors = this.buildAnchors();
        return this.anchors;
      }
    
      /** 三档锚点（dBFS）；含环境底噪下限与单调化修正 */
      buildAnchors() {
        const raw = {};
        for (const p of this.prompts) raw[p] = anchorFromFrames(this.collected.get(p) ?? []);
        const anomalies = [];
        if (raw.normal < raw.whisper) { anomalies.push('normal<whisper'); raw.normal = raw.whisper; }
        if (raw.shout < raw.normal) { anomalies.push('shout<normal'); raw.shout = raw.normal; }
    
        // 锚点下限纪律：任何一档都必须比环境底噪高出 minSnrDb，否则该档在噪声里不可辨
        const minSnr = cfg('voiceCalibration.minAnchorSnrDb', 6);
        const amb = this.ambientFloor();
        if (amb != null) {
          if (raw.whisper < amb + minSnr) { anomalies.push('whisper_below_ambient+snr'); raw.whisper = amb + minSnr; }
          if (raw.normal < raw.whisper) raw.normal = raw.whisper;
          if (raw.shout < raw.normal) raw.shout = raw.normal;
        }
        // 注意：这里**不做** noiseFloorMaxDb 夹取。夹取会把高增益机型的"正常说话"压到
        // 与低增益机型相同的绝对电平，从而抹掉设备差异——那正是绝对阈值方案的老毛病。
        const anchors = {
          whisper: raw.whisper,
          normal: raw.normal,
          shout: raw.shout,
          anomalies,
          ambientDb: amb ?? null,
          capturedAt: this.capturedAt ?? null,
        };
        return anchors;
      }
    
      /** 完整性校验：用于 M0 的"三步采样 UI 原型"验收 */
      validate() {
        const problems = [];
        for (const p of this.prompts) {
          const n = this.collected.get(p)?.length ?? 0;
          if (n === 0) problems.push(`${p}: 未采样`);
          else if (n < this.sampleFrames * 0.5) problems.push(`${p}: 采样不足（${n}/${this.sampleFrames}）`);
        }
        if (this.anchors) {
          const { whisper, normal, shout } = this.anchors;
          const minSep = cfg('voiceCalibration.minAnchorSeparationDb', 6);
          if (normal - whisper < minSep) problems.push('耳语与正常说话间距 ' + (normal - whisper).toFixed(1) + 'dB < ' + minSep + 'dB（太窄：轻声会被判成正常）');
          if (shout - normal < minSep) problems.push('正常与喊叫间距 ' + (shout - normal).toFixed(1) + 'dB < ' + minSep + 'dB（太窄：正常说话会被判成喊叫）');
          if (shout - whisper < minSep * 2) problems.push('三档总跨度仅 ' + (shout - whisper).toFixed(1) + 'dB，麦克风动态范围过小或未按提示发声，建议重新校准');
        } else problems.push('未生成锚点');
        return { ok: problems.length === 0, problems };
      }
    
      toJSON() {
        return {
          anchors: this.anchors,
          samples: Object.fromEntries([...this.collected].map(([k, v]) => [k, v.length])),
        };
      }
    
      static fromJSON(obj) {
        const c = new VoiceCalibrator();
        c.anchors = obj?.anchors ?? null;
        c.done = !!c.anchors;
        return c;
      }
    }
    
    /**
     * 运行时分档器：滚动噪声底 + 峰值追踪 + 滞后 → 相对电平分档。
     */
    class VoiceBandClassifier {
      constructor(opts = {}) {
        const c = cfg('voiceCalibration', {});
        this.anchors = opts.anchors ?? null;
        this.noiseFloorWindowFrames = framesFor(opts.noiseWindowMs ?? c.noiseFloorWindowMs ?? 10000);
        this.noiseFloorPercentile = (opts.noisePercentile ?? c.noiseFloorPercentile ?? 10) / 100;
        this.noiseFastWindowFrames = framesFor(opts.noiseFastWindowMs ?? c.noiseFastWindowMs ?? 2000);
        this.noiseFastPercentile = (opts.noiseFastPercentile ?? c.noiseFastPercentile ?? 5) / 100;
        this.levelWindowFrames = framesFor(opts.levelWindowMs ?? c.levelWindowMs ?? 500);
        this.levelStatistic = opts.levelStatistic ?? c.levelStatistic ?? 'p90';
        this.hysteresisDb = opts.hysteresisDb ?? c.hysteresisDb ?? 2.0;
        // 安全上限：允许超出本人三档范围 ±clampCeil 倍，超出即夹取（仅防极端值，不参与分档语义）
        this.clampCeil = Math.abs(opts.clampCeil ?? c.clampCeil ?? 1.5);
        this.peakBoost = opts.peakBoost ?? c.peakBoost ?? 1.15;
        this.noiseRing = new Ring(this.noiseFloorWindowFrames);
        this.noiseFastRing = new Ring(this.noiseFastWindowFrames);
        this.levelRing = new Ring(this.levelWindowFrames);
        this.bands = null;
        this.currentBand = null;
        if (this.anchors) this.setAnchors(this.anchors);
      }
    
      /**
       * 动态范围归一化（V8 N2 "相对电平"的严格实现）。
       *
       * 物理定义（不依赖任何配置夹取）：
       *    0 = 本人「正常说话」锚点
       *   -1 = 本人「耳语」锚点（下侧除以 normal-whisper）
       *   +1 = 本人「喊叫」锚点（上侧除以 shout-normal）
       * 于是该设备三档锚点恒为 -1/0/+1，边界恒为 -0.5/+0.5——与绝对增益完全无关。
       *
       * 首轮实测的三个反例（全部固化为回归测试）：
       * ① 绝对夹取 ±24dB → 低增益机型喊叫(+16dB)被压平，正常说话被误判为喊叫；
       * ② 沿用旧的对称默认夹取 [0.25,3] → 负下限变成 +0.25，耳语/正常全被抬进"正常"档；
       * ③ 上下两侧共用一个 range → 耳语锚点不再映射到 -1，边界失去物理含义。
       * 结论：必须分段归一化，且安全夹取必须关于 0 对称。
       */
      _normalize(relDb, anchors) {
        const up = Math.max(1, anchors.shout - anchors.normal);
        const down = Math.max(1, anchors.normal - anchors.whisper);
        const v = relDb >= 0 ? relDb / up : relDb / down;
        const ceil = this.clampCeil;
        return Math.min(ceil, Math.max(-ceil, v));
      }
    
      setAnchors(anchors) {
        if (!anchors) return;
        this.anchors = anchors;
        const up = Math.max(1, anchors.shout - anchors.normal);
        const down = Math.max(1, anchors.normal - anchors.whisper);
        this.bands = {
          whisper: { lo: -Infinity, hi: -0.5 },
          normal: { lo: -0.5, hi: 0.5 },
          shout: { lo: 0.5, hi: Infinity },
          boundaries: [-0.5, 0.5],
          rangeDb: { up: round4(up), down: round4(down) },
        };
      }
    
      /**
       * 噪声底：长窗（10s 第10百分位）与快窗（2s 第5百分位）取小。
       * 快窗是关键——玩家一开口，长窗就再也看不到静默帧；快窗保证说话中途仍能估到底噪。
       */
      noiseFloor() {
        const cap = cfg('voiceCalibration.noiseFloorMaxDb', -30);
        const longFloor = this.noiseRing.n ? percentile(this.noiseRing.values(), this.noiseFloorPercentile) : null;
        const fastFloor = this.noiseFastRing.n ? percentile(this.noiseFastRing.values(), this.noiseFastPercentile) : null;
        if (longFloor == null && fastFloor == null) return this.anchors?.ambientDb ?? SILENCE_DBFS;
        const f = Math.min(longFloor ?? Infinity, fastFloor ?? Infinity);
        return Math.min(f, cap);
      }
    
      /** 当前窗口电平：levelStatistic（默认 p90）比 max 抗爆音、比 mean 抗停顿 */
      currentLevel() {
        if (this.levelRing.n === 0) return SILENCE_DBFS;
        const vals = this.levelRing.values();
        if (this.levelStatistic === 'max') return Math.max(...vals);
        if (this.levelStatistic === 'mean') return vals.reduce((a, b) => a + b, 0) / vals.length;
        if (this.levelStatistic === 'median') return median(vals);
        const m = /^p(\d{1,2})$/.exec(this.levelStatistic);
        return m ? percentile(vals, Number(m[1]) / 100) : Math.max(...vals);
      }
    
      /**
       * 喂入一帧，返回分档结果。
       * @param {number} dbfs 当前帧声压级
       * @param {{noiseFloor?:number, anchors?:object}} [opts] 可覆盖（测试与跨机型实验用）
       */
      push(dbfs, opts = {}) {
        const v = Number.isFinite(dbfs) ? dbfs : SILENCE_DBFS;
        this.noiseRing.push(v);
        this.noiseFastRing.push(v);
        this.levelRing.push(v);
        const anchors = opts.anchors ?? this.anchors;
        if (!anchors) return { band: null, reason: 'uncalibrated', levelDb: v };
        let bands = this.bands;
        if (anchors !== this.anchors) {
          const savedA = this.anchors;
          const savedB = this.bands;
          this.setAnchors(anchors);
          bands = this.bands;
          this.anchors = savedA;
          this.bands = savedB;
        }
        return this._classify(v, anchors, bands, opts);
      }
    
      _classify(v, anchors, bands, opts = {}) {
        const nf = opts.noiseFloor ?? this.noiseFloor();
        const ref = anchors.normal;
        const relDb = v - ref;
        const norm = this._normalize(relDb, anchors);
        const floorNorm = this._normalize(nf - ref, anchors);
        // 峰值：本帧与 500ms 窗口统计的较响者，再乘一点余量（比纯窗口统计更贴合"这一声有多响"）
        const stat = opts.peak ?? this.currentLevel();
        const peak = opts.peak ?? Math.max(v, stat) * this.peakBoost;
        const relPeak = this._normalize(peak - ref, anchors);
        const snrDb = round4(relDb - (nf - ref));
        const snrPeakDb = round4(peak - nf);
        const h = this.hysteresisDb / 2 / Math.max(1, anchors.shout - anchors.normal);
    
        // 可辨下限：说话电平不比实测噪声底高出 minRuntimeSnrDb 时，不得宣称任何分档。
        // 用峰值 SNR（而非单帧 SNR）判定——单帧抖动不应让一整句话"消失"。
        const minSnr = opts.minRuntimeSnrDb ?? cfg('voiceCalibration.minRuntimeSnrDb', 6);
        if (opts.peak == null && snrPeakDb < minSnr) {
          const changed = this.currentBand !== 'indistinguishable';
          this.currentBand = null;
          return {
            band: 'indistinguishable',
            bandIndex: -1,
            reason: 'snr_below_min',
            snrDb,
            snrPeakDb,
            minSnrDb: minSnr,
            levelDb: round4(v),
            relDb: round4(relDb),
            levelNorm: round4(norm),
            noiseFloorDb: round4(nf),
            changed,
            confidence: 0,
          };
        }
    
        const [b1, b2] = bands.boundaries;
    
        let band;
        const cur = this.currentBand;
        if (cur === 'whisper') band = relPeak >= b1 + h ? (relPeak >= b2 + h ? 'shout' : 'normal') : 'whisper';
        else if (cur === 'normal') band = relPeak >= b2 + h ? 'shout' : relPeak < b1 - h ? 'whisper' : 'normal';
        else if (cur === 'shout') band = relPeak < b1 - h ? 'whisper' : relPeak < b2 - h ? 'normal' : 'shout';
        else band = relPeak < b1 ? 'whisper' : relPeak < b2 ? 'normal' : 'shout';
    
        // tie-break 纪律：落在边界上取更响的一档（宁可被听见，不可被漏判）
        if (relPeak === b1) band = 'normal';
        if (relPeak === b2) band = 'shout';
    
        const changed = band !== this.currentBand;
        this.currentBand = band;
    
        // 置信度：离最近边界的归一化距离（用于跨机型一致率与校准质量分析）
        const dist = Math.min(Math.abs(relPeak - b1), Math.abs(relPeak - b2));
        return {
          band,
          bandIndex: BAND_IDS.indexOf(band),
          levelDb: round4(v),
          relDb: round4(relDb),
          levelNorm: round4(norm),
          relPeakNorm: round4(relPeak),
          noiseFloorDb: round4(nf),
          floorNorm: round4(floorNorm),
          snrDb: round4(relDb - (nf - ref)),
          boundaryNorm: [round4(b1), round4(b2)],
          dynamicRangeDb: bands.rangeDb ?? null,
          confidence: round4(Math.min(1, dist / 0.35)),
          changed,
        };
      }
    
      reset(keepAnchors = true) {
        this.noiseRing.clear();
        this.noiseFastRing.clear();
        this.levelRing.clear();
        this.currentBand = null;
        if (!keepAnchors) {
          this.anchors = null;
          this.bands = null;
        }
      }
    }
    
    /**
     * 跨机型一致率：同一段录音在三台设备上的分档结果一致率（M0 硬指标 ≥90%）。
     * @param {Record<string, string[]>} perDevice 设备名 → 逐帧分档序列
     */
    function crossDeviceAgreement(perDevice) {
      const devices = Object.keys(perDevice);
      if (devices.length < 2) return { agreement: null, reason: 'need>=2 devices', frames: 0 };
      const n = Math.min(...devices.map((d) => perDevice[d].length));
      if (n === 0) return { agreement: 0, frames: 0 };
      let agree = 0;
      for (let i = 0; i < n; i++) {
        const first = perDevice[devices[0]][i];
        if (devices.every((d) => perDevice[d][i] === first)) agree++;
      }
      return { agreement: round4(agree / n), frames: n, devices: devices.length };
    }
    
    /** 无麦人格包固定强度（天然免疫设备差异，V9 §8） */
    function personaFixedIntensity(personaId) {
      const p = cfg(`personaPacks.${personaId}`, null);
      if (!p) throw new Error(`unknown persona: ${personaId}`);
      return p.fixedIntensity;
    }
    
    /** 分档 → StimulusEvent 强度（走配置表，不硬编码） */
    function bandToStimulus(band, opts = {}) {
      // 不可操作分档（indistinguishable / null）返回 null 而不是抛错：
      // 运行时"说话电平低于噪声底"是常态，抛错会让整帧崩溃（实测在帧 102 抛 unknown band）。
      const key = BAND_SOURCE_KEY[band];
      if (!key) return null;
      const src = cfg(`stimulusSources.${key}`);
      if (!src) return null;
      let intensity = src.intensity;
      if (opts.personaId) {
        const p = cfg(`personaPacks.${opts.personaId}`);
        if (opts.mute) intensity = p.fixedIntensity;
        else intensity = Math.round(intensity * (p.stimulusMultiplier ?? 1));
      }
      // 人耳听觉阈值近似：强度不得低于噪声底推导的可听阈值（避免"喊叫"被判成无声）
      const minAudible = opts.minAudibleIntensity ?? null;
      if (minAudible != null) intensity = Math.max(intensity, minAudible);
      return { sourceKey: key, intensity, radiusM: src.radiusM, type: src.type, globalBroadcast: !!src.globalBroadcast };
    }
    
    const __internals = { clamp, round4, framesFor };
    
    mod.exports = { median, percentile, anchorFromFrames, crossDeviceAgreement, personaFixedIntensity, bandToStimulus, Ring, VoiceCalibrator, VoiceBandClassifier, BAND_IDS, BAND_SOURCE_KEY, SILENCE_DBFS, __internals };
  };
  __tables["__m2"] = function (mod) {
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
    var __ns0 = __req("__m0");
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
    
    mod.exports = { makeStimulus, makeVoiceStimulus, effectiveHearingThreshold, canHear, hearStimulus, stimulusIntensity, movementStimulusKey, stimulusRadiusTable, MONSTER_IDS };
  };
  __tables["__m3"] = function (mod) {
    /**
     * 三怪行为状态机（V9 §7：三段式 巡逻 → 调查 → 追逐 → 回归，失联 12 秒回归）
     *
     * 这是 Host 权威端的确定性状态机：同一条刺激序列 + 同一初态 → 同一状态轨迹（可回归测试）。
     * 本文件不碰 Unity、不碰网络，只吃"每 tick 的输入"吐"每 tick 的输出"。
     */
    var __ns0 = __req("__m0");
    var cfg = __ns0.cfg;
    var __ns1 = __req("__m2");
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
    
    mod.exports = { axisClear, finalRageActive, MonsterBrain, STATES };
  };
  __tables["__m4"] = function (mod) {
    /**
     * Level DSL Builder（V9 §19.2：关卡即数据，运行时拼装，编辑器零参与）
     *
     * 输入：data/levels/*.json（房间矩形 + 门洞 + 道具 + 证据点 + 事件）
     * 输出：编译后的几何契约（轴对齐墙段 / 门 / 套件实例 / 巡逻路点 / 连通图）
     *
     * 三个消费者：
     *  ① web/ 灰盒渲染器（2.5D 射线投射）与碰撞
     *  ② 怪物导航（路点图 A*）与视线/隔音判定
     *  ③ 内容工厂（新地图 = 一份新 JSON，边际成本 ≤40 小时，V9 §14 P1）
     */
    const WALL_SIDES = ['north', 'south', 'west', 'east'];
    
    /**
     * 关卡数据注入点。
     * core/ **不读文件系统**——Node 侧由 tools/node-config.mjs 读取 JSON，浏览器/APK 由页面内联。
     * 打包器第一次运行就暴露过 core 依赖 node:fs 的问题，这条纪律必须保住。
     */
    function installLevel(level) {
      return level;
    }
    
    /** 由注入的数据编译关卡（Node 与浏览器同一条路径） */
    function buildLevelFromData(dsl) {
      return buildLevel(dsl.id, null, dsl);
    }
    
    /** AABB 房间矩形 */
    function rect(room) {
      const [w, , d] = room.size;
      const [x, , z] = room.pos;
      return { x0: x, z0: z, x1: x + w, z1: z + d, w, d };
    }
    
    /**
     * 墙段编译：把房间矩形拆成 4 条边，再按门洞切出缺口。
     * door: { id, wall, offsetM, widthM, locked }  offsetM 从该边起点算起。
     */
    function compileWalls(room) {
      const r = rect(room);
      const doorsOn = (wall) => (room.doors ?? []).filter((d) => d.wall === wall);
      const segs = [];
    
      const cut = (wall, ax, az, bx, bz, length) => {
        const ds = doorsOn(wall)
          .map((d) => {
            const w = d.widthM ?? 1.2;
            const start = Math.max(0, Math.min(length - w, d.offsetM));
            return { ...d, widthM: w, start, end: start + w };
          })
          .sort((a, b) => a.start - b.start);
        const ux = (bx - ax) / (length || 1);
        const uz = (bz - az) / (length || 1);
        let cursor = 0;
        const pointAt = (t) => ({ x: round3(ax + ux * t), z: round3(az + uz * t) });
        for (const d of ds) {
          if (d.start > cursor) segs.push({ room: room.id, wall, a: pointAt(cursor), b: pointAt(d.start) });
          const c = { ...pointAt(d.start), t: d.start };
          const e = { ...pointAt(d.end), t: d.end };
          segs.push({ room: room.id, wall, a: { x: c.x, z: c.z }, b: { x: e.x, z: e.z }, kind: 'door_gap', doorId: d.id });
          cursor = d.end;
        }
        if (cursor < length) segs.push({ room: room.id, wall, a: pointAt(cursor), b: pointAt(length) });
      };
    
      cut('north', r.x0, r.z0, r.x1, r.z0, r.w); // 北：x 递增
      cut('south', r.x0, r.z1, r.x1, r.z1, r.w); // 南：x 递增
      cut('west', r.x0, r.z0, r.x0, r.z1, r.d); // 西：z 递增
      cut('east', r.x1, r.z0, r.x1, r.z1, r.d); // 东：z 递增
      return segs;
    }
    
    /** 单入口点（门洞中心，含朝向法线，用于门状态与视线遮挡） */
    function compileDoors(level) {
      const out = [];
      for (const room of level.rooms) {
        const r = rect(room);
        for (const d of room.doors ?? []) {
          const w = d.widthM ?? 1.2;
          const mid = d.offsetM + w / 2;
          let pos;
          let normal;
          if (d.wall === 'north') { pos = { x: r.x0 + mid, z: r.z0 }; normal = { x: 0, z: -1 }; }
          else if (d.wall === 'south') { pos = { x: r.x0 + mid, z: r.z1 }; normal = { x: 0, z: 1 }; }
          else if (d.wall === 'west') { pos = { x: r.x0, z: r.z0 + mid }; normal = { x: -1, z: 0 }; }
          else { pos = { x: r.x1, z: r.z0 + mid }; normal = { x: 1, z: 0 }; }
          out.push({
            id: d.id,
            room: room.id,
            wall: d.wall,
            widthM: w,
            locked: !!d.locked,
            blocksSight: true,           // 门体遮挡视线
            blocksSound: false,          // 关着的门不隔音（恐怖设计：门挡视线不挡声音）
            open: false,
            pos: { x: round3(pos.x), z: round3(pos.z) },
            normal,
            connectsTo: d.connectsTo ?? null,
          });
        }
      }
      return out;
    }
    
    /** 距离场房间（用于"证据点是否够深""撤离点是否够远"的量化校验） */
    function roomCenter(level, roomId) {
      const room = level.rooms.find((r) => r.id === roomId);
      if (!room) throw new Error(`unknown room: ${roomId}`);
      const r = rect(room);
      return { x: round3((r.x0 + r.x1) / 2), z: round3((r.z0 + r.z1) / 2), rect: r };
    }
    
    function roomAt(level, x, z) {
      for (const room of level.rooms) {
        const r = rect(room);
        if (x >= r.x0 && x <= r.x1 && z >= r.z0 && z <= r.z1) return { id: room.id, zone: room.zone, rect: r, meta: room };
      }
      return null;
    }
    
    /** 门 → 连接的两个空间（按门两侧各探 0.6m 判定），用于连通图 */
    function doorConnections(level, door) {
      const probe = (sign) => roomAt(level, door.pos.x + door.normal.x * sign * 0.6, door.pos.z + door.normal.z * sign * 0.6);
      const a = probe(+1);
      const b = probe(-1);
      return [a?.id ?? null, b?.id ?? null];
    }
    
    /** 连通图：节点=房间，边=门 */
    function buildRoomGraph(level) {
      const nodes = Object.fromEntries(level.rooms.map((r) => [r.id, { id: r.id, zone: r.zone, doors: [] }]));
      const links = [];
      for (const door of compileDoors(level)) {
        const [a, b] = doorConnections(level, door);
        if (!a || !b || a === b) continue;
        nodes[a].doors.push({ to: b, door: door.id });
        nodes[b].doors.push({ to: a, door: door.id });
        links.push({ a, b, door: door.id, locked: door.locked });
      }
      return { nodes, links };
    }
    
    /** 关卡编译入口（数据必须由调用方注入；core 不读文件系统） */
    function buildLevel(levelId, dir, injected) {
      const dsl = injected;
      if (!dsl) throw new Error('buildLevel 需要注入关卡数据（Node 侧用 tools/node-config.mjs 读取）: ' + levelId);
      const walls = dsl.rooms.flatMap(compileWalls);
      const doors = compileDoors(dsl);
      const graph = buildRoomGraph(dsl);
    
      const kitIds = new Set();
      const props = [];
      for (const room of dsl.rooms) {
        for (const p of room.props ?? []) {
          const c = roomCenter(dsl, room.id);
          kitIds.add(p.kit);
          props.push({
            room: room.id,
            kit: p.kit,
            pos: { x: round3(c.rect.x0 + p.pos[0]), z: round3(c.rect.z0 + p.pos[2]) },
            rotY: p.rot ?? 0,
            blocksMovement: p.blocksMovement ?? false,
            stimulus: p.stimulus ?? null,
          });
        }
      }
    
      const evidencePoints = dsl.rooms
        .filter((r) => r.evidencePoint)
        .map((r) => {
          const c = roomCenter(dsl, r.id);
          return { room: r.id, zone: r.zone, pos: { x: c.x, z: c.z }, depthM: round3(distanceFrom(c, spawnPointOf(dsl))) };
        });
    
      return {
        id: dsl.id,
        name: dsl.name,
        meta: dsl.meta ?? {},
        bounds: levelBounds(dsl),
        rooms: dsl.rooms.map((r) => ({ ...r, rect: rect(r) })),
        zones: dsl.zones ?? {},
        walls,
        doors,
        props,
        kitIds: [...kitIds],
        evidencePoints,
        spawns: dsl.spawns ?? {},
        extractionPoints: dsl.extractionPoints ?? [],
        patrolRoutes: dsl.patrolRoutes ?? {},
        events: dsl.events ?? [],
        graph,
        stats: {
          roomCount: dsl.rooms.length,
          wallSegments: walls.length,
          doorCount: doors.length,
          propCount: props.length,
          evidenceCount: evidencePoints.length,
          kitCount: kitIds.size,
        },
      };
    }
    
    function spawnPointOf(dsl) {
      const s = dsl.spawns?.players?.[0] ?? { x: 0, z: 0 };
      return { x: s.x, z: s.z };
    }
    
    function levelBounds(dsl) {
      const rs = dsl.rooms.map(rect);
      return {
        x0: Math.min(...rs.map((r) => r.x0)),
        z0: Math.min(...rs.map((r) => r.z0)),
        x1: Math.max(...rs.map((r) => r.x1)),
        z1: Math.max(...rs.map((r) => r.z1)),
      };
    }
    
    function distanceFrom(a, b) {
      return Math.hypot(a.x - b.x, a.z - b.z);
    }
    
    /**
     * 房间级 A*（怪物导航基线：路点图，非网格）。
     * 低端机同屏寻路代理 ≤2（V9 §7），故用房间图而非逐格 A* 是刻意的性能决策。
     */
    function findRoomPath(level, fromRoomId, toRoomId) {
      const g = level.graph.nodes;
      if (!g[fromRoomId] || !g[toRoomId]) return null;
      if (fromRoomId === toRoomId) return [fromRoomId];
      const open = [{ id: fromRoomId, g: 0, f: 0 }];
      const came = new Map();
      const best = new Map([[fromRoomId, 0]]);
      const h = (id) => distanceFrom(roomCenter(level, id), roomCenter(level, toRoomId));
      while (open.length) {
        open.sort((a, b) => a.f - b.f);
        const cur = open.shift();
        if (cur.id === toRoomId) {
          const path = [cur.id];
          let p = cur.id;
          while (came.has(p)) { p = came.get(p); path.unshift(p); }
          return path;
        }
        for (const e of g[cur.id].doors) {
          const ng = cur.g + 1;
          if (best.has(e.to) && best.get(e.to) <= ng) continue;
          best.set(e.to, ng);
          came.set(e.to, cur.id);
          open.push({ id: e.to, g: ng, f: ng + h(e.to) });
        }
      }
      return null;
    }
    
    /** 路径 → 世界坐标路点序列（经门中心，供实际移动使用） */
    function roomPathToWaypoints(level, path) {
      if (!path) return [];
      const pts = [];
      for (let i = 0; i < path.length - 1; i++) {
        const [a, b] = [path[i], path[i + 1]];
        const link = level.graph.links.find((l) => (l.a === a && l.b === b) || (l.a === b && l.b === a));
        if (link) {
          const door = level.doors.find((d) => d.id === link.door);
          if (door) pts.push({ x: door.pos.x, z: door.pos.z });
        }
      }
      pts.push(roomCenter(level, path[path.length - 1]));
      return pts;
    }
    
    /**
     * 视线判定：轴对齐墙段 + 关门遮挡（供"低语者安静时几乎失明"等语义使用）。
     * 采用分段-线段相交判定，纯 2D、零依赖、确定性。
     */
    function hasLineOfSight(level, a, b, opts = {}) {
      const segs = level.walls.filter((w) => w.kind !== 'door_gap');
      for (const s of segs) if (segmentsIntersect(a, b, s.a, s.b)) return false;
      for (const door of level.doors) {
        if (!door.open && door.blocksSight && pointNearSegment(door.pos, a, b, door.widthM / 2)) return false;
      }
      return true;
    }
    
    function cross(o, a, b) {
      return (a.x - o.x) * (b.z - o.z) - (a.z - o.z) * (b.x - o.x);
    }
    function segmentsIntersect(p1, p2, p3, p4) {
      const d1 = cross(p3, p4, p1);
      const d2 = cross(p3, p4, p2);
      const d3 = cross(p1, p2, p3);
      const d4 = cross(p1, p2, p4);
      if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
      return false;
    }
    function pointNearSegment(p, a, b, tol) {
      const l2 = (b.x - a.x) ** 2 + (b.z - a.z) ** 2;
      if (l2 === 0) return Math.hypot(p.x - a.x, p.z - a.z) <= tol;
      let t = ((p.x - a.x) * (b.x - a.x) + (p.z - a.z) * (b.z - a.z)) / l2;
      t = Math.max(0, Math.min(1, t));
      return Math.hypot(p.x - (a.x + t * (b.x - a.x)), p.z - (a.z + t * (b.z - a.z))) <= tol;
    }
    
    const round3 = (v) => Math.round(v * 1000) / 1000;
    
    mod.exports = { installLevel, buildLevelFromData, compileWalls, compileDoors, roomCenter, roomAt, doorConnections, buildRoomGraph, buildLevel, spawnPointOf, levelBounds, distanceFrom, findRoomPath, roomPathToWaypoints, hasLineOfSight, segmentsIntersect, WALL_SIDES };
  };
  __tables["__m5"] = function (mod) {
    /**
     * 关卡几何编译（渲染与碰撞共用，浏览器/Node 双端零依赖）
     *
     * 关键取舍：**不逐格画墙**。30×30 网格里若每格都画立方体，仅外墙就有上千个面，
     * 移动端 WebGL 立刻掉帧（V9 §14 P2 的"性能过程阈值"正是为拦这类实现）。
     * 做法：把同向相邻的墙格合并成"条带"，内部被房间包围的墙格直接剔除。
     */
    
    const WALL_H = 3.2;
    
    /**
     * 由**房间矩形 + 门格**生成整面墙（推荐的权威路径）。
     *
     * 为什么不用"逐格挖墙 + 合并条带"（首版做法，用户反馈"咋还有一堆条形墙呢"）：
     *  · 逐格合并只在**横向**生效，竖直墙永远合不了 → 65 个单格条带，视觉上就是一堆断续短墙；
     *  · 每格独立生成盒体还会在相邻条的接缝处共面重叠，产生 z-fighting 条纹。
     * 房间本身是矩形，因此一面墙就是**一段连续 span**，只需按门格把它切成若干段——
     * 段数从数百降到几十，墙是整面的，碰撞盒也不再碎。
     */
    function roomWalls(rooms, doorCells, opts = {}) {
      const thickness = opts.thickness ?? 0.22;
      const height = opts.height ?? 3.2;
      const isDoor = (x, z) => doorCells.has(x + ',' + z);
      const spans = [];
    
      /**
       * 墙线位置 = **格边界**（rect 是格索引：第 x 格覆盖世界 [x, x+1)）。
       *   北墙 z = z0 − 0.5，南墙 z = z1 − 0.5，西墙 x = x0 − 0.5，东墙 x = x1 − 0.5。
       * 门格是"落在两空间之间的那一格"，即与房间相邻的那一格：
       *   北墙看 (t, z0−1)、南墙看 (t, z1)、西墙看 (x0−1, t)、东墙看 (x1, t)。
       * 首版用墙线坐标去当格索引查门（19.5、20.5 之类），永远查不到，
       * 于是门格被当成实心墙——这正是"怪卡在门口"和"墙互相穿透看着像一堆条形墙"的根因。
       */
      for (const room of rooms) {
        const { x0, x1, z0, z1 } = room.rect;
        const cut = (orientation, line, from, to, doorCoord) => {
          let runStart = null;
          for (let t = from; t <= to; t++) {
            const cellIsDoor = t < to && isDoor(
              orientation === 'h' ? t : doorCoord,
              orientation === 'h' ? doorCoord : t,
            );
            if (cellIsDoor) {
              if (runStart !== null) { spans.push(mkSpan(orientation, line, runStart, t)); runStart = null; }
            } else if (runStart === null) {
              runStart = t;
            }
          }
          if (runStart !== null) spans.push(mkSpan(orientation, line, runStart, to));
        };
    
        cut('h', z0 - 0.5, x0, x1, z0 - 1);   // 北墙：门格在 z0-1 那一行
        cut('h', z1 - 0.5, x0, x1, z1);       // 南墙：门格在 z1 那一行
        cut('v', x0 - 0.5, z0, z1, x0 - 1);   // 西墙：门格在 x0-1 那一列
        cut('v', x1 - 0.5, z0, z1, x1);       // 东墙：门格在 x1 那一列
      }
      return spans;
    
      function mkSpan(orientation, line, a, b) {
        const len = b - a;
        if (orientation === 'h') {
          return { orientation, len, cx: (a + b) / 2, cz: line, sx: len, sz: thickness, height };
        }
        return { orientation, len, cx: line, cz: (a + b) / 2, sx: thickness, sz: len, height };
      }
    }
    
    /** 内表面剔除：四邻至少一侧是房间地板、且至少一侧不是（即真正暴露在外的墙格） */
    function wallCells(grid) {
      const H = grid.length;
      const W = grid[0].length;
      const isFloor = (x, z) => {
        if (x < 0 || z < 0 || x >= W || z >= H) return false;
        return grid[z][x] !== '#';
      };
      const out = [];
      for (let z = 0; z < H; z++) {
        for (let x = 0; x < W; x++) {
          if (grid[z][x] !== '#') continue;
          const n = [isFloor(x, z - 1), isFloor(x, z + 1), isFloor(x - 1, z), isFloor(x + 1, z)].filter(Boolean).length;
          if (n > 0) out.push({ x, z });
        }
      }
      return out;
    }
    
    /** 合并为条带：先横向连续、再纵向连续 */
    function mergeStrips(cells) {
      const key = (x, z) => x + ',' + z;
      const pool = new Set(cells.map((c) => key(c.x, c.z)));
      const strips = [];
      // 横向
      for (const c of [...cells].sort((a, b) => (a.z - b.z) || (a.x - b.x))) {
        if (!pool.has(key(c.x, c.z))) continue;
        let len = 1;
        while (pool.has(key(c.x + len, c.z))) { pool.delete(key(c.x + len, c.z)); len++; }
        pool.delete(key(c.x, c.z));
        strips.push({ axis: 'x', x0: c.x, z0: c.z, len });
      }
      return strips;
    }
    
    /**
     * 墙条带 → 盒体列表（轴对齐，含 0.26m 厚度）。
     * 每个盒体给出 {cx, cz, sx, sz} 供碰撞与实例化复用。
     */
    function buildWallBoxes(grid, thickness = 0.26) {
      return mergeStrips(wallCells(grid)).map((s) => ({
        x0: s.x0, z0: s.z0, x1: s.x0 + s.len, z1: s.z0 + 1,
        cx: s.x0 + s.len / 2, cz: s.z0 + 0.5,
        sx: s.len, sz: 1,
        h: WALL_H,
        thickness,
      }));
    }
    
    /** 道具 AABB（玩家/怪物碰撞用；渲染另走 GLB 实例） */
    function propBoxes(level) {
      const out = [];
      for (const room of level.rooms) {
        for (const p of room.props ?? []) {
          const wx = room.rect.x0 + p.pos[0];
          const wz = room.rect.z0 + p.pos[2];
          const [sx, sz] = KIT_FOOTPRINT[p.kit] ?? [0.8, 0.6];
          out.push({ kind: p.kit, cx: wx, cz: wz, sx, sz, x0: wx - sx / 2, x1: wx + sx / 2, z0: wz - sz / 2, z1: wz + sz / 2 });
        }
      }
      return out;
    }
    
    /** 套件footprint（米）：与 Blender 套件尺寸一致 */
    const KIT_FOOTPRINT = {
      bed_b: [2.05, 0.95],
      locker_a: [0.92, 0.55],
      cabinet_a: [0.85, 0.45],
      desk_a: [1.3, 0.65],
      gurney_a: [1.95, 0.7],
    };
    
    /** 门格集合（关着的门挡路；锁死门永远挡路） */
    function doorBlockers(level) {
      return (level.doors ?? [])
        .filter((d) => d.locked)
        .map((d) => ({ kind: 'door_locked', cx: d.pos.x, cz: d.pos.z, sx: 1, sz: 1, x0: d.cell[0], x1: d.cell[0] + 1, z0: d.cell[1], z1: d.cell[1] + 1, doorId: d.id }));
    }
    
    /** 玩家胶囊半径内的可站立判定（含房间边界与道具） */
    function makeCollider(level, radius = 0.34) {
      const boxes = [...propBoxes(level), ...doorBlockers(level)];
      const W = level.gridSize.w;
      const H = level.gridSize.h;
      const grid = level.grid;
    
      const passable = (x, z) => {
        const gx = Math.floor(x);
        const gz = Math.floor(z);
        if (gx < 0 || gz < 0 || gx >= W || gz >= H) return false;
        if (grid[gz][gx] === '#') return false;
        return true;
      };
    
      /**
       * 分离轴滑动 + **子步进**。
       * 子步进不是可选优化：单帧位移若一次判定，5m 的位移会被判成"终点合法"而穿过整面墙
       * （首轮实测：从出生点向西 5m 直接穿墙落到门厅）。每步不超过 0.25m。
       */
      const MAX_STEP = 0.25;
      return function resolve(from, delta) {
        let { x, z } = from;
        const steps = Math.max(1, Math.ceil(Math.max(Math.abs(delta.x), Math.abs(delta.z)) / MAX_STEP));
        const dx = delta.x / steps;
        const dz = delta.z / steps;
        const blocked = (px, pz) =>
          !passable(px, pz) || boxes.some((b) => px + radius > b.x0 && px - radius < b.x1 && pz + radius > b.z0 && pz - radius < b.z1);
        for (let i = 0; i < steps; i++) {
          const nx = x + dx;
          if (!blocked(nx + Math.sign(dx) * radius, z)) x = nx;
          const nz = z + dz;
          if (!blocked(x, nz + Math.sign(dz) * radius)) z = nz;
        }
        return { x, z };
      };
    }
    
    /** 供渲染：地板/天花大平面（整层一张，而不是逐格） */
    function floorQuad(level) {
      const { x0, z0, x1, z1 } = levelBoundsOf(level);
      return { x0, z0, x1, z1 };
    }
    
    function levelBoundsOf(level) {
      const rs = level.rooms.map((r) => r.rect);
      return {
        x0: Math.min(...rs.map((r) => r.x0)),
        z0: Math.min(...rs.map((r) => r.z0)),
        x1: Math.max(...rs.map((r) => r.x1)),
        z1: Math.max(...rs.map((r) => r.z1)),
      };
    }
    
    /**
     * 按墙 span 构建碰撞器（与渲染共用同一份几何，避免看得见的墙不挡人）。
     * 首版用逐格墙格生成碰撞盒，且怪物根本没有接碰撞解析——两处叠加就是"怪贴我身上"。
     */
    function makeRoomCollider(level, radius = 0.34, opts = {}) {
      const doorCells = new Set((level.doors ?? []).map((d) => d.cell.join(',')));
      const spans = roomWalls(level.rooms, doorCells, { thickness: opts.thickness ?? 0.22 });
      const boxes = spans.map((sp) => ({
        x0: sp.cx - sp.sx / 2, x1: sp.cx + sp.sx / 2,
        z0: sp.cz - sp.sz / 2, z1: sp.cz + sp.sz / 2,
        kind: 'wall',
      }));
      for (const d of level.doors ?? []) {
        if (!d.locked) continue;
        boxes.push({ x0: d.cell[0], x1: d.cell[0] + 1, z0: d.cell[1], z1: d.cell[1] + 1, kind: 'door_locked' });
      }
      for (const b of propBoxes(level)) boxes.push(b);
      const W = level.gridSize.w;
      const H = level.gridSize.h;
      const MAX_STEP = 0.25;
      const blocked = (x, z) =>
        !(x > 0.3 && z > 0.3 && x < W - 0.3 && z < H - 0.3) ||
        boxes.some((b) => x + radius > b.x0 && x - radius < b.x1 && z + radius > b.z0 && z - radius < b.z1);
      return function resolve(from, delta) {
        let x = from.x;
        let z = from.z;
        const steps = Math.max(1, Math.ceil(Math.max(Math.abs(delta.x), Math.abs(delta.z)) / MAX_STEP));
        const dx = delta.x / steps;
        const dz = delta.z / steps;
        for (let i = 0; i < steps; i++) {
          const nx = x + dx;
          if (!blocked(nx + Math.sign(dx) * radius, z)) x = nx;
          const nz = z + dz;
          if (!blocked(x, nz + Math.sign(dz) * radius)) z = nz;
        }
        return { x, z };
      };
    }
    
    mod.exports = { roomWalls, wallCells, mergeStrips, buildWallBoxes, propBoxes, doorBlockers, makeCollider, floorQuad, levelBoundsOf, makeRoomCollider, KIT_FOOTPRINT };
  };
  __tables["__m6"] = function (mod) {
    /**
     * 极简 WebGL2 渲染器：顶点色 + 单向雾 + 手电光锥叠加。
     *
     * 刻意不做 PBR/阴影：恐怖氛围由"浓雾 + 手电锥形光 + 近黑环境"承担（V9 §11 设计哲学），
     * 这也是低端机 30fps 红线的现实前提（V9 §14 P2）。
     */
    const VS = `#version 300 es
    in vec3 aPos; in vec3 aNrm;
    uniform mat4 uVP; uniform vec3 uOrigin;
    out vec3 vNrm; out float vDist; out vec3 vWorld;
    void main() {
      vWorld = aPos;
      vec4 p = uVP * vec4(aPos, 1.0);
      gl_Position = p;
      vNrm = aNrm;
      vDist = length(aPos.xz - uOrigin.xz);
    }`;
    
    const FS = `#version 300 es
    precision mediump float;
    in vec3 vNrm; in float vDist; in vec3 vWorld;
    uniform vec3 uColor; uniform vec3 uFogColor;
    uniform float uBase;
    uniform float uFogNear; uniform float uFogFar;
    uniform vec3 uLightPos; uniform vec3 uLightDir; uniform float uLightRange; uniform float uLightCos;
    out vec4 outColor;
    void main() {
      // 环境：极暗基底，模拟"只有手电"的场面
      float ambient = uBase * (0.85 + 0.15 * max(dot(normalize(vNrm), vec3(0.0, 1.0, 0.0)), 0.0));
      // 手电：位置衰减 × 锥角衰减 × 法线朝向
      vec3 toFrag = vWorld - uLightPos;
      float dist = length(toFrag);
      vec3 dir = toFrag / max(dist, 0.001);
      float cone = smoothstep(uLightCos, uLightCos + 0.18, dot(dir, normalize(uLightDir)));
      float falloff = clamp(1.0 - dist / uLightRange, 0.0, 1.0);
      float ndl = clamp(dot(normalize(vNrm), -dir), 0.0, 1.0);
      float torch = cone * falloff * falloff * (0.35 + 0.65 * ndl) * 2.35;
      vec3 lit = uColor * (ambient + torch);
      float fog = clamp((vDist - uFogNear) / max(uFogFar - uFogNear, 0.001), 0.0, 1.0);
      outColor = vec4(mix(lit, uFogColor, fog), 1.0);
    }`;
    
    function createRenderer(canvas) {
      const gl = canvas.getContext('webgl2', { antialias: false, alpha: false, powerPreference: 'high-performance' });
      if (!gl) throw new Error('本机浏览器不支持 WebGL2');
    
      function compile(type, src) {
        const sh = gl.createShader(type);
        gl.shaderSource(sh, src);
        gl.compileShader(sh);
        if (!gl.getShaderParameter(sh, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(sh));
        return sh;
      }
      const prog = gl.createProgram();
      gl.attachShader(prog, compile(gl.VERTEX_SHADER, VS));
      gl.attachShader(prog, compile(gl.FRAGMENT_SHADER, FS));
      gl.linkProgram(prog);
      if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(prog));
      gl.useProgram(prog);
    
      const U = {};
      for (const n of ['uVP', 'uOrigin', 'uColor', 'uFogColor', 'uFogNear', 'uFogFar', 'uLightPos', 'uLightDir', 'uLightRange', 'uLightCos', 'uBase']) {
        U[n] = gl.getUniformLocation(prog, n);
      }
      const aPos = gl.getAttribLocation(prog, 'aPos');
      const aNrm = gl.getAttribLocation(prog, 'aNrm');
    
      const meshes = [];
      const stats = { drawCalls: 0, triangles: 0 };
    
      /** 上传一批几何（verts: [x,y,z,nx,ny,nz]*, idx, color） */
      function upload(verts, idx, color, name) {
        const vao = gl.createVertexArray();
        gl.bindVertexArray(vao);
        const vbo = gl.createBuffer();
        gl.bindBuffer(gl.ARRAY_BUFFER, vbo);
        gl.bufferData(gl.ARRAY_BUFFER, verts, gl.STATIC_DRAW);
        gl.enableVertexAttribArray(aPos);
        gl.vertexAttribPointer(aPos, 3, gl.FLOAT, false, 24, 0);
        gl.enableVertexAttribArray(aNrm);
        gl.vertexAttribPointer(aNrm, 3, gl.FLOAT, false, 24, 12);
        const ibo = gl.createBuffer();
        gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, ibo);
        gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, idx, gl.STATIC_DRAW);
        gl.bindVertexArray(null);
        const m = { vao, count: idx.length, color, name };
        meshes.push(m);
        return m;
      }
    
      function draw(mesh, mvp, origin, light, opts = {}) {
        gl.bindVertexArray(mesh.vao);
        if (opts.depthWrite === false) gl.depthMask(false);
        if (opts.additive) { gl.enable(gl.BLEND); gl.blendFunc(gl.SRC_ALPHA, gl.ONE); }
        gl.uniformMatrix4fv(U.uVP, false, mvp);
        gl.uniform3f(U.uOrigin, origin.x, 0, origin.z);
        gl.uniform3f(U.uColor, mesh.color[0], mesh.color[1], mesh.color[2]);
        gl.uniform3f(U.uLightPos, light.pos.x, light.pos.y, light.pos.z);
        gl.uniform3f(U.uLightDir, light.dir.x, light.dir.y, light.dir.z);
        gl.uniform1f(U.uLightRange, light.range);
        gl.uniform1f(U.uLightCos, light.cosOuter);
        gl.uniform1f(U.uBase, mesh.base ?? 0.16);
        const triCount = (mesh.count ?? 0) / 3;
        gl.drawElements(gl.TRIANGLES, mesh.count ?? 0, gl.UNSIGNED_SHORT, 0);
        if (opts.additive) gl.disable(gl.BLEND);
        if (opts.depthWrite === false) gl.depthMask(true);
        stats.drawCalls++;
        stats.triangles += Number.isFinite(triCount) ? triCount : 0;
      }
    
      function beginFrame(w, h, fog) {
        gl.viewport(0, 0, w, h);
        gl.enable(gl.DEPTH_TEST);
        gl.disable(gl.BLEND);
        gl.clearColor(fog.color[0], fog.color[1], fog.color[2], 1);
        gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
        gl.uniform3f(U.uFogColor, fog.color[0], fog.color[1], fog.color[2]);
        gl.uniform1f(U.uFogNear, fog.near);
        gl.uniform1f(U.uFogFar, fog.far);
        stats.drawCalls = 0;
        stats.triangles = 0;
      }
    
      /**
       * 按颜色子范围切分上传。
       * 教训：把跨全图的地板大四边形与墙条带塞进同一个 VBO、再按"颜色分组"整体绘制，
       * 会把地板当成墙画——几何数据必须按绘制单元切分，而不是按颜色索引去猜 offset。
       * 每个 (颜色, 子范围) 组合 = 一个 VAO + 一次 draw call，count 精确对应。
       */
      function uploadRanges(verts, indices, ranges, namePrefix) {
        const out = [];
        const vertexStride = 6;
        for (const r of ranges) {
          const vStart = r.start;
          const vEnd = r.start + r.count;
          const localIdx = [];
          for (const i of indices) if (i >= vStart && i < vEnd) localIdx.push(i - vStart);
          if (localIdx.length === 0) continue;
          const sub = verts.slice(vStart * vertexStride, vEnd * vertexStride);
          const mesh = upload(sub, new Uint16Array(localIdx), r.color, namePrefix + ':' + r.name);
          out.push({ mesh, color: r.color, triangles: localIdx.length / 3 });
        }
        return out;
      }
    
      /**
       * 按颜色合并：把同一颜色的多个子范围拼成一段连续缓冲。
       * 动机：内饰有 169 个实例、每个贡献 1~8 个颜色块，逐块绘制让 draw call 冲到 181，
       * 直接违反 V9 的性能门禁（Draw Call ≤120）。合并后每颜色一次 draw call。
       */
      function uploadMerged(verts, indices, ranges, namePrefix) {
        const byColor = new Map();
        for (const r of ranges) {
          const key = r.color.join(',');
          if (!byColor.has(key)) byColor.set(key, { color: r.color, parts: [] });
          byColor.get(key).parts.push(r);
        }
        const out = [];
        for (const { color, parts } of byColor.values()) {
          const vCount = parts.reduce((a, p) => a + p.count, 0);
          const merged = new Float32Array(vCount * 6);
          const mergedIdx = [];
          let vOffset = 0;
          for (const p of parts) {
            merged.set(verts.subarray(p.start * 6, (p.start + p.count) * 6), vOffset * 6);
            for (const i of indices) {
              if (i >= p.start && i < p.start + p.count) mergedIdx.push(vOffset + (i - p.start));
            }
            vOffset += p.count;
          }
          if (mergedIdx.length === 0) continue;
          out.push({ vao: upload(merged, new Uint16Array(mergedIdx), color, namePrefix + ':' + color.join(',')).vao, count: mergedIdx.length, color });
        }
        return out;
      }
    
      return { gl, upload, uploadRanges, uploadMerged, draw, beginFrame, stats, meshes };
    }
    
    /** 列主序 4×4 透视 + 视角矩阵（避免引入 mat4 库） */
    function makeCamera() {
      return {
        projection(fovDeg, aspect, near, far) {
          const f = 1 / Math.tan((fovDeg * Math.PI) / 360);
          return new Float32Array([f / aspect, 0, 0, 0, 0, f, 0, 0, 0, 0, (far + near) / (near - far), -1, 0, 0, (2 * far * near) / (near - far), 0]);
        },
        view(px, py, pz, yaw, pitch) {
          const cy = Math.cos(yaw), sy = Math.sin(yaw);
          const cp = Math.cos(pitch), sp = Math.sin(pitch);
          // forward/right/up（yaw 绕 Y，pitch 绕右轴）
          const fx = sy * cp, fy = sp, fz = -cy * cp;
          const rx = cy, ry = 0, rz = sy;
          const ux = ry * fz - rz * fy, uy = rz * fx - rx * fz, uz = rx * fy - ry * fx;
          return new Float32Array([
            rx, ux, -fx, 0,
            ry, uy, -fy, 0,
            rz, uz, -fz, 0,
            -(rx * px + ry * py + rz * pz), -(ux * px + uy * py + uz * pz), fx * px + fy * py + fz * pz, 1,
          ]);
        },
        multiply(a, b) {
          const o = new Float32Array(16);
          for (let c = 0; c < 4; c++) {
            for (let r = 0; r < 4; r++) {
              o[c * 4 + r] = a[r] * b[c * 4] + a[4 + r] * b[c * 4 + 1] + a[8 + r] * b[c * 4 + 2] + a[12 + r] * b[c * 4 + 3];
            }
          }
          return o;
        },
      };
    }
    
    mod.exports = { createRenderer, makeCamera };
  };
  __tables["__m7"] = function (mod) {
    /**
     * 极简 GLB 解析器（零依赖）。
     * 只支持本仓库 Blender 导出的子集：单场景、mesh 节点、POSITION/NORMAL/COLOR_0、材质 baseColorFactor。
     * 为什么自己写而不引 three.js：本机无包管理器兜底（V9 §19 零依赖纪律），
     * 且解析产物直接喂给自写渲染器，比引入 600KB 运行时更可控。
     */
    function parseGLB(arrayBuffer) {
      const dv = new DataView(arrayBuffer);
      const magic = dv.getUint32(0, true);
      if (magic !== 0x46546c67) throw new Error('不是 GLB 文件（magic 不匹配）');
      const version = dv.getUint32(4, true);
      if (version !== 2) throw new Error('仅支持 glTF 2.0，实得版本 ' + version);
    
      let offset = 12;
      let json = null;
      let bin = null;
      while (offset < dv.byteLength) {
        const len = dv.getUint32(offset, true);
        const type = dv.getUint32(offset + 4, true);
        const body = arrayBuffer.slice(offset + 8, offset + 8 + len);
        if (type === 0x4e4f534a) json = JSON.parse(new TextDecoder().decode(body));
        else if (type === 0x004e4942) bin = body;
        offset += 8 + len + ((4 - (len % 4)) % 4);
      }
      if (!json) throw new Error('GLB 缺少 JSON chunk');
      return buildScene(json, bin);
    }
    
    const COMP = { 5120: Int8Array, 5121: Uint8Array, 5122: Int16Array, 5123: Uint16Array, 5125: Uint32Array, 5126: Float32Array };
    const NUM = { SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4, MAT4: 16 };
    
    function readAccessor(json, bin, index) {
      const acc = json.accessors[index];
      const view = json.bufferViews[acc.bufferView];
      const TA = COMP[acc.componentType];
      const num = NUM[acc.type];
      const stride = view.byteStride ?? num * TA.BYTES_PER_ELEMENT;
      const base = (view.byteOffset ?? 0) + (acc.byteOffset ?? 0);
      const out = new Float32Array(acc.count * num);
      for (let i = 0; i < acc.count; i++) {
        const dv = new DataView(bin, base + i * stride, num * TA.BYTES_PER_ELEMENT);
        for (let c = 0; c < num; c++) {
          const o = c * TA.BYTES_PER_ELEMENT;
          out[i * num + c] = TA === Float32Array ? dv.getFloat32(o, true)
            : TA === Uint16Array ? dv.getUint16(o, true)
            : TA === Uint8Array ? (acc.normalized ? dv.getUint8(o) / 255 : dv.getUint8(o))
            : TA === Int16Array ? dv.getInt16(o, true)
            : dv.getUint32(o, true);
        }
      }
      return { data: out, num, count: acc.count };
    }
    
    function getColorFactor(json, matIndex) {
      const m = json.materials?.[matIndex];
      const f = m?.pbrMetallicRoughness?.baseColorFactor ?? [0.8, 0.8, 0.8, 1];
      return [f[0], f[1], f[2], f[3] ?? 1];
    }
    
    function buildScene(json, bin) {
      const nodes = [];
      for (const node of json.nodes ?? []) {
        if (node.mesh == null) continue;
        const mesh = json.meshes[node.mesh];
        const prims = mesh.primitives.map((p) => {
          const pos = readAccessor(json, bin, p.attributes.POSITION);
          const nrm = p.attributes.NORMAL != null ? readAccessor(json, bin, p.attributes.NORMAL) : null;
          const idx = p.indices != null ? readAccessor(json, bin, p.indices) : null;
          const color = [0.75, 0.75, 0.75, 1];
          const matIdx = p.material ?? 0;
          const f = getColorFactor(json, matIdx);
          color[0] = f[0]; color[1] = f[1]; color[2] = f[2]; color[3] = f[3];
          // 顶点数组打平为 [x,y,z, nx,ny,nz]
          const vcount = pos.count;
          const verts = new Float32Array(vcount * 6);
          for (let i = 0; i < vcount; i++) {
            verts[i * 6] = pos.data[i * 3];
            verts[i * 6 + 1] = pos.data[i * 3 + 1];
            verts[i * 6 + 2] = pos.data[i * 3 + 2];
            verts[i * 6 + 3] = nrm ? nrm.data[i * 3] : 0;
            verts[i * 6 + 4] = nrm ? nrm.data[i * 3 + 1] : 1;
            verts[i * 6 + 5] = nrm ? nrm.data[i * 3 + 2] : 0;
          }
          const indices = idx ? new Uint16Array(idx.data) : new Uint16Array(vcount).map((_, i) => i);
          return { verts, indices, color, materialName: json.materials?.[matIdx]?.name ?? ('mat' + matIdx) };
        });
        nodes.push({ name: node.name ?? mesh.name, primitives: prims });
      }
      return {
        nodes,
        byName: Object.fromEntries(nodes.map((n) => [n.name, n])),
        materials: (json.materials ?? []).map((m) => ({ name: m.name, color: getColorFactor(json, json.materials.indexOf(m)) })),
        stats: { nodeCount: nodes.length, primitiveCount: nodes.reduce((a, n) => a + n.primitives.length, 0) },
      };
    }
    
    mod.exports = { parseGLB };
  };
  __tables["__m8"] = function (mod) {
    /**
     * 世界装配：Level DSL + Blender 套件 + 共享几何 → 可渲染网格与碰撞体。
     * V9 §19.1 C4「烘焙零编辑器」：没有 NavMesh 烘焙、没有 lightmap，全部运行时生成。
     */
    var __ns0 = __req("__m7");
    var parseGLB = __ns0.parseGLB;
    var __ns1 = __req("__m5");
    var roomWalls = __ns1.roomWalls,
        propBoxes = __ns1.propBoxes,
        KIT_FOOTPRINT = __ns1.KIT_FOOTPRINT;
    
    const WALL_COLORS = {
      mat_bone: [0.847, 0.812, 0.733],
      mat_soot: [0.055, 0.051, 0.047],
      mat_ink: [0.102, 0.102, 0.102],
      mat_rust: [0.431, 0.290, 0.184],
      mat_mold: [0.361, 0.549, 0.431],
      mat_blood: [0.545, 0.118, 0.118],
      mat_paper: [0.941, 0.902, 0.824],
    };
    
    /** 由关卡网格生成墙/地板/天花板的大网格（顶点色，避免材质切换） */
    function buildStructure(level) {
      const grid = level.grid;
      const W = level.gridSize.w;
      const H = level.gridSize.h;
      // 按房间矩形生成整面墙（含门洞），不再逐格拼条带——
      // 逐格方案只在横向合并，竖直墙永远碎成单格，视觉上就是一堆断续短墙。
      const doorCells = new Set((level.doors ?? []).map((d) => d.cell.join(',')));
      const wallSpans = roomWalls(level.rooms, doorCells, { thickness: 0.22, height: 3.2 });
      const verts = [];
      const idx = [];
    
      const quad = (a, b, c, d, n) => {
        const base = verts.length / 6;
        for (const p of [a, b, c, d]) verts.push(p[0], p[1], p[2], n[0], n[1], n[2]);
        idx.push(base, base + 1, base + 2, base, base + 2, base + 3);
      };
      const boxes = (cx, cz, sx, sz, y0, y1, colorIdx) => {
        const x0 = cx - sx / 2, x1 = cx + sx / 2, z0 = cz - sz / 2, z1 = cz + sz / 2;
        // 四个侧面
        quad([x0, y0, z0], [x1, y0, z0], [x1, y1, z0], [x0, y1, z0], [0, 0, -1]);
        quad([x1, y0, z1], [x0, y0, z1], [x0, y1, z1], [x1, y1, z1], [0, 0, 1]);
        quad([x0, y0, z1], [x0, y0, z0], [x0, y1, z0], [x0, y1, z1], [-1, 0, 0]);
        quad([x1, y0, z0], [x1, y0, z1], [x1, y1, z1], [x1, y1, z0], [1, 0, 0]);
        return base0(colorIdx);
      };
      const colorRanges = [];
      const base0 = (c) => { colorRanges[c] = colorRanges[c] ?? [verts.length / 6, 0]; return c; };
    
      // 墙条带（按 y 从地到顶）
      const before = verts.length / 6;
      for (const sp of wallSpans) {
        const hx = sp.cx - sp.sx / 2, hx2 = sp.cx + sp.sx / 2;
        const hz = sp.cz - sp.sz / 2, hz2 = sp.cz + sp.sz / 2;
        const h = sp.height;
        quad([hx, 0, hz], [hx2, 0, hz], [hx2, h, hz], [hx, h, hz], [0, 0, -1]);
        quad([hx2, 0, hz2], [hx, 0, hz2], [hx, h, hz2], [hx2, h, hz2], [0, 0, 1]);
        quad([hx, 0, hz2], [hx, 0, hz], [hx, h, hz], [hx, h, hz2], [-1, 0, 0]);
        quad([hx2, 0, hz], [hx2, 0, hz2], [hx2, h, hz2], [hx2, h, hz], [1, 0, 0]);
      }
      colorRanges.push({ name: 'walls', start: before, count: verts.length / 6 - before, color: WALL_COLORS.mat_bone });
    
      /**
       * 地板与天花：按关卡产物里的 floorRects **逐块**生成，而不是一张覆盖全图的 30×30 大平面。
       * 首版用单张跨 30m 的四边形，两个后果（用户反馈"天花板与地板没有"）：
       *  ① 大平面盖在实心岩体上（可走区只占网格一小部分）；
       *  ② 近平面裁剪下整片消失——抬头/低头看时地面就没了。
       * 逐块还有个硬收益：每块与房间一一对应，证据点/线路布置有落点。
       */
      const rects = level.floorRects ?? [{ x0: 0, z0: 0, x1: W, z1: H }];
      const fBefore = verts.length / 6;
      for (const r of rects) {
        quad([r.x0, 0, r.z0], [r.x0, 0, r.z1], [r.x1, 0, r.z1], [r.x1, 0, r.z0], [0, 1, 0]);
      }
      colorRanges.push({ name: 'floor', start: fBefore, count: verts.length / 6 - fBefore, color: WALL_COLORS.mat_soot });
    
      const cBefore = verts.length / 6;
      for (const r of rects) {
        quad([r.x0, 3.2, r.z0], [r.x1, 3.2, r.z0], [r.x1, 3.2, r.z1], [r.x0, 3.2, r.z1], [0, -1, 0]);
      }
      colorRanges.push({ name: 'ceiling', start: cBefore, count: verts.length / 6 - cBefore, color: WALL_COLORS.mat_ink });
    
      return {
        verts: new Float32Array(verts),
        indices: new Uint16Array(idx),
        colorRanges,
        wallSpans,
        spanCount: wallSpans.length,
      };
    }
    
    /** 套件实例放置（位置来自关卡 JSON 的 room.props） */
    function kitInstances(level) {
      const out = [];
      for (const room of level.rooms) {
        for (const p of room.props ?? []) {
          out.push({
            kit: p.kit,
            x: room.rect.x0 + p.pos[0],
            z: room.rect.z0 + p.pos[2],
            rot: ((p.rot ?? 0) * Math.PI) / 180,
          });
        }
      }
      for (const e of level.evidencePoints) out.push({ kit: 'evidence_case', x: e.pos.x, z: e.pos.z, rot: 0, evidenceId: e.id });
      for (const e of level.extractionPoints) out.push({ kit: e.id === 'deep' ? 'extract_deep' : 'extract_standard', x: e.pos.x, z: e.pos.z, rot: 0, extractionId: e.id });
      // 门：门格中心即门洞中心；绕 Y 轴旋转让门框横向贴合门洞。
      // 门框由 jamb_l/jamb_r/lintel 三件构成（总宽 1.0m，与门格同宽），
      // 首版是一个 1.0×0.26×2.35 的实心块，塞进门洞后从正面看就是一根柱子，把门堵死。
      for (const d of level.doors) {
        const rot = d.orientation === 'ns' ? 0 : Math.PI / 2;
        const variant = d.locked ? 'door_locked' : 'door_frame';
        out.push({ kit: variant + ':jamb_l', x: d.pos.x, z: d.pos.z, rot, doorId: d.id });
        out.push({ kit: variant + ':jamb_r', x: d.pos.x, z: d.pos.z, rot, doorId: d.id });
        if (!d.locked) {
          out.push({ kit: 'door_frame:lintel', x: d.pos.x, z: d.pos.z, rot, doorId: d.id });
          out.push({ kit: 'door_frame:panel', x: d.pos.x, z: d.pos.z, rot, doorId: d.id });
          out.push({ kit: 'door_frame:handle', x: d.pos.x, z: d.pos.z, rot, doorId: d.id });
        } else {
          out.push({ kit: 'door_locked:bar', x: d.pos.x, z: d.pos.z, rot, doorId: d.id });
        }
      }
    
      // 线索与可交互物：证据点带发光柱（远处可见），拾取物按固定种子分布
      const rand = (() => { let s = 424242; return () => ((s = (s * 1664525 + 1013904223) >>> 0) / 4294967296); })();
      for (const e of level.evidencePoints) {
        out.push({ kit: 'evidence_case', x: e.pos.x, z: e.pos.z, rot: 0, evidenceId: e.id });
        out.push({ kit: 'evidence_beacon', x: e.pos.x, z: e.pos.z, rot: 0, evidenceId: e.id, beacon: true });
      }
      const PICKUPS = ['battery_pickup', 'flare_pickup', 'sedative_pickup'];
      let idx = 0;
      for (const room of level.rooms) {
        if (room.lightZone === 'safe') continue;              // 安全区不放补给
        if (room.props.length === 0) continue;
        const kind = PICKUPS[idx++ % PICKUPS.length];
        const anchor = room.props[0];
        out.push({
          kit: kind,
          x: room.rect.x0 + anchor.pos[0] + 0.9,
          z: room.rect.z0 + anchor.pos[2] - 0.9,
          rot: 0,
          pickup: kind.replace('_pickup', ''),
        });
      }
      // 配电箱：每个区一台（切换灯光）
      for (const room of level.rooms.filter((r) => r.id === 'corridor_main' || r.id === 'ward_hall' || r.id === 'morgue')) {
        out.push({ kit: 'breaker_light', x: room.rect.x0 + 1.5, z: room.rect.z0 + 1.5, rot: 0, breaker: room.zone });
      }
      return out;
    }
    
    /** 把 GLB 套件网格按实例变换烘焙进一个静态缓冲（一次 draw call 画完所有道具） */
    function bakeInstances(glb, instances) {
      const verts = [];
      const idx = [];
      const colorRanges = [];
      const missing = new Set();
      for (const inst of instances) {
        // 套件名支持子件选择：'door_frame:jamb_l' → GLB 节点 'kit_door_frame_jamb_l'
        // （门由门柱/上槛/门扇/把手四件构成，不能再当单个实心块）
        const [base, part] = String(inst.kit).split(':');
        const node = part
          ? (glb.byName['kit_' + base + '_' + part] ?? glb.byName['kit_door_' + part])
          : (glb.byName['kit_' + base] ?? glb.byName['kit_' + base + '_a']);
        if (!node) { missing.add(inst.kit); continue; }
        const cos = Math.cos(inst.rot), sin = Math.sin(inst.rot);
        for (const prim of node.primitives) {
          const base = verts.length / 6;
          for (let i = 0; i < prim.verts.length; i += 6) {
            const x = prim.verts[i], y = prim.verts[i + 1], z = prim.verts[i + 2];
            const nx = prim.verts[i + 3], ny = prim.verts[i + 4], nz = prim.verts[i + 5];
            verts.push(inst.x + x * cos - z * sin, y, inst.z + x * sin + z * cos,
                       nx * cos - nz * sin, ny, nx * sin + nz * cos);
          }
          for (const v of prim.indices) idx.push(base + v);
          colorRanges.push({ name: prim.materialName, start: base, count: prim.verts.length / 6, color: prim.color.slice(0, 3) });
        }
      }
      return { verts: new Float32Array(verts), indices: new Uint16Array(idx), colorRanges, missing: [...missing] };
    }
    
    /** 怪体烘焙：把三怪的部件合成一个可变换的网格（每帧按位置重放） */
    function bakeMonster(glb, monsterId) {
      const prefix = { stitcher: 'kit_m_stitcher', whisperer: 'kit_m_whisperer', coroner: 'kit_m_coroner' }[monsterId];
      const parts = glb.nodes.filter((n) => n.name.startsWith(prefix));
      const verts = [];
      const idx = [];
      const colorRanges = [];
      for (const node of parts) {
        for (const prim of node.primitives) {
          const base = verts.length / 6;
          for (let i = 0; i < prim.verts.length; i += 6) {
            verts.push(prim.verts[i], prim.verts[i + 1], prim.verts[i + 2], prim.verts[i + 3], prim.verts[i + 4], prim.verts[i + 5]);
          }
          for (const v of prim.indices) idx.push(base + v);
          colorRanges.push({ name: prim.materialName, start: base, count: prim.verts.length / 6, color: prim.color.slice(0, 3) });
        }
      }
      return { verts: new Float32Array(verts), indices: new Uint16Array(idx), colorRanges, partCount: parts.length };
    }
    
    mod.exports = { buildStructure, kitInstances, bakeInstances, bakeMonster, WALL_COLORS };
  };
  __tables["__m9"] = function (mod) {
    /**
     * 输入层：键盘 + 触屏 + 桌面鼠标。
     *
     * 首版有一个致命缺陷（用户报告"能转视角但无法移动"）：
     * `consume()` 每帧调用 `recomputeKeys()`，而它**无条件用键盘状态覆盖** forward/strafe。
     * 触屏刚在 pointermove 里设好的移动值，下一帧就被清成 0 —— 于是永远无法移动。
     * 正确做法：**键盘与触屏各存一份状态**，输出时按绝对值取大者合并，互不覆盖。
     *
     * 触屏布局：左半屏拖动 = 移动轴；右半屏拖动 = 视角。
     * 桌面：WASD/方向键移动 + 鼠标拖动转视角；点击画面锁定指针后可直接推鼠标转视角。
     */
    function createInput(canvas, overlay) {
      const keys = { forward: 0, strafe: 0, run: false, crouch: false };
      const touchAxes = { forward: 0, strafe: 0, run: false, crouch: false };
      const look = { dx: 0, dy: 0 };
      const pointers = new Map();
      let wantPointerLock = false;
    
      const pick = (a, b) => (Math.abs(a) >= Math.abs(b) ? a : b);
      const merge = () => ({
        forward: pick(keys.forward, touchAxes.forward),
        strafe: pick(keys.strafe, touchAxes.strafe),
        run: keys.run || touchAxes.run,
        crouch: keys.crouch,
      });
    
      // ── 键盘 ──────────────────────────────────────────────
      const onKey = (e, down) => {
        const k = (e.key ?? '').toLowerCase();
        switch (k) {
          case 'w': case 'arrowup': e.preventDefault(); keys.forward = down ? 1 : 0; break;
          case 's': case 'arrowdown': e.preventDefault(); keys.forward = down ? -1 : 0; break;
          case 'd': case 'arrowright': e.preventDefault(); keys.strafe = down ? 1 : 0; break;
          case 'a': case 'arrowleft': e.preventDefault(); keys.strafe = down ? -1 : 0; break;
          case 'shift': keys.run = down; break;
          case 'c': case 'control': keys.crouch = down; break;
          default: break;
        }
      };
      window.addEventListener('keydown', (e) => onKey(e, true));
      window.addEventListener('keyup', (e) => onKey(e, false));
      window.addEventListener('blur', () => { keys.forward = 0; keys.strafe = 0; keys.run = false; keys.crouch = false; });
    
      // ── 指针（触屏 + 鼠标）─────────────────────────────────
      // 判定"是不是触摸"不能只看 pointerType：部分 WebView 的合成事件 pointerType 为空，
      // 那会把触摸当鼠标处理 → 玩家无法移动（正是用户报的故障）。设备支持触摸就按触摸分区。
      const deviceHasTouch = typeof window !== 'undefined' && ('ontouchstart' in window || (navigator.maxTouchPoints ?? 0) > 0);
      const isTouchEvent = (e) => e.pointerType === 'touch' || e.pointerType === 'pen' || (!e.pointerType && deviceHasTouch);
    
      canvas.addEventListener('pointerdown', (e) => {
        try { canvas.setPointerCapture(e.pointerId); } catch { /* 部分 WebView 不支持，忽略 */ }
        const touch = isTouchEvent(e);
        pointers.set(e.pointerId, {
          x0: e.clientX, y0: e.clientY, x: e.clientX, y: e.clientY,
          // 触屏按屏幕中线分区；鼠标一律当视角（移动靠键盘）
          side: touch && e.clientX < window.innerWidth / 2 ? 'move' : 'look',
        });
        if (!touch) wantPointerLock = true;
      });
    
      canvas.addEventListener('pointermove', (e) => {
        const p = pointers.get(e.pointerId);
        if (!p) return;
        // 指针锁定状态下用 movementX/Y（鼠标持续推着转视角）
        const locked = document.pointerLockElement === canvas;
        const dx = locked ? (e.movementX ?? 0) : e.clientX - p.x;
        const dy = locked ? (e.movementY ?? 0) : e.clientY - p.y;
    
        if (p.side === 'move') {
          const R = 70; // 触屏摇杆半径（px）
          const ox = e.clientX - p.x0;
          const oy = e.clientY - p.y0;
          touchAxes.strafe = Math.max(-1, Math.min(1, ox / R));
          touchAxes.forward = Math.max(-1, Math.min(1, -oy / R));
          touchAxes.run = Math.hypot(ox, oy) > R * 1.35;
        } else {
          look.dx += dx;
          look.dy += dy;
        }
        p.x = e.clientX;
        p.y = e.clientY;
      });
    
      const endPointer = (e) => {
        const p = pointers.get(e.pointerId);
        if (p && p.side === 'move') { touchAxes.forward = 0; touchAxes.strafe = 0; touchAxes.run = false; }
        pointers.delete(e.pointerId);
      };
      canvas.addEventListener('pointerup', endPointer);
      canvas.addEventListener('pointercancel', endPointer);
      canvas.addEventListener('pointerleave', endPointer);
      canvas.addEventListener('contextmenu', (e) => e.preventDefault());
    
      canvas.addEventListener('click', () => {
        if (wantPointerLock && typeof canvas.requestPointerLock === 'function' && document.pointerLockElement !== canvas) {
          const r = canvas.requestPointerLock();
          if (r && typeof r.catch === 'function') r.catch(() => {});
        }
      });
    
      /** 每帧取一次并清空累积视角位移 */
      function consume() {
        const axes = merge();
        const out = { ...axes, look: { dx: look.dx, dy: look.dy } };
        look.dx = 0;
        look.dy = 0;
        return out;
      }
    
      return { consume, get hasTouch() { return 'ontouchstart' in window; }, debug: { keys, touchAxes } };
    }
    
    mod.exports = { createInput };
  };
  __tables["__m10"] = function (mod) {
    /**
     * 声纹采集（浏览器 WebAudio 等价验证，V8 N1 的替代路径）
     *
     * 为什么要写成可替换件：本机没有 Unity/Vivox，真正的采集源待 Unity 侧决定
     * （V8 N1 优先级 ① Vivox 本地能量回调 ② external audio input ③ 轮盘降级）。
     * 这里用 getUserMedia + AnalyserNode 走**同一条判定链**（core/src/voiceprint.mjs），
     * 因此校准参数与分档语义在换实现后依然可比——这正是 V9 §19 解耦架构的用处。
     *
     * 关键：必须关闭浏览器的自动增益/降噪/回声消除，否则 AGC 会把"耳语"拉成"正常"，
     * 跨机型一致率实验就失去意义（同一句悄悄话在两台手机上必须是两个绝对电平）。
     */
    const FRAME_MS = 50;
    
    async function createVoiceInput(config) {
      const state = { db: -100, ready: false, error: null, status: 'init' };
      let analyser = null;
      let buf = null;
      let lastAt = 0;
    
      async function init() {
        if (!navigator.mediaDevices?.getUserMedia) throw new Error('本机浏览器无 getUserMedia');
        const stream = await navigator.mediaDevices.getUserMedia({
          audio: {
            echoCancellation: false,
            noiseSuppression: false,
            autoGainControl: false,
            channelCount: 1,
          },
        });
        const ctx = new (window.AudioContext ?? window.webkitAudioContext)();
        if (ctx.state === 'suspended') await ctx.resume();
        const src = ctx.createMediaStreamSource(stream);
        analyser = ctx.createAnalyser();
        analyser.fftSize = 2048;
        analyser.smoothingTimeConstant = 0.15;
        src.connect(analyser);
        buf = new Float32Array(analyser.fftSize);
        state.ready = true;
        state.status = 'live';
        state.context = ctx;
        state.stream = stream;
        await ctx.resume();
        return state;
      }
    
      /** 帧级 dBFS：50ms 内取最新一段时域样本的 RMS，转 dBFS（满量程参考 1.0） */
      function readDb() {
        if (!analyser) return -100;
        const now = performance.now();
        if (now - lastAt < FRAME_MS * 0.5) return state.db;
        lastAt = now;
        analyser.getFloatTimeDomainData(buf);
        let sum = 0;
        for (let i = 0; i < buf.length; i++) sum += buf[i] * buf[i];
        const rms = Math.sqrt(sum / buf.length);
        state.db = rms > 1e-7 ? 20 * Math.log10(rms) : -100;
        return state.db;
      }
    
      try {
        await init();
      } catch (err) {
        state.error = String(err.message ?? err);
        state.status = 'denied';
      }
    
      return { state, readDb, frameMs: FRAME_MS };
    }
    
    mod.exports = { createVoiceInput };
  };
  __tables["__m11"] = function (mod) {
    /**
     * HUD（V9 §11 设计系统）：全部代码构建，零编辑器（§19.1 C2），Token 直接落为样式。
     * 信息层级：Layer0 致命（怪物仇恨闪烁）/ Layer1 战术（理智环·轮盘·声纹）/ Layer2 状态（电量·证据）
     */
    function createHud(config, tokens) {
      const C = tokens.color;
      const el = document.getElementById('hud');
      const css = (o) => Object.entries(o).map(([k, v]) => k + ':' + v).join(';');
    
      el.insertAdjacentHTML('beforeend', `
        <div id="top" style="${css({ position: 'absolute', top: '0', left: '0', right: '0', display: 'flex', gap: '10px', padding: '10px 12px', alignItems: 'flex-start', pointerEvents: 'none' })}">
          <div id="sanity" style="${css({ background: 'rgba(8,8,8,0.55)', border: '1px solid ' + C.hudDim, padding: '8px 10px', minWidth: '150px' })}">
            <div style="${css({ color: C.hudDim, fontSize: '10px', letterSpacing: '0.18em' })}">理智 SANITY</div>
            <div id="sanityBar" style="${css({ height: '8px', background: '#201d1a', margin: '6px 0 3px' })}"><div id="sanityFill" style="${css({ height: '100%', width: '100%', background: C.mold })}"></div></div>
            <div id="sanityText" style="${css({ color: C.paper, fontSize: '11px' })}">镇定 100</div>
          </div>
          <div id="status" style="${css({ background: 'rgba(8,8,8,0.55)', border: '1px solid ' + C.hudDim, padding: '8px 10px', color: C.paper, fontSize: '11px', lineHeight: '1.6' })}">
            <div>手电 <span id="battery">120</span>s</div>
            <div>证据 <span id="evidence">0</span>/<span id="evidenceTotal">5</span></div>
            <div>用时 <span id="clock">0:00</span></div>
          </div>
          <div style="flex:1"></div>
          <div id="mic" style="${css({ background: 'rgba(8,8,8,0.55)', border: '1px solid ' + C.hudDim, padding: '8px 10px', minWidth: '186px', color: C.paper, fontSize: '11px' })}">
            <div style="${css({ color: C.hudDim, fontSize: '10px', letterSpacing: '0.18em' })}">声纹 VOICEPRINT</div>
            <div id="micDb" style="${css({ fontFamily: 'monospace', fontSize: '12px' })}">-- dBFS</div>
            <div id="micBand" style="${css({ fontWeight: '700', letterSpacing: '0.08em' })}">未校准</div>
            <div id="micBar" style="${css({ height: '6px', background: '#201d1a', marginTop: '5px', position: 'relative' })}"><div id="micFill" style="${css({ height: '100%', width: '0%', background: C.signal })}"></div></div>
          </div>
        </div>
    
        <div id="log" style="${css({ position: 'absolute', left: '12px', bottom: '96px', width: '300px', color: C.paper, fontSize: '11px', lineHeight: '1.5', textShadow: '0 0 6px #000', pointerEvents: 'none' })}"></div>
    
        <div id="minimapWrap" style="${css({ position: 'absolute', right: '10px', bottom: '10px', border: '1px solid ' + C.hudDim, background: 'rgba(8,8,8,0.6)' })}">
          <canvas id="minimap" width="240" height="240" style="display:block"></canvas>
        </div>
    
        <div id="hint" style="${css({ position: 'absolute', top: '50%', left: '50%', transform: 'translate(-50%,-50%)', color: C.blood, fontSize: '30px', fontWeight: '700', letterSpacing: '0.2em', opacity: '0', transition: 'opacity 160ms', textShadow: '0 0 20px #000', pointerEvents: 'none' })}">它听见你了</div>
    
        <div id="crosshair" style="${css({ position: 'absolute', top: '50%', left: '50%', width: '3px', height: '3px', margin: '-1.5px 0 0 -1.5px', background: C.hudDim, pointerEvents: 'none' })}"></div>
    
        <div id="calib" style="${css({ position: 'absolute', inset: '0', background: 'rgba(6,6,7,0.86)', display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', color: C.paper, textAlign: 'center', padding: '24px' })}">
          <div style="${css({ fontSize: '11px', letterSpacing: '0.24em', color: C.hudDim })}">第 0 局 · 声纹校准</div>
          <div id="calibTitle" style="${css({ fontSize: '26px', margin: '14px 0 6px', fontWeight: '700' })}">保持安静</div>
          <div id="calibDesc" style="${css({ fontSize: '13px', color: C.bone, maxWidth: '420px', lineHeight: '1.7' })}">正在采集环境底噪——这一步决定"多小声才算耳语"。</div>
          <div id="calibBar" style="${css({ width: '320px', height: '6px', background: '#201d1a', marginTop: '18px' })}"><div id="calibFill" style="${css({ height: '100%', width: '0%', background: C.signal })}"></div></div>
          <div id="calibLive" style="${css({ fontFamily: 'monospace', fontSize: '12px', marginTop: '10px', color: C.hudDim })}">-- dBFS</div>
          <div id="calibAnchors" style="${css({ marginTop: '16px', fontSize: '11px', color: C.bone, fontFamily: 'monospace' })}"></div>
          <button id="calibSkip" style="${css({ marginTop: '20px', background: 'transparent', color: C.hudDim, border: '1px solid ' + C.hudDim, padding: '8px 14px', fontSize: '12px', letterSpacing: '0.1em' })}">跳过校准（用无麦人格包固定强度）</button>
        </div>
    
        <div id="end" style="${css({ position: 'absolute', inset: '0', background: 'rgba(6,6,7,0.9)', display: 'none', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', color: C.paper, textAlign: 'center', padding: '24px' })}">
          <div id="endTitle" style="${css({ fontSize: '30px', fontWeight: '700', letterSpacing: '0.16em' })}"></div>
          <div id="endBody" style="${css({ marginTop: '14px', fontSize: '13px', lineHeight: '1.8', color: C.bone, maxWidth: '460px' })}"></div>
          <button id="again" style="${css({ marginTop: '22px', background: 'transparent', color: C.paper, border: '1px solid ' + C.hudDim, padding: '10px 18px', fontSize: '13px', letterSpacing: '0.1em' })}">再来一局</button>
        </div>
      `);
    
      const missing = [];
      const $ = (id) => {
        const el = document.getElementById(id);
        if (!el) missing.push(id);
        return el;
      };
      const logLines = [];
      const sanityBands = config.sanity.bands;
    
      $('calibSkip')?.addEventListener('click', () => window.dispatchEvent(new Event('whisper:skip-calibration')));
      $('again')?.addEventListener('click', () => location.reload());
      if ($('evidenceTotal')) $('evidenceTotal').textContent = String(config.evidenceTotal ?? 5);
    
      const phases = {
        ambient: ['保持安静', '正在采集环境底噪——这一步决定"多小声才算耳语"。'],
        whisper: ['用耳语说一句话', '像在队友耳边说悄悄话那样，持续到进度条走完。'],
        normal: ['用平常音量说话', '正常聊天音量，不要刻意压低也不要喊。'],
        shout: ['喊一声', '放开音量喊——受惊尖叫同样计费，所以要先量出你的上限。'],
        done: ['校准完成', ''],
      };
    
      function setPhase(phase, calibrator) {
        if (!calibrator && !$('calib')) return;
        const box = $('calib');
        if (phase === 'done') { box.style.display = 'none'; return; }
        box.style.display = 'flex';
        const [title, desc] = phases[phase] ?? ['', ''];
        $('calibTitle').textContent = title;
        $('calibDesc').textContent = desc;
        const a = calibrator?.anchors;
        $('calibAnchors').textContent = a
          ? '耳语 ' + a.whisper.toFixed(1) + ' / 正常 ' + a.normal.toFixed(1) + ' / 喊叫 ' + a.shout.toFixed(1) + ' dBFS　底噪 ' + (a.ambientDb ?? 0).toFixed(1)
          : '';
      }
    
      function setCalibProgress(frac) {
        $('calibFill').style.width = Math.round(Math.max(0, Math.min(1, frac)) * 100) + '%';
      }
    
      function flashHeard(id, det) {
        const names = { stitcher: '缝匠', whisperer: '低语者', coroner: '收殓人' };
        log('【' + names[id] + ' 听见了】距离 ' + det.distanceM.toFixed(1) + 'm（阈值 ' + det.effectiveThreshold.toFixed(1) + '）');
        const hint = $('hint');
        hint.textContent = names[id] + '听见你了';
        hint.style.opacity = '1';
        clearTimeout(hint._t);
        hint._t = setTimeout(() => { hint.style.opacity = '0'; }, 1200);
      }
    
      function log(msg) {
        logLines.push(msg);
        if (logLines.length > 6) logLines.shift();
        $('log').innerHTML = logLines.map((l) => '<div>' + l.replace(/</g, '&lt;') + '</div>').join('');
      }
    
      const mm = $('minimap');
      const mmCtx = mm.getContext('2d');
      const CANVAS_STATE = { level: null, state: null, monsters: null };
      const T = tokens.color;
    
      function drawMinimap() {
        const { level, state, monsters } = CANVAS_STATE;
        if (!level) return;
        const S = mm.width / (level.gridSize.w + 2);
        mmCtx.clearRect(0, 0, mm.width, mm.height);
        mmCtx.fillStyle = 'rgba(10,10,11,0.85)';
        mmCtx.fillRect(0, 0, mm.width, mm.height);
        for (let z = 0; z < level.gridSize.h; z++) {
          for (let x = 0; x < level.gridSize.w; x++) {
            const ch = level.grid[z][x];
            if (ch === '#') continue;
            mmCtx.fillStyle = ch === '+' ? T.signal : ch === 'L' ? T.blood : 'rgba(216,207,187,0.20)';
            mmCtx.fillRect((x + 1) * S, (z + 1) * S, S, S);
          }
        }
        mmCtx.fillStyle = T.rust;
        for (const r of level.rooms) {
          mmCtx.strokeStyle = 'rgba(216,207,187,0.16)';
          mmCtx.strokeRect((r.rect.x0 + 1) * S, (r.rect.z0 + 1) * S, (r.rect.x1 - r.rect.x0) * S, (r.rect.z1 - r.rect.z0) * S);
        }
        for (const e of level.evidencePoints) {
          mmCtx.fillStyle = state.collected?.has(e.id) ? 'rgba(240,230,210,0.35)' : T.paper;
          mmCtx.fillRect((e.pos.x + 1) * S - 2, (e.pos.z + 1) * S - 2, 4, 4);
        }
        for (const ep of level.extractionPoints) {
          mmCtx.fillStyle = ep.safe ? T.mold : T.blood;
          mmCtx.fillRect((ep.pos.x + 1) * S - 3, (ep.pos.z + 1) * S - 3, 6, 6);
        }
        for (const id of Object.keys(monsters ?? {})) {
          const p = monsters[id].brain.position;
          mmCtx.fillStyle = monsters[id].brain.state === 'chase' ? T.danger : 'rgba(139,30,30,0.65)';
          mmCtx.beginPath();
          mmCtx.arc((p.x + 1) * S, (p.z + 1) * S, 3.5, 0, Math.PI * 2);
          mmCtx.fill();
        }
        const px = (state.pos.x + 1) * S, pz = (state.pos.z + 1) * S;
        mmCtx.fillStyle = T.signal;
        mmCtx.beginPath();
        mmCtx.arc(px, pz, 3, 0, Math.PI * 2);
        mmCtx.fill();
        mmCtx.strokeStyle = T.signal;
        mmCtx.beginPath();
        mmCtx.moveTo(px, pz);
        mmCtx.lineTo(px + Math.sin(state.yaw) * 12, pz - Math.cos(state.yaw) * 12);
        mmCtx.stroke();
      }
    
      function update(payload) {
        if (!$('sanityFill')) return;
        const { state, level, voiceInfo, monsterStates, calibration, classifier } = payload;
        CANVAS_STATE.level = level;
        CANVAS_STATE.state = state;
        CANVAS_STATE.monsters = monsterStates;
    
        const pct = Math.max(0, Math.min(100, state.sanity));
        $('sanityFill').style.width = pct + '%';
        const band = sanityBands.find((b) => pct >= b.min && pct <= b.max) ?? sanityBands[0];
        $('sanityFill').style.background = band.id === 'composed' ? T.mold : band.id === 'uneasy' ? T.signal : band.id === 'fear' ? T.rust : T.blood;
        $('sanityText').textContent = band.label + ' ' + Math.round(pct);
    
        $('battery').textContent = Math.round(state.battery);
        $('evidence').textContent = String(state.evidence);
        const m = Math.floor(state.elapsed / 60), s = Math.floor(state.elapsed % 60);
        $('clock').textContent = m + ':' + String(s).padStart(2, '0');
    
        const db = state.debug.voice?.levelDb ?? state.debug.lastDb ?? -100;
        $('micDb').textContent = (db > -99 ? db.toFixed(1) : '--') + ' dBFS';
        const bandNames = { whisper: '耳语', normal: '正常说话', shout: '喊叫', indistinguishable: '不可辨（低于噪声底+6dB）', null: '静默' };
        if (calibration.phase !== 'done') {
          $('micBand').textContent = '校准中…';
          setCalibProgress(calibration.framesInPhase / (calibration.phase === 'ambient' ? 30 : calibration.phaseFrames));
          $('calibLive').textContent = (db > -99 ? db.toFixed(1) : '--') + ' dBFS';
        } else {
          const cls = classifier;
          $('micBand').textContent = cls?.anchors ? (bandNames[voiceInfo?.band ?? null] ?? '—') : '未校准（无麦模式：固定强度 ' + config.personaPacks.recruit.fixedIntensity + '）';
          const rel = voiceInfo?.relPeakNorm;
          $('micFill').style.width = rel == null ? '0%' : Math.round(Math.max(0, Math.min(1, (rel + 1.2) / 2.4)) * 100) + '%';
        }
    
        drawMinimap();
      }
    
      function endScreen(state, level) {
        const box = $('end');
        if (!box) { console.warn('HUD 缺少 #end 元素'); return; }
        box.style.display = 'flex';
        if (state.extracted) {
          $('endTitle').textContent = '撤离成功';
          $('endTitle').style.color = T.mold;
          $('endBody').innerHTML = '撤离点：' + state.extracted.label + '（×' + state.extracted.rewardScale + '）<br>证据 ' + state.evidence + '/' + level.evidencePoints.length + '　用时 ' + state.elapsed.toFixed(1) + 's<br>残响碎片 ' + Math.round(state.evidence * 200 + 150 + (state.elapsed < 600 ? 100 : 0)) ;
        } else if (state.caught || state.sanity <= 0) {
          $('endTitle').textContent = state.caught ? '被抓住了' : '理智崩溃';
          $('endTitle').style.color = T.blood;
          $('endBody').innerHTML = '证据 ' + state.evidence + '/' + level.evidencePoints.length + '　用时 ' + state.elapsed.toFixed(1) + 's<br>死亡归因：最后一次声纹事件由 HUD 日志给出——被听见就是被抓住的原因。';
        }
      }
    
      return { setPhase, update, log, flashHeard, endScreen, setCalibProgress };
    }
    
    mod.exports = { createHud };
  };
  __tables["__m12"] = function (mod) {
    /**
     * 灰盒可玩端主循环：把已实现并测试过的四块接起来 ——
     *   ① Level DSL 关卡（13 房间 / 已编译校验）
     *   ② 声纹判定链（个人校准 + 分段归一化相对电平）
     *   ③ 声纹 → 听觉索敌（三怪阈值梯度）
     *   ④ 三怪三段式状态机
     * 玩家侧：手电电量、理智、证据、撤离。V9 §19.5 三层调参在此体现：数值全部走 config。
     */
    var __ns0 = __req("__m1");
    var VoiceCalibrator = __ns0.VoiceCalibrator,
        VoiceBandClassifier = __ns0.VoiceBandClassifier,
        BAND_IDS = __ns0.BAND_IDS,
        bandToStimulus = __ns0.bandToStimulus;
    var __ns1 = __req("__m2");
    var makeStimulus = __ns1.makeStimulus,
        movementStimulusKey = __ns1.movementStimulusKey,
        canHear = __ns1.canHear;
    var __ns2 = __req("__m3");
    var MonsterBrain = __ns2.MonsterBrain;
    var __ns3 = __req("__m4");
    var findRoomPath = __ns3.findRoomPath,
        roomPathToWaypoints = __ns3.roomPathToWaypoints;
    var __ns4 = __req("__m5");
    var makeRoomCollider = __ns4.makeRoomCollider;
    var __ns5 = __req("__m6");
    var createRenderer = __ns5.createRenderer,
        makeCamera = __ns5.makeCamera;
    var __ns6 = __req("__m7");
    var parseGLB = __ns6.parseGLB;
    var __ns7 = __req("__m8");
    var buildStructure = __ns7.buildStructure,
        kitInstances = __ns7.kitInstances,
        bakeInstances = __ns7.bakeInstances,
        bakeMonster = __ns7.bakeMonster;
    var __ns8 = __req("__m9");
    var createInput = __ns8.createInput;
    var __ns9 = __req("__m10");
    var createVoiceInput = __ns9.createVoiceInput;
    var __ns10 = __req("__m11");
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
    
    mod.exports = { startGame };
  };
  __tables["__m13"] = function (mod) {
    /**
     * 启动引导（服务端与 APK 资产两种形态共用）。
     *
     * 两条加载路径：
     *  ① 若页面已内联 `window.__WHISPER_BOOTSTRAP__`（APK 资产形态，出包时由 tools/embed-bootstrap.mjs 注入），
     *     直接用内联数据，**零网络请求**；
     *  ② 否则走 HTTP（本机灰盒服务形态），并显式检查状态码——404 静默失败曾导致"点击开始无反应"。
     *
     * 绝对路径纪律：`/src/...` 在两种形态下都能解析（WebView 的资产源根 = https://appassets.../）。
     */
    var __ns0 = __req("__m12");
    var boot = __ns0.startGame;
    
    async function getJSON(url) {
      const res = await fetch(url, { cache: 'no-store' });
      if (!res.ok) throw new Error('加载失败：' + url + ' → HTTP ' + res.status);
      return res.json();
    }
    
    async function startGame() {
      const inline = globalThis.__WHISPER_BOOTSTRAP__;
      let config;
      let tokens;
      if (inline && inline.config && inline.designTokens) {
        config = inline.config;
        tokens = inline.designTokens;
      } else {
        config = await getJSON('/api/config');
        tokens = await getJSON('/api/design-tokens');
      }
      config.levelId = inline?.levelId ?? 'asylum_v1';
    
      // 关卡同样优先用内联数据（APK 形态下不再需要 /api/levels）
      if (inline?.level) globalThis.__WHISPER_LEVEL__ = inline.level;
      if (inline?.kitsBase64) globalThis.__WHISPER_KITS_B64__ = inline.kitsBase64;
    
      return boot(config, tokens);
    }
    
    mod.exports = { startGame };
  };
  var __entry = __req("__m13");
  globalThis.startGame = function () { return __entry.startGame(); };
})();
