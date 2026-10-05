using System;
using System.Collections.Generic;
using Whisper.Gameplay.Config;

namespace Whisper.Gameplay.Objectives
{
    /// <summary>
    /// 局内任务（恐鬼症对齐：合同日志里的**可选目标 / Optional Objectives**）。
    ///
    /// ## 与"每日任务"的区别（这是两套东西，不能混为一谈）
    /// | | 局内任务（本类） | 每日/每周任务（<c>TaskSystem</c>） |
    /// |---|---|---|
    /// | 作用域 | **一局** | 跨局、按天/周刷新 |
    /// | 出现位置 | 合同日志 / 局内 HUD | 主界面任务板 |
    /// | 数量 | 每局固定几条（官方为 3 条量级） | 每日 3 + 每周 1 |
    /// | 奖励 | 结算时附加钱与经验 | 完成即发 |
    /// | 生成 | 按**本局种子**抽 | 按 dayIndex 抽 |
    ///
    /// ## 官方目标类型（可确证的目标名，见 phasmophobia.fandom.com/wiki/Objectives）
    /// 找证据 / EMF 5 级 / 拍到鬼 / 鬼事件 / 用熏香驱鬼 / 用十字架阻止猎杀 /
    /// 运动传感器 / 盐 / 蜡烛 / 通灵盒回应 / 鬼写书 / 清理脏水 等。
    /// **具体数值与抽取权重官方未取到** → 全部读 `data/config.json` 的 `objectives.*`，标 `design`。
    ///
    /// ## 确定性
    /// 按本局种子派生（xorshift32），**不读时钟** —— 与 gate-physics 的纪律一致，且联机可复现。
    /// </summary>
    public enum ObjectiveKind
    {
        FindEvidence,       // 找齐 N 条证据
        EmfLevel5,          // 读到一次 EMF 5 级
        PhotographGhost,    // 拍到鬼
        WitnessGhostEvent,  // 目击一次鬼事件
        RepelWithSmudge,    // 用熏香驱鬼
        StopHuntWithCrucifix, // 用十字架阻止一次猎杀
        MotionSensor,       // 触发运动传感器
        SaltFootprint,      // 用盐拍到足迹
        CandleLit,          // 点燃蜡烛
        SpiritBoxResponse,  // 通灵盒得到回应
        GhostWriting,       // 鬼写字
        DirtyWater,         // 清理脏水
    }

    /// <summary>一条局内任务。</summary>
    public sealed class Objective
    {
        public string Id;
        public ObjectiveKind Kind;
        /// <summary>目标次数（大多为 1）。</summary>
        public float Target = 1f;
        public string Text;
        public int RewardMoney;
        public int RewardXp;
        /// <summary>本局进度。</summary>
        public float Progress;
        public bool Completed;
        /// <summary>奖励是否已计入结算（防重复）。</summary>
        public bool Claimed;

        public float Ratio => Target <= 0f ? (Completed ? 1f : 0f) : Math.Min(1f, Progress / Target);

        public string Describe()
            => $"{(Completed ? "✔" : "○")} {Text}"
             + (Target > 1f ? $"（{Progress:0}/{Target:0}）" : "")
             + $" · +{RewardMoney} / +{RewardXp} 经验";
    }

    /// <summary>局内任务系统。</summary>
    public sealed class ObjectiveSystem
    {
        readonly List<Objective> _list = new List<Objective>();
        readonly GameConfigReader _cfg;
        int _count;
        uint _seed;

        /// <summary>生成失败原因（null = 正常）。</summary>
        public string LoadProblem { get; private set; }

        /// <summary>本局任务（只读）。</summary>
        public IReadOnlyList<Objective> All => _list;

        public ObjectiveSystem(GameConfigReader cfg)
        {
            _cfg = cfg;
            _count = cfg.Int("objectives.perContract", 3);
        }

