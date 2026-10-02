using System;
using System.Collections.Generic;
using Whisper.Gameplay.Config;
using Whisper.Gameplay.Extraction;
using Whisper.Gameplay.Hearing;
using Whisper.Gameplay.Hud;
using Whisper.Gameplay.Items;
using Whisper.Gameplay.Level;
using Whisper.Gameplay.Match;
using Whisper.Gameplay.Monsters;
using Whisper.Gameplay.Sanity;
using Whisper.Gameplay.Voice;

namespace Whisper.Gameplay.Session
{
    /// <summary>一局跑完后的结果（供结算与测试断言）。</summary>
    public struct SessionOutcome
    {
        public bool Ended;
        public bool Survived;
        public ExtractionKind Extraction;
        public int EvidenceCollected;
        public int EvidenceTotal;
        public int SurvivingAllies;
        public float ElapsedSeconds;
        public int Fragments;
        public string Breakdown;
    }

    /// <summary>
    /// 一局游戏的组装与推进（V9 §7 / §19.2）——**把已移植的各个系统真正接起来**。
    ///
    /// 为什么需要它：此前每个系统都能单独断言，但"它们合起来能不能跑完一局"没人验证过。
    /// 本类把 声纹判定 → 听觉索敌 → 三怪状态机 → 理智 → 道具/证据 → 对局阶段 → 撤离结算
    /// 按 tick 顺序串起来，且**不依赖 UnityEngine** —— 因此可以由本机跑手真跑一整局。
    ///
    /// 每帧顺序（顺序本身是设计的一部分，改动需同步本注释）：
    ///   ① 对局阶段推进（保护期/主阶段/狂暴/撤离）+ 触发动态事件
    ///   ② 手电电量 → 理智（开灯看得见但费电，关灯省电但掉理智）
    ///   ③ 玩家位姿注入 → 怪物视觉（保护期内不允许"看见入追击"）
    ///   ④ 拾取（电池/信号弹/镇静剂）与证据
    ///   ⑤ 声纹刺激 → 逐怪可听性 → 怪物状态机推进
    ///   ⑥ HUD 模型刷新（显示什么由模型算，UI 只贴字符串）
    /// </summary>
    public sealed class GameSession
    {
        public readonly GameConfigReader Cfg;
        public readonly LevelData Level;
        public readonly SanitySystem Sanity;
        public readonly ItemSystem Items;
        public readonly MatchDirector Director;
        public readonly HudModel Hud;
        public readonly Hearing.Hearing HearingSystem;
        public readonly VoiceBandClassifier Voice;
        public readonly List<MonsterBrain> Monsters = new List<MonsterBrain>();
        public readonly List<string> EventLog = new List<string>();

        public float PlayerX { get; set; }
        public float PlayerZ { get; set; }
        public bool SeenByPlayer { get; set; }
        public int SurvivingAllies { get; set; }
        public SessionOutcome Outcome { get; private set; }

        readonly List<Stimulus> _pending = new List<Stimulus>();

