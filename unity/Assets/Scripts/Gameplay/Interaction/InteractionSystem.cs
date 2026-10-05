using System;
using System.Collections.Generic;
using Whisper.Gameplay.Config;
using Whisper.Gameplay.Hearing;
using Whisper.Gameplay.Power;

namespace Whisper.Gameplay.Interaction
{
    /// <summary>鬼的一次互动种类（官方均有这类"存在感"事件）。</summary>
    public enum GhostInteract
    {
        ToggleDoor,
        ToggleLight,
        ThrowObject,
        Knock,
        BreakerOff,
    }

    /// <summary>一次互动的结果（供 HUD、EMF、音频消费）。</summary>
    public struct InteractEvent
    {
        public GhostInteract Kind;
        public string RoomId;
        public float X, Z;
        /// <summary>产生的声纹强度（喂给 Hearing 体系）。0 = 无声。</summary>
        public float SoundIntensity;
        public float SoundRadiusM;
        public string Text;
    }

    /// <summary>
    /// 互动系统（恐鬼症对齐 · spec §3.5）。
    ///
    /// ## 为什么这是"恐怖感"的核心而不是装饰
    /// 官方里玩家在**看不到鬼**的时候靠什么确认"它在"？就是这些互动：门自己开、灯自己灭、
    /// 东西被扔、敲击声。它们把"看不见的威胁"变成**可感知的事件**。
    /// 本工程的 Hearing/Stimulus 体系已经能把声响变成怪物的感知输入 —— 所以鬼互动是**双向**的：
    /// 玩家听得到（怕），鬼也知道玩家在哪（危险）。
    ///
    /// ## 玩家侧互动
    /// · 开关灯（按房间，经 <see cref="PowerSystem"/>）
    /// · 切总闸（同上）
    /// · 开关门与拾取物已在 <c>ItemSystem</c> / <c>LevelBuilder.Doors</c> 实现，本类不重复
    ///
    /// ## 确定性
    /// 鬼互动的**时机**由外部节拍驱动（<see cref="Tick"/> 传入 dt 与"间隔"），**概率**用
    /// 调用方给的确定性随机源（本类只消费 0..1 的数，不自己取随机数）——
    /// 这样 gate-physics 的"禁 UnityEngine.Random / DateTime"纪律不会被绕过，且联机可复现。
    /// </summary>
    public sealed class InteractionSystem
    {
        readonly PowerSystem _power;
        readonly float _ghostIntervalMin, _ghostIntervalMax;
        readonly float _knockIntensity, _knockRadius;
        readonly float _throwIntensity, _throwRadius;
        readonly float _lightToggleIntensity, _lightToggleRadius;
        readonly float _doorIntensity, _doorRadius;

        float _nextGhostInteractIn;
        int _count;
        InteractEvent _last;

        /// <summary>累计发生的鬼互动次数（HUD/自检可核）。</summary>
        public int GhostInteractCount => _count;
        /// <summary>最近一次鬼互动（HUD 显示"刚刚：灯灭了"）。</summary>
        public InteractEvent Last => _last;
        /// <summary>最近一次互动的可读文案（空 = 还没有过）。</summary>
        public string LastText { get; private set; } = "";

        /// <summary>发生鬼互动时触发（音频/相机抖动/HUD 订阅）。</summary>
        public event Action<InteractEvent> OnGhostInteract;

        public InteractionSystem(GameConfigReader cfg, PowerSystem power)
        {
            _power = power;
            _ghostIntervalMin = cfg.Float("interaction.ghost.intervalMinSec", 8f);
            _ghostIntervalMax = cfg.Float("interaction.ghost.intervalMaxSec", 22f);
            _knockIntensity = cfg.Float("interaction.ghost.knock.intensity", 55f);
            _knockRadius = cfg.Float("interaction.ghost.knock.radiusM", 14f);
            _throwIntensity = cfg.Float("interaction.ghost.throw.intensity", 70f);
            _throwRadius = cfg.Float("interaction.ghost.throw.radiusM", 18f);
            _lightToggleIntensity = cfg.Float("interaction.ghost.light.intensity", 25f);
            _lightToggleRadius = cfg.Float("interaction.ghost.light.radiusM", 12f);
            _doorIntensity = cfg.Float("interaction.ghost.door.intensity", 45f);
            _doorRadius = cfg.Float("interaction.ghost.door.radiusM", 12f);
            _nextGhostInteractIn = _ghostIntervalMin;
        }

        /// <summary>
        /// 推进节拍。`r01` 是**确定性随机数**（0..1），由调用方按匹配种子派生
        /// （本类不自己取随机 —— 见类注释的纪律）。
        /// </summary>
        /// <param name="dt">帧步长（秒）。</param>
        /// <param name="roll">0..1 的确定性随机数工厂（每次调用给一个新值）。</param>
        /// <param name="ghostRoomId">鬼当前所在房间（灯/门互动优先发生在这里）。</param>
        /// <param name="ghostX">鬼位置 X（互动事件的位置）。</param>
        /// <param name="ghostZ">鬼位置 Z。</param>
        /// <param name="rooms">场景房间清单（供挑一个房间做互动）。</param>
        public void Tick(float dt, Func<float> roll, string ghostRoomId, float ghostX, float ghostZ,
                         IReadOnlyList<string> rooms)
        {
            if (dt <= 0f) return;
            _nextGhostInteractIn -= dt;
            if (_nextGhostInteractIn > 0f) return;

            // 下一次的间隔
            float span = Math.Max(0.5f, _ghostIntervalMax - _ghostIntervalMin);
            _nextGhostInteractIn = _ghostIntervalMin + span * Safe(roll);

            var ev = Pick(roll, ghostRoomId, ghostX, ghostZ, rooms);
            _count++;
            _last = ev;
            LastText = ev.Text;
            OnGhostInteract?.Invoke(ev);
        }

