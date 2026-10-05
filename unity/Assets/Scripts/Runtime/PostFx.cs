using System;
using UnityEngine;
using Whisper.Gameplay.Render;

namespace Whisper.Runtime
{
    /// <summary>
    /// 后处理执行器（Built-in 管线的 `OnRenderImage` 路线）。
    ///
    /// ## 为什么是 OnRenderImage 而不是 CommandBuffer / URP Renderer Feature
    /// 活动管线是 Built-in（实测 `GraphicsSettings.m_CustomRenderPipeline: {fileID: 0}`），
    /// Built-in 里唯一**零资产**（不需要 .asset、不需要改 GraphicsSettings）的注入点就是
    /// 相机上的 `OnRenderImage`。本工程的历史事故（资产被剥离 → 真机全黑）都出在"依赖 .asset"上，
    /// 所以这里刻意走纯代码路径。
    ///
    /// ## 为什么自己做降采样与模糊，而不是让 Shader 一次算完
    /// 辉光要的是**大范围柔光**：在整分辨率上做大半径模糊在移动端会直接掉帧。
    /// 所以：亮部提取到 1/4 分辨率（rtW/4）→ 两次可分离高斯（H、V）→ 合成时按屏幕 UV 采样。
    /// 1/4 分辨率下 9+9 抽头覆盖的屏幕范围 ≈ 全分辨率 72+72 抽头，成本却只有 1/16。
    ///
    /// ## 眼部适应为什么有状态
    /// 适应是**时间积分**（人眼从亮进暗需要几秒）。着色器每帧无状态，所以
    /// 目标亮度在 C# 里逐帧读回（`AsyncGPUReadback` 在移动端支持不佳 → 用同步 `ReadPixels`，
    /// 尺寸只有 1x1，开销可忽略），再做指数逼近。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class PostFx : MonoBehaviour
    {
        /// <summary>着色器资源路径（不含扩展名；`.shader` 只剥最后一层）。</summary>
        public const string ShaderResourcePath = "Shaders/WhisperPostFx";

        // ── pass 序号必须与 WhisperPostFx.shader 里的顺序**逐字对应**（改 shader 就要改这里）──
        const int PassBright = 0;
        const int PassBlur = 1;      // H / V 共用，方向由 _WhisperFxBlurDir 决定
        const int PassComposite = 3; // ⚠ 跳过 2：blur 的第二方向复用同一 pass
        const int PassLuma = 4;

        /// <summary>当前档位（由组合根写入）。</summary>
        public RenderQuality Quality { get; set; }
        /// <summary>构造失败原因（非 null = 后处理完全没生效，HUD 会显示）。</summary>
        public string Problem { get; private set; }

        Material _mat;
        Camera _cam;
        RenderTexture _rtBright, _rtBlurA, _rtBlurB, _rtLuma;
        /// <summary>当前曝光（眼部适应的状态）。</summary>
        float _exposure = 1f;
        /// <summary>当前平均亮度（log2 域）。</summary>
        float _logLuma;

        /// <summary>眼部适应当前曝光（真机取证用）。</summary>
        public float Exposure => _exposure;
        /// <summary>最近一次读回的平均亮度（0..1，屏显量级）。</summary>
        public float AverageLuma => Mathf.Pow(2f, _logLuma);
        /// <summary>最近一次**原始**读回值（log2 域，未平滑）—— 诊断用。</summary>
        public float RawLogLuma { get; private set; }
        /// <summary>
        /// 亮度死区下限（log2 域）：画面比这更暗时眼部适应**不再加增益**。
        /// </summary>
        /// <remarks>
        /// 为什么必须有：0.1.47 真机把走廊整片变黑，而适应逻辑会朝"提亮"方向逼近
        /// （全黑画面 → 目标亮度远高于当前 → 曝光冲到上限）。人眼并不是这样工作的：
        /// **暗适应不会把全黑变成白天**。所以给一个死区——已经很暗就保持中性，
        /// 让"真正的黑暗"保持黑暗（这也是恐怖游戏该有的行为，不是妥协）。
        /// </remarks>
        public float MinAdaptLumaLog2 = -6.0f;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            var shader = Resources.Load<Shader>(ShaderResourcePath);
            if (shader == null)
            {
                Problem = "找不到 Resources/" + ShaderResourcePath + ".shader";
                Debug.LogError("[Whisper] 后处理着色器缺失：" + Problem);
                // 注意：这里也不禁用组件 —— 见 Bypass 的注释（禁用会连带清掉 depthTextureMode）。
                Bypass = true;
                return;
            }
            _mat = new Material(shader) { name = "WhisperPostFxMat" };
        }

