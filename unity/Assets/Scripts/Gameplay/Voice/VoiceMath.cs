using System;
using System.Collections.Generic;

namespace Whisper.Gameplay.Voice
{
    /// <summary>
    /// 声纹判定链的数学基元（V9 §6 / 独立验证轨要求"按机制移植"）。
    ///
    /// 移植来源：灰盒 0.6.0 的 `src/modules/__m1.js`（该实现已在 WebView 侧经 14 模块对拍验证）。
    /// 一致性由 `data/vectors/voice-classification.json` 锁定：同一批向量在两端必须给出同一结果
    /// （生成与比对见 tools/voice-port-vectors.mjs）。
    /// </summary>
    public static class VoiceMath
    {
        /// <summary>dBFS 下限（静音底），避免 -Infinity 参与运算。</summary>
        public const float SilenceDbfs = -100f;

        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

        public static float Round4(float v) => (float)Math.Round(v * 1e4) / 1e4f;

        /// <summary>中位数（偶数个取中间两数均值）；空集返回静音底。</summary>
        public static float Median(IReadOnlyList<float> xs)
        {
            if (xs == null || xs.Count == 0) return SilenceDbfs;
            var s = new List<float>(xs);
            s.Sort();
            int m = s.Count >> 1;
            return s.Count % 2 == 1 ? s[m] : (s[m - 1] + s[m]) / 2f;
        }

        /// <summary>线性插值百分位（p ∈ [0,1]）。</summary>
        public static float Percentile(IReadOnlyList<float> xs, float p)
        {
            if (xs == null || xs.Count == 0) return SilenceDbfs;
            var s = new List<float>(xs);
            s.Sort();
            if (s.Count == 1) return s[0];
            float idx = Clamp(p, 0f, 1f) * (s.Count - 1);
            int lo = (int)Math.Floor(idx);
            int hi = (int)Math.Ceiling(idx);
            if (lo == hi) return s[lo];
            return s[lo] + (s[hi] - s[lo]) * (idx - lo);
        }

        /// <summary>
        /// 固定容量环形缓冲（对应灰盒的 `Ring`）：零 GC 抖动的滑窗。
        /// </summary>
        public sealed class Ring
        {
            readonly float[] _buf;
            int _idx;
            public int N { get; private set; }

            public Ring(int capacity)
            {
                _buf = new float[Math.Max(1, capacity)];
            }

            public void Push(float v)
            {
                _buf[_idx] = v;
                _idx = (_idx + 1) % _buf.Length;
                if (N < _buf.Length) N++;
            }

            /// <summary>清空（对应灰盒 Ring.clear）。</summary>
            public void Clear() { _idx = 0; N = 0; }

            /// <summary>按时间顺序返回当前窗口（旧 → 新）；与灰盒 `values()` 同序。</summary>
            public List<float> Values()
            {
                var outList = new List<float>(N);
                if (N == 0) return outList;
                int start = N < _buf.Length ? 0 : _idx;
                for (int i = 0; i < N; i++) outList.Add(_buf[(start + i) % _buf.Length]);
                return outList;
            }
        }

        /// <summary>毫秒 → 帧数（frameMs 来自配置表，默认 50ms/帧）。</summary>
        public static int FramesFor(float ms, float frameMs)
        {
            if (frameMs <= 0f) frameMs = 50f;
            return Math.Max(1, (int)Math.Round(ms / frameMs));
        }

        /// <summary>
        /// 锚点统计（anchorStatistic = median_of_speech_frames_p50）：
        /// 先丢掉静音帧，再取"不低于中位数 -3dB"的语音帧，最后取其中位数。
        /// 为什么不是 max / top-decile：采样期间本就有大量静默（换气、停顿），用 max 会把换气爆音当锚点。
        /// </summary>
        public static float AnchorFromFrames(IReadOnlyList<float> frames)
        {
            if (frames == null || frames.Count == 0) return SilenceDbfs;
            var valid = new List<float>();
            foreach (var f in frames) if (!float.IsNaN(f) && !float.IsInfinity(f) && f > SilenceDbfs) valid.Add(f);
            if (valid.Count == 0) return SilenceDbfs;
            float med = Median(valid);
            var speech = new List<float>();
            foreach (var f in valid) if (f >= med - 3f) speech.Add(f);
            return Median(speech.Count > 0 ? speech : valid);
        }
    }
}
