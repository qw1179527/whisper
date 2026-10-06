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
        /// <summary>死因：null/空 = 没死或活着撤离。见 <see cref="GameSession.DeathCauseCaught"/> 等常量。</summary>
        public string DeathCause;
        /// <summary>致死的那只怪的 id（仅 <see cref="GameSession.DeathCauseCaught"/> 时非空）。</summary>
        public string KilledBy;
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
        /// <summary>局内任务（合同日志里的可选目标）——每局按种子抽，进度由局内事件累加。</summary>
        public readonly Objectives.ObjectiveSystem Objectives;
        public readonly Hearing.Hearing HearingSystem;
        public readonly VoiceBandClassifier Voice;
        public readonly List<MonsterBrain> Monsters = new List<MonsterBrain>();
        public readonly List<string> EventLog = new List<string>();

        public float PlayerX { get; set; }
        public float PlayerZ { get; set; }

        /// <summary>
        /// 货车安全区（世界 AABB），由 Runtime 每帧写入（见 HallScene.TruckSafeZone）。
        /// 为什么是纯数据而不是引用 TruckScene：Gameplay 层不反向依赖 Runtime 的几何 ——
        /// 保持"玩法不依赖具体车辆实现"，将来换多辆车或多个安全区也不用改玩法层。
        /// 默认值放在地底且尺寸为零：**任何点都不在内**（fail-safe：宁可不保护，也不误保护）。
        /// </summary>
        /// ⚠ 这里的类型是**裸浮点而不是 Vector3**：`Whisper.Gameplay` 程序集**不引用 UnityEngine**
        /// （实测 CS0246：'Vector3' could not be found）。这是本项目的分层约定 ——
        /// **玩法层零 Unity 依赖**：几何判断留在 Runtime（有 Unity）侧，玩法层只收数值与 bool。
        /// 也因此 `SanitySystem.Tick(..., inSafeZone: bool, ...)` 收的是 bool 而不是坐标。
        public float TruckSafeCenterX { get; set; }
        public float TruckSafeCenterZ { get; set; } = -1000f;   // 默认远在地底 → 任何点都不在内（fail-safe）
        public float TruckSafeSizeX { get; set; }
        public float TruckSafeSizeZ { get; set; }

        /// <summary>玩家当前是否在货车安全区内（§8：鬼无法进入，在车内不掉理智）。</summary>
        public bool PlayerInSafeZone
        {
            get
            {
                if (TruckSafeSizeX <= 0f || TruckSafeSizeZ <= 0f) return false;
                float dx = Math.Abs(PlayerX - TruckSafeCenterX);
                float dz = Math.Abs(PlayerZ - TruckSafeCenterZ);
                return dx <= TruckSafeSizeX * 0.5f && dz <= TruckSafeSizeZ * 0.5f;
            }
        }

        /// <summary>
        /// 怪物是否**允许**进入某点。§8：安全区内鬼无法进入 —— 故安全区内一律返回 false。
        /// 供怪物寻路在选下一个路径点时过滤（而不是让怪物撞墙后自己放弃，那会产生抖动）。
        /// </summary>
        public bool MonsterMayEnter(float x, float z)
        {
            if (TruckSafeSizeX <= 0f || TruckSafeSizeZ <= 0f) return true;
            float dx = Math.Abs(x - TruckSafeCenterX);
            float dz = Math.Abs(z - TruckSafeCenterZ);
            return !(dx <= TruckSafeSizeX * 0.5f && dz <= TruckSafeSizeZ * 0.5f);
        }
        public bool SeenByPlayer { get; set; }
        public int SurvivingAllies { get; set; }
        public SessionOutcome Outcome { get; private set; }

        // ── 被鬼抓到致死（用户 2026-10-06 要求：「被鬼猎杀到的话会有跳脸」）──
        /// <summary>死因：被追击中的鬼抓到。</summary>
        public const string DeathCauseCaught = "caught";
        /// <summary>死因：理智归零后崩溃。</summary>
        public const string DeathCauseSanity = "sanity";

        /// <summary>
        /// 玩家被抓到（致死）时触发。参数 = 那只怪的 id。
        ///
        /// ## 为什么是事件而不是直接调视图
        /// 本类**不依赖 UnityEngine**（文件头写明：因此本机跑手能真跑完一整局）。
        /// 跳脸是 Unity 侧的表现层，由 <c>GameBootstrap</c> 订阅本事件去播 —— 这与
        /// <c>ItemSystem.OnLog</c> 是同一个模式。若在这里直接引用 <c>JumpscareView</c>，
        /// 纯逻辑层就被拖进引擎依赖，本机 157 条断言的编译前提会一起塌掉。
        /// </summary>
        public event Action<string> PlayerKilled;

        /// <summary>本局是否已被抓到致死（HUD/取证可读）。</summary>
        public bool PlayerCaught { get; private set; }
        /// <summary>最近一次致死判定时的实测距离（米）——取证用，证明判据真的按距离走。</summary>
        public float LastCaughtDistanceM { get; private set; } = -1f;

        readonly List<Stimulus> _pending = new List<Stimulus>();

        // ── 可观测性：把每帧的视觉判定分量暴露出来 ──
        // 为什么：端到端用例里"怪为什么没进追击"曾无法判断（距离/朝向/保护期/阈值四个条件互相耦合），
        // 只能靠反复猜测。把这些分量显式暴露后，断言与调试都能直接看到是哪一项不成立。
        public float LastSightDistance { get; private set; }
        public float LastSightRange { get; private set; }
        public bool LastSeenEligible { get; private set; }
        public bool LastChaseCondition { get; private set; }

        /// <summary>本局使用的碰撞几何（构造时编译一次；证据点落位与出生点都基于它）。</summary>
        public LevelGeometry Geometry { get; }

        public GameSession(LevelData level, GameConfigReader cfg, int matchSeed = 0, VoiceAnchors? voiceAnchors = null)
        {
            Level = level;
            Cfg = cfg;
            // 几何编译一次并复用：证据点落位、出生点、可达性都基于它（此前 GameSession 完全没有几何，
            // 于是"证据点在房间中心"这种几何缺陷在会话层根本无从发现）。
            Geometry = LevelGeometry.Compile(level);
            int evidenceTotal = 0;
            foreach (var r in level.Rooms) if (r.EvidencePoint) evidenceTotal++;
            Sanity = new SanitySystem(cfg);
            Items = new ItemSystem(cfg, evidenceTotal);
            Director = new MatchDirector(cfg, matchSeed);
            Hud = new HudModel(cfg, evidenceTotal);
            // 局内任务（合同日志里的可选目标）：按本局种子抽，与天气/风向同一来源 → 联机可复现。
            Objectives = new Objectives.ObjectiveSystem(cfg);
            Objectives.BeginContract((uint)matchSeed ^ 0x5F3759DFu);
            HearingSystem = new Hearing.Hearing(cfg);
            Voice = VoiceBandClassifier.FromConfig(cfg, voiceAnchors ?? new VoiceAnchors(-46f, -30f, -14f, -62f));
            Items.OnLog += (msg) => EventLog.Add(msg);
            // 证据每收一条就喂给局内任务（"找齐 N 条证据"那条要进度）。
            // 为什么挂在 OnLog 而不是另开回调：ItemSystem 已经在事件里广播了收集事实，
            // 再开一条并行回调等于两份真相源，容易只更新一处。
            Items.OnLog += (msg) =>
            {
                if (msg != null && msg.StartsWith("拾取证据", StringComparison.Ordinal))
                    Objectives.ReportEvidence(Items.EvidenceCount, Items.EvidenceTotal);
            };

            // 出生点：第一间房（入口区）中心
            // 出生点：入口房间的**空可走格**（房间中心可能被家具占用；灰盒端同口径）
            if (level.Rooms.Count > 0)
            {
                var r0 = level.Rooms[0];
                if (!Geometry.TryFindFreeCell(r0.CenterX, r0.CenterZ, out float sx, out float sz)) { sx = r0.CenterX; sz = r0.CenterZ; }
                PlayerX = sx; PlayerZ = sz;
            }

            // 证据点落到世界坐标（房间局部 → 世界）
            foreach (var r in level.Rooms)
            {
                if (!r.EvidencePoint) continue;
                // 证据点**不能直接用房间中心**：家具可能正好压住中心，代理最近只能站到家具外侧 ——
                // 实测病床 0.9×2.0 压中心时最近站位距证据点 1.35m > 拾取半径 0.9m，
                // 5 个证据只拿得到 1 个，**关卡不可通关**（独立复核第 2 轮 F-A）。
                // 改由 EvidencePlacer 在几何上挑"代理真正站得到"的位置。
                float ex = r.CenterX, ez = r.CenterZ;
                if (Geometry == null || !Whisper.Gameplay.Items.EvidencePlacer.TryPlace(Geometry, level, r, out ex, out ez))
                {
                    // 退化到中心并记录：这是关卡缺陷，必须让它可见，而不是静默用中心
                    System.Console.WriteLine($"[Whisper] ⚠ 证据点落位失败（房间 {r.Id} 无与门连通的可站格），退化到房间中心");
                }
                Items.EvidencePoints.Add(new EvidencePoint { Id = r.Id, X = ex, Z = ez });
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
            // 【§8 安全区】inSafeZone 原先是硬编码 false —— 而 SanitySystem.Tick 早就支持它
            // （有 inSafeZone 恢复分支），只是从来没接线。这正是"能力已存在但没接上"的又一例。
            Sanity.Tick(dt, args.TorchOn, inSafeZone: PlayerInSafeZone, lightsOut: args.LightsOut);
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
                // 看见的三个条件（与灰盒 __m12 一致）：玩家侧"看见"、距离在阈值内、保护期已过。
                // 灰盒还用 12m 作为"这段时间内可被看见"的硬上限（sightRange 另算阈值），这里同样保留。
                float pdist = Distance(brain.Position.X, brain.Position.Z, PlayerX, PlayerZ);
                LastSightDistance = pdist;
                LastSightRange = Cfg.Float($"monsters.{brain.Id}.sightRangeM", 0f);
                LastSeenEligible = SeenByPlayer;
                bool seenCond = chaseAllowed && SeenByPlayer;
                LastChaseCondition = seenCond && pdist <= 12f && LastSightRange >= pdist;

                // **状态迁移必须显式做**：MonsterBrain 只在已处于 chase 时才消费 seenPlayer，
                // 它自己没有"看见 → 进入追击"的迁移（灰盒由 __m12 的 updateMonsters 显式 _enter('chase')）。
                // 我第一版漏了这一步，后果是"怪永远不追人"——端到端用例把它逼出来了。
                if (LastChaseCondition)
                {
                    brain.SetTarget(new Vec2(PlayerX, PlayerZ));
                    brain.Enter("chase");
                }
                else if (brain.State == "chase")
                {
                    // 看不见了：去最后已知位置搜索（而不是继续贴着玩家坐标），V9 §7
                    brain.Enter("investigate");
                }
                var r = brain.Step(tick, false, null);
                // 接触判定：保护期内不判（V9 §7）
                float contactDist = Distance(r.Position.X, r.Position.Z, PlayerX, PlayerZ);
                if (Director.ContactEnabled && contactDist < 1.0f)
                {
                    // 【两条接触语义必须分开 · 2026-10-06】
                    //   ① 追击期被抓到 → **即死**（用户要求「被鬼猎杀到会有跳脸」；这也是恐鬼症官方行为）
                    //   ② 其余状态（巡逻/调查/回巢）擦身而过 → 只扣理智
                    // 为什么不能合并成一条：monsterBehavior.contactNote 记录了刻意的设计取舍 ——
                    // 「碰到就结束对局会让『没说话也被抓』变成必然」，那会让本作的核心机制（声音管理）
                    // 失去意义。分开之后两条设计同时成立。
                    bool lethal = Cfg.Bool("death.caught.requiresChase", true)
                        ? string.Equals(r.State, "chase", StringComparison.Ordinal)
                        : true;
                    float killDist = Cfg.Float("death.caught.killDistanceM", 1.0f);
                    if (lethal && contactDist <= killDist)
                    {
                        LastCaughtDistanceM = contactDist;
                        PlayerCaught = true;
                        EventLog.Add($"被 {Hud.MonsterLabel(brain.Id)} 抓到（距离 {contactDist:0.00}m ≤ {killDist:0.00}m）：跳脸");
                        var handler = PlayerKilled;
                        if (handler != null) handler(brain.Id);
                        EndMatch(false, ExtractionKind.None, DeathCauseCaught, brain.Id);
                        return;   // 对局已结束，本帧不再推进（避免死后还继续算理智/撤离）
                    }
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
            if (Sanity.Value <= 0f && !Sanity.Collapsed) EndMatch(false, ExtractionKind.None, DeathCauseSanity, null);
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
            => EndMatch(survived, kind, null, null);

        /// <summary>
        /// 结束一局并结算（带死因）。
        /// `deathCause` / `killedBy` 让结算页与取证能说清「为什么结束」——
        /// 此前只有一句"未能撤离"，抓到致死与理智崩溃在结果里**无法区分**。
        /// </summary>
        public SessionOutcome EndMatch(bool survived, ExtractionKind kind, string deathCause, string killedBy)
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
                DeathCause = survived ? null : deathCause,
                KilledBy = killedBy,
            };
            Director.EndMatch();
            if (survived) EventLog.Add($"撤离成功（{r2.ExtractionLabel}）：残响碎片 {r2.Fragments}");
            else if (string.Equals(deathCause, DeathCauseCaught, StringComparison.Ordinal))
                EventLog.Add("对局结束：被鬼抓到");
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