        /// <summary>已完成的条数。</summary>
        public int CompletedCount
        {
            get { int n = 0; foreach (var o in _list) if (o.Completed) n++; return n; }
        }

        /// <summary>全部完成？</summary>
        public bool AllCompleted => _list.Count > 0 && CompletedCount == _list.Count;

        /// <summary>
        /// 为一局生成任务。**幂等**：同一 seed 重复调用不重掷（重掷会丢进度）。
        /// </summary>
        public void BeginContract(uint seed)
        {
            if (seed == _seed && _list.Count > 0) return;
            _seed = seed;
            _list.Clear();

            var pool = ReadPool();
            if (pool.Count == 0) { LoadProblem = "配置里没有 objectives.pool"; return; }

            var used = new HashSet<int>();
            uint st = seed ^ 0x6D2B79F5u;
            for (int i = 0; i < _count && used.Count < pool.Count; i++)
            {
                int idx = PickIndex(ref st, pool.Count, used);
                if (idx < 0) break;
                used.Add(idx);
                _list.Add(Make(pool[idx], i));
            }
            LoadProblem = _list.Count == 0 ? "objectives.pool 抽不出条目" : null;
        }

        // ── 局内事件上报（由玩法层调用；每个方法对应一族官方目标） ──

        /// <summary>证据：已收 / 总数。</summary>
        public void ReportEvidence(int collected, int total)
        {
            foreach (var o in _list)
            {
                if (o.Kind != ObjectiveKind.FindEvidence || o.Completed) continue;
                o.Progress = collected >= total && total > 0 ? o.Target : Math.Min(collected, o.Target);
                if (o.Progress >= o.Target) o.Completed = true;
            }
        }
        /// <summary>EMF 读数（5 级才算达成官方那条）。</summary>
        public void ReportEmfLevel(int level) => Bump(ObjectiveKind.EmfLevel5, level >= 5 ? 1f : 0f, replace: level >= 5);
        /// <summary>拍到鬼。</summary>
        public void ReportPhoto() => Bump(ObjectiveKind.PhotographGhost, 1f);
        /// <summary>目击鬼事件。</summary>
        public void ReportGhostEvent() => Bump(ObjectiveKind.WitnessGhostEvent, 1f);
        /// <summary>熏香驱鬼。</summary>
        public void ReportSmudge() => Bump(ObjectiveKind.RepelWithSmudge, 1f);
        /// <summary>十字架阻止猎杀。</summary>
        public void ReportCrucifix() => Bump(ObjectiveKind.StopHuntWithCrucifix, 1f);
        /// <summary>运动传感器触发。</summary>
        public void ReportMotionSensor() => Bump(ObjectiveKind.MotionSensor, 1f);
        /// <summary>盐上足迹。</summary>
        public void ReportSaltFootprint() => Bump(ObjectiveKind.SaltFootprint, 1f);
        /// <summary>点燃蜡烛。</summary>
        public void ReportCandleLit() => Bump(ObjectiveKind.CandleLit, 1f);
        /// <summary>通灵盒回应。</summary>
        public void ReportSpiritBox() => Bump(ObjectiveKind.SpiritBoxResponse, 1f);
        /// <summary>鬼写字。</summary>
        public void ReportGhostWriting() => Bump(ObjectiveKind.GhostWriting, 1f);
        /// <summary>清理脏水。</summary>
        public void ReportDirtyWater() => Bump(ObjectiveKind.DirtyWater, 1f);

        /// <summary>
        /// 结算：把**已完成未领取**的奖励汇总（由 Settlement 叠加到本局收益）。
        /// 与 TaskSystem.ClaimRewards 同样分开"完成"与"发奖"两步，避免 HUD 刷新时重复发。
        /// </summary>
        public void Claim(out int money, out int xp)
        {
            money = 0; xp = 0;
            foreach (var o in _list)
            {
                if (!o.Completed || o.Claimed) continue;
                o.Claimed = true;
                money += o.RewardMoney;
                xp += o.RewardXp;
            }
        }

