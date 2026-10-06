using System;
using System.Reflection;
using UnityEngine;

namespace Whisper.Runtime
{
    /// <summary>
    /// URP 相机后处理开关 —— **一处启用，所有相机都走它**。
    ///
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// 为什么必须有这个类（2026-10-06 由"逐项后处理 ON/OFF 取证"抓出来的真缺陷）
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// 现象：逐项取证对 6 个后处理（Bloom / Vignette / ChromaticAberration / FilmGrain /
    /// ColorAdjustments / Tonemapping）逐一做 ON/OFF 对照，结果**全部 0.000%**
    /// —— 连 Tonemapping 这种"装上就必然改变像素"的也纹丝不动。
    ///
    /// 根因：**全仓零引用 `renderPostProcessing`**。
    /// URP 里"相机是否参与后处理"由 `UniversalAdditionalCameraData.renderPostProcessing` 决定，
    /// **默认 false**。即 UrpSetup 那 6 个 Volume 组件一直在 profile 里、参数也对，
    /// 但**没有任何相机去看它们** —— 整条后处理栈从未生效。
    /// 这正是本项目反复出现的失效形态：「配了 ≠ 生效」。
    ///
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// ⚠ 为什么这里用**反射**而不是直接写 `UnityEngine.Rendering.Universal` 类型
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// 第一版我直接 `using UnityEngine.Rendering.Universal;` + 调 `GetUniversalAdditionalCameraData()`，
    /// 本机语法门禁**全绿**，而**云端真实 Unity 构建直接判红**：
    /// ```
    /// Assets/Scripts/Runtime/CameraPostFx.cs(2,29): error CS0234:
    ///   The type or namespace name 'Universal' does not exist in the namespace 'UnityEngine.Rendering'
    /// ```
    /// 根因：`Whisper.Runtime.asmdef` **不引用 URP 程序集**，而 URP 包的 `autoReferenced`
    /// **只对默认的 Assembly-CSharp 生效**，对自定义 asmdef 无效
    /// （`Assets/Editor/UrpSetup.cs` 能直接写 URP 类型，是因为 Editor 目录**没有 asmdef**、
    ///   落在默认程序集里 —— 同一个工程里两种情况并存，极易误判）。
    ///
    /// 而本机门禁为什么没拦住：`tools/gate-editor-api.mjs` 自己的"诚实边界"写得很清楚 ——
    /// > **桩是我写的，我编造一个 API，桩就替它背书。**
    /// 桩里有 `UnityEngine.Rendering.Universal` 命名空间 ⇒ 语法检查认为一切正常。
    /// **只有真 Unity 构建能发现程序集引用缺失。**
    ///
    /// ⇒ 反射是这里**正确**的形态（也是本仓既定的安全形态：`gate-editor-api` 明确豁免反射，
    ///    它在 `RenderEvidenceCapture` 里已用于调 `GameBootstrap.BuildCamera`）：
    ///    · 不依赖编译期成员/程序集存在 ⇒ 不会因为 asmdef 引用问题编译失败；
    ///    · URP 缺失时**优雅降级**（记 LastProblem，而不是崩）；
    ///    · 代价是成员名写错只在运行时暴露 —— 所以下面每个名字都独立判空并把原因记下来。
    ///
    /// ⚠ 不要去给 `Whisper.Runtime.asmdef` 加 URP 引用：`tools/gen-asmdef.mjs` 的规则表是固定的
    ///   （Runtime: Core/Gameplay/Net/Audio/Backend），加一条会与规则表冲突；
    ///   而纯逻辑层之外的单点需求用反射解决，正是这套架构的既定取舍。
    /// </summary>
    public static class CameraPostFx
    {
        /// <summary>最近一次失败原因（null = 没失败）；诊断与门禁可读。</summary>
        public static string LastProblem { get; private set; }

        const string DataTypeName = "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData";

        /// <summary>
        /// 让该相机参与 URP 后处理，并按需请求深度纹理。
        ///
        /// `needDepth`：SSAO/自研雾的深度重建需要 `_CameraDepthTexture`。
        /// 现阶段所有相机都传 true（成本是每帧一张深度图，室内黑场恐怖游戏值得）。
        /// </summary>
        public static void Enable(Camera cam, bool needDepth = true)
        {
            LastProblem = null;
            if (cam == null) { LastProblem = "相机为 null"; return; }

            var data = GetOrAddCameraData(cam);
            if (data == null) return;   // LastProblem 已在内部写好

            // ⚠ 成员名独立判空：反射写错名只在运行时暴露，所以每一处都把原因留下来
            if (!TrySet(data, "renderPostProcessing", true)) return;
            if (needDepth) TrySet(data, "requiresDepthTexture", true);
            EnableAntiAliasing(data);
        }