        public GameSession(LevelData level, GameConfigReader cfg, int matchSeed = 0, VoiceAnchors? voiceAnchors = null)
        {
            Level = level;
            Cfg = cfg;
            int evidenceTotal = 0;
            foreach (var r in level.Rooms) if (r.EvidencePoint) evidenceTotal++;
            Sanity = new SanitySystem(cfg);
            Items = new ItemSystem(cfg, evidenceTotal);
            Director = new MatchDirector(cfg, matchSeed);
            Hud = new HudModel(cfg, evidenceTotal);
            HearingSystem = new Hearing.Hearing(cfg);
            Voice = VoiceBandClassifier.FromConfig(cfg, voiceAnchors ?? new VoiceAnchors(-46f, -30f, -14f, -62f));
            Items.OnLog += (msg) => EventLog.Add(msg);

            // 出生点：第一间房（入口区）中心
            if (level.Rooms.Count > 0) { PlayerX = level.Rooms[0].CenterX; PlayerZ = level.Rooms[0].CenterZ; }

            // 证据点落到世界坐标（房间局部 → 世界）
            foreach (var r in level.Rooms)
            {
                if (!r.EvidencePoint) continue;
                Items.EvidencePoints.Add(new EvidencePoint { Id = r.Id, X = r.CenterX, Z = r.CenterZ });
            }
            // 撤离点
            if (level.Extraction != null)
            {
                var std = FindRoom(level.Extraction.Standard);
                if (std != null) Items.ExtractionPoints.Add(new ExtractionPoint
                { Id = std.Id, Label = "标准撤离点", X = std.CenterX, Z = std.CenterZ, Safe = true, RewardScale = Cfg.Float("level.extraction.standardPoint.rewardScale", 1f) });
                var deep = FindRoom(level.Extraction.Deep);
                if (deep != null) Items.ExtractionPoints.Add(new ExtractionPoint
                { Id = deep.Id, Label = "深处撤离点", X = deep.CenterX, Z = deep.CenterZ, Safe = false, RewardScale = Cfg.Float("level.extraction.deepPoint.rewardScale", 1.3f) });
            }
            // 三怪：巡逻点取房间中心（首尾相连）
            var patrol = new List<Vec2>();
            foreach (var r in level.Rooms) patrol.Add(new Vec2(r.CenterX, r.CenterZ));
            foreach (var id in new[] { "stitcher", "whisperer", "coroner" })
            {
                var start = patrol.Count > 0 ? patrol[patrol.Count / 2] : new Vec2(0f, 0f);
                Monsters.Add(new MonsterBrain(id, cfg, start, new List<Vec2>(patrol)));
            }
        }

        Room FindRoom(string id)
        {
            foreach (var r in Level.Rooms) if (r.Id == id) return r;
            return null;
        }

        /// <summary>注入一条声纹刺激（本帧内由 <see cref="Tick"/> 消费）。</summary>
        public void EmitStimulus(in Stimulus s) => _pending.Add(s);

        /// <summary>按当前档位把一次语音判定结果转成刺激（走配置表强度/半径）。</summary>
        public Stimulus? StimulusFromVoice(BandDecision decision, long tick = 0)
        {
            if (decision.Band == null || decision.Band == "indistinguishable") return null;
            string key = VoiceBandClassifier.BandSourceKey(decision.Band);
            if (key == null) return null;
            float intensity = Cfg.Float($"stimulusSources.{key}.intensity", 0f);
            float? radius = Cfg.Float($"stimulusSources.{key}.radiusM", 0f);
            if (radius.HasValue && radius.Value <= 0f) radius = null;
            return new Stimulus(key, "voice", intensity, radius, false, PlayerX, PlayerZ, tick);
        }

