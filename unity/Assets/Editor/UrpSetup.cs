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
                // 不静默：管线没配好会让**整屏纯黑/品红**，那必须留下可查的痕迹。
                // 但不在这里 throw —— 让真正用到渲染的任务（取证/构建）自己失败并给出上下文。
                Debug.LogError($"[UrpSetup] 编辑器加载时自动配置 URP 失败：{e.GetType().Name}: {e.Message}");
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
            bool postFxReady = VerifyRendererResources(rendererData);
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
            var t = rendererData.GetType();
            var pi = t.GetProperty("postProcessData");
            object ppd = pi != null ? pi.GetValue(rendererData) : null;
            if (ppd == null)
            {
                var fi = t.GetField("m_PostProcessData",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (fi != null) ppd = fi.GetValue(rendererData);
            }
            if (ppd != null)
            {
                Debug.Log("[UrpSetup] ✓ RendererData 资源引用齐备（postProcessData 非 null）");
                return true;
            }

            // 响亮报错（进 CI 日志、显眼），但**不中断**：
            // 后处理降级 ≠ 游戏不能跑；真正的判据在取证的像素级 ON/OFF 对照里。
            Debug.LogError("[UrpSetup] ✗ RendererData.postProcessData 仍为 null —— "
                + "URP 会**静默跳过**全部后处理（辉光/暗角/色差/颗粒/调色/色调映射一个都不会生效）。"
                + "画面仍是完整可玩的，但后处理这一整类效果缺失 —— 取证的『逐项后处理 ON/OFF』会判红。");
            return false;
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
            // threshold 偏高（0.9）是为了"只有真亮的东西才发光"，否则整个画面糊成一团。
            bloom.intensity.value = 0.35f; bloom.intensity.overrideState = true;
            bloom.threshold.value = 0.90f; bloom.threshold.overrideState = true;
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
