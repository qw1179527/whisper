using System;
using System.Collections.Generic;
using Whisper.Gameplay.Config;
using Whisper.Gameplay.Hearing;

namespace Whisper.Gameplay.Monsters
{
    public static class MonsterStates
    {
        public static readonly string[] All = { "patrol", "investigate", "chase", "return" };
    }

    public struct Vec2
    {
        public float X, Z;
        public Vec2(float x, float z) { X = x; Z = z; }
        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => new Vec2(a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t);
        public override string ToString() => $"({X},{Z})";
    }

    /// <summary>移动委托：注入后走与玩家共用的碰撞解析（含子步进与墙体阻挡）；不注入时退化为直线移动（纯逻辑测试用）。</summary>
    public delegate Vec2 MoveResolver(Vec2 from, Vec2 to, float maxStep, out bool blocked);

    public struct MonsterStepResult
    {
        public string State;
        public Vec2 Position;
        public float Speed;
        public bool Moved;
        public Vec2 MovedTo;
        public bool Arrived;
    }

    public struct StateTransition
    {
        public long Tick;
        public string From, To;
    }

    /// <summary>
    /// 三怪行为状态机（V9 §7：三段式 巡逻 → 调查 → 追逐 → 回归，失联 12 秒回归）。
    ///
    /// 这是 Host 权威端的**确定性状态机**：同一条刺激序列 + 同一初态 → 同一状态轨迹。
    /// 本类不碰 Unity、不碰网络，只吃"每 tick 的输入"吐"每 tick 的输出"——
    /// 因此可以用向量完全锁定（见 tools/monster-port-vectors.mjs）。
    ///
    /// 语义纪律（灰盒首版曾搞错，直接导致"没说话也被抓"）：
    ///   **听见 → 调查**声源；**只有看见（sight）才进追击**。
    ///   唯一例外是「挑衅者」人格的仇恨锁定（那是显式给它的代价）。
    ///   绝不能让"一声音 = 被锁定"，否则任何语音都等于死亡，核心机制就不成立。
    ///
    /// 移植自灰盒 `src/modules/__m3.js`。
    /// </summary>
    public sealed class MonsterBrain
    {
        readonly GameConfigReader _cfg;
        public string Id { get; }
        public float SpeedMps { get; }
        public float ChaseSpeedScale { get; }
        public Vec2 Position { get; private set; }
        public List<Vec2> PatrolPoints { get; }
        public int PatrolIndex { get; private set; }
        public string State { get; private set; } = "patrol";
        public Vec2? Target { get; private set; }
        public Stimulus? LastStimulus { get; private set; }
        public long? LastHeardTick { get; private set; }
        public long Tick { get; private set; }
        public long StateEnteredTick { get; private set; }
        public readonly List<StateTransition> History = new List<StateTransition>();
        /// <summary>由最恐惧的玩家理智档注入（0.2 = +20%）。</summary>
        public float PerceptionBonus { get; set; }
        public long? AggroLockUntil { get; private set; }
        public MoveResolver Mover { get; set; }

        readonly float _investigateArriveRadiusM;
        readonly float _lostContactTicks;
        readonly float _tickRate;

        static readonly Dictionary<string, string> _sourceTypeCache = new Dictionary<string, string>(StringComparer.Ordinal);

        public MonsterBrain(string monsterId, GameConfigReader cfg, Vec2? position = null, List<Vec2> patrolPoints = null, float? chaseSpeedScale = null)
        {
            _cfg = cfg;
            float speed = cfg.Float($"monsters.{monsterId}.speedMps", float.NaN);
            if (float.IsNaN(speed)) throw new ArgumentException($"unknown monster: {monsterId}");
            Id = monsterId;
            SpeedMps = speed;
            // 追击倍率：per-monster 优先（配置只在 monsters.* 下有；monsterBehavior 下没有该键），
            // 再回落 V9 §7 表7-2 的全局值 1.6（与灰盒 `?? 1.6` 兜底一致）
            float perMonsterScale = cfg.Float($"monsters.{monsterId}.chaseSpeedScale", 1.6f);
            ChaseSpeedScale = chaseSpeedScale ?? perMonsterScale;
            Position = position ?? new Vec2(0f, 0f);
            PatrolPoints = patrolPoints ?? new List<Vec2> { new Vec2(0f, 0f) };
            _investigateArriveRadiusM = cfg.Float("monsterBehavior.investigateArriveRadiusM", 1.5f);
            _tickRate = cfg.Float("network.tickRate", 60f);
            _lostContactTicks = cfg.Float("monsterBehavior.lostContactSeconds", 12f) * _tickRate;
        }

