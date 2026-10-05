using System;
using System.Collections.Generic;
using Whisper.Gameplay.Config;

namespace Whisper.Gameplay.Monsters
{
    /// <summary>
    /// 鬼房（Ghost Room）—— 用户 2026-10-04 晚澄清的语义。
    ///
    /// ## 用户原话与我的错误
    /// > 「不是鬼所处的房间就是鬼房，**鬼房是一个设定**，证据**灵球只在鬼房**能被看见，
    /// >   游戏途中鬼**可能会换鬼房**，概率看选择的难度」
    ///
    /// 我此前把它实现成"鬼现在在哪，哪就是鬼房"（每帧用怪物坐标反查房间），这是错的，
    /// 而且一错错三处：
    /// ① **温度**：鬼房跟着鬼跑 → 鬼一离开就回暖、"那个房间冷过"这件事留不下痕迹；
    /// ② **灵球证据**：没有"只在鬼房可见"的判据，灵球就没法用来确认鬼房；
    /// ③ **换鬼房**：逻辑上不可能发生。
    ///
    /// ## 正确语义
    /// · 鬼房是**开局选定的一个房间**，是一个**设定** —— 不随鬼移动而变；
    /// · 鬼房**恒冷**（"被设定过的房间"），与"鬼周围瞬时降温"是**两件独立的事**，可同时存在；
    /// · **灵球只在鬼房可见**；
    /// · 鬼**可以在途中换鬼房**：`minIntervalSec` 到点后**掷一次**概率（按难度），命中才换。
    ///
    /// ## 为什么"到点掷一次"而不是"每帧掷"
    /// 用户说"概率看难度"但没说频率。若每帧掷，换个不停，鬼房就失去"一个设定"的意义。
    /// 所以是**最小间隔 + 到点掷一次** —— 与猎杀节奏（`HuntScheduler`）同一套设计语言：
    /// 间隔只是触发条件，实际发生靠概率，因此"很久不换"也是正常结果。
    ///
    /// ## 确定性
    /// 换房用**注入的确定性随机源**（联机各端必须换到同一个房间，否则灵球/温度全不同步）。
    /// </summary>
    public sealed class GhostRoom
    {
        readonly float _minIntervalSec;
        readonly float _relocateChance;
        readonly Func<float> _roll;
        readonly List<string> _candidates = new List<string>();

        float _sinceLastChange;

        /// <summary>当前鬼房 id（null = 还没选定）。</summary>
        public string RoomId { get; private set; }
        /// <summary>换过几次鬼房（0 = 仍是开局那间）。</summary>
        public int RelocationCount { get; private set; }
        /// <summary>本帧是否**刚刚**换了鬼房（用于触发一次性提示/灵球刷新）。</summary>
        public bool JustRelocated { get; private set; }
        /// <summary>距下次可以掷"是否换房"还有多少秒。</summary>
        public float NextRollInSec => Math.Max(0f, _minIntervalSec - _sinceLastChange);
        /// <summary>本局难度 id。</summary>
        public string Difficulty { get; }

        /// <summary>
        /// 构造。`difficulty` 决定换房概率；`roll` 注入确定性随机源（联机必须各端同种子）。
        /// </summary>
        public GhostRoom(GameConfigReader cfg, string difficulty, Func<float> roll)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            _roll = roll ?? throw new ArgumentNullException(nameof(roll), "换鬼房必须有确定性随机源（联机各端要换到同一间）");

            Difficulty = string.IsNullOrEmpty(difficulty)
                ? (GameConfig.Get("ghostRoom.relocate.defaultDifficulty") as string ?? "normal")
                : difficulty;

            _minIntervalSec = Req(cfg, "ghostRoom.relocate.minIntervalSec");
            _relocateChance = Req(cfg, $"ghostRoom.relocate.perDifficulty.{Difficulty}.chance");
            if (_relocateChance < 0f || _relocateChance > 1f)
                throw new InvalidOperationException($"ghostRoom.relocate.perDifficulty.{Difficulty}.chance 必须在 [0,1]");
            if (_minIntervalSec <= 0f)
                throw new InvalidOperationException("ghostRoom.relocate.minIntervalSec 必须为正 —— 否则会换个不停，鬼房就不再是「一个设定」");
        }

        /// <summary>注册候选房间（只有这些房间可能成为鬼房）。</summary>
        public void AddCandidate(string roomId)
        {
            if (!string.IsNullOrEmpty(roomId) && !_candidates.Contains(roomId)) _candidates.Add(roomId);
        }

        /// <summary>
        /// 开局选鬼房。**由种子/序号决定**，不依赖随机源 —— 各端用同样的候选表就得到同一间。
        /// `pick` 传入一个 0..1 的确定值（例如由局种子派生），落在哪间就选哪间。
        /// </summary>
        public void SelectInitial(float pick)
        {
            if (_candidates.Count == 0) throw new InvalidOperationException("还没有注册任何候选房间");
            int i = (int)(pick * _candidates.Count);
            if (i < 0) i = 0;
            if (i >= _candidates.Count) i = _candidates.Count - 1;
            RoomId = _candidates[i];
            _sinceLastChange = 0f;
            RelocationCount = 0;
            JustRelocated = false;
        }

        /// <summary>
        /// 推进 dt 秒。到点后**掷一次**概率决定是否换房；命中则换到**另一间**（不会换到同一间）。
        /// 返回 true 表示本帧换了鬼房。
        /// </summary>
        public bool Tick(float dtSec)
        {
            JustRelocated = false;
            if (dtSec <= 0f || RoomId == null) return false;

            _sinceLastChange += dtSec;
            if (_sinceLastChange < _minIntervalSec) return false;

            _sinceLastChange = 0f;                      // 无论是否换，都重置计时（否则会每帧连掷）
            if (_candidates.Count < 2) return false;    // 只有一间可选 → 无从换起
            if (_roll() >= _relocateChance) return false;   // 没掷中：保持原鬼房（"很久不换"是正常结果）

            // 换到**另一间**：把当前间从候选里排除后按同一次掷值取
            var others = new List<string>(_candidates);
            others.Remove(RoomId);
            float p = _roll();
            int i = (int)(p * others.Count);
            if (i < 0) i = 0;
            if (i >= others.Count) i = others.Count - 1;
            RoomId = others[i];
            RelocationCount++;
            JustRelocated = true;
            return true;
        }

        /// <summary>灵球是否在这个房间可见（用户：**只在鬼房可见**）。</summary>
        public bool OrbVisibleIn(string roomId)
            => !string.IsNullOrEmpty(roomId) && string.Equals(roomId, RoomId, StringComparison.Ordinal);

        /// <summary>该房间是否就是鬼房。</summary>
        public bool IsGhostRoom(string roomId)
            => !string.IsNullOrEmpty(roomId) && string.Equals(roomId, RoomId, StringComparison.Ordinal);

        /// <summary>HUD 描述。</summary>
        public string Describe()
            => RoomId == null
                ? "鬼房：未选定"
                : $"鬼房 {RoomId}（换过 {RelocationCount} 次 · 难度 {Difficulty} · 下次可换 {NextRollInSec:0}s）";

        float Req(GameConfigReader cfg, string path)
        {
            var raw = GameConfig.Get(path);
            if (raw is long l) return l;
            if (raw is double d) return (float)d;
            if (raw is int i) return i;
            throw new InvalidOperationException($"配置缺 {path} —— 鬼房机制不许硬编码数值");
        }
    }
}
