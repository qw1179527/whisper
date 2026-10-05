using System;
using Whisper.Gameplay.Config;

namespace Whisper.Gameplay.Monsters
{
    /// <summary>
    /// 猎杀节奏调度（补充方案 2026-10-04 第三节 / 用户第 150 行）。
    ///
    /// ## 为什么单独一个类（而不是塞进 MonsterBrain）
    /// 用户对这条机制的要求非常具体，而且**明确否掉了最直觉的写法**：
    /// > "不是理智一到阈值就疯狂猎杀" · "理智越低越频繁，但绝不是一直猎杀" ·
    /// > "一般至少间隔 60s" · "Demon 最低可缩到 20s，但不是完全按这个时间" ·
    /// > "存在有时可以一直不猎杀的情形"
    ///
    /// 所以判据不是"理智 &lt; 阈值 → 猎杀"，而是：
    ///   **冷却（minIntervalSec）+ 概率判定（每秒，与理智负相关）**
    /// 机制本身是概率性的，因此"很久不猎杀"与"连着猎两次"都可能在合法范围内出现 ——
    /// 这正是设计意图。把它做成纯逻辑，就能在没有 Unity 的机器上按**固定随机源**验区间。
    ///
    /// ## 时间基准
    /// 全部用**累计秒**（调用方喂 `dt`），不读 `Time.time` —— 纯逻辑可测、可回放。
    /// </summary>
    public sealed class HuntScheduler
    {
        /// <summary>一次猎杀的生命周期。</summary>
        public enum Phase
        {
            /// <summary>不在猎杀（可能在冷却，也可能只是没掷中）。</summary>
            Idle,
            /// <summary>正在猎杀中（此时**不可能**再次触发猎杀）。</summary>
            Hunting,
        }

        readonly float _minIntervalSec;
        readonly float _baseChancePerSec;
        readonly float _sanityFactor;
        readonly float _maxChancePerSec;
        readonly float _huntDurationSec;
        readonly Func<float> _roll;          // 返回 [0,1)，注入以便测试用固定序列

        float _sinceHuntEnded;               // 距上次猎杀结束的秒数
        float _huntingLeft;                  // 本次猎杀剩余秒数
        int _huntCount;

        /// <summary>已完成（或进行中）的猎杀次数。测试与统计用。</summary>
        public int HuntCount => _huntCount;
        /// <summary>当前阶段。</summary>
        public Phase Current { get; private set; } = Phase.Idle;
        /// <summary>距下次**可以**开始猎杀还有多少秒（0 = 冷却已过；不代表一定会猎）。</summary>
        public float CooldownLeftSec => Math.Max(0f, _minIntervalSec - _sinceHuntEnded);
        /// <summary>本次猎杀剩余秒数（不在猎杀时为 0）。</summary>
        public float HuntingLeftSec => _huntingLeft;

        /// <summary>
        /// 从配置构造。`monsterId` 用来取该鬼的**专属冷却/概率覆盖**（如 Demon 的 20s）。
        /// 缺配置 → 抛可读异常（本项目的纪律：不许代码里藏第二份数值真源）。
        /// </summary>
        public HuntScheduler(GameConfigReader cfg, string monsterId, Func<float> roll = null)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            _roll = roll ?? DefaultRoll;

            _minIntervalSec = Float($"hunt.perMonster.{monsterId}.minIntervalSec",
                                    Float("hunt.default.minIntervalSec", 0f));
            _baseChancePerSec = Float($"hunt.perMonster.{monsterId}.baseChancePerSec",
                                      Float("hunt.default.baseChancePerSec", 0f));
            _sanityFactor = Float("hunt.default.sanityFactor", 0f);
            _maxChancePerSec = Float("hunt.default.maxChancePerSec", 0f);
            _huntDurationSec = Float("hunt.default.huntDurationSec", 0f);

            if (_minIntervalSec <= 0f)
                throw new InvalidOperationException("配置缺 hunt.default.minIntervalSec（或非正）—— 猎杀节奏必须有冷却，否则会退化成「到阈值就疯狂猎杀」");
            if (_baseChancePerSec <= 0f)
                throw new InvalidOperationException("配置缺 hunt.default.baseChancePerSec（或非正）—— 没有概率就没有「有时不猎杀」的语义");
            if (_huntDurationSec <= 0f)
                throw new InvalidOperationException("配置缺 hunt.default.huntDurationSec（或非正）");
        }