        /// <summary>外部显式迁移状态（组合根/会话使用）。为什么暴露：状态迁移必须由"看见/听见"的判定方触发，
        /// 而状态机自身只负责在给定状态下的移动 —— 分工不清会导致"怪永远不追人"这类静默缺陷。</summary>
        public void Enter(string state) => EnterInternal(state);

        /// <summary>设置导航目标（追击时指向玩家最后已知位置）。</summary>
        public void SetTarget(Vec2 target) => Target = target;

        void EnterInternal(string state)
        {
            if (State == state) return;
            History.Add(new StateTransition { Tick = Tick, From = State, To = state });
            State = state;
            StateEnteredTick = Tick;
        }

        /// <summary>注入一条刺激（Host 端已按可听性筛过）：听见 → 调查；仅挑衅者仇恨锁定时进追击。</summary>
        public void OnStimulus(in Stimulus stim, long? tick = null)
        {
            long t = tick ?? Tick;
            LastStimulus = stim;
            LastHeardTick = t;
            Target = new Vec2(stim.X, stim.Z);
            if (AggroLockUntil.HasValue && t < AggroLockUntil.Value) { EnterInternal("chase"); return; }
            if (State != "chase") EnterInternal("investigate");
        }

        /// <summary>挑衅者人格：锁定仇恨（5 秒，由配置 personaPacks.taunter 的 traits 决定）。</summary>
        public void ApplyAggroLock(long tick)
        {
            bool hasTrait = HasPersonaTrait("taunter", "aggro_lock_5s");
            if (!hasTrait) return;
            AggroLockUntil = tick + (long)(5f * _tickRate);
        }

        bool HasPersonaTrait(string personaId, string trait)
        {
            var traits = ConfigList($"personaPacks.{personaId}.traits");
            return traits != null && traits.Contains(trait);
        }

        List<string> ConfigList(string path)
        {
            // GameConfigReader 只暴露标量；这里回落到静态 GameConfig（同一真源）
            var v = GameConfig.Get(path);
            if (!(v is List<object> list)) return null;
            var outList = new List<string>(list.Count);
            foreach (var o in list) outList.Add(o as string);
            return outList;
        }

        /// <summary>推进一个 tick。input 只含"本 tick 是否看见玩家"与玩家位置（对应灰盒 step 的入参）。</summary>
        public MonsterStepResult Step(long? tick = null, bool seenPlayer = false, Vec2? playerPos = null)
        {
            Tick = tick ?? Tick + 1;
            float dt = 1f / _tickRate;
            bool moved = false, arrived = false;
            Vec2 movedTo = Position;

            switch (State)
            {
                case "patrol":
                {
                    var p = PatrolPoints[PatrolIndex % PatrolPoints.Count];
                    var r = MoveToward(p, SpeedMps * dt, out moved, out movedTo);
                    Position = r;
                    if (Distance(Position, p) <= _investigateArriveRadiusM) { PatrolIndex++; arrived = true; }
                    break;
                }
                case "investigate":
                {
                    if (!Target.HasValue) { EnterInternal("patrol"); break; }
                    var r = MoveToward(Target.Value, SpeedMps * dt, out moved, out movedTo);
                    Position = r;
                    if (Distance(Position, Target.Value) <= _investigateArriveRadiusM)
                    {
                        Target = null; arrived = true;
                        EnterInternal("return");   // 到达声源后短暂停留再回归巡逻（回归态承担"停留"语义）
                    }
                    break;
                }
                case "chase":
                {
                    if (seenPlayer && playerPos.HasValue) Target = playerPos.Value;
                    if (Target.HasValue)
                    {
                        Position = MoveToward(Target.Value, SpeedMps * ChaseSpeedScale * dt, out moved, out movedTo);
                    }
                    else if (playerPos.HasValue)
                    {
                        Position = MoveToward(playerPos.Value, SpeedMps * ChaseSpeedScale * dt, out moved, out movedTo);
                    }
                    break;
                }
                case "return":
                {
                    var p = PatrolPoints[PatrolIndex % PatrolPoints.Count];
                    Position = MoveToward(p, SpeedMps * dt, out moved, out movedTo);
                    if (Distance(Position, p) <= _investigateArriveRadiusM) { PatrolIndex++; EnterInternal("patrol"); }
                    break;
                }
                default:
                    throw new InvalidOperationException($"unknown state: {State}");
            }

            // 失联 12 秒 → 回归巡逻（V9 §7）
            if ((State == "chase" || State == "investigate") && LastHeardTick.HasValue)
            {
                if (Tick - LastHeardTick.Value > _lostContactTicks)
                {
                    LastStimulus = null;
                    Target = null;
                    EnterInternal("return");
                }
            }

            return new MonsterStepResult
            {
                State = State,
                Position = new Vec2(Round3(Position.X), Round3(Position.Z)),
                Speed = State == "chase" ? SpeedMps * ChaseSpeedScale : SpeedMps,
                Moved = moved,
                MovedTo = movedTo,
                Arrived = arrived,
            };
        }

