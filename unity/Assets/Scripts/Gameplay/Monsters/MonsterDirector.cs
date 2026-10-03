using System;
using System.Collections.Generic;
using Whisper.Gameplay.Config;
using Whisper.Gameplay.Hearing;
using Whisper.Gameplay.Level;
// 名字冲突消解：`Hearing` 既是**命名空间**（Whisper.Gameplay.Hearing）又是**类名**（Hearing）。
// 直接写 `Hearing` 会被解析成命名空间（CS0118）。别名一次，全文件用 HearingEngine。
using HearingEngine = Whisper.Gameplay.Hearing.Hearing;

namespace Whisper.Gameplay.Monsters
{
    /// <summary>一只怪物对外可读的渲染状态（Unity 侧照它摆物体）。</summary>
    public readonly struct MonsterView
    {
        public readonly string Id;
        public readonly string Label;
        public readonly string State;
        public readonly float X, Z;
        public readonly float SpeedMps;
        /// <summary>本帧是否看得见玩家（用于 HUD 与"被发现了"反馈）。</summary>
        public readonly bool SeesPlayer;

        public MonsterView(string id, string label, string state, float x, float z, float speedMps, bool seesPlayer)
        {
            Id = id; Label = label; State = state; X = x; Z = z; SpeedMps = speedMps; SeesPlayer = seesPlayer;
        }
    }

    /// <summary>
    /// 怪物总控（V9 §7 三怪）—— **纯逻辑层**，不引用 UnityEngine，因此本机可真编译真跑。
    ///
    /// 为什么需要它：`MonsterBrain`（状态机）与 `Hearing`（听觉判定）都已就绪且有移植等价性断言，
    /// 但**没有任何东西把它们接起来**——没有实例化、没有把"听见"喂进去、没有驱动移动。
    /// 结果就是"怪永远不动也不追人"这类静默缺陷。本类负责这条接线。
    ///
    /// 三怪的差异化（V9 §7 表7-2，数值全部来自配置）：
    ///   缝匠 stitcher  教学怪（压力型）  听觉 medium(30)    视野 14m
    ///   低语者 whisperer 语音猎手（核心） 听觉 very_high(10) 视野 6m   ← 对语音极敏感、安静时几乎失明
    ///   收殓人 coroner 终局压迫（高压）  听觉 low(55)       视野 20m  ← 以视野为主
    ///
    /// 语义纪律（`MonsterBrain` 头注释即已强调）：**听见→调查；只有看见才进追击**。
    /// 绝不能让"一声音 = 被锁定"，否则任何语音都等于死亡，核心机制就不成立。
    /// </summary>
    public sealed class MonsterDirector
    {
        /// <summary>视线采样步长（米）。0.25 = 关卡格尺寸的一半，够细且不至于每帧几千次判定。</summary>
        const float SightSampleStepM = 0.25f;

        readonly GameConfigReader _cfg;
        readonly HearingEngine _hearing;
        readonly LevelGeometry _geo;
        readonly List<MonsterBrain> _brains = new List<MonsterBrain>();
        readonly Dictionary<string, float> _sightRangeM = new Dictionary<string, float>(StringComparer.Ordinal);
        readonly Dictionary<string, string> _labels = new Dictionary<string, string>(StringComparer.Ordinal);

        public IReadOnlyList<MonsterBrain> Brains => _brains;
        /// <summary>本帧发出的刺激（HUD/诊断用）。</summary>
        public int LastHeardCount { get; private set; }

        public MonsterDirector(GameConfigReader cfg, LevelGeometry geo,
            IReadOnlyList<string> monsterIds, IReadOnlyList<Vec2> patrolPoints, int startOffset = 0)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            _geo = geo ?? throw new ArgumentNullException(nameof(geo));
            _hearing = new HearingEngine(cfg);
            var points = patrolPoints != null && patrolPoints.Count > 0
                ? new List<Vec2>(patrolPoints)
                : new List<Vec2> { new Vec2(0f, 0f) };

            for (int i = 0; i < monsterIds.Count; i++)
            {
                var id = monsterIds[i];
                float sight = cfg.Float($"monsters.{id}.sightRangeM", float.NaN);
                // 视野缺失不能静默兜底：兜一个"看起来合理"的值等于在代码里藏第二份数值真源
                if (float.IsNaN(sight)) throw new InvalidOperationException($"配置缺失：monsters.{id}.sightRangeM");
                _sightRangeM[id] = sight;
                _labels[id] = cfg.String($"monsters.{id}.label", id);

                // 每只怪从**不同的**巡逻点起步，并把自己的巡逻顺序旋转到该点开头——
                // 否则三只怪会挤在原点、走同一条线。
                // （第一版我在 Unity 侧用 Mover 走"一大步"想把它挪过去，那是错的：
                //   Resolve 会按 maxStep 切分并沿墙滑动，落点不可控。）
                int startIdx = points.Count > 0 ? (startOffset + i) % points.Count : 0;
                var rotated = new List<Vec2>(points.Count);
                for (int k = 0; k < points.Count; k++) rotated.Add(points[(startIdx + k) % points.Count]);

                var brain = new MonsterBrain(id, cfg, rotated[0], rotated);
                // 移动解析注入：这是"状态机已就绪但没人接线"缺的那一环
                brain.Mover = MakeMover(geo);
                _brains.Add(brain);
            }
        }