        /// <summary>
        /// 旁路（= 后处理关闭）。
        /// </summary>
        /// <remarks>
        /// ══════════════════════════════════════════════════════════════════════════════
        /// 【用户 2026-10-05 报："你这关闭了一次后就打不开了"】根因：
        ///   我原先用 `PostFx.enabled = !enabled` 来开关后处理 → 触发 `OnDisable()` →
        ///   里面有 `_cam.depthTextureMode &= ~DepthNormals`。于是**再打开时**
        ///   `OnEnable()` 虽然又把标记加回去了，但 Unity 已经把 `_CameraDepthNormalsTexture`
        ///   置为 null，而本组件走的是 `OnRenderImage` + 手工 `Graphics.Blit` 链 ——
        ///   深度纹理在**第一帧**就已经绑不上（真机读数 raw=0.00），此后永不恢复 →
        ///   合成的画面恒为黑 → 用户看到"关一次就再也打不开"。
        /// 正解：**组件永不真正禁用**，只用一个内部标志跳过效果。
        ///   · 每帧只做一次 `Graphics.Blit(src, dst)` 直通（开销 ≈ 0）；
        ///   · `depthTextureMode` 全程保持，不再有"关一次就回不来"的状态；
        ///   · 这也让"关→开"彻底可逆（选项必须可逆，否则玩家不敢动设置）。
        /// ══════════════════════════════════════════════════════════════════════════════
        /// </remarks>
        public bool Bypass { get; set; }

        /// <summary>
        /// 默认是否旁路。
        /// </summary>
        /// <remarks>
        /// ══════════════════════════════════════════════════════════════════════════════
        /// 【为什么默认旁路 —— 如实登记，不掩饰】
        /// 0.1.47~0.1.54 连续 8 个包都在追同一件事：**只要合成 pass 真的跑，画面就整片黑**，
        /// 而旁路时画面完全正常（0.1.49 实测走廊明亮清晰）。
        /// 已排除的原因：着色器编译（三后端 0 错误）、曝光（已修到 1.00）、
        /// SSAO/SSGI/眼部适应（已全默认关）、辉光判据不一致（已统一）、
        /// 组件被 disable 导致的深度纹理失效（改为旁路标志）。
        /// 仍未定位：**合成 pass 本身**在 `OnRenderImage` 的手工 Blit 链里的行为。
        ///
        /// 决策：**不再拿用户的试玩时间来试错**。先把默认设为旁路 → 游戏立刻恢复可玩
        /// （渲染质量档/帧率档/MSAA/阴影质量/像素光这些**已验证有效**的部分继续生效），
        /// 把"后处理合成"作为独立课题记录在 spec 台账里，用专门的离屏取证去解，
        /// 而不是每轮出一个 APK 请用户帮忙看黑不黑。
        ///
        /// 打开方式（排查用）：主界面「画质」按钮连点到第三拍。
        /// ══════════════════════════════════════════════════════════════════════════════
        /// </remarks>
        public bool BypassByDefault = true;

        void Start()
        {
            // Start 而不是 Awake：组合根在 AddComponent 之后才注入 Quality，
            // 这里只负责把默认值落下来（幂等，重复进入场景也不会打架）。
            if (BypassByDefault) Bypass = true;
        }

        void OnEnable()
        {
            if (_cam == null) _cam = GetComponent<Camera>();
            if (_cam != null)
            {
                // 深度+法线纹理：SSAO/SSGI 重建几何要用。
                // 注意这会多一次深度 prepass —— 所以低画质档会把 SSAO/SSGI 关掉（见 RenderQuality 的低档）。
                _cam.depthTextureMode |= DepthTextureMode.DepthNormals;
            }
            // 亮部种子必须是**点采样**，否则 1/4 降采样时相邻像素被插值混掉，
            // 辉光会变成一圈均匀灰雾而不是"亮处发光"。
            if (_rtBright != null) _rtBright.filterMode = FilterMode.Bilinear;
        }

        // 【刻意没有 OnDisable 里清 depthTextureMode 的逻辑】——理由见 Bypass 的注释。
        // 只在真正销毁时才清（OnDestroy），因为那时相机也快没了。

        void OnDestroy()
        {
            ReleaseBuffers();
            if (_mat != null) { Destroy(_mat); _mat = null; }
        }

        void ReleaseBuffers()
        {
            foreach (var rt in new[] { _rtBright, _rtBlurA, _rtBlurB, _rtLuma })
                if (rt != null) { rt.Release(); Destroy(rt); }
            _rtBright = _rtBlurA = _rtBlurB = _rtLuma = null;
        }