        /// <summary>反应链：把本 tick 的所有刺激交给该怪，返回听见的那些（对应灰盒 reactTo）。</summary>
        public List<Stimulus> ReactTo(IEnumerable<Stimulus> stimuli, Hearing.Hearing hearing, long? tick = null)
        {
            var heard = new List<Stimulus>();
            var ctx = new HearingContext { PerceptionBonus = PerceptionBonus };
            foreach (var s in stimuli)
            {
                var r = hearing.CanHear(Id, s, Position.X, Position.Z, ctx);
                if (r.Audible)
                {
                    heard.Add(s);
                    OnStimulus(s, tick);
                }
            }
            return heard;
        }

        Vec2 MoveToward(Vec2 target, float maxStep, out bool moved, out Vec2 movedTo)
        {
            moved = false;
            movedTo = target;
            if (Mover != null)
            {
                var res = Mover(Position, target, maxStep, out bool blocked);
                moved = true;
                movedTo = res;
                return res;
            }
            // 无 mover 时退化为直线移动（仅纯逻辑测试）
            float dx = target.X - Position.X, dz = target.Z - Position.Z;
            // 注意：JS 的 `|| 1` 依赖 0 为 falsy，C# 里必须显式判断
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d <= 0f) d = 1f;
            if (d <= maxStep) { moved = true; return target; }
            moved = true;
            return new Vec2(Position.X + dx / d * maxStep, Position.Z + dz / d * maxStep);
        }

        static float Distance(Vec2 a, Vec2 b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// 取整到 3 位小数，**必须对齐 JS 的 Math.round 语义**。
        ///
        /// 踩过的坑：C# 默认 `Math.Round` 是**银行家舍入**（.5 取偶），而 JS `Math.round` 是**向 +∞ 取整**。
        /// 实测在 x=13.3035 这类中点上两者差 1 个千分位（13.304 vs 13.303），
        /// 导致状态机向量逐点比对出现假差异。此处显式按 JS 语义实现。
        /// </summary>
        static float Round3(float v)
        {
            float scaled = v * 1000f;
            return (float)(Math.Floor(scaled + 0.5) / 1000.0);
        }

        /// <summary>调试与测试快照（对应灰盒 snapshot；history 取最后 8 条）。</summary>
        public Dictionary<string, object> Snapshot()
        {
            var hist = new List<object>();
            int start = Math.Max(0, History.Count - 8);
            for (int i = start; i < History.Count; i++)
                hist.Add(new Dictionary<string, object> { ["tick"] = History[i].Tick, ["from"] = History[i].From, ["to"] = History[i].To });
            return new Dictionary<string, object>
            {
                ["id"] = Id,
                ["state"] = State,
                ["position"] = new Dictionary<string, object> { ["x"] = Position.X, ["z"] = Position.Z },
                ["patrolIndex"] = PatrolIndex,
                ["lastHeardTick"] = LastHeardTick,
                ["history"] = hist,
            };
        }

        /// <summary>终局狂暴：撤离倒计时进入最后窗口（V9 §7）。</summary>
        public static bool FinalRageActive(GameConfigReader cfg, float secondsToExtraction) =>
            secondsToExtraction <= cfg.Float("monsterBehavior.finalRageWindowBeforeExtractionSec", 60f);

        /// <summary>同轴直线是否无墙（怪物寻路用；对应灰盒 axisClear）。grid[z][x] === '#' 视为墙。</summary>
        public static bool AxisClear(string[][] grid, Vec2 a, Vec2 b)
        {
            if (Math.Abs(a.Z - b.Z) < 1e-6f)
            {
                int row = (int)Math.Floor(a.Z);
                for (float x = Math.Min(a.X, b.X); x <= Math.Max(a.X, b.X); x++)
                {
                    int col = (int)x;
                    if (grid != null && row >= 0 && row < grid.Length && grid[row] != null && col >= 0 && col < grid[row].Length && grid[row][col] == "#") return false;
                }
                return true;
            }
            if (Math.Abs(a.X - b.X) < 1e-6f)
            {
                int col = (int)Math.Floor(a.X);
                for (float z = Math.Min(a.Z, b.Z); z <= Math.Max(a.Z, b.Z); z++)
                {
                    int row = (int)z;
                    if (grid != null && row >= 0 && row < grid.Length && grid[row] != null && col >= 0 && col < grid[row].Length && grid[row][col] == "#") return false;
                }
                return true;
            }
            return false;
        }
    }
}
