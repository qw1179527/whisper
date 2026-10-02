using System;
using System.Collections.Generic;
using Whisper.Gameplay.Config;
using Whisper.Gameplay.Sanity;

namespace Whisper.Gameplay.Hud
{
    /// <summary>「怪物听见了」的提示（V9 §6：让玩家能归因"我哪一声被听见了"）。</summary>
    public struct HearingNotice
    {
        public string MonsterLabel;
        public float DistanceM;
        public float EffectiveThreshold;
    }

    /// <summary>
    /// HUD 数据模型（纯 C#，可在本机断言）。
    ///
    /// 为什么把模型与 UI 分开：Unity 侧的 HUD 无法在本机跑，但**HUD 显示什么、怎么算**
    /// 是玩法逻辑（例如"理智档位文案 + 四舍五入后的百分比"、"时钟 mm:ss"）。
    /// 把这段逻辑抽成零引擎依赖的模型后，它就能被断言覆盖；Unity 侧只剩"把字符串贴到控件上"。
    ///
    /// 文案口径与灰盒 `__m11` 保持一致（怪物名、距离一位小数、阈值一位小数、时钟 mm:ss）。
    /// </summary>
    public sealed class HudModel
    {
        readonly GameConfigReader _cfg;
        readonly List<string> _log = new List<string>();
        public int MaxLogLines { get; set; } = 6;

        public string SanityBandLabel { get; private set; } = "-";
        public int SanityPercent { get; private set; }
        public int BatterySeconds { get; private set; }
        public int Evidence { get; private set; }
        public int EvidenceTotal { get; private set; }
        public float ElapsedSeconds { get; private set; }
        public string ClockText { get; private set; } = "0:00";
        public string StageText { get; private set; } = "-";
        public IReadOnlyList<string> LogLines => _log;

        public HudModel(GameConfigReader cfg, int evidenceTotal = 0)
        {
            _cfg = cfg;
            EvidenceTotal = evidenceTotal;
        }

        /// <summary>怪物名（配置 label，如「低语者」）。</summary>
        public string MonsterLabel(string monsterId) => _cfg.String($"monsters.{monsterId}.label", monsterId);

        /// <summary>每帧刷新（数据来自各系统，不在这里反算玩法规则）。</summary>
        public void Update(SanitySystem sanity, float batterySeconds, int evidence, float elapsedSeconds)
        {
            if (sanity != null)
            {
                SanityBandLabel = sanity.Band.Label ?? "-";
                float pct = sanity.Max > 0f ? sanity.Value / sanity.Max * 100f : 0f;
                SanityPercent = (int)Math.Round(pct);
            }
            BatterySeconds = (int)Math.Round(batterySeconds);
            Evidence = evidence;
            ElapsedSeconds = elapsedSeconds;
            int total = (int)elapsedSeconds;
            ClockText = $"{total / 60}:{total % 60:00}";
        }

        public void SetStageText(string text) => StageText = text;

        /// <summary>写入一条日志（超出上限丢最旧的）。</summary>
        public void Log(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            _log.Insert(0, msg);            // 新的在上（与灰盒一致）
            while (_log.Count > MaxLogLines) _log.RemoveAt(_log.Count - 1);
        }

        /// <summary>怪物听见时的提示（格式与灰盒一致：『【低语者 听见了】距离 12.3m（阈值 10.0）』）。</summary>
        public string FormatHearing(in HearingNotice n) =>
            $"【{n.MonsterLabel} 听见了】距离 {n.DistanceM:0.0}m（阈值 {n.EffectiveThreshold:0.0}）";

        /// <summary>按灰盒口径组装最终一行状态（便于 HUD 与日志对拍）。</summary>
        public string FormatStatusLine() =>
            $"{SanityBandLabel} {SanityPercent} · 电量 {BatterySeconds}s · 证据 {Evidence}/{EvidenceTotal} · {ClockText} · {StageText}";

        /// <summary>结算页正文（口径与灰盒 `__m11` 结算一致）。</summary>
        public string FormatSettlement(Extraction.SettlementResult r) =>
            $"撤离点：{r.ExtractionLabel}（×{r.RewardScale:0.#}）\n" +
            $"证据 {r.Evidence}/{r.EvidenceTotal}　用时 {r.ElapsedSeconds:0.0}s\n" +
            $"残响碎片 {r.Fragments}";
    }
}