        /// <summary>
        /// 由关卡几何构造 MoveResolver（注入给 MonsterBrain）。
        /// 用与玩家**同一套** `LevelGeometry.Resolve`，避免"玩家能走怪不能走"或反过来的双口径问题。
        /// </summary>
        public static MoveResolver MakeMover(LevelGeometry geo, float radius = 0.34f)
        {
            if (geo == null) throw new ArgumentNullException(nameof(geo));
            return (Vec2 from, Vec2 to, float maxStep, out bool blocked) =>
            {
                float dx = to.X - from.X, dz = to.Z - from.Z;
                float len = (float)Math.Sqrt(dx * dx + dz * dz);
                // 只走 maxStep 这么远（MonsterBrain 传入 speed*dt）
                if (len > maxStep && len > 1e-6f)
                {
                    dx = dx / len * maxStep;
                    dz = dz / len * maxStep;
                }
                var r = geo.Resolve(from.X, from.Z, dx, dz, radius);
                blocked = r.Blocked;
                return new Vec2(r.X, r.Z);
            };
        }

        /// <summary>把一条刺激分发给所有能听见它的怪物（判定用 Hearing，不自己拍脑袋比距离）。</summary>
        public void EmitStimulus(in Stimulus stim)
        {
            LastHeardCount = 0;
            for (int i = 0; i < _brains.Count; i++)
            {
                var b = _brains[i];
                var ctx = new HearingContext { PerceptionBonus = b.PerceptionBonus };
                var r = _hearing.CanHear(b.Id, stim, b.Position.X, b.Position.Z, ctx);
                if (!r.Audible) continue;
                b.OnStimulus(stim);
                LastHeardCount++;
            }
        }

        /// <summary>
        /// 视线判定：距离 ≤ sightRangeM ×(1+感知加成) **且** 沿途无墙。
        /// 为什么必须查墙：只比距离会让怪物"隔三堵墙看见你"，玩家会觉得游戏在耍赖。
        /// 采用网格步进采样（近似），格尺寸 0.5m 下 0.25m 步长足够。
        /// </summary>
        public bool CanSee(string monsterId, Vec2 monsterPos, Vec2 playerPos, float perceptionBonus = 0f)
        {
            float range = _sightRangeM[monsterId] * (1f + perceptionBonus);
            float dx = playerPos.X - monsterPos.X, dz = playerPos.Z - monsterPos.Z;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            if (dist > range) return false;
            if (dist < 1e-3f) return true;

            int steps = (int)Math.Ceiling(dist / SightSampleStepM);
            for (int i = 1; i < steps; i++)   // 端点不采（怪脚下与玩家脚下本身就是可走的）
            {
                float t = (float)i / steps;
                if (!_geo.Passable(monsterPos.X + dx * t, monsterPos.Z + dz * t)) return false;
            }
            return true;
        }

        /// <summary>
        /// 推进所有怪物一帧。返回可渲染状态。
        /// </summary>
        /// <param name="tick">对局 tick（60/s）。</param>
        /// <param name="playerPos">玩家位置（追击目标）。</param>
        /// <param name="playerNoiseLevel01">玩家噪音水平 0..1（跑动/说话会抬高；用于视野外的"被注意到"）。</param>
        public MonsterView[] Tick(long tick, Vec2 playerPos, float playerNoiseLevel01 = 0f)
        {
            var views = new MonsterView[_brains.Count];
            for (int i = 0; i < _brains.Count; i++)
            {
                var b = _brains[i];
                bool seen = CanSee(b.Id, b.Position, playerPos, b.PerceptionBonus);
                var r = b.Step(tick, seen, playerPos);
                views[i] = new MonsterView(b.Id, _labels[b.Id], r.State, r.Position.X, r.Position.Z,
                    b.SpeedMps * (r.State == "chase" ? b.ChaseSpeedScale : 1f), seen);
            }
            return views;
        }

        /// <summary>按关卡房间生成巡逻点（每间房取一个空可走格；怪在房里转，而不是挤在原点）。</summary>
        public static List<Vec2> PatrolPointsFromLevel(LevelGeometry geo, LevelData level)
        {
            var pts = new List<Vec2>();
            if (geo == null || level == null) return pts;
            foreach (var room in level.Rooms)
                if (geo.TryFindFreeCell(room.CenterX, room.CenterZ, out float fx, out float fz))
                    pts.Add(new Vec2(fx, fz));
            if (pts.Count == 0) pts.Add(new Vec2(0f, 0f));
            return pts;
        }

        /// <summary>三怪的 id 顺序（V9 §7：教学怪 → 核心怪 → 终局怪，难度递增）。</summary>
        public static readonly string[] DefaultMonsterIds = { "stitcher", "whisperer", "coroner" };
    }
}
