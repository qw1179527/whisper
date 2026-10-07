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

        /// <summary>
        /// 视角偏航（度，绕 Y）。**只由 <see cref="Look"/> 改变**，不再由移动方向决定。
        ///
        /// 真机反馈修正（2026-10-03）：首版把 Yaw 设成"移动方向"，结果**玩家完全无法转头**——
        /// 回头看身后、边走边环顾都做不到，第一人称恐怖游戏最重要的操作直接缺失。
        /// 正确定义：Yaw/Pitch 属于**视角**，移动输入是**视角相对**的（WASD 语义），
        /// 两者正交。这也是"能走"与"能看"必须分开的原因。
        /// </summary>
        public float YawDeg { get; private set; }

        /// <summary>视角俯仰（度）。夹在 ±<see cref="MaxPitchDeg"/>，防止翻过头。</summary>
        public float PitchDeg { get; private set; }

        /// <summary>俯仰上限（度）。第一人称惯例：略小于 90，避免万向节附近的翻转观感。</summary>
        public const float MaxPitchDeg = 80f;

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

        /// <summary>
        /// 转动视角（由触屏右半屏拖拽/鼠标位移驱动）。**与移动解耦**。
        /// 俯仰夹在 ±<see cref="MaxPitchDeg"/>；偏航归一化到 [0,360)。
        /// </summary>
        public void Look(float deltaYawDeg, float deltaPitchDeg)
        {
            float y = YawDeg + deltaYawDeg;
            y %= 360f;
            if (y < 0f) y += 360f;
            YawDeg = y;

            float p = PitchDeg + deltaPitchDeg;
            if (p > MaxPitchDeg) p = MaxPitchDeg;
            if (p < -MaxPitchDeg) p = -MaxPitchDeg;
            PitchDeg = p;
        }

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
            // 输入是**视角相对**的（WASD 语义）：摇杆向上 = 朝"面朝方向"走，而不是朝世界 +Z 走。
            // 真机反馈修正（2026-10-03）：首版把输入当世界方向、并把 Yaw 设成移动方向，
            // 于是"转头"这个操作根本不存在——视角被移动绑死。现在两者正交：
            // 朝向由 Look() 决定，移动方向 = 输入按 Yaw 旋转。
            float inX = inputX / len, inZ = inputZ / len;
            double yawRad = YawDeg * Math.PI / 180.0;
            float cos = (float)Math.Cos(yawRad), sin = (float)Math.Sin(yawRad);
            // 绕 Y 轴旋转（Unity 的左手系：yaw=0 面向 +Z，yaw=90° 面向 +X）
            float nx = inX * cos + inZ * sin;
            float nz = -inX * sin + inZ * cos;
            // 归一化保护：cos/sin 的组合在浮点下可能略偏，避免"斜走比直走快"重现
            float nlen = (float)Math.Sqrt(nx * nx + nz * nz);
            if (nlen > 1e-6f) { nx /= nlen; nz /= nlen; }

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
