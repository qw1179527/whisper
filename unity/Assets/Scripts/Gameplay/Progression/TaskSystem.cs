using System;
using System.Collections.Generic;
using Whisper.Gameplay.Config;

namespace Whisper.Gameplay.Progression
{
    /// <summary>任务类别（官方确有这些类别；具体文案/数值未取到 → 走配置）。</summary>
    public enum TaskGoal
    {
        /// <summary>收齐本局全部证据。</summary>
        CollectAllEvidence,
        /// <summary>拍到鬼（照片证据）。</summary>
        PhotographGhost,
        /// <summary>用某件道具（如 EMF）达成一次有效读数。</summary>
        UseToolReading,
        /// <summary>活着撤离。</summary>
        SurviveAndExtract,
        /// <summary>在鬼房累计待够 N 秒。</summary>
        DwellInGhostRoom,
    }

    /// <summary>一条任务（每日或每周）。</summary>
    public sealed class Task
    {
        public string Id;
        public bool Weekly;
        public TaskGoal Goal;
        /// <summary>目标次数/秒数（< 0 表示不需要参数）。</summary>
        public float Target;
        /// <summary>道具 id（Goal=UseToolReading 时用）。</summary>
        public string ToolId;
        public string Text;
        public int RewardXp;
        public int RewardMoney;

        /// <summary>当前进度（只增不减；换日时整条重置）。</summary>
        public float Progress;
        /// <summary>是否已完成（领过奖也算完成）。</summary>
        public bool Completed;
        /// <summary>是否已发奖（防重复发）。</summary>
        public bool Rewarded;

        /// <summary>进度 0..1（UI 画进度条）。</summary>
        public float Ratio => Target <= 0f ? (Completed ? 1f : 0f) : Math.Min(1f, Progress / Target);

        /// <summary>进度文案。</summary>
        public string Describe()
            => $"[{(Weekly ? "周" : "日")}] {Text} · {Progress:0.#}/{Target:0.#}{(Completed ? " ✓" : "")}"
             + $" → {RewardXp} 经验 + {RewardMoney} 钱";
    }

    /// <summary>
    /// 每日/每周任务（恐鬼症对齐 · spec §3.3）。
    ///
    /// ## 结构（可确证的官方框架）
    /// **每日 3 条 + 每周 1 条**，完成给额外经验/钱。
    ///
    /// ## 确定性纪律（本工程的硬约束）
    /// 任务按**日期种子**生成 —— 但 `gate-physics` **禁止 DateTime.Now/UtcNow 参与玩法判定**
    /// （跨端不一致）。所以这里把"今天是第几天"作为**参数**传进来（由调用方从可信来源给出并同步），
    /// 本类内部只用 `dayIndex` 派生 xorshift，**不读时钟**。这样同一 dayIndex 在任何设备上生成同一组任务。
    ///
    /// ## 数值来源
    /// 任务池与奖励**从配置读**（`tasks.pool[]` / `tasks.dailyCount` / `tasks.weeklyCount`），
    /// 代码零硬编码。官方具体奖励数值未取到 → 配置标 `design`。
    /// </summary>
    public sealed class TaskSystem
    {
        readonly List<Task> _daily = new List<Task>();
        readonly List<Task> _weekly = new List<Task>();
        readonly GameConfigReader _cfg;

        /// <summary>当日种子（由调用方给；换日时调用 <see cref="RollForDay"/>）。</summary>
        public int DayIndex { get; private set; } = -1;

        /// <summary>生成失败原因（null = 正常）。</summary>
        public string LoadProblem { get; private set; }

        public TaskSystem(GameConfigReader cfg) { _cfg = cfg; }

        /// <summary>每日任务（只读）。</summary>
        public IReadOnlyList<Task> Daily => _daily;
        /// <summary>每周任务（只读）。</summary>
        public IReadOnlyList<Task> Weekly => _weekly;