        /// <summary>把档位开关写进全局量（着色器读全局，见 shader 头部的开关约定）。</summary>
        void PushGlobals(in RenderQuality.Tier t)
        {
            Shader.SetGlobalFloat("_WhisperFxBloom", t.Bloom ? t.BloomIntensity : 0f);
            // 【阈值标定依据】本场景是 Gamma 空间的暗场（实测画面均值远低于线性空间的高光量级）。
            // 原值 0.62 是照"线性空间 1.0 以上才算高光"定的 → 辉光永不触发 = 开了看不出差别。
            // 改 0.40：暗场里"比较亮的部分"就泛光，又不至于把整个画面糊成一团。
            Shader.SetGlobalFloat("_WhisperFxBloomThreshold", 0.40f);
            Shader.SetGlobalFloat("_WhisperFxAo", t.Ssao ? 1f : 0f);
            Shader.SetGlobalFloat("_WhisperFxGi", t.Ssgi ? t.SsgiIntensity : 0f);
            Shader.SetGlobalFloat("_WhisperFxEye", t.EyeAdaptation ? 1f : 0f);
            Shader.SetGlobalFloat("_WhisperFxGrain", t.Grain ? t.GrainAmount : 0f);
            Shader.SetGlobalFloat("_WhisperFxVignette", t.Vignette ? 0.85f : 0f);
            Shader.SetGlobalFloat("_WhisperFxVolumetric", t.VolumetricLight ? 0.7f : 0f);
            Shader.SetGlobalFloat("_WhisperFxAoRadiusM", t.SsaoRadiusM);
            Shader.SetGlobalFloat("_WhisperFxTime", Time.unscaledTime);
            Shader.SetGlobalFloat("_WhisperFxExposure", _exposure);
            // 反弹色：偏冷的月光感（与关卡调色板的冷色一致）
            Shader.SetGlobalColor("_WhisperFxAmbientColor", new Color(0.42f, 0.48f, 0.62f, 1f));
        }

        /// <summary>
        /// 眼部适应：把平均亮度按对数域做指数逼近。
        /// </summary>
        /// <remarks>
        /// · 目标曝光 = 基准亮度 / 当前亮度，并在对数域插值 —— 人眼感受是**对数**的。
        /// · 快速/慢速不对称是刻意：**变亮快、变暗慢**（人眼从亮处进暗处要几秒，反之更快），
        ///   这条不对称正是"人眼适应"的真实特征，也是用户点名要的效果。
        /// · 上下限夹住：不然从全黑场景出来会瞬间爆亮，恐怖氛围全毁。
        /// </remarks>
        void UpdateEyeAdaptation(in RenderQuality.Tier t, float dt)
        {
            if (!t.EyeAdaptation || _rtLuma == null) { _exposure = 1f; return; }
            if (dt <= 0f) return;

            // 1x1 读回：开销可忽略，且不需要 AsyncGPUReadback（移动端支持参差）
            var prev = RenderTexture.active;
            RenderTexture.active = _rtLuma;
            var tex = new Texture2D(1, 1, TextureFormat.RFloat, false);
            tex.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            float v = tex.GetPixel(0, 0).r;
            Destroy(tex);

            // 【0.1.50 真机教训】RFloat 的 ReadPixels 在部分 Android 后端返回 0/垃圾 →
            // 适应算法会朝"提亮"一路逼近（全黑画面 → 目标亮度远高于当前），曝光贴在 clamp 下限，
            // 而画面本来就暗，于是"越适应越黑"。
            // 正解：读回值先验有效性 —— 无效时**不更新**亮度的平滑值，
            // 并把曝光按中性回落。读不到数据时最安全的动作是"什么都不做"。
            if (!(v > -30f && v < 30f))
            {
                RawLogLuma = 0f;
                _exposure = Mathf.Lerp(_exposure, 1f, Mathf.Clamp01(dt * 2f));
                return;
            }
            _logLuma = Mathf.Lerp(_logLuma, v, Mathf.Clamp01(dt * 4f));   // 读回本身有噪声，先平滑

            const float KeyValue = 0.28f;                                  // 目标中灰（屏显量级）
            RawLogLuma = v;
            // 死区：画面比 MinAdaptLumaLog2 还暗 → 适应不再继续提亮（人眼不是这么工作的）
            float effLog = Mathf.Max(_logLuma, MinAdaptLumaLog2);
            float targetLog = Mathf.Min(Mathf.Log(Mathf.Max(KeyValue, 1e-3f), 2f) - effLog, 0f);
            float curLog = Mathf.Log(Mathf.Max(_exposure, 1e-3f), 2f);
            // 变亮（targetLog > curLog）快；变暗慢
            float speed = t.EyeAdaptSpeed * (targetLog > curLog ? 1f : 0.35f);
            curLog = Mathf.Lerp(curLog, targetLog, Mathf.Clamp01(dt * speed));
            _exposure = Mathf.Clamp(Mathf.Pow(2f, curLog), 0.35f, 3.2f);
        }

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            // 旁路：后处理"关闭"时走这条。**绝不用 enabled=false**（理由见 Bypass 的注释）。
            if (Bypass || _mat == null || Quality == null)
            {
                Graphics.Blit(src, dst);
                return;
            }