        InteractEvent Pick(Func<float> roll, string ghostRoom, float gx, float gz, IReadOnlyList<string> rooms)
        {
            float r = Safe(roll);
            // 权重（官方这些行为都有；权重是本工程设计值，写在这里是为了可读，数值本身在配置里
            // 通过各行为的 intensity/radius 间接体现）
            if (r < 0.30f) return MakeDoor(roll, ghostRoom, gx, gz);
            if (r < 0.58f) return MakeLight(roll, ghostRoom, gx, gz, rooms);
            if (r < 0.80f) return MakeThrow(roll, ghostRoom, gx, gz);
            if (r < 0.94f) return MakeKnock(roll, ghostRoom, gx, gz);
            return MakeBreakerOff(ghostRoom, gx, gz);
        }

        InteractEvent MakeDoor(Func<float> roll, string room, float x, float z)
            => new InteractEvent
            {
                Kind = GhostInteract.ToggleDoor, RoomId = room, X = x, Z = z,
                SoundIntensity = _doorIntensity, SoundRadiusM = _doorRadius,
                Text = "有门自己动了",
            };

        InteractEvent MakeLight(Func<float> roll, string room, float x, float z, IReadOnlyList<string> rooms)
        {
            // 优先动鬼所在房间的灯；鬼房没灯就随便挑一个有灯的
            string target = room;
            if (target == null || !_power.IsSwitchOn(target))
            {
                if (rooms != null && rooms.Count > 0)
                    target = rooms[(int)(Safe(roll) * rooms.Count) % rooms.Count];
            }
            bool changed = target != null && _power.ToggleRoomLight(target);
            return new InteractEvent
            {
                Kind = GhostInteract.ToggleLight, RoomId = target, X = x, Z = z,
                SoundIntensity = _lightToggleIntensity, SoundRadiusM = _lightToggleRadius,
                Text = changed ? $"「{target}」的灯自己" + (_power.IsRoomLit(target) ? "亮了" : "灭了") : "灯开关轻响了一下",
            };
        }

        InteractEvent MakeThrow(Func<float> roll, string room, float x, float z)
            => new InteractEvent
            {
                Kind = GhostInteract.ThrowObject, RoomId = room, X = x, Z = z,
                SoundIntensity = _throwIntensity, SoundRadiusM = _throwRadius,
                Text = "有东西被扔了出去",
            };

        InteractEvent MakeKnock(Func<float> roll, string room, float x, float z)
            => new InteractEvent
            {
                Kind = GhostInteract.Knock, RoomId = room, X = x, Z = z,
                SoundIntensity = _knockIntensity, SoundRadiusM = _knockRadius,
                Text = "墙上传来三下敲击",
            };

        InteractEvent MakeBreakerOff(string room, float x, float z)
        {
            // 只在总闸开着时才可能被关掉（否则"关一个已经关的总闸"没有意义，也不给玩家压力）
            bool did = _power != null && _power.BreakerOn && Safe(null) < _power.GhostOffChance + 1f;
            if (_power != null && _power.BreakerOn) did = _power.ToggleBreaker(false);
            return new InteractEvent
            {
                Kind = GhostInteract.BreakerOff, RoomId = room, X = x, Z = z,
                SoundIntensity = 0f, SoundRadiusM = 0f,   // 断电本身无声（灯突然全灭才是信号）
                Text = did ? "整栋楼的灯一起灭了 —— 总闸被关了" : "电闸那边响了一下",
            };
        }

        static float Safe(Func<float> roll)
        {
            if (roll == null) return 0.5f;
            float v = roll();
            if (v < 0f) return 0f;
            if (v >= 1f) return 0.999999f;
            return v;
        }

        /// <summary>把互动转成声纹（喂给 Hearing）——本工程已有的体系，不重复实现感知。</summary>
        /// <remarks>
        /// ⚠ `Stimulus` 是**带构造函数的 struct**（字段 SourceKey/Type/Intensity/RadiusM(float?)/…），
        /// 我第一版用对象初始化器只赋了 4 个字段 → 编译不过。必须走它的构造函数。
        /// </remarks>
        public static Stimulus ToStimulus(in InteractEvent e, long tick = 0)
            => new Stimulus(
                sourceKey: "ghost_" + e.Kind.ToString().ToLowerInvariant(),
                type: "ghost",
                intensity: e.SoundIntensity,
                radiusM: e.SoundRadiusM,
                globalBroadcast: false,
                x: e.X, z: e.Z, tick: tick);

        /// <summary>HUD 一行摘要。</summary>
        public string Describe()
            => _count == 0 ? "互动：暂无" : $"互动：{_count} 次 · 最近「{LastText}」";
    }
}