        /// <summary>
        /// 相机级抗锯齿 = **FXAA**。
        ///
        /// 【为什么是 FXAA】官方原文（`docs/reference-urp17-setup.md` §7.1）：
        /// &gt; "Note: **For anti-aliasing on mobile platforms, Unity recommends that you use FXAA.**"
        ///
        /// 本工程另开了管线级 MSAA 2x（`UrpSetup`），两者**互补而非重复**：
        ///   · MSAA 只处理**几何边缘**，官方明说它"does not fix shader aliasing issues"；
        ///   · FXAA 是后处理，处理的是**最终画面的锯齿**，顺带覆盖自发光小面积高对比边缘
        ///     （灯带/屏幕/鬼眼）——那正是本作黑场里最显眼的一类走样。
        ///
        /// 为什么用反射写：`Whisper.Runtime.asmdef` 不引用 URP 程序集（见类头说明），
        /// 直接写 `AntialiasingMode` 会 CS0234。
        ///
        /// 为什么单独成方法：这是一个**独立可验的交付项**（② 要求"每一项都要有 ON/OFF 像素证据"），
        /// 单独成方法便于取证脚本对它做 ON/OFF 对照，而不是混在"相机初始化"里说不清。
        /// </summary>
        static void EnableAntiAliasing(Component data)
        {
            var p = data.GetType().GetProperty("antialiasing",
                BindingFlags.Public | BindingFlags.Instance);
            if (p == null || !p.CanWrite)
            {
                Debug.LogWarning("[Whisper] URP 相机数据上没有可写的 antialiasing —— 相机级抗锯齿未启用");
                return;
            }
            // 枚举值由**名字**解析：不硬编码整数（枚举底层值随版本可能变）
            var modeType = p.PropertyType;
            object fxaa = null;
            foreach (var name in new[] { "FastApproximateAntialiasing", "FXAA", "FastApproximate" })
            {
                // 期望路径：名字不匹配会抛 ArgumentException，那是**预期内**的探测失败，不是错误
                try { fxaa = System.Enum.Parse(modeType, name); break; }
                catch (ArgumentException) { /* 该名字不在这个枚举里，试下一个 */ }
            }
            if (fxaa == null)
            {
                Debug.LogWarning($"[Whisper] {modeType.Name} 里找不到 FXAA 成员（候选名都试过）—— 相机级抗锯齿未启用");
                return;
            }
            p.SetValue(data, fxaa);

            // 质量档（可写）：官方有 Low/Medium/High；先取 Medium（质量与开销的中间档）
            var q = data.GetType().GetProperty("antialiasingQuality", BindingFlags.Public | BindingFlags.Instance);
            if (q != null && q.CanWrite)
            {
                object med = null;
                // 同上：质量档名可能随版本变化，取不到就保持默认（不算失败）
                try { med = System.Enum.Parse(q.PropertyType, "Medium"); }
                catch (ArgumentException) { /* 无 Medium 档则保持引擎默认 */ }
                if (med != null) q.SetValue(data, med);
            }
        }

        /// <summary>
        /// 读回相机的抗锯齿模式名（取证：证明 FXAA 真的被设上了，而不是"我调过函数"）。
        /// 返回 null = 拿不到（URP 缺失或成员改名）。
        /// </summary>
        public static string AntiAliasingModeName(Camera cam)
        {
            if (cam == null) return null;
            var data = GetOrAddCameraData(cam);
            if (data == null) return null;
            var p = data.GetType().GetProperty("antialiasing", BindingFlags.Public | BindingFlags.Instance);
            if (p == null) return null;
            var v = p.GetValue(data);
            return v != null ? v.ToString() : "null";
        }

        /// <summary>本类是否已把某个相机接进后处理（取证/自检可读）。</summary>
        public static bool IsEnabled(Camera cam)
        {
            if (cam == null) return false;
            var data = GetOrAddCameraData(cam);
            if (data == null) return false;
            var p = data.GetType().GetProperty("renderPostProcessing", BindingFlags.Public | BindingFlags.Instance);
            if (p == null || p.PropertyType != typeof(bool)) return false;
            return (bool)p.GetValue(data);
        }

        /// <summary>
        /// 拿到（没有就加）URP 的逐相机数据组件。
        ///
        /// 优先用 URP 官方的扩展方法 `GetUniversalAdditionalCameraData()`：
        /// 它**没有就自动加、有就返回现有的**，天然幂等。
        /// 自己 `AddComponent` 会在 URP 已加过时得到**第二个**实例，而 URP 只认它自己那个
        /// ⇒「我又加了一个、却仍然不生效」，是最难查的一类。
        /// </summary>
        static Component GetOrAddCameraData(Camera cam)
        {
            // ① 官方扩展方法（反射调用，避免编译期依赖 URP 程序集）
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var ext = asm.GetType("UnityEngine.Rendering.Universal.CameraExtensions", false);
                    if (ext == null) continue;
                    var m = ext.GetMethod("GetUniversalAdditionalCameraData",
                        BindingFlags.Public | BindingFlags.Static);
                    if (m == null) continue;
                    var got = m.Invoke(null, new object[] { cam }) as Component;
                    if (got != null) return got;
                }
            }
            catch (Exception e) { LastProblem = "调 GetUniversalAdditionalCameraData 失败：" + e.Message; }

            // ② 兜底：直接在相机上找那个类型（URP 可能已经加过）
            var type = FindType(DataTypeName);
            if (type == null)
            {
                LastProblem = $"找不到类型 {DataTypeName} —— URP 包没装或不叫这个名字，后处理不会生效";
                return null;
            }
            var existing = cam.GetComponent(type) as Component;
            if (existing != null) return existing;

            // ③ 都没有才自己加（同时守住"不重复加"：上面已确认不存在）
            try { return cam.gameObject.AddComponent(type) as Component; }
            catch (Exception e) { LastProblem = "AddComponent 失败：" + e.Message; return null; }
        }

        static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }

        static bool TrySet(Component data, string member, bool value)
        {
            var p = data.GetType().GetProperty(member, BindingFlags.Public | BindingFlags.Instance);
            if (p == null || !p.CanWrite || p.PropertyType != typeof(bool))
            {
                LastProblem = $"{data.GetType().Name} 上没有可写的 bool 成员 {member} —— "
                    + "URP 版本可能改了名；后处理不会按预期生效";
                return false;
            }
            p.SetValue(data, value);
            return true;
        }
    }
}