        /// <summary>HUD 多行（合同日志）。</summary>
        public string Describe()
        {
            if (LoadProblem != null) return "本局任务：" + LoadProblem;
            if (_list.Count == 0) return "本局任务：（未生成）";
            var sb = new System.Text.StringBuilder("本局任务（").Append(CompletedCount).Append('/').Append(_list.Count).Append("）：");
            foreach (var o in _list) sb.Append("\n  ").Append(o.Describe());
            return sb.ToString();
        }

        /// <summary>单行摘要（HUD 顶部一行）。</summary>
        public string OneLine()
            => _list.Count == 0 ? "本局任务：—"
             : $"本局任务 {CompletedCount}/{_list.Count}"
               + (_list.Count > 0 && !_list[0].Completed ? " · 当前：" + _list[0].Text : "");

        // ── 内部 ──

        void Bump(ObjectiveKind kind, float amount, bool replace = false)
        {
            if (amount <= 0f) return;
            foreach (var o in _list)
            {
                if (o.Kind != kind || o.Completed) continue;
                o.Progress = replace ? amount : o.Progress + amount;
                if (o.Progress >= o.Target) { o.Progress = o.Target; o.Completed = true; }
            }
        }

        int PickIndex(ref uint st, int count, HashSet<int> used)
        {
            for (int guard = 0; guard < count * 4; guard++)
            {
                st ^= st << 13; st ^= st >> 17; st ^= st << 5;
                int idx = (int)(st % (uint)count);
                if (!used.Contains(idx)) return idx;
            }
            return -1;
        }

        Objective Make(Dictionary<string, object> src, int ordinal)
        {
            var o = new Objective { Id = "obj" + ordinal };
            string kind = src.TryGetValue("kind", out var k) && k is string ks ? ks : "findEvidence";
            o.Kind = ParseKind(kind);
            o.Target = Num(src, "target", o.Kind == ObjectiveKind.FindEvidence ? 3f : 1f);
            o.Text = src.TryGetValue("text", out var t) && t is string ts ? ts : kind;
            o.RewardMoney = (int)Num(src, "rewardMoney", 40f);
            o.RewardXp = (int)Num(src, "rewardXp", 60f);
            return o;
        }

        static ObjectiveKind ParseKind(string s)
        {
            switch (s)
            {
                case "emfLevel5": return ObjectiveKind.EmfLevel5;
                case "photographGhost": return ObjectiveKind.PhotographGhost;
                case "witnessGhostEvent": return ObjectiveKind.WitnessGhostEvent;
                case "repelWithSmudge": return ObjectiveKind.RepelWithSmudge;
                case "stopHunt": return ObjectiveKind.StopHuntWithCrucifix;
                case "motionSensor": return ObjectiveKind.MotionSensor;
                case "saltFootprint": return ObjectiveKind.SaltFootprint;
                case "candleLit": return ObjectiveKind.CandleLit;
                case "spiritBoxResponse": return ObjectiveKind.SpiritBoxResponse;
                case "ghostWriting": return ObjectiveKind.GhostWriting;
                case "dirtyWater": return ObjectiveKind.DirtyWater;
                default: return ObjectiveKind.FindEvidence;
            }
        }

        static float Num(Dictionary<string, object> m, string key, float fallback)
        {
            if (!m.TryGetValue(key, out var v)) return fallback;
            if (v is double d) return (float)d;
            if (v is long l) return l;
            return fallback;
        }

        List<Dictionary<string, object>> ReadPool()
        {
            var list = new List<Dictionary<string, object>>();
            var arr = _cfg.Get("objectives.pool") as System.Collections.IList;
            if (arr == null) return list;
            for (int i = 0; i < arr.Count; i++)
                if (arr[i] is Dictionary<string, object> m) list.Add(m);
            return list;
        }
    }
}