        /// <summary>已完成条数（含已发奖）。</summary>
        public int CompletedCount
        {
            get
            {
                int n = 0;
                foreach (var t in _daily) if (t.Completed) n++;
                foreach (var t in _weekly) if (t.Completed) n++;
                return n;
            }
        }

        /// <summary>
        /// 为某一天生成任务。**幂等**：同一天重复调用不会重掷（已完成的进度不会丢）。
        /// </summary>
        public void RollForDay(int dayIndex)
        {
            if (dayIndex == DayIndex && (_daily.Count > 0 || _weekly.Count > 0)) return;
            DayIndex = dayIndex;
            _daily.Clear();
            _weekly.Clear();

            var pool = ReadPool();
            if (pool.Count == 0) { LoadProblem = "配置里没有 tasks.pool"; return; }

            int dailyCount = _cfg.Int("tasks.dailyCount", 3);
            int weeklyCount = _cfg.Int("tasks.weeklyCount", 1);

            uint st = (uint)(dayIndex * 2654435761u) ^ 0x9E3779B9u;
            // 每日：不重复地抽 dailyCount 条
            var used = new HashSet<int>();
            for (int i = 0; i < dailyCount && used.Count < pool.Count; i++)
            {
                int idx = PickIndex(ref st, pool.Count, used);
                if (idx < 0) break;
                used.Add(idx);
                _daily.Add(MakeTask(pool[idx], dayIndex, i, false));
            }
            // 每周：用**另一个派生子**，与每日不共享序列（否则改每日数量会连带改周任务）
            uint stw = (uint)(dayIndex / 7 * 40503u) ^ 0x85EBCA6Bu;
            var usedW = new HashSet<int>();
            for (int i = 0; i < weeklyCount && usedW.Count < pool.Count; i++)
            {
                int idx = PickIndex(ref stw, pool.Count, usedW);
                if (idx < 0) break;
                usedW.Add(idx);
                _weekly.Add(MakeTask(pool[idx], dayIndex, 1000 + i, true));
            }
            LoadProblem = null;
        }

        /// <summary>某天是否已有生成好的任务。</summary>
        public bool HasTasksFor(int dayIndex) => dayIndex == DayIndex && (_daily.Count > 0 || _weekly.Count > 0);

        // ── 进度累加（由局内事件调用） ──

        /// <summary>证据：本局已收 / 总数。</summary>
        public void ReportEvidence(int collected, int total)
        {
            foreach (var t in _daily) Bump(t, TaskGoal.CollectAllEvidence, collected >= total && total > 0 ? t.Target : collected);
            foreach (var t in _weekly) Bump(t, TaskGoal.CollectAllEvidence, collected >= total && total > 0 ? t.Target : collected);
        }
        /// <summary>拍到鬼。</summary>
        public void ReportPhoto() { BumpAll(TaskGoal.PhotographGhost, 1f); }
        /// <summary>某道具产生有效读数。</summary>
        public void ReportToolReading(string toolId)
        {
            foreach (var t in _daily) if (t.Goal == TaskGoal.UseToolReading && (t.ToolId == null || t.ToolId == toolId)) Bump(t, TaskGoal.UseToolReading, 1f);
            foreach (var t in _weekly) if (t.Goal == TaskGoal.UseToolReading && (t.ToolId == null || t.ToolId == toolId)) Bump(t, TaskGoal.UseToolReading, 1f);
        }
        /// <summary>存活撤离。</summary>
        public void ReportSurvivedExtract() { BumpAll(TaskGoal.SurviveAndExtract, 1f); }
        /// <summary>在鬼房停留（累加秒数）。</summary>
        public void ReportGhostRoomDwell(float seconds) { if (seconds > 0f) BumpAll(TaskGoal.DwellInGhostRoom, seconds); }

        void BumpAll(TaskGoal goal, float amount)
        {
            foreach (var t in _daily) Bump(t, goal, amount);
            foreach (var t in _weekly) Bump(t, goal, amount);
        }