        /// <summary>推进一帧。</summary>
        public void Tick(float dt)
        {
            if (Outcome.Ended) return;

            // ① 阶段与动态事件
            var fired = Director.Tick(dt);
            foreach (var e in fired)
            {
                EventLog.Add($"动态事件：{e.Type}（第 {e.AtSecond:0} 秒）");
                ApplyEvent(e);
            }

            // ② 手电 → 理智
            Items.TickFlashlight(dt);
            var args = Items.SanityArgs;
            Sanity.Tick(dt, args.TorchOn, inSafeZone: false, lightsOut: args.LightsOut);
            if (Sanity.TriggerCollapse)
            {
                // 崩溃尖叫：产生声纹刺激并把怪物引过来（V9 附录 A-2 的代价）
                string screamKey = Sanity.CollapseStimulusKey;
                float si = Cfg.Float($"stimulusSources.{screamKey}.intensity", 100f);
                _pending.Add(new Stimulus(screamKey, "voice", si, null, true, PlayerX, PlayerZ, (long)(Director.ElapsedSeconds * 60f)));
                EventLog.Add("理智崩溃：尖叫（怪物会被引过来）");
                Sanity.ClearCollapseFlag();
            }

            // ③ 视觉：保护期内不允许"看见入追击"
            bool chaseAllowed = Director.ChaseBySightAllowed;

            // ④ 拾取与证据
            foreach (var l in Items.TryPickup(PlayerX, PlayerZ)) EventLog.Add(l);
            foreach (var l in Items.TryCollectEvidence(PlayerX, PlayerZ)) EventLog.Add(l);

            // ⑤ 声纹 → 逐怪可听 → 状态机
            long tick = (long)(Director.ElapsedSeconds * 60f);
            foreach (var brain in Monsters)
            {
                var heard = brain.ReactTo(_pending, HearingSystem, tick);
                foreach (var s in heard)
                {
                    var det = HearingSystem.CanHear(brain.Id, s, brain.Position.X, brain.Position.Z, new HearingContext { PerceptionBonus = brain.PerceptionBonus });
                    EventLog.Add(Hud.FormatHearing(new HearingNotice
                    { MonsterLabel = Hud.MonsterLabel(brain.Id), DistanceM = det.DistanceM, EffectiveThreshold = det.EffectiveThreshold }));
                }
                bool seen = chaseAllowed && SeenByPlayer;
                var r = brain.Step(tick, seen, seen ? new Vec2(PlayerX, PlayerZ) : (Vec2?)null);
                // 接触判定：保护期内不判（V9 §7）
                if (Director.ContactEnabled && Distance(r.Position.X, r.Position.Z, PlayerX, PlayerZ) < 1.0f)
                {
                    Sanity.MonsterContact();
                    EventLog.Add($"被 {Hud.MonsterLabel(brain.Id)} 接触：理智 −{Cfg.Float("monsterBehavior.contactSanityLoss", 35f):0}");
                }
            }
            _pending.Clear();

            // ⑥ 撤离与 HUD
            var ep = Items.TryExtract(PlayerX, PlayerZ);
            if (ep.HasValue) EndMatch(true, ep.Value.Safe || ep.Value.Id == Items.ExtractionPoints[0].Id ? (ep.Value.Id == FindRoom(Level.Extraction?.Deep)?.Id ? ExtractionKind.Deep : ExtractionKind.Standard) : ExtractionKind.Standard);

            Director.ApplyTo(Sanity, Monsters.Count > 0 ? Monsters[0] : null);
            Hud.Update(Sanity, Items.BatterySeconds, Items.EvidenceCount, Director.ElapsedSeconds);
            Hud.SetStageText(StageLabel());
            if (Sanity.Value <= 0f && !Sanity.Collapsed) EndMatch(false, ExtractionKind.None);
        }

        void ApplyEvent(ScheduledEvent e)
        {
            switch (e.Type)
            {
                case "blackout":
                    Items.LightsOffZones.Add("all");
                    break;
                case "door_lock_shift":
                    // 锁门效果由门系统承担；此处只记录（门系统在 Unity 侧实例化）
                    break;
                case "child_laughter":
                    _pending.Add(new Stimulus("recorder_play", "device",
                        Cfg.Float("stimulusSources.recorder_play.intensity", 55f),
                        Cfg.Float("stimulusSources.recorder_play.radiusM", 16f), false, PlayerX, PlayerZ, 0));
                    break;
            }
        }

        string StageLabel()
        {
            switch (Director.Stage)
            {
                case MatchStage.Grace: return "保护期";
                case MatchStage.Main: return "主阶段";
                case MatchStage.FinalRage: return "终局狂暴";
                case MatchStage.Extraction: return "撤离阶段";
                default: return "已结束";
            }
        }

        /// <summary>结束一局并结算。</summary>
        public SessionOutcome EndMatch(bool survived, ExtractionKind kind)
        {
            int evidenceTotal = 0;
            foreach (var r in Level.Rooms) if (r.EvidencePoint) evidenceTotal++;
            var r2 = Settlement.Compute(Cfg, new MissionOutcome
            {
                Survived = survived,
                Extraction = kind,
                EvidenceCollected = Items.EvidenceCount,
                EvidenceTotal = evidenceTotal,
                SurvivingAllies = SurvivingAllies,
                ElapsedSeconds = Director.ElapsedSeconds,
            });
            Outcome = new SessionOutcome
            {
                Ended = true, Survived = survived, Extraction = kind,
                EvidenceCollected = r2.Evidence, EvidenceTotal = evidenceTotal,
                SurvivingAllies = SurvivingAllies, ElapsedSeconds = r2.ElapsedSeconds,
                Fragments = r2.Fragments, Breakdown = r2.Breakdown,
            };
            Director.EndMatch();
            if (survived) EventLog.Add($"撤离成功（{r2.ExtractionLabel}）：残响碎片 {r2.Fragments}");
            else EventLog.Add("对局结束：未能撤离");
            return Outcome;
        }

        static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
