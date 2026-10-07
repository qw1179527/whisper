using System;
using System.Collections.Generic;

namespace Whisper.Gameplay.Voice
{
    /// <summary>校准采样结果（对应灰盒 VoiceCalibrator 的状态输出）。</summary>
    public sealed class CalibrationResult
    {
        public bool Done;
        public float AmbientDb;
        public float Whisper;
        public float Normal;
        public float Shout;
        public float SnrDb;
        public bool Accepted;
        public string RejectReason;
    }

    /// <summary>
    /// 个人校准（V9 §6 / V8 N2 整改）：**相对电平**，彻底取代 V6 的绝对 dBFS 硬阈值。
    ///
    /// 为什么必须个人校准：不同机型麦克风增益 / AGC 策略 / 握持姿势差异可达 10~25dB——
    /// 同一句话在 A 机是"耳语"、在 B 机可能是"喊叫"，核心机制公平性会被摧毁。
    ///
    /// 流程（第 0 局教学的三步采样）：先静默采环境底噪 → 依次 whisper/normal/shout 各采 N 帧 → 出锚点。
    /// 移植自灰盒 `src/modules/__m1.js` 的 VoiceCalibrator；一致性由 data/vectors 锁定。
    /// </summary>
    public sealed class VoiceCalibrator
    {
        public int SamplesPerPrompt { get; }
        public int SampleFrames { get; }
        public IReadOnlyList<string> Prompts { get; }

        readonly Dictionary<string, List<float>> _collected = new Dictionary<string, List<float>>(StringComparer.Ordinal);
        readonly List<float> _ambient = new List<float>();
        string _current;
        public bool Done { get; private set; }
        public CalibrationResult Result { get; private set; }

        public VoiceCalibrator(int samplesPerPrompt = 3, int sampleFrames = 52, IReadOnlyList<string> prompts = null)
        {
            SamplesPerPrompt = samplesPerPrompt;
            SampleFrames = Math.Max(1, sampleFrames);
            Prompts = prompts ?? new[] { "whisper", "normal", "shout" };
        }

        public void Reset()
        {
            _collected.Clear();
            _ambient.Clear();
            _current = null;
            Done = false;
            Result = null;
        }

        /// <summary>喂入一帧环境音（第 0 局第 0 步：先静默采底噪）。</summary>
        public int PushAmbientFrame(float dbfs)
        {
            _ambient.Add(Finite(dbfs));
            return _ambient.Count;
        }

        /// <summary>环境底噪（默认取 60 分位；与灰盒 ambientFloor 同参数）。</summary>
        public float? AmbientFloor(float percentilePct = 60f)
        {
            if (_ambient.Count == 0) return null;
            return VoiceMath.Round4(VoiceMath.Percentile(_ambient, percentilePct / 100f));
        }

        /// <summary>开始某档采样：whisper | normal | shout。</summary>
        public void StartPrompt(string promptId)
        {
            if (!Contains(promptId)) throw new ArgumentException($"unknown prompt: {promptId}");
            _current = promptId;
            if (!_collected.ContainsKey(promptId)) _collected[promptId] = new List<float>();
        }

        /// <summary>单帧喂入结果：是否被忽略 / 当前档 / 进度 / 是否采满。</summary>
    public readonly struct PushResult
        {
            public readonly bool Ignored;
            public readonly string PromptId;
            public readonly float Progress;
            public readonly bool Complete;
            public PushResult(bool ignored, string promptId, float progress, bool complete)
            {
                Ignored = ignored; PromptId = promptId; Progress = progress; Complete = complete;
            }
        }

        /// <summary>喂入一帧 dBFS；未处于采样态时返回 Ignored。</summary>
        public PushResult PushFrame(float dbfs)
        {
            if (_current == null) return new PushResult(true, null, 0f, false);
            var arr = _collected[_current];
            arr.Add(Finite(dbfs));
            float progress = VoiceMath.Clamp((float)arr.Count / SampleFrames, 0f, 1f);
            bool complete = arr.Count >= SampleFrames;
            if (complete)
            {
                var finished = _current;
                _current = null;
                Done = AllPromptsCollected();
                if (Done) Result = BuildResult();
                return new PushResult(false, finished, progress, true);
            }
            return new PushResult(false, _current, progress, false);
        }

        bool AllPromptsCollected()
        {
            foreach (var p in Prompts)
                if (!_collected.TryGetValue(p, out var l) || l.Count == 0) return false;
            return true;
        }

        /// <summary>由三档采样帧出锚点（anchorFromFrames），并做信噪比准入。</summary>
        CalibrationResult BuildResult()
        {
            float whisper = VoiceMath.AnchorFromFrames(_collected["whisper"]);
            float normal = VoiceMath.AnchorFromFrames(_collected["normal"]);
            float shout = VoiceMath.AnchorFromFrames(_collected["shout"]);
            float ambient = AmbientFloor() ?? VoiceMath.SilenceDbfs;
            float snr = VoiceMath.Round4(normal - ambient);
            return new CalibrationResult
            {
                Done = true,
                AmbientDb = ambient,
                Whisper = VoiceMath.Round4(whisper),
                Normal = VoiceMath.Round4(normal),
                Shout = VoiceMath.Round4(shout),
                SnrDb = snr,
                Accepted = true,   // 准入判定由使用者按 minAnchorSnrDb 决定（见 IsAnchorUsable）
                RejectReason = null,
            };
        }

        /// <summary>锚点是否可用（V9 配置 voiceCalibration.minAnchorSnrDb）：正常说话锚点须高出底噪足够多。</summary>
        public static bool IsAnchorUsable(CalibrationResult r, float minAnchorSnrDb) =>
            r != null && r.Done && r.SnrDb >= minAnchorSnrDb;

        static float Finite(float v) => (float.IsNaN(v) || float.IsInfinity(v)) ? VoiceMath.SilenceDbfs : v;
        bool Contains(string p)
        {
            foreach (var x in Prompts) if (x == p) return true;
            return false;
        }
    }
}
