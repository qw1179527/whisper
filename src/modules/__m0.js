/* 低语计划 · 灰盒源树分模块产物（tools/split-modules.mjs 生成，勿手改）
 * 模块：__m0 → m0.js
 * 职责：无文档注释：由顶层声明 `__CFG` 推断为数据/配置模块（首字符 {）
 * 来源：baseline/game-0.6.0.js 第 15~523 行（15008 字节，逐字节搬移）
 * 包装改写（唯一改动，可审计）：mod.exports → module.exports；__req("__mN") → require("__mN")
 * 依赖：无｜导出：cfg, designTokens, installConfig, loadConfig, resetConfig, stimulusSource
 */
'use strict';

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
    module.exports = { installConfig: installConfig, loadConfig: loadConfig, cfg: cfg, stimulusSource: stimulusSource, designTokens: designTokens, resetConfig: resetConfig };
