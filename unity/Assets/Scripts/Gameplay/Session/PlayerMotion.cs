using System;
using Whisper.Gameplay.Config;
using Whisper.Gameplay.Level;

namespace Whisper.Gameplay.Session
{
    /// <summary>移动形态（V9 §7：形态决定脚步刺激强度与半径）。</summary>
    public enum MoveMode
    {
        Crouch = 0,
        Walk = 1,
        Run = 2,
    }

    /// <summary>一步移动的结果。</summary>
    public readonly struct MoveStep
    {
        /// <summary>本步实际位移（米）。被墙挡住时会小于期望值。</summary>
        public readonly float MovedM;
        /// <summary>本步是否被挡（供 HUD/动画判定"撞墙"）。</summary>
        public readonly bool Blocked;
        /// <summary>
        /// 本步是否跨过了一个"步幅"，需要发出脚步刺激；非 null 时值为刺激源 key
        /// （`walk_footstep` / `run_footstep` / `crouch_footstep`，V9 §7 由移动形态决定）。
        /// </summary>
        public readonly string FootstepStimulusKey;

        public MoveStep(float movedM, bool blocked, string footstepKey)
        {
            MovedM = movedM; Blocked = blocked; FootstepStimulusKey = footstepKey;
        }
    }

    /// <summary>
    /// 玩家移动的**纯逻辑层**（不引用 UnityEngine → 本机可真编译真跑断言）。
    ///
    /// 为什么拆两层：`Resolve` 的碰撞语义（分离轴 + 子步进）是玩法正确性的核心，
    /// 必须能在没有 Unity 的机器上被测到；而读触摸、驱动 Transform/相机只能靠引擎。
    /// 本类负责前者，`Runtime/PlayerController.cs` 负责后者。这与
    /// LevelBuilder（引擎侧）/ LevelAssembly（纯逻辑）的拆法一致。
    ///
    /// 速度**必须来自配置**（V9 §7 原注：「速度必须配置化，代码不得硬编码」）——
    /// 因此本类在构造时就把三个速度读出来并校验，缺配置直接抛错（宁可在 Boot 期红字暴露，
    /// 也不要静默用一个编造的默认速度跑起来）。
    /// </summary>
    public sealed class PlayerMotion
    {
        /// <summary>
        /// 玩家碰撞半径（米）。**不是配置值**：V9 未定义该字段，它是与关卡格尺寸配套的几何常量，
        /// 与 `EvidencePlacer.AgentRadiusM` 必须一致（否则"能站的地方"与"能走的地方"两套口径）。
        /// </summary>
        public const float AgentRadiusM = 0.34f;

        /// <summary>步幅（米）。**不是配置值**：V9 未定义；仅用于决定多久发一次脚步刺激。</summary>
        public const float StrideLengthM = 0.75f;

        readonly GameConfigReader _cfg;

        public float X { get; private set; }
        public float Z { get; private set; }
        /// <summary>朝向（度，绕 Y）。第一人称下由移动方向决定。</summary>
        public float YawDeg { get; private set; }

        public float WalkSpeedMps { get; }
        public float RunSpeedMps { get; }
        public float CrouchSpeedMps { get; }

        /// <summary>累计走过的距离（米）——脚步刺激按它跨步幅触发。</summary>
        public float DistanceTravelledM { get; private set; }
        /// <summary>上次发出脚步刺激时的累计距离。</summary>
        float _lastFootstepAtM;
        /// <summary>最近一步是否被挡（HUD 用）。</summary>
        public bool LastStepBlocked { get; private set; }

