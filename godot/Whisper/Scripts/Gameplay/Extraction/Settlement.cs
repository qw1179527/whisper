using System;
using Whisper.Gameplay.Config;

namespace Whisper.Gameplay.Extraction
{
    /// <summary>撤离点类型（V9 §7 撤离双点制）。</summary>
    public enum ExtractionKind { None, Standard, Deep }

    /// <summary>一局的结果输入（结算只需要这些事实）。</summary>
    public struct MissionOutcome
    {
        public bool Survived;
        public ExtractionKind Extraction;
        /// <summary>已收集证据点数（V9 §7：证据是本作唯一的"永久成长"来源）。</summary>
        public int EvidenceCollected;
        /// <summary>本局总证据点数（关卡内 evidencePoints 数量）。</summary>
        public int EvidenceTotal;
        /// <summary>存活队友数。</summary>
        public int SurvivingAllies;
        /// <summary>用时（秒）。</summary>
        public float ElapsedSeconds;
    }

    /// <summary>结算结果（对应灰盒 HUD 结算页显示的四行）。</summary>
    public struct SettlementResult
    {
        public ExtractionKind Extraction;
        public string ExtractionLabel;
        public float RewardScale;
        public int Evidence;
        public int EvidenceTotal;
        public float ElapsedSeconds;
        /// <summary>残响碎片（currency，V9 §7：**不可付费购买**）。</summary>
        public int Fragments;
        /// <summary>结算明细（用于"为什么是这个数"的可解释展示）。</summary>
        public string Breakdown;
    }

    /// <summary>
    /// 撤离与结算（V9 §7 撤离双点制 / economy 配置）。
    ///
    /// 数值纪律：全部来自 data/config.json 的 level.extraction 与 economy.formula，代码零硬编码。
    /// 碎片只由**局内表现**决定：证据数 / 存活队友 / 效率（10 分钟内撤离），
    /// 对应配置红线「碎片不可付费购买」——这是经济系统的设计底线，实现层不得开口子。
    ///
    /// 移植自灰盒 `__m11` 的结算页计算：
    ///     fragments = round(evidence*200 + 150 + (elapsed &lt; 600s ? 100 : 0))
    ///
    /// **已知缺口（如实登记，不擅自"修好"）**：rewardScale 未参与碎片计算，
    /// 即"深处撤离点 +30%"在 0.6.0 里只是 UI 文案、没有生效。本移植保持同行为以便与灰盒对拍，
    /// 并把该缺口交给人类裁决（见 docs/mechanism-gaps.md）。
    /// </summary>
    public static class Settlement
    {
        public const string DeepScaleGapNote =
            "缺口：配置 economy.formula.deepExtractionScale=1.3 未参与碎片计算（灰盒同此行为，属 UI 文案与实现不一致）";

        public static SettlementResult Compute(GameConfigReader cfg, in MissionOutcome o)
        {
            int fEvidence = (int)cfg.Float("economy.formula.evidence", 200f);
            int fAlly = (int)cfg.Float("economy.formula.survivingAlly", 150f);
            int fEfficiency = (int)cfg.Float("economy.formula.efficiencyUnder10min", 100f);
            float efficiencyWindowSec = cfg.Float("level.matchSeconds.0", 600f);   // [600,900] 的下界即"10 分钟内"

            float rewardScale = 1f;
            string label = "-";
            if (o.Extraction == ExtractionKind.Standard)
            {
                rewardScale = cfg.Float("level.extraction.standardPoint.rewardScale", 1f);
                label = "标准撤离点";
            }
            else if (o.Extraction == ExtractionKind.Deep)
            {
                rewardScale = cfg.Float("level.extraction.deepPoint.rewardScale", 1.3f);
                label = "深处撤离点";
            }

            int fragments = 0;
            var parts = new System.Text.StringBuilder();
            if (o.Survived && o.Extraction != ExtractionKind.None)
            {
                int fromEvidence = o.EvidenceCollected * fEvidence;
                int fromAlly = o.SurvivingAllies > 0 ? fAlly : 0;
                bool efficient = o.ElapsedSeconds < efficiencyWindowSec;
                int fromEfficiency = efficient ? fEfficiency : 0;
                fragments = (int)Math.Round((double)(fromEvidence + fromAlly + fromEfficiency));
                parts.Append($"证据 {o.EvidenceCollected}×{fEvidence}={fromEvidence}");
                if (fromAlly > 0) parts.Append($" + 队友存活 {fAlly}");
                if (fromEfficiency > 0) parts.Append($" + 效率(<{efficiencyWindowSec / 60f:0}分) {fEfficiency}");
            }
            else
            {
                parts.Append("未撤离：本局碎片为 0（V9 §7：只有带着证据活着出去才算数）");
            }

            return new SettlementResult
            {
                Extraction = o.Extraction,
                ExtractionLabel = label,
                RewardScale = rewardScale,
                Evidence = o.EvidenceCollected,
                EvidenceTotal = o.EvidenceTotal,
                ElapsedSeconds = o.ElapsedSeconds,
                Fragments = fragments,
                Breakdown = parts.ToString(),
            };
        }

        /// <summary>撤离点是否安全（V9 §7：标准点安全 / 深处点 +30% 但更危险）。</summary>
        public static bool IsSafe(GameConfigReader cfg, ExtractionKind kind)
        {
            if (kind == ExtractionKind.Standard) return cfg.Bool("level.extraction.standardPoint.safe", true);
            if (kind == ExtractionKind.Deep) return cfg.Bool("level.extraction.deepPoint.safe", false);
            return false;
        }

        /// <summary>开局保护期（V9 §7：期间不判接触、怪不因"看见"入追击、巡逻不进入口区）。</summary>
        public static float StartGraceSeconds(GameConfigReader cfg) => cfg.Float("level.startGraceSeconds", 20f);

        /// <summary>动态事件数区间（V9 §19.2：每局注入 2~3 个）。</summary>
        public static void DynamicEventRange(GameConfigReader cfg, out int min, out int max)
        {
            var raw = GameConfig.Get("level.dynamicEventsPerMatch");
            min = 2; max = 3;
            if (raw is System.Collections.Generic.List<object> list && list.Count >= 2)
            {
                min = list[0] is long a ? (int)a : min;
                max = list[1] is long b ? (int)b : max;
            }
        }
    }
}