        /// <summary>
        /// 默认随机源 = **确定性 PRNG**（xorshift32）。
        ///
        /// 【为什么不用 `System.Random`】`gate-physics` 判红过这一点，而且它是对的：
        /// `System.Random` 的默认种子来自系统时间，**跨端不可复现**；本工程是 60 Tick 同步的联机游戏，
        /// 猎杀判定必须各端**逐帧一致**，否则会出现"主机那边已经开猎、客户端还没开"的不同步。
        /// 改成自带状态的确定性 PRNG 后：同一种子 → 同一序列 → 各端猎杀时刻完全一致，
        /// 而且测试可以注入种子复现任何一次猎杀序列。
        /// </summary>
        static float DefaultRoll() => NextDeterministic();

        // xorshift32：状态必须非 0；本工程只需要"确定性 + 分布够用"，不需要密码学强度
        static uint _prngState = 0x9E3779B9u;

        /// <summary>由 host 统一下发种子（联机时各端用同一个种子）。0 会被规整为非 0。</summary>
        public static void Seed(uint seed) => _prngState = seed == 0u ? 0x9E3779B9u : seed;

        static float NextDeterministic()
        {
            uint x = _prngState;
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            _prngState = x;
            // 取高 24 位映射到 [0,1)：避免低位质量差
            return (x >> 8) / 16777216f;
        }

        /// <summary>本帧的**每秒猎杀概率**（已按理智曲线放大并封顶）。公开出来便于断言与调试。</summary>
        public float ChancePerSec(float sanity, float threshold)
        {
            if (threshold <= 0f) return 0f;
            if (sanity > threshold) return 0f;                     // 未到阈值：触发条件不成立
            float below = 1f - Math.Max(0f, sanity) / threshold;   // 0 = 刚过阈值，1 = 理智归零
            return Math.Min(_maxChancePerSec, _baseChancePerSec * (1f + _sanityFactor * below));
        }

        /// <summary>
        /// 推进一帧。
        /// **取消条件（必须全部满足才可能开始猎杀）**：
        ///   ① 当前不在猎杀中（猎杀中不能再次猎杀）
        ///   ② 冷却已过（距上次猎杀结束 ≥ minIntervalSec）
        ///   ③ 理智 ≤ 该鬼的猎杀阈值（阈值只是**触发条件**，不是"到了就猎"）
        ///   ④ 概率掷中（每秒概率见 `ChancePerSec`，与理智负相关）
        /// 返回 true 表示**本帧开始了一次猎杀**。
        /// </summary>
        public bool Tick(float dtSec, float sanity, float threshold)
        {
            if (dtSec <= 0f) return false;

            if (Current == Phase.Hunting)
            {
                _huntingLeft -= dtSec;
                if (_huntingLeft <= 0f) { _huntingLeft = 0f; Current = Phase.Idle; _sinceHuntEnded = 0f; }
                return false;                      // 猎杀中永不再次猎杀
            }

            _sinceHuntEnded += dtSec;
            if (_sinceHuntEnded < _minIntervalSec) return false;    // 冷却未过

            float p = ChancePerSec(sanity, threshold);
            if (p <= 0f) return false;                              // 未到阈值

            // 概率判定：按 dt 折算（每秒 p → 本帧 p*dt，钳到 1）
            float hit = Math.Min(1f, p * dtSec);
            if (_roll() >= hit) return false;                       // 没掷中 —— "有时可以一直不猎杀"

            BeginHunt();
            return true;
        }

        /// <summary>
        /// 由外部事件**强制**开始一次猎杀（诅咒猎杀 / Onryo 蜡烛被吹灭 / Gallu 暴怒 等）。
        /// 这些来源**无视理智与冷却**（补充方案第五节），调用方负责传 `ignoreCooldown: true`。
        /// 猎杀中调用会被忽略（猎杀中不能再次猎杀）。
        /// </summary>
        public bool ForceHunt(bool ignoreCooldown)
        {
            if (Current == Phase.Hunting) return false;
            if (!ignoreCooldown && _sinceHuntEnded < _minIntervalSec) return false;
            BeginHunt();
            return true;
        }

        void BeginHunt()
        {
            Current = Phase.Hunting;
            _huntingLeft = _huntDurationSec;
            _huntCount++;
        }

        float Float(string path, float fallback)
        {
            var raw = GameConfig.Get(path);
            if (raw is long l) return l;
            if (raw is double d) return (float)d;
            if (raw is int i) return i;
            return fallback;
        }
    }
}