        public PlayerMotion(GameConfigReader cfg, float x = 0f, float z = 0f)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            WalkSpeedMps = RequireSpeed("player.walkSpeedMps");
            RunSpeedMps = RequireSpeed("player.runSpeedMps");
            CrouchSpeedMps = RequireSpeed("player.crouchSpeedMps");
            if (RunSpeedMps < WalkSpeedMps || WalkSpeedMps < CrouchSpeedMps)
                throw new InvalidOperationException(
                    $"配置不自洽：跑 {RunSpeedMps} ≥ 走 {WalkSpeedMps} ≥ 蹲 {CrouchSpeedMps} 必须成立（V9 §7）");
            X = x; Z = z;
        }

        float RequireSpeed(string path)
        {
            float v = _cfg.Float(path, float.NaN);
            // 不写死默认值：写死一个"看起来合理"的速度，恰好是本项目最忌讳的
            // "代码里藏一份数值真源"。缺配置就在 Boot 期红字报出来。
            if (float.IsNaN(v) || v <= 0f)
                throw new InvalidOperationException($"配置缺失或非法：{path}（V9 §7 要求速度配置化，代码不得硬编码）");
            return v;
        }

        public float SpeedFor(MoveMode mode) =>
            mode == MoveMode.Run ? RunSpeedMps : mode == MoveMode.Crouch ? CrouchSpeedMps : WalkSpeedMps;

        public void SetPosition(float x, float z) { X = x; Z = z; }
        public void SetYaw(float deg) => YawDeg = deg;

        /// <summary>刺激源 key（V9 §7：移动形态 → 脚步刺激）。</summary>
        public static string StimulusKeyFor(MoveMode mode) =>
            mode == MoveMode.Run ? "run_footstep" : mode == MoveMode.Crouch ? "crouch_footstep" : "walk_footstep";

        /// <summary>
        /// 走一步。
        /// </summary>
        /// <param name="inputX">期望方向 X（会被归一化；与 inputZ 同为 0 表示不动）。</param>
        /// <param name="inputZ">期望方向 Z。</param>
        /// <param name="mode">移动形态（蹲/走/跑）。</param>
        /// <param name="dtSec">本步时长（秒）。</param>
        /// <param name="geo">关卡几何（提供分离轴 + 子步进碰撞解析）。</param>
        /// <param name="maxDtSec">单步时长上限——防止卡帧导致一步穿过整面墙。</param>
        public MoveStep Step(float inputX, float inputZ, MoveMode mode, float dtSec,
            LevelGeometry geo, float maxDtSec = 0.1f)
        {
            if (geo == null) throw new ArgumentNullException(nameof(geo));
            if (dtSec <= 0f) return new MoveStep(0f, false, null);

            // 卡帧保护：子步进能防穿墙，但 dt 本身也要封顶（否则单帧 1 秒 = 5.6 米位移，
            // 子步进会把它切成很多小步，性能上没必要，且语义上不该"补走"卡帧期间的距离）。
            float dt = Math.Min(dtSec, maxDtSec);

            float len = (float)Math.Sqrt(inputX * inputX + inputZ * inputZ);
            if (len <= 1e-4f)
            {
                LastStepBlocked = false;
                return new MoveStep(0f, false, null);
            }
            float nx = inputX / len, nz = inputZ / len;
            YawDeg = (float)(Math.Atan2(nx, nz) * 180.0 / Math.PI);   // 朝向移动方向

            float speed = SpeedFor(mode);
            var r = geo.Resolve(X, Z, nx * speed * dt, nz * speed * dt, AgentRadiusM);
            float moved = (float)Math.Sqrt((r.X - X) * (r.X - X) + (r.Z - Z) * (r.Z - Z));
            X = r.X; Z = r.Z;
            LastStepBlocked = r.Blocked;
            DistanceTravelledM += moved;

            // 跨过步幅就发一次脚步刺激（V9 §7：脚步是怪物听觉的主要来源之一）
            string key = null;
            if (DistanceTravelledM - _lastFootstepAtM >= StrideLengthM)
            {
                _lastFootstepAtM = DistanceTravelledM;
                key = StimulusKeyFor(mode);
            }
            return new MoveStep(moved, r.Blocked, key);
        }
    }
}
