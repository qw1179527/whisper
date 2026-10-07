using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Whisper.Gameplay.Voice
{
    /// <summary>个人锚点（三档绝对 dBFS）。</summary>
    public struct VoiceAnchors
    {
        public float AmbientDb;
        public float Whisper;
        public float Normal;
        public float Shout;
        public VoiceAnchors(float whisper, float normal, float shout, float ambientDb = -100f)
        {
            Whisper = whisper; Normal = normal; Shout = shout; AmbientDb = ambientDb;
        }
    }

    /// <summary>一次判定的结果（对应灰盒 `push()` 的返回）。</summary>
    public struct BandDecision
    {
        /// <summary>whisper | normal | shout | indistinguishable（不可辨，不得宣称任何档）| null（未校准）。</summary>
        public string Band;
        public int BandIndex;
        /// <summary>仅异常路径有值：uncalibrated | snr_below_min（成功路径为 null，与灰盒一致）。</summary>
        public string Reason;
        public float LevelDb;
        public float RelDb;
        /// <summary>分段归一化的**电平**值：-1=耳语锚点 0=正常 +1=喊叫（仅表征，不参与判档）。</summary>
        public float Norm;
        /// <summary>参与判档的**峰值**归一化值（迟滞状态机的输入）。</summary>
        public float RelPeakNorm;
        public float SnrDb;
        public float SnrPeakDb;
        public float NoiseFloorDb;
        public float FloorNorm;
        public float Confidence;
        public bool Changed;
    }

    /// <summary>
    /// 声纹分档器（V9 §6）：把逐帧 dBFS 映射为 whisper / normal / shout 三档。
    ///
    /// 核心是**分段归一化**（V8 N2「相对电平」的严格实现）：
    ///     0 = 本人「正常说话」锚点；
    ///    -1 = 本人「耳语」锚点（下侧除以 normal-whisper）；
    ///    +1 = 本人「喊叫」锚点（上侧除以 shout-normal）。
    /// 于是该设备三档锚点恒为 -1/0/+1、边界恒为 -0.5/+0.5，与绝对增益完全无关。
    ///
    /// 三个已固化为回归的反例（灰盒首轮实测）：
    ///   ① 绝对夹取 ±24dB → 低增益机型喊叫被压平、正常说话被误判为喊叫；
    ///   ② 对称夹取 [0.25,3] 当负下限 → 耳语/正常全被抬进「正常」档；
    ///   ③ 上下共用同一 range → 耳语锚点不再映射到 -1，边界失去物理含义。
    /// 结论：必须分段归一化，且安全夹取必须关于 0 对称。
    ///
    /// 移植自灰盒 `src/modules/__m1.js` 的 VoiceBandClassifier；一致性由 data/vectors 锁定。
    /// </summary>
    public sealed class VoiceBandClassifier
    {
        public VoiceAnchors? Anchors { get; private set; }
        readonly int _noiseWindowFrames;
        readonly float _noisePercentile;
        readonly int _noiseFastWindowFrames;
        readonly float _noiseFastPercentile;
        readonly int _levelWindowFrames;
        readonly string _levelStatistic;
        public float HysteresisDb { get; }
        public float ClampCeil { get; }
        public float PeakBoost { get; }
        public float MinRuntimeSnrDb { get; }
        public float NoiseFloorMaxDb { get; }

        readonly VoiceMath.Ring _noiseRing;
        readonly VoiceMath.Ring _noiseFastRing;
        readonly VoiceMath.Ring _levelRing;

        /// <summary>迟滞状态机的当前档（跨帧保持；null = 尚未定档）。</summary>
        public string CurrentBand { get; private set; }

        /// <summary>档位边界（归一化坐标，恒为 -0.5 / +0.5）。</summary>
        public static readonly float[] Boundaries = { -0.5f, 0.5f };

        public VoiceBandClassifier(
            VoiceAnchors? anchors = null,
            float frameMs = 50f,
            int noiseWindowMs = 10000,
            float noisePercentile = 10f,
            int noiseFastWindowMs = 2000,
            float noiseFastPercentile = 5f,
            int levelWindowMs = 500,
            string levelStatistic = "p90",
            float hysteresisDb = 2.0f,
            float clampCeil = 1.5f,
            float peakBoost = 1.15f,
            float minRuntimeSnrDb = 6f,
            float noiseFloorMaxDb = -30f)
        {
            _noiseWindowFrames = VoiceMath.FramesFor(noiseWindowMs, frameMs);
            _noisePercentile = noisePercentile / 100f;
            _noiseFastWindowFrames = VoiceMath.FramesFor(noiseFastWindowMs, frameMs);
            _noiseFastPercentile = noiseFastPercentile / 100f;
            _levelWindowFrames = VoiceMath.FramesFor(levelWindowMs, frameMs);
            _levelStatistic = levelStatistic;
            HysteresisDb = hysteresisDb;
            ClampCeil = Math.Abs(clampCeil);
            PeakBoost = peakBoost;
            MinRuntimeSnrDb = minRuntimeSnrDb;
            NoiseFloorMaxDb = noiseFloorMaxDb;

            _noiseRing = new VoiceMath.Ring(_noiseWindowFrames);
            _noiseFastRing = new VoiceMath.Ring(_noiseFastWindowFrames);
            _levelRing = new VoiceMath.Ring(_levelWindowFrames);

            if (anchors.HasValue) SetAnchors(anchors.Value);
        }

        public void SetAnchors(VoiceAnchors a) => Anchors = a;

        /// <summary>分段归一化（上下两侧各自除以本人区间；夹取关于 0 对称）。</summary>
        public float Normalize(float relDb, VoiceAnchors a)
        {
            float up = Math.Max(1f, a.Shout - a.Normal);
            float down = Math.Max(1f, a.Normal - a.Whisper);
            float v = relDb >= 0f ? relDb / up : relDb / down;
            return Math.Min(ClampCeil, Math.Max(-ClampCeil, v));
        }

        /// <summary>噪声底：长窗(10s p10) 与 快窗(2s p5) 取小，再受噪声底上限夹取。</summary>
        public float NoiseFloor()
        {
            float? longFloor = _noiseRing.N > 0 ? VoiceMath.Percentile(_noiseRing.Values(), _noisePercentile) : (float?)null;
            float? fastFloor = _noiseFastRing.N > 0 ? VoiceMath.Percentile(_noiseFastRing.Values(), _noiseFastPercentile) : (float?)null;
            if (longFloor == null && fastFloor == null) return Anchors?.AmbientDb ?? VoiceMath.SilenceDbfs;
            float f = Math.Min(longFloor ?? float.PositiveInfinity, fastFloor ?? float.PositiveInfinity);
            return Math.Min(f, NoiseFloorMaxDb);
        }

        /// <summary>当前窗口电平：levelStatistic（默认 p90）——比 max 抗爆音、比 mean 抗停顿。</summary>
        public float CurrentLevel()
        {
            if (_levelRing.N == 0) return VoiceMath.SilenceDbfs;
            var vals = _levelRing.Values();
            switch (_levelStatistic)
            {
                case "max": { float m = float.NegativeInfinity; foreach (var v in vals) if (v > m) m = v; return m; }
                case "mean": { float s = 0f; foreach (var v in vals) s += v; return s / vals.Count; }
                case "median": return VoiceMath.Median(vals);
                default:
                    var match = Regex.Match(_levelStatistic ?? "", "^p(\\d{1,2})$");
                    if (match.Success) return VoiceMath.Percentile(vals, int.Parse(match.Groups[1].Value) / 100f);
                    { float m = float.NegativeInfinity; foreach (var v in vals) if (v > m) m = v; return m; }
            }
        }

        /// <summary>
        /// 喂入一帧并返回分档结果（**逐字对齐**灰盒 `_classify`）。
        ///
        /// 关键点（移植时踩过的坑）：
        ///   · 判档输入是 **relPeak（峰值归一化）**，不是电平归一化 norm —— 用 norm 会在整句中途抖动跳档；
        ///   · 不可辨时返回 band = `"indistinguishable"`、reason = `"snr_below_min"`（不是 null）、
        ///     且 bandIndex = -1；把 currentBand 置 null 但**保留**迟滞状态语义；
        ///   · 边界上 tie-break 取更响的一档（宁可被听见，不可被漏判）。
        /// </summary>
        public BandDecision Push(float dbfs, float? noiseFloorOverride = null, float? peakOverride = null)
        {
            float v = (float.IsNaN(dbfs) || float.IsInfinity(dbfs)) ? VoiceMath.SilenceDbfs : dbfs;
            _noiseRing.Push(v);
            _noiseFastRing.Push(v);
            _levelRing.Push(v);

            var res = new BandDecision
            {
                Band = null, BandIndex = -1, Reason = "uncalibrated",
                LevelDb = v, Norm = 0f, RelDb = 0f, RelPeakNorm = 0f,
                SnrDb = 0f, SnrPeakDb = 0f, NoiseFloorDb = VoiceMath.SilenceDbfs,
                FloorNorm = 0f, Confidence = 0f, Changed = false,
            };
            if (!Anchors.HasValue) return res;

            var a = Anchors.Value;
            float nf = noiseFloorOverride ?? NoiseFloor();
            float refDb = a.Normal;
            float relDb = v - refDb;
            float norm = Normalize(relDb, a);
            float floorNorm = Normalize(nf - refDb, a);
            float stat = peakOverride ?? CurrentLevel();
            // 峰值：本帧与 500ms 窗口统计的较响者，再乘一点余量（更贴合"这一声有多响"）
            float peak = peakOverride ?? Math.Max(v, stat) * PeakBoost;
            float relPeak = Normalize(peak - refDb, a);
            float snrDb = VoiceMath.Round4(relDb - (nf - refDb));
            float snrPeakDb = VoiceMath.Round4(peak - nf);
            float h = HysteresisDb / 2f / Math.Max(1f, a.Shout - a.Normal);

            res.LevelDb = VoiceMath.Round4(v);
            res.RelDb = VoiceMath.Round4(relDb);
            res.Norm = VoiceMath.Round4(norm);
            res.RelPeakNorm = VoiceMath.Round4(relPeak);
            res.NoiseFloorDb = VoiceMath.Round4(nf);
            res.FloorNorm = VoiceMath.Round4(floorNorm);
            res.SnrDb = snrDb;
            res.SnrPeakDb = snrPeakDb;

            // 可辨下限：说话电平不比实测噪声底高出 minRuntimeSnrDb 时，不得宣称任何分档。
            // 用**峰值 SNR** 判定——单帧抖动不应让一整句话"消失"。
            if (peakOverride == null && snrPeakDb < MinRuntimeSnrDb)
            {
                bool changedToIndist = CurrentBand != "indistinguishable";
                CurrentBand = null;
                res.Band = "indistinguishable";
                res.BandIndex = -1;
                res.Reason = "snr_below_min";
                res.Changed = changedToIndist;
                res.Confidence = 0f;
                return res;
            }

            float b1 = Boundaries[0], b2 = Boundaries[1];
            string cur = CurrentBand;
            string band;
            if (cur == "whisper") band = relPeak >= b1 + h ? (relPeak >= b2 + h ? "shout" : "normal") : "whisper";
            else if (cur == "normal") band = relPeak >= b2 + h ? "shout" : (relPeak < b1 - h ? "whisper" : "normal");
            else if (cur == "shout") band = relPeak < b1 - h ? "whisper" : (relPeak < b2 - h ? "normal" : "shout");
            else band = relPeak < b1 ? "whisper" : (relPeak < b2 ? "normal" : "shout");

            // tie-break 纪律：落在边界上取更响的一档（宁可被听见，不可被漏判）
            if (relPeak == b1) band = "normal";
            if (relPeak == b2) band = "shout";

            res.Changed = band != CurrentBand;
            CurrentBand = band;
            res.Band = band;
            res.BandIndex = Array.IndexOf(BandIds, band);
            // 与灰盒一致：成功路径**不设** reason（只有不可辨/未校准才有 reason）
            res.Reason = null;
            float dist = Math.Min(Math.Abs(relPeak - b1), Math.Abs(relPeak - b2));
            res.Confidence = VoiceMath.Round4(Math.Min(1f, dist / 0.35f));
            return res;
        }

        /// <summary>档位 id 顺序（对应灰盒 BAND_IDS）。</summary>
        public static readonly string[] BandIds = { "whisper", "normal", "shout" };

        /// <summary>清空滑窗与迟滞状态（keepAnchors=false 时连锚点一起清）。</summary>
        public void Reset(bool keepAnchors = true)
        {
            _noiseRing.Clear();
            _noiseFastRing.Clear();
            _levelRing.Clear();
            CurrentBand = null;
            if (!keepAnchors) Anchors = null;
        }

        /// <summary>归一化值 → 档位名（无迟滞的纯判定，测试与调试面板用）。</summary>
        public static string BandOf(float norm)
        {
            if (norm >= 0.5f) return "shout";
            if (norm <= -0.5f) return "whisper";
            return "normal";
        }

        /// <summary>
        /// 从配置表装配（**唯一正确入口**）：参数优先级 = 配置表 > 灰盒代码兜底默认。
        ///
        /// 为什么必须有它：移植时我曾按"看起来合理"的默认值（hysteresis=2、minRuntimeSnr=6、levelWindow=500ms）
        /// 直接构造，结果与灰盒判定不一致（实测：配置里 hysteresisDb=3、minRuntimeSnrDb=8、levelWindowMs=700）。
        /// 数值一律不许猜——配置缺失才回落到灰盒的 ?? 默认。
        /// </summary>
        public static VoiceBandClassifier FromConfig(Whisper.Gameplay.Config.GameConfigReader cfg, VoiceAnchors? anchors = null)
        {
            float frameMs = cfg.Float("voiceCalibration.frameMs", 50f);
            return new VoiceBandClassifier(
                anchors,
                frameMs,
                cfg.Int("voiceCalibration.noiseFloorWindowMs", 10000),
                cfg.Float("voiceCalibration.noiseFloorPercentile", 10f),
                cfg.Int("voiceCalibration.noiseFastWindowMs", 2000),      // 配置未定义 → 灰盒 ?? 默认
                cfg.Float("voiceCalibration.noiseFastPercentile", 5f),    // 同上
                cfg.Int("voiceCalibration.levelWindowMs", 500),
                cfg.String("voiceCalibration.levelStatistic", "p90"),
                cfg.Float("voiceCalibration.hysteresisDb", 2.0f),
                cfg.Float("voiceCalibration.clampCeil", 1.5f),
                cfg.Float("voiceCalibration.peakBoost", 1.15f),
                cfg.Float("voiceCalibration.minRuntimeSnrDb", 6f),        // 配置 8 覆盖代码 6
                cfg.Float("voiceCalibration.noiseFloorMaxDb", -30f));
        }

        public static string BandSourceKey(string band)
        {
            switch (band)
            {
                case "whisper": return "voice_whisper";
                case "normal": return "voice_normal";
                case "shout": return "voice_shout";
                default: return null;
            }
        }
    }
}
