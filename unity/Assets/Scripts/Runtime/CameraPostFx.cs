using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Whisper.Runtime
{
    /// <summary>
    /// URP 相机后处理开关 —— **一处启用，所有相机都走它**。
    ///
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// 为什么必须有这个类（2026-10-06 由"逐项后处理 ON/OFF 取证"抓出来的真缺陷）
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// 现象：新加的逐项取证对 6 个后处理（Bloom / Vignette / ChromaticAberration /
    /// FilmGrain / ColorAdjustments / Tonemapping）逐一做 ON/OFF 对照，结果**全部是 0.000%**
    /// —— 连 Tonemapping 这种"装上就必然改变像素"的也纹丝不动。
    ///
    /// 根因：**全仓零引用 `renderPostProcessing`**。
    /// URP 里"相机是否参与后处理"由 `UniversalAdditionalCameraData.renderPostProcessing` 决定，
    /// **默认 false**。也就是说：UrpSetup 那 6 个 Volume 组件一直都在 profile 里、
    /// 参数也写对了，但**没有任何一个相机去看它们** —— 整条后处理栈从未生效。
    ///
    /// 这正是本项目反复出现的失效形态（「配了 ≠ 生效」「门禁全绿但结果是错的」）：
    /// 配置文件、Volume Profile、着色器全都"看起来对"，唯独缺了把相机接进去的那一行。
    ///
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// 为什么用 `GetUniversalAdditionalCameraData()` 而不是 `AddComponent<T>()`
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// 那是 URP 官方给的扩展方法：**没有就自动加、有就返回现有的**，因此天然幂等。
    /// 自己 `AddComponent<UniversalAdditionalCameraData>()` 在 URP 已经加过时会得到**第二个**实例，
    /// 而 URP 只认它自己那个 —— 于是"我又加了一个、却仍然不生效"，是最难查的一类。
    ///
    /// 为什么 Runtime 的 asmdef 不需要显式引用 URP 程序集：`com.unity.render-pipelines.universal`
    /// 是 **autoReferenced** 的预编译程序集（与 `UrpSetup.cs` 用 URP 类型同理）。
    /// asmdef 的 references 规则表由 `tools/gen-asmdef.mjs` 固定（Runtime: Core/Gameplay/Net/Audio/Backend），
    /// 加一条会与规则表冲突 —— 而这里确实不需要加。
    /// </summary>
    public static class CameraPostFx
    {
        /// <summary>
        /// 让该相机参与 URP 后处理，并按需开启深度/不透明纹理。
        ///
        /// `needDepth`：SSAO/自研雾的深度重建需要 `_CameraDepthTexture`。
        /// 现阶段所有相机都传 true（成本是每帧一张深度图，室内黑场恐怖游戏值得）。
        /// </summary>
        public static void Enable(Camera cam, bool needDepth = true)
        {
            if (cam == null) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null)
            {
                // 不静默：拿不到 URP 相机数据 = 后处理一定不生效，必须留下可查的痕迹
                Debug.LogWarning("[Whisper] 相机拿不到 UniversalAdditionalCameraData —— URP 后处理不会生效");
                return;
            }
            data.renderPostProcessing = true;
            // 后处理里要用的额外渲染目标（在 URP Asset 侧是 supportsCameraDepthTexture；
            // 相机侧再明确一次，避免"Asset 开了、相机没请求"的静默落差）。
            if (needDepth) data.requiresDepthTexture = true;
        }

        /// <summary>本类是否已把某个相机接进后处理（取证/自检可读）。</summary>
        public static bool IsEnabled(Camera cam)
        {
            if (cam == null) return false;
            var data = cam.GetUniversalAdditionalCameraData();
            return data != null && data.renderPostProcessing;
        }
    }
}