        static void Bump(Task t, TaskGoal goal, float amount)
        {
            if (t == null || t.Goal != goal || t.Completed) return;
            t.Progress += amount;
            if (t.Progress >= t.Target) { t.Progress = t.Target; t.Completed = true; }
        }

        /// <summary>
        /// 结算奖励：把**已完成但未发奖**的任务奖励加到档案上，返回本次发放的经验/钱。
        /// 为什么单独一步：任务完成与"何时发奖"必须分开 —— 否则中途读一次 HUD 就会重复发奖。
        /// </summary>
        public void ClaimRewards(Progression prog, out int xp, out int money)
        {
            xp = 0; money = 0;
            if (prog == null) return;
            foreach (var t in AllTasks())
            {
                if (!t.Completed || t.Rewarded) continue;
                t.Rewarded = true;
                xp += t.RewardXp;
                money += t.RewardMoney;
            }
            if (xp > 0) prog.AddXp(xp);
            if (money > 0) prog.AddMoney(money);
        }

        IEnumerable<Task> AllTasks()
        {
            foreach (var t in _daily) yield return t;
            foreach (var t in _weekly) yield return t;
        }

        /// <summary>HUD 多行摘要（主界面任务板用）。</summary>
        public string Describe()
        {
            if (LoadProblem != null) return "任务：" + LoadProblem;
            var sb = new System.Text.StringBuilder();
            sb.Append("每日任务（").Append(DayIndex).Append("）：");
            if (_daily.Count == 0) sb.Append("（空）");
            foreach (var t in _daily) sb.Append('\n').Append("  ").Append(t.Describe());
            sb.Append("\n每周任务：");
            if (_weekly.Count == 0) sb.Append("（空）");
            foreach (var t in _weekly) sb.Append('\n').Append("  ").Append(t.Describe());
            return sb.ToString();
        }

        // ── 内部 ──

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

        Task MakeTask(Dictionary<string, object> src, int dayIndex, int ordinal, bool weekly)
        {
            var t = new Task { Weekly = weekly, Id = (weekly ? "w" : "d") + dayIndex + "_" + ordinal };
            string goal = src.TryGetValue("goal", out var g) && g is string gs ? gs : "collectAllEvidence";
            t.Goal = ParseGoal(goal);
            t.Target = Num(src, "target", t.Goal == TaskGoal.CollectAllEvidence ? 3f : 1f);
            t.ToolId = src.TryGetValue("toolId", out var ti) && ti is string tis ? tis : null;
            t.Text = src.TryGetValue("text", out var tx) && tx is string txs ? txs : goal;
            // 周任务奖励按倍率放大（官方语义：周任务更值钱）
            float mul = weekly ? _cfg.Float("tasks.weeklyRewardMultiplier", 4f) : 1f;
            t.RewardXp = (int)(Num(src, "rewardXp", 50f) * mul);
            t.RewardMoney = (int)(Num(src, "rewardMoney", 30f) * mul);
            return t;
        }

        static TaskGoal ParseGoal(string s)
        {
            switch (s)
            {
                case "photographGhost": return TaskGoal.PhotographGhost;
                case "useToolReading": return TaskGoal.UseToolReading;
                case "surviveAndExtract": return TaskGoal.SurviveAndExtract;
                case "dwellInGhostRoom": return TaskGoal.DwellInGhostRoom;
                default: return TaskGoal.CollectAllEvidence;
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
            // ⚠ GameConfig.Get 只按 `.` 查字典，**不支持 [i] 下标** → 必须整段取出来再遍历
            var arr = Config.GameConfig.Get("tasks.pool") as System.Collections.IList;
            if (arr == null) return list;
            for (int i = 0; i < arr.Count; i++)
                if (arr[i] is Dictionary<string, object> m) list.Add(m);
            return list;
        }
    }
}