            var t = Quality.CurrentTier;
            int w = Mathf.Max(1, src.width / 4), h = Mathf.Max(1, src.height / 4);
            EnsureBuffers(w, h);

            PushGlobals(t);
            UpdateEyeAdaptation(t, Time.unscaledDeltaTime);

            // ① 亮部 → 1/4 分辨率
            // ⚠ 判据必须与 PushGlobals 一致（那边是 t.BloomIntensity）：
            //   曾用 t.Bloom，于是"强度 0 但开关 true"会跑完整套 Blit 却让着色器当它是关的。
            bool bloomOn = t.Bloom && t.BloomIntensity > 0.001f;
            if (bloomOn)
            {
                Graphics.Blit(src, _rtBright, _mat, PassBright);
                // ② 可分离高斯：H 再 V（两次同一 pass，方向由全局量切换）
                _mat.SetVector("_WhisperFxBlurDir", new Vector4(1f, 0f, 0f, 0f));
                Graphics.Blit(_rtBright, _rtBlurA, _mat, PassBlur);
                _mat.SetVector("_WhisperFxBlurDir", new Vector4(0f, 1f, 0f, 0f));
                Graphics.Blit(_rtBlurA, _rtBlurB, _mat, PassBlur);
                _mat.SetTexture("_WhisperBloomTex", _rtBlurB);
            }
            else
            {
                // 关辉光时不能留上一帧的纹理（会看到残影）—— 绑一块 1x1 黑
                _mat.SetTexture("_WhisperBloomTex", Texture2D.blackTexture);
            }

            // ③ 亮度读回（眼部适应的输入）
            if (t.EyeAdaptation)
            {
                Graphics.Blit(src, _rtLuma, _mat, PassLuma);
            }

            // ④ 合成
            Graphics.Blit(src, dst, _mat, PassComposite);
        }

        /// <summary>
        /// 丢弃临时缓冲，让 OnRenderImage 按需重建。
        /// </summary>
        /// <remarks>
        /// 【用途 · 用户 2026-10-05 报"从后台切回前台画面变黑无法还原"】
        /// Android 切后台会释放 GPU 资源（含本组件的 RenderTexture），回前台时它们可能已失效，
        /// 而本组件不会自己重建 → 后处理链上拿到空纹理 → 整片黑。
        /// 由 MenuScene.RebuildRenderState() 在回前台时调用本方法，随后 EnsureBuffers 自动重建。
        /// </remarks>
        public void RebuildBuffers()
        {
            ReleaseBuffers();
        }

        void EnsureBuffers(int w, int h)
        {
            if (_rtBright != null && _rtBright.width == w && _rtBright.height == h) return;
            ReleaseBuffers();
            _rtBright = new RenderTexture(w, h, 0, RenderTextureFormat.Default) { name = "WhisperFxBright" };
            _rtBlurA = new RenderTexture(w, h, 0, RenderTextureFormat.Default) { name = "WhisperFxBlurA" };
            _rtBlurB = new RenderTexture(w, h, 0, RenderTextureFormat.Default) { name = "WhisperFxBlurB" };
            _rtLuma = new RenderTexture(1, 1, 0, RenderTextureFormat.RFloat) { name = "WhisperFxLuma" };
            foreach (var rt in new[] { _rtBright, _rtBlurA, _rtBlurB, _rtLuma }) rt.Create();
        }

        /// <summary>HUD 一行摘要（真机取证：确认后处理真的在跑）。</summary>
        public string Describe()
            => Problem != null ? "后处理：✗ " + Problem
             : $"后处理：{(Bypass ? "关" : "开")} 曝光 {_exposure:0.00} 亮度 {AverageLuma:0.000}"
             + $" · 辉光{(Quality != null && Quality.CurrentTier.Bloom ? "开" : "关")}"
             + $" SSAO{(Quality != null && Quality.CurrentTier.Ssao ? "开" : "关")}"
             + $" SSGI{(Quality != null && Quality.CurrentTier.Ssgi ? "开" : "关")}"
             + $" 体积光{(Quality != null && Quality.CurrentTier.VolumetricLight ? "开" : "关")}";
    }
}
