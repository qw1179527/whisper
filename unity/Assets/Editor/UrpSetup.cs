using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Whisper.Editor
{
    /// <summary>
    /// URP 17 的**代码真源**：创建并挂载 `UniversalRenderPipelineAsset` + `UniversalRendererData`，
    /// 填充 Volume Profile，并按"室内黑场恐怖 + 手机"配置画质档。
    ///
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// 为什么必须有这个脚本（不是"想升级"，是画质链的**根因**）
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// 实测（2026-10-06）：`unity/ProjectSettings/GraphicsSettings.asset:40`
    /// = `m_CustomRenderPipeline: {fileID: 0}` —— **没有分配任何 Render Pipeline Asset**，
    /// 所以尽管 `com.unity.render-pipelines.universal@17.0.3` 在依赖里，**实际跑的是 Built-in**。
    /// 官方定义：`GraphicsSettings.defaultRenderPipeline` 为 null 时默认管线即 Built-in。
    ///
    /// 后果（用户点名的 24 项画质）：抗锯齿/TAA、辉光、体积光、色差、颗粒、阴影质量、反射/折射
    /// 这**一整片在架构上够不着** —— 它们全是 URP 的 Volume / renderer feature 能力。
    /// 同时 `QualitySettings` 的当前档是 Very Low（`shadows: 0`、`pixelLightCount: 0`），
    /// 连"手电筒照亮房间"都做不到。
    ///
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// 为什么是 Editor 脚本而不是手写 .asset（**这条不能妥协**）
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// `unity/ProjectSettings/README.md` 明令："**不要手工创建 .asset/.unity/.meta 文件
    /// （GUID 会错，Unity 打开即报错）**"。URP Asset / Renderer Data / Volume Profile 全是 .asset，
    /// 所以只能由编辑器生成。本机没有 Unity Editor ⇒ 由云端 CI 的 `-executeMethod` 跑本脚本。
    ///
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// 设计纪律（对应本项目反复踩过的坑）
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// ① **幂等**：已存在就复用（按路径 load），不重复创建 —— 否则每跑一次 CI 就多一个资产。
    /// ② **只读字段找不到就抛异常，绝不静默跳过**：URP 17 里 `supportsMainLightShadows` 等一大票
    ///    属性只有 getter，必须走 `SerializedObject` 改**序列化字段名**；而字段名会随版本漂移
    ///    （官方源码里已有 `[Obsolete]` 的先例）。静默跳过 = "配了个看起来对、实际没生效的管线"，
    ///    正是本项目最忌讳的"构建成功但产品不对"。找不到就 FAIL 并把真实字段名打进日志。
    /// ③ **最后必须断言 `GraphicsSettings.currentRenderPipeline != null` 且类型正确**：
    ///    这是唯一可信的"真的生效了"证据，也是本脚本的判决点。
    /// ④ 数值全部标注为**工程建议**（官方只给"往哪调"的方向与少数平台硬条件），
    ///    出处见 `docs/reference-urp17-setup.md` §10.2——不要把建议值当成官方推荐值引用。
    /// </summary>
    public static class UrpSetup
    {
        const string SettingsDir  = "Assets/Settings";
        const string RendererPath = SettingsDir + "/WhisperUniversalRenderer.asset";
        const string PipelinePath = SettingsDir + "/WhisperURPAsset.asset";
        const string VolumePath   = "Assets/DefaultVolumeProfile.asset";

        /// <summary>
        /// 编辑器加载 / 脚本重编译后**自动**确保 URP 已启用。
        ///
        /// ══════════════════════════════════════════════════════════════════════════════
        /// 为什么必须是 [InitializeOnLoad] 而不是"只在 BuildScript 里调一次"
        /// ══════════════════════════════════════════════════════════════════════════════
        /// 【2026-10-06 实测踩到的真事故，代价是一轮 CI 与一屏纯黑】
        /// 我最初只在 `BuildScript.BuildAndroid()` 里调 `ConfigureUrp()`。结果：
        ///   · `unity-android`（走 BuildScript）→ 构建成功，URP 正常；
        ///   · `unity-agent` 的 **render-evidence**（走 `RenderEvidenceCapture.Run`，**不经过 BuildScript**）
        ///     → URP **从未启用**，而新着色器已是 URP 专用（pass tag = `UniversalForward`）。
        ///     在 Built-in 下没有**任何**兼容的颜色 pass（只剩 ShadowCaster/DepthOnly）⇒ **整屏纯黑**。
        ///     取证日志：所有 30 张图都变成统一的 9 KB、平均亮度 5.2、开灯/关灯差异 0.000%。
        ///
        /// ⇒ 结论：**"管线已启用"必须是编辑器级不变量，而不是某条代码路径的副作用。**
        ///    凡是能在 CI 里跑起来的东西（构建、取证、测试、任意 `-executeMethod`）都必须看到同一套设置。
        ///    这也是本项目反复写下的纪律："能做成数据/代码保证的，不要靠记得。"
        ///
        /// 幂等：已挂对就立刻返回（不重复建资产、不做无意义的 AssetDatabase 写入）。
        /// </summary>
        [InitializeOnLoadMethod]
        static void EnsureOnEditorLoad()
        {
            try
            {
                var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
                var active = GraphicsSettings.currentRenderPipeline;
                if (urp != null && active == urp)
                {
                    // 已经是对的：不动任何东西（批量模式下 AssetDatabase 写入很贵）
                    Debug.Log($"[UrpSetup] URP 已启用（编辑器加载检查）· {active.name}");
                    return;
                }
                Debug.Log("[UrpSetup] 编辑器加载时发现 URP 未启用 —— 自动配置（本次运行的所有任务都需要它）");
                ConfigureUrp();
            }
            catch (Exception e)
            {
                // 【2026-10-06 实测：这个时机失败是**预期内**的】
                // 日志原文：
                //   [UrpSetup] 编辑器加载时自动配置 URP 失败：InvalidImportException:
                //     Cannot load. Path Packages/.../Textures/BlueNoise64/L/LDR_LLL1_0.png
                //     is correct but AssetDatabase cannot load now.
                // 含义：`[InitializeOnLoadMethod]` 的时机**早于资产导入完成** —— 那一刻
                // AssetDatabase 不能加载包内资源 ⇒ 配置中途炸掉 ⇒ URP Asset 没挂上 ⇒
                // `currentRenderPipeline == null` ⇒ **渲染整屏近黑**（实测 mean luma 5.2）。
                //
                // ⇒ 降级为 Warning（原先写 Error 会让人误以为任务已坏），
                //   并把正确性交给**入口方法里的显式调用**（RenderEvidenceCapture.Run /
                //   BuildScript.BuildAndroid）。**不要依赖"加载时机"这种隐式契约。**
                Debug.LogWarning($"[UrpSetup] 编辑器加载时配置 URP 未成功（时机早于资产导入，属预期）："
                    + $"{e.GetType().Name}: {e.Message}"
                    + " —— 已交由入口方法（取证/构建）显式配置，不影响任务");
            }
        }

        [MenuItem("Whisper/配置 URP 渲染管线（画质链根因修复）")]
        public static void ConfigureUrp()
        {
            if (!AssetDatabase.IsValidFolder(SettingsDir))
                AssetDatabase.CreateFolder("Assets", "Settings");

            // ── ① Renderer Data ──
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, RendererPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[UrpSetup] 新建 RendererData → {RendererPath}");
            }
            else Debug.Log($"[UrpSetup] 复用已有 RendererData → {RendererPath}");
            // ⚠ **必须**在建好资产之后补资源引用（见方法注释：新建的实例不会走 OnEnable）
            EnsureRendererResources(rendererData);

            // ── ② URP Asset（**必须用官方工厂方法**，官方 API 页明确列出）──
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (urp == null)
            {
                urp = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(urp, PipelinePath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[UrpSetup] 新建 URP Asset → {PipelinePath}");
            }
            else Debug.Log($"[UrpSetup] 复用已有 URP Asset → {PipelinePath}");

            // 渲染器列表（只读属性 → 走序列化字段）
            AssignRendererData(urp, rendererData);
            ConfigureRendererData(rendererData);
            ConfigurePipelineSettings(urp);
            FillDefaultVolumeProfile(urp);

            EditorUtility.SetDirty(urp);
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // ── ③ 挂载：默认管线 + 当前质量档 override ──
            GraphicsSettings.defaultRenderPipeline = urp;
            QualitySettings.renderPipeline = urp;

            // ── ④ 判决点：currentRenderPipeline 是唯一可信的"生效"证据 ──
            var active = GraphicsSettings.currentRenderPipeline;
            if (active == null)
                throw new InvalidOperationException(
                    "[UrpSetup] URP 未生效：GraphicsSettings.currentRenderPipeline == null（仍是 Built-in）");
            if (!(active is UniversalRenderPipelineAsset))
                throw new InvalidOperationException(
                    $"[UrpSetup] 生效的管线类型不是 URP，而是 {active.GetType().FullName}");

            // ── ⑤ 资源完备性判决（**必须放在全部配置完成之后**）──────────────────────
            // 原先这条判在 EnsureRendererResources 里，而那一步跑在 AssignRendererData **之前** ⇒
            // 一抛异常，刚建好的 URP Asset 就没挂上管线 ⇒ currentRenderPipeline=null ⇒ 渲染全废。
            // 「一个资源没填上」被放大成「管线全废」= 判据位置错，不是判据太严。
            // 现在：该做的全做完、管线确认生效，最后才判资源 —— 抛出去时工程状态是完整一致的。
            EnsureSsaoFeature(rendererData);
            bool postFxReady = VerifyRendererResources(rendererData);

            // ── **读盘上资产**核对阴影开关是否真落盘（2026-10-06）────────────────────
            // 【为什么】阴影三条对照全 0.000%（见 docs/mechanism-gaps.md）。
            // 第一待查项就是：`m_MainLightShadowsSupported` 到底有没有**落盘**。
            // 本项目有"设了不等于生效"的先例 ⇒ 必须**读回盘上字段**核对，不能只信设置代码跑过。
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
                if (asset != null)
                {
                    var t = asset.GetType();
                    var sb = new System.Text.StringBuilder();
                    foreach (var fname in new[] { "m_MainLightShadowsSupported", "m_AdditionalLightShadowsSupported",
                                                  "m_SoftShadowsSupported", "m_ShadowDistance", "m_MainLightShadowmapResolution" })
                    {
                        var f = t.GetField(fname, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        sb.Append(fname).Append('=').Append(f != null ? f.GetValue(asset)?.ToString() : "字段不存在").Append(" · ");
                    }
                    Debug.Log("[UrpSetup][核对盘上资产] " + sb);
                    // 主光阴影必须为 true —— 否则阴影链在第一环就断了
                    var fm = t.GetField("m_MainLightShadowsSupported",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    bool ok = fm != null && fm.GetValue(asset) is bool b && b;
                    if (!ok)
                        Debug.LogError("[UrpSetup] ✗ **盘上资产**的 m_MainLightShadowsSupported 不是 true"
                            + " —— 阴影链在第一环就断了（这解释了三条阴影对照全 0.000%）");
                }
            }
            if (!postFxReady) Debug.LogError("[UrpSetup] 后处理不可用（见上一条）—— 其余渲染配置已全部完成并生效");

            Debug.Log($"[UrpSetup] ✓ active render pipeline = {active.name} ({active.GetType().FullName})"
                + $" · MSAA={urp.msaaSampleCount} · HDR={urp.supportsHDR} · renderScale={urp.renderScale}"
                + $" · shadowDistance={urp.shadowDistance} · cascades={urp.shadowCascadeCount}"
                + $" · additionalLights={urp.maxAdditionalLightsCount}");
        }

        /// <summary>
        /// 补上 UniversalRendererData 的**资源引用**（后处理资源、shader 资源）。
        ///
        /// ══════════════════════════════════════════════════════════════════════════════════
        /// 为什么必须显式做（2026-10-06 逐项 ON/OFF 取证挖到的最底层原因）
        /// ══════════════════════════════════════════════════════════════════════════════════
        /// 取证诊断（第 31 轮）：
        /// ```
        /// [RENDER][URP诊断] RendererData=UniversalRendererData
        ///   · postProcessData=**null（后处理会被静默跳过）** · renderingMode=Forward
        /// ```
        /// `UniversalRendererData` 的 `OnEnable` 会调
        /// `ResourceReloader.TryReloadAllNullIn(this, packagePath)` 把 `postProcessData` 等填上 ——
        /// **但那只在"资产被导入 / 域重载"时发生**。我们用 `CreateInstance` + `CreateAsset`
        /// 在**运行中的编辑器里**新建它，走不到那条路径 ⇒ 资源保持 null ⇒ **URP 静默跳过整个后处理 pass**。
        ///
        /// 后果正是前几轮看到的现象：相机开了后处理、profile 挂上了、6 个组件都在、
        /// 测量地基可靠（连拍 0.000%），而后处理**一个都不生效、且不报任何错**。
        ///
        /// 教训（本项目第 N 次）：「配了 ≠ 生效」。这次失效点在**最底层**：
        /// 不是效果参数、不是相机开关、不是 profile 挂载，而是"渲染器数据缺资源"。
        /// ⇒ 排查"效果不生效"应**从最底层的资源引用往上查**，而不是从参数往下猜。
        ///
        /// 实现：用官方 `ResourceReloader.TryReloadAllNullIn`（可能是 internal → 反射调用）。
        /// 它只填**为 null** 的字段、已有值不动 ⇒ 天然幂等、可重复调用。
        /// 拿不到时**不静默**：打印警告并明确说明后果。
        /// </summary>
        static void EnsureRendererResources(UniversalRendererData rendererData)
        {
            if (rendererData == null) return;

            // ── ① 先试官方 ResourceReloader（最正确的那条路）──────────────────────────
            const string urpPackagePath = "Packages/com.unity.render-pipelines.universal";
            if (TryResourceReload(rendererData, urpPackagePath))
            {
                Debug.Log("[UrpSetup] ✓ 渲染器资源引用已由 ResourceReloader 补齐");
                return;
            }

            // ── ② 回退：**直接加载已落盘的资源资产**（PostProcessData 在 URP 包里是个 .asset）──
            // 【2026-10-06 为什么必须有这条回退】第一版只走 ①，而 `ResourceReloader` 是
            // `UnityEditor.Rendering` 下的 **internal** 类型（core 包），我用
            // `asm.GetType(name)` 只搜**公开**类型 ⇒ 找不到 ⇒ 我那时的实现**throw** ⇒
            // **把刚建好的 URP Asset 一起废掉**（后面 AssignRendererData 没执行）⇒
            // `currentRenderPipeline = null` ⇒ 全盘崩（31 项判红，URP Asset=无）。
            //
            // ⚠ 教训：**判决点不能放在"会让后续步骤全失效"的位置**。
            //    抛异常本身没错，但抛在建了一半的状态上，就是把"一个资源没填上"放大成"管线全废"。
            //    ⇒ 先尽力补齐（含回退路径），最后才判；且判之前不要把工程置于半成品状态。
            bool hung = false;
            try
            {
                var ppdType = FindTypeAny("UnityEngine.Rendering.Universal.PostProcessData");
                if (ppdType != null)
                {
                    // ⚠ 不硬编码路径。第一版我写了两个"看起来对"的路径，**两个都不存在**（资产在别的子目录）
                    //   ⇒ 回退失败 ⇒（当时的）判决点 throw ⇒ 整个 URP 配置被废掉。
                    // 正解：**在 URP 包目录下按类型搜资产**（`AssetDatabase.FindAssets` 支持按类型过滤），
                    // 这样资产换位置也不会失效。
                    var guids = AssetDatabase.FindAssets("t:PostProcessData",
                        new[] { "Packages/com.unity.render-pipelines.universal" });
                    for (int i = 0; i < guids.Length && !hung; i++)
                    {
                        var p = AssetDatabase.GUIDToAssetPath(guids[i]);
                        var asset = AssetDatabase.LoadAssetAtPath(p, ppdType);
                        if (asset == null) continue;
                        hung = SetMember(rendererData, "m_PostProcessData", asset)
                            || SetMember(rendererData, "postProcessData", asset);
                        if (hung) Debug.Log($"[UrpSetup] ✓ postProcessData 已挂上（按类型搜到）：{p}");
                    }
                    if (!hung && guids.Length == 0)
                        Debug.LogWarning("[UrpSetup] URP 包里搜不到 PostProcessData 资产（t:PostProcessData 零命中）");
                }
                else
                {
                    Debug.LogWarning("[UrpSetup] 找不到类型 PostProcessData（URP 版本差异？）");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UrpSetup] 回退挂 postProcessData 失败：{e.GetType().Name}: {e.Message}");
            }

            if (hung) { EditorUtility.SetDirty(rendererData); AssetDatabase.SaveAssets(); return; }

            // ── ③ 两条路都没成：**只警告，绝不 throw**（throw 在这里会把半成品状态留给调用方）──
            // 但仍要把后果说清楚，并把结论留给调用方在**配置全部完成后**去判（见 ConfigureUrp 末尾）。
            Debug.LogWarning("[UrpSetup] ⚠ 补渲染器资源引用失败（ResourceReloader 与回退路径都没成）—— "
                + "postProcessData 若为 null，URP 会**静默跳过**全部后处理（辉光/暗角/色差/颗粒/调色/色调映射一个都不生效）");
        }

        /// <summary>试官方 `ResourceReloader.TryReloadAllNullIn(asset, packagePath)`。成功返回 true。</summary>
        static bool TryResourceReload(UnityEngine.Object asset, string packagePath)
        {
            try
            {
                // ⚠ 必须用 GetTypes() + NonPublic：ResourceReloader 是 **internal** 类型，
                //    `Assembly.GetType(name)` 默认只找公开类型（我就是这么踩进坑的）。
                System.Type reloader = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    System.Type[] types;
                    try { types = asm.GetTypes(); } catch { continue; }   // 个别程序集 GetTypes 会抛
                    foreach (var t in types)
                        if (t.FullName == "UnityEditor.Rendering.ResourceReloader") { reloader = t; break; }
                    if (reloader != null) break;
                }
                if (reloader == null) return false;
                var m = reloader.GetMethod("TryReloadAllNullIn",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Static);
                if (m == null) return false;
                m.Invoke(null, new object[] { asset, packagePath });
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
                return true;
            }
            catch { return false; }
        }

        /// <summary>按名字给对象写成员（属性或字段，公开或非公开）。成功返回 true。</summary>
        static bool SetMember(object target, string name, object value)
        {
            var t = target.GetType();
            var p = t.GetProperty(name, System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (p != null && p.CanWrite) { p.SetValue(target, value); return true; }
            var f = t.GetField(name, System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (f != null) { f.SetValue(target, value); return true; }
            return false;
        }

        /// <summary>按全名找类型（**含 internal** —— 不能只用 Asm.GetType，那样搜不到 internal）。</summary>
        static System.Type FindTypeAny(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                System.Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types) if (t.FullName == fullName) return t;
            }
            return null;
        }

        /// <summary>
        /// **配置全部完成之后**的资源完备性检查。
        ///
        /// ══════════════════════════════════════════════════════════════════════════════════
        /// ⚠ 这里**刻意不 throw** —— 我在这上面连栽两次，值得写清楚
        /// ══════════════════════════════════════════════════════════════════════════════════
        /// 第一版把 throw 放在 `EnsureRendererResources` 里，而那一步跑在 `AssignRendererData`
        /// **之前** ⇒ 一抛异常，刚建好的 URP Asset 没挂上管线 ⇒ `currentRenderPipeline = null`
        /// ⇒ **整个工程渲染全废**（实测：31 项判据不成立）。
        /// 第二版把 throw 挪到末尾，仍然 throw ⇒ 同样把一次任务整个废掉。
        ///
        /// **两次的错是同一个**：把「一个可选资源缺失」升级成「全盘失败」。
        /// 而这两件事的严重程度完全不同：
        ///   · postProcessData 为 null ⇒ **后处理降级**（画面少了辉光/颗粒/色差…），但游戏**能跑、能玩**；
        ///   · 管线没挂上 ⇒ **什么都渲染不出来**。
        /// 抛异常把前者变成了后者 —— 那是**用判据制造了比缺陷更严重的后果**。
        ///
        /// ⇒ 正确做法：**响亮地报告 + 留下可判定的标记**，让真正该判的地方去判
        ///   （取证脚本的"逐项后处理 ON/OFF"判据会在像素层面暴露它，那才是对的判据位置）。
        /// </summary>
        static bool VerifyRendererResources(UniversalRendererData rendererData)
        {
            // ══════════════════════════════════════════════════════════════════════════════
            // 【2026-10-06 第三次修这条 —— 前两次我都"猜"了对象/成员名，都错】
            //   第一次：核验**内存里传进来的实例**
            //   第二次：核验**按路径 LoadAssetAtPath 的实例**
            // 两次都报 null，而同轮 `[RENDER][URP诊断]` 用**另一条反射路径**读到 `postProcessData=有`，
            // 且 7 项后处理 ON/OFF 全部可辨。⇒ 问题在**我的读取方式**，不在产品。
            //
            // ⇒ 正解：**用与那条已验证可行的诊断完全相同的读法**（`rendererDataList` 属性 →
            //   元素 → `GetProperty("postProcessData") ?? GetField("postProcessData")`），
            //   并在读不到时**把实际成员名打出来**，不再猜。
            // ══════════════════════════════════════════════════════════════════════════════
            object target = null;
            try
            {
                var urpAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath)
                               ?? GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (urpAsset != null)
                {
                    var listProp = urpAsset.GetType().GetProperty("rendererDataList");
                    if (listProp != null)
                    {
                        var arr = listProp.GetValue(urpAsset) as System.Collections.IEnumerable;
                        if (arr != null) foreach (var x in arr) { target = x; break; }
                    }
                }
            }
            catch { }
            if (target == null) target = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (target == null) target = rendererData;
            if (target == null)
            {
                Debug.LogWarning("[UrpSetup] 核验时拿不到 RendererData（三路都为空）—— 跳过资源核验");
                return false;
            }

            var dt = target.GetType();
            System.Reflection.MemberInfo member = dt.GetProperty("postProcessData") ?? (System.Reflection.MemberInfo)dt.GetField("postProcessData");
            object ppdValue = null;
            if (member is System.Reflection.PropertyInfo mpi) ppdValue = mpi.GetValue(target);
            else if (member is System.Reflection.FieldInfo mfi) ppdValue = mfi.GetValue(target);
            if (ppdValue != null)
            {
                Debug.Log("[UrpSetup] ✓ RendererData 资源引用齐备（postProcessData 非 null；"
                    + "读取方式与 [RENDER][URP诊断] 完全一致）");
                return true;
            }

            // 读不到 ⇒ 把实际成员名打出来，供下一次直接定位（不再猜）
            var names = new System.Text.StringBuilder();
            foreach (var f in dt.GetFields(System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                if (f.Name.ToLowerInvariant().Contains("post")) names.Append(f.Name).Append(' ');
            Debug.LogError("[UrpSetup] ✗ postProcessData 读到 null（member=" + (member?.Name ?? "未找到")
                + "；类型里含 'post' 的字段：" + (names.Length > 0 ? names.ToString() : "无")
                + "）—— 若产品侧后处理 ON/OFF 仍可辨，则问题在本核验的读取方式，不在产品");
            return false;
        }

        /// <summary>
        /// 给 RendererData 挂上 **SSAO**（屏幕空间环境光遮蔽）Renderer Feature。
        ///
        /// ══════════════════════════════════════════════════════════════════════════════════
        /// 为什么现在才能做（一条被 Built-in 时代限制挡住的功能）
        /// ══════════════════════════════════════════════════════════════════════════════════
        /// `data/config.json` 的 `render.tiers` 里长期写着：
        /// &gt; `_disabledWhy`: "ssao/ssgi/eyeAdaptation/**volumetricLight** 默认关：
        /// &gt;   它们在 **OnRenderImage 的 Blit 链**里拿不到深度/读回数据（真机 raw=0.00），开启会黑屏。"
        ///
        /// **那条理由属于 Built-in 管线** —— `OnRenderImage` 是 Built-in 的相机回调。
        /// 我们已经迁到 URP，SSAO 在 URP 里的正确形态是 **Renderer Feature**
        /// （`ScreenSpaceAmbientOcclusion`，官方 API 页已核：`docs/reference-urp17-setup.md` §…），
        /// 由管线在渲染器内部、**在深度可用之后**执行 ⇒ 根本不存在"拿不到深度"的问题。
        ///
        /// ⇒ ① 环境/氛围渲染（墙角与缝隙变暗）由此可以真正开工。
        ///
        /// ⚠ 用反射：`Whisper.Runtime`/Editor 脚本不引用 URP 程序集时直接写 URP 类型会 CS0234
        ///   （本项目已实测踩过这个坑）。反射不依赖编译期成员存在。
        /// 幂等：已经有一个 SSAO 特性就直接返回（不重复挂）。
        /// </summary>
        static void EnsureSsaoFeature(UniversalRendererData rendererData)
        {
            try
            {
                var featType = FindTypeAny("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion");
                if (featType == null)
                {
                    Debug.LogWarning("[UrpSetup] 找不到 ScreenSpaceAmbientOcclusion（URP 版本差异？）—— SSAO 未挂");
                    return;
                }
                // 幂等：已有同类型特性就跳过
                var listProp = rendererData.GetType().GetProperty("rendererFeatures");
                var list = listProp != null ? listProp.GetValue(rendererData) as System.Collections.IList : null;
                if (list != null)
                {
                    foreach (var f in list)
                        if (f != null && f.GetType() == featType)
                        {
                            Debug.Log("[UrpSetup] SSAO 特性已存在（跳过）");
                            return;
                        }
                }

                var feat = ScriptableObject.CreateInstance(featType);
                if (feat == null) { Debug.LogWarning("[UrpSetup] SSAO 特性实例化失败"); return; }
                feat.name = "SSAO";

                // 参数走 SerializedObject（URP 把它们放在一个可序列化的 settings 结构里，
                // 不同小版本字段名会变 —— 所以**逐个尝试并如实报告哪个没设上**，不静默）
                try
                {
                    var so = new SerializedObject(feat);
                    int applied = 0;
                    // 常见字段名（URP 17）：m_Settings.* 下的 Intensity / Radius / SampleCount / Falloff
                    foreach (var path in new[]
                    {
                        "m_Settings.Intensity", "m_Settings.Radius", "m_Settings.Falloff",
                        "m_Settings.DirectLightingStrength", "m_Settings.SampleCount",
                    })
                    {
                        var p = so.FindProperty(path);
                        if (p == null) continue;
                        if (path.EndsWith("Intensity")) p.floatValue = 0.55f;
                        else if (path.EndsWith("Radius")) p.floatValue = 0.35f;   // 室内小尺度，别糊成一片
                        else if (path.EndsWith("Falloff")) p.floatValue = 120f;
                        else if (path.EndsWith("DirectLightingStrength")) p.floatValue = 0.15f;
                        else if (path.EndsWith("SampleCount")) p.intValue = 4;     // 移动端省
                        applied++;
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                    Debug.Log($"[UrpSetup] SSAO 参数应用 {applied} 项（字段名随版本变，0 项=用了默认值）");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[UrpSetup] SSAO 参数设置失败（用默认值继续）：{e.GetType().Name}: {e.Message}");
                }

                AssetDatabase.AddObjectToAsset(feat, rendererData);
                if (list != null) list.Add(feat);
                EditorUtility.SetDirty(rendererData);
                AssetDatabase.SaveAssets();
                Debug.Log("[UrpSetup] ✓ SSAO Renderer Feature 已挂上（环境光遮蔽：墙角/缝隙变暗）");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UrpSetup] 挂 SSAO 失败：{e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>
        /// 把 RendererData 挂进 URP Asset 的渲染器列表。
        /// 为什么走 SerializedObject：`rendererDataList` 在 URP 17 是 `ReadOnlySpan&lt;ScriptableRendererData&gt;`
        /// ⇒ **没有 setter**，只能改 `m_RendererDataList` 字段。
        /// </summary>
        static void AssignRendererData(UniversalRenderPipelineAsset urp, UniversalRendererData data)
        {
            var so = new SerializedObject(urp);
            var list = so.FindProperty("m_RendererDataList");
            if (list == null) Fail(urp, "m_RendererDataList");
            list.arraySize = Mathf.Max(1, list.arraySize);
            list.GetArrayElementAtIndex(0).objectReferenceValue = data;
            var idx = so.FindProperty("m_DefaultRendererIndex");
            if (idx != null) idx.intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);
        }

        /// <summary>
        /// URP Asset 侧设置。
        /// 【数值口径】下表为**工程建议**，官方只给"往哪调"的方向与平台硬条件
        /// （见 `docs/reference-urp17-setup.md` §10.1/§10.2）。**任何数值都必须以真机帧率为准。**
        /// </summary>
        static void ConfigurePipelineSettings(UniversalRenderPipelineAsset urp)
        {
            // ── 可直接赋值的（API 页为 { get; set; }）──
            urp.supportsHDR = true;                 // 恐怖游戏的 Bloom/自发光需要 >1 亮度
            urp.msaaSampleCount = 2;                // 保守：4x 在移动端带宽吃紧
            urp.renderScale = 1.0f;                 // 先 1.0，真机掉帧再降
            urp.upscalingFilter = UpscalingFilterSelection.Linear;   // GLES3 安全（FSR 需 sm4.5）
            urp.supportsCameraDepthTexture = true;  // 自研雾/遮挡淡出/水面泡沫需要
            urp.supportsCameraOpaqueTexture = false;// 开了会与 MSAA 冲突；只有做水面折射才开
            urp.useSRPBatcher = true;               // 前提：shader 已按 URP 改造出 CBUFFER（本轮已改）
            urp.supportsDynamicBatching = false;    // 有 GPU instancing 时官方建议关
            urp.shadowDistance = 20f;               // 室内：15~25m
            urp.shadowCascadeCount = 2;             // 官方方向："reduce Cascade Count"
            urp.mainLightShadowmapResolution = 1024;
            urp.maxAdditionalLightsCount = 4;       // 手电 + 少量室内灯；逐物体上限压低
            urp.colorGradingMode = ColorGradingMode.LowDynamicRange;   // 更省
            urp.colorGradingLutSize = 32;

            // ── 只读属性（API 页为 { get; }）→ 必须走序列化字段 ──
            SetSerialized(urp, "m_MainLightShadowsSupported",    p => p.boolValue = true);
            SetSerialized(urp, "m_AdditionalLightShadowsSupported", p => p.boolValue = false); // 手电不投影（官方方向）
            SetSerialized(urp, "m_SoftShadowsSupported",         p => p.boolValue = true);
            SetSerialized(urp, "m_SupportsLightCookies",         p => p.boolValue = true);   // 手电光锥
            SetSerialized(urp, "m_SupportsLightLayers",          p => p.boolValue = true);   // 玩法用，成本≈0
            SetSerialized(urp, "m_AdditionalLightsRenderingMode", p => p.intValue = 1);      // 1 = PerPixel
            SetSerialized(urp, "m_AdditionalLightsPerObjectLimit", p => p.intValue = 4);
            SetSerialized(urp, "m_VolumeFrameworkUpdateMode",     p => p.intValue = 0);      // 0 = EveryFrame
            SetSerialized(urp, "m_RequireDepthTexture",           p => p.boolValue = true);
            SetSerialized(urp, "m_RequireOpaqueTexture",          p => p.boolValue = false);

            // ── **QualitySettings 侧**（2026-10-06 实测抓到：它会把上面全盖掉）────────
            // 【证据】新加的「阴影质量 ON/OFF 像素证据」实测：
            //   `shadowmapResolution 256 vs 2048 变化 **0.000%**`（亮度 52.0 vs 52.0）⇒ 阴影根本没生效。
            // 根因：本工程 `ProjectSettings/QualitySettings.asset` 的当前档是 **Very Low**
            //   （`m_CurrentQuality: 5`），而该档 `shadows: 0`、`pixelLightCount: 0`。
            //   Unity 里 **QualitySettings 的阴影开关优先于 URP Asset** ⇒
            //   上面把 `m_MainLightShadowsSupported` 设成 true **完全无效**。
            // （本文件头部的注释第 25 行其实早就写着这件事，但配置代码里漏了这一步 ——
            //   "写下了" 与 "做到了" 是两件事，这类漏接在本项目已出现多次。）
            // ⇒ 显式把质量档设为**最高档**并打开阴影：
            //   这是**取证/构建环境**的统一基线；产品在真机上仍可按档位下调（那属于性能策略）。
            int top = QualitySettings.names != null ? QualitySettings.names.Length - 1 : 0;
            if (top >= 0) QualitySettings.SetQualityLevel(top, applyExpensiveChanges: true);
            QualitySettings.shadows = UnityEngine.ShadowQuality.All;
            QualitySettings.shadowResolution = UnityEngine.ShadowResolution.High;
            QualitySettings.shadowDistance = urp.shadowDistance;
            QualitySettings.pixelLightCount = 8;          // 22 盏房间点光要能被逐像素点亮
            QualitySettings.antiAliasing = 2;
            Debug.Log($"[UrpSetup] QualitySettings → 档 {QualitySettings.names?[QualitySettings.GetQualityLevel()]}"
                + $" · shadows={QualitySettings.shadows} · shadowResolution={QualitySettings.shadowResolution}"
                + $" · shadowDistance={QualitySettings.shadowDistance} · pixelLightCount={QualitySettings.pixelLightCount}");
        }

        /// <summary>
        /// Renderer Data 侧设置。
        /// Depth Priming 取 `Disabled`：官方明说 `Auto` **不支持 Android**，且开了它自定义 shader
        /// 必须有 DepthOnly/DepthNormals，否则**物体会不可见**（本轮已给两个着色器补上这两个 pass，
        /// 但仍按官方建议在移动端关掉，少一个风险面）。
        /// </summary>
        static void ConfigureRendererData(UniversalRendererData data)
        {
            data.renderingMode = RenderingMode.Forward;      // Deferred 需 G-buffer，移动端带宽贵
            data.depthPrimingMode = DepthPrimingMode.Disabled;
            data.intermediateTextureMode = IntermediateTextureMode.Auto;
            EditorUtility.SetDirty(data);
        }

        /// <summary>
        /// 填充那个**空的** `Assets/DefaultVolumeProfile.asset`（实测 `components: []`）。
        ///
        /// 【用户点名的项直接落在这里】辉光(Bloom) · 色差(ChromaticAberration) · 颗粒(FilmGrain) ·
        /// 氛围/色泽(ColorAdjustments / Tonemapping / Vignette) —— 它们全是 URP 的 Volume 组件，
        /// 在 Built-in 下**没有位置**，这就是"辉光/颗粒/色差一直做不出来"的原因。
        ///
        /// 取舍（工程判断，依据 §10.2 末注）：移动端**慎用** DepthOfField / MotionBlur
        /// （多次降采样 + 需要运动向量），故本轮不加；性价比最高的四件套先落地。
        /// </summary>
        static void FillDefaultVolumeProfile(UniversalRenderPipelineAsset urp)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            if (profile == null) throw new InvalidOperationException($"[UrpSetup] 找不到 {VolumePath}");

            // 幂等：只补缺的，不清空已有的
            var bloom = Ensure<Bloom>(profile);
            var vig   = Ensure<Vignette>(profile);
            var grade = Ensure<ColorAdjustments>(profile);
            var tone  = Ensure<Tonemapping>(profile);
            var grain = Ensure<FilmGrain>(profile);
            var ca    = Ensure<ChromaticAberration>(profile);

            // 【辉光】恐怖游戏的核心氛围手段：亮处（手电、荧光棒、鬼眼自发光）向四周溢出。
            // 【2026-10-06 实调】原 threshold 0.90 / intensity 0.35 在**暗场**几乎不触发：
            // 逐项 ON/OFF 取证实测 **0.000%**（判据下限 0.5%）。
            // 本作是黑场恐怖游戏，画面里几乎没有 0.9 以上的亮部 ⇒ 等于没有辉光。
            // 改为 0.75 / 0.55：让自发光（灯带/屏幕/鬼眼）真的溢出，仍不足以糊成一团。
            bloom.intensity.value = 0.55f; bloom.intensity.overrideState = true;
            bloom.threshold.value = 0.75f; bloom.threshold.overrideState = true;
            bloom.scatter.value = 0.65f;   bloom.scatter.overrideState = true;
            bloom.highQualityFiltering.value = false; bloom.highQualityFiltering.overrideState = true;  // 移动端省

            // 【暗角】把视线压向画面中心 —— 恐怖游戏的基础构图手段
            vig.intensity.value = 0.42f;  vig.intensity.overrideState = true;
            vig.smoothness.value = 0.45f; vig.smoothness.overrideState = true;

            // 【色泽】去饱和 + 提对比 + 略压曝光：恐怖片调色
            grade.saturation.value = -18f;  grade.saturation.overrideState = true;
            grade.contrast.value = 12f;     grade.contrast.overrideState = true;
            grade.postExposure.value = -0.15f; grade.postExposure.overrideState = true;

            // 【色调映射】ACES：高光滚降，自发光不会直接削顶成一片白
            tone.mode.value = TonemappingMode.ACES; tone.mode.overrideState = true;

            // 【颗粒】胶片质感；必须随帧变化，静止的颗粒看起来像脏屏幕
            grain.intensity.value = 0.25f; grain.intensity.overrideState = true;
            grain.response.value = 0.70f;  grain.response.overrideState = true;

            // 【色差】边缘彩边：低强度是"镜头感"，高了是"坏了"，故只给 0.08
            ca.intensity.value = 0.08f; ca.intensity.overrideState = true;

            // 新建出来的 VolumeComponent 是 ScriptableObject（**子资产**），
            // 不 AddObjectToAsset 持久化，下次打开工程 components 里的引用会变成 null。
            foreach (var c in profile.components)
                if (c != null && AssetDatabase.GetAssetPath(c) != VolumePath)
                    AssetDatabase.AddObjectToAsset(c, profile);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            urp.volumeProfile = profile;   // 该属性可写
            EditorUtility.SetDirty(urp);
            AssetDatabase.SaveAssets();

            var names = new StringBuilder();
            foreach (var c in profile.components) names.Append(c != null ? c.GetType().Name + " " : "null ");
            Debug.Log($"[UrpSetup] Volume Profile {VolumePath} → {profile.components.Count} 个组件：{names}");
            if (profile.components.Count == 0)
                throw new InvalidOperationException("[UrpSetup] Volume Profile 仍是空的 —— 后处理不会生效");
        }

        /// <summary>幂等地取一个 Volume 组件：已有就返回它，没有就新建。</summary>
        static T Ensure<T>(VolumeProfile profile) where T : VolumeComponent
        {
            // 显式 for 循环而不是 List.Find + lambda：lambda 在这里会被推断成方法组，
            // 报 CS1503/CS0019（本机语法门禁实测）。显式循环更好读，也没有推断歧义。
            for (int i = 0; i < profile.components.Count; i++)
            {
                var c = profile.components[i];
                if (c is T hit) return hit;
            }
            return profile.Add<T>(overrides: true);
        }

        /// <summary>
        /// 改只读属性的序列化字段。**找不到就抛异常并 dump 出真实字段名** ——
        /// 静默跳过会产出"看起来配好了、实际没生效"的管线（本项目最忌讳的失效形态）。
        /// </summary>
        static void SetSerialized(UnityEngine.Object asset, string propertyPath, Action<SerializedProperty> set)
        {
            var so = new SerializedObject(asset);
            var p = so.FindProperty(propertyPath);
            if (p == null) Fail(asset, propertyPath);
            set(p);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        static void Fail(UnityEngine.Object asset, string propertyPath)
        {
            var so = new SerializedObject(asset);
            var it = so.GetIterator();
            var sb = new StringBuilder();
            while (it.NextVisible(true)) sb.Append(it.propertyPath).Append("  ");
            throw new InvalidOperationException(
                $"[UrpSetup] 序列化字段不存在：{propertyPath}（URP 版本漂移？）\n"
                + $"资产 {asset.GetType().Name} 的真实字段：{sb}");
        }
    }
}
