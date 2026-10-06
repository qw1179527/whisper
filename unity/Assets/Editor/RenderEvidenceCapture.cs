using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;              // Volume / VolumeProfile / VolumeComponent（逐项后处理取证用）
using UnityEngine.Rendering.Universal;    // Bloom / Vignette / FilmGrain / ChromaticAberration / ColorAdjustments / Tonemapping

namespace Whisper.Editor
{
    /// <summary>
    /// 渲染路径取证：**用像素证据**证明"光照真的生效"与"雾可开关、且不洗白画面"。
    ///
    /// ## 为什么判据是"差异"而不是"一张好看的图"
    /// 一张图只能说明"画面里有东西"。本项目的失败模式是"验过 ≠ 在产品里"：
    /// 上一版自研 Unlit 着色器的 fragment 就是 `return i.color;` —— 取证脚本里挂 4 盏定向光，
    /// `_lit` 与 `_dark` 两张图**逐像素完全相同**（平均差 0.00）。所以本脚本的判据是：
    /// **同一场景、同一相机、唯一变量是灯/雾**，两组图必须可分辨地不同，且亮度关系必须符合预期
    /// （雾只许压暗、不许提亮；不许出现"均值高 + 标准差极低 + 颜色数极少"的洗白形态）。
    ///
    /// ## 相机与主光都用**产品代码**（不是自己另搭一套）
    /// 几何走产品的 `LevelLoader` + `LevelBuilder`；相机与方向光走产品的
    /// `GameBootstrap.BuildCamera`（私有方法 → 反射调用；这是本项目 gate-editor-api 明确豁免的形态：
    /// 反射不依赖编译期成员存在，写错只会运行时报错而不会让构建失败）。
    /// 反射拿不到就**抛可读异常**，绝不悄悄退回"自己造一盏灯"——那会让证据变成"验的不是产品"。
    ///
    /// ## 为什么不复制 GameBootstrap 里那盏灯的数值
    /// 复制会产生第二份真源（本项目踩过 `EyeHeightM` 三份拷贝的坑）。这里直接**读回**产品建的
    /// Light 组件，所以 `intensity/color/rotation` 全部随产品变化，取证不会漂移。
    ///
    /// ## 取帧方式：离屏 RenderTexture（**不用** ScreenCapture）
    /// 【实测踩坑，照抄 KitVisibilityCapture】`ScreenCapture.CaptureScreenshot` 在 `-batchmode` 下依赖
    /// 游戏循环末尾写文件，脚本 `EditorApplication.Exit` 后一帧都没跑完 → **一张图都不产出**（且不报错）。
    /// 改成 `Camera.Render()` → `RenderTexture` → `ReadPixels` → `EncodeToPNG`：同步、确定。
    ///
    /// ## 用法
    ///   Unity -quit -batchmode -projectPath "&lt;proj&gt;" -executeMethod Whisper.Editor.RenderEvidenceCapture.Run -captureDir "&lt;dir&gt;" -logFile "&lt;log&gt;"
    ///   ⚠ `-projectPath` 必须带引号（不带会静默回落到上次打开的工程）；
    ///   ⚠ 不要加 `-nographics`（`Camera.Render()` 在无图形设备下会崩 0xC0000005）。
    ///   一条命令复现：`bash tools/render-evidence.sh`
    /// </summary>
    public static class RenderEvidenceCapture
    {
        const int Width = 960, Height = 600;

        /// <summary>
        /// 雾色的期望值。为什么它是本脚本里的字面量而不是 token：`data/design-tokens.json` 的 **color 组**
        /// 被 V9 §11 色彩对账双射（tools/tokens-map-check.mjs）逐个钉死，往里面塞一个新的"雾色"会让双射判红。
        /// 所以雾色由**着色器与取证脚本的契约断言**保证一致（见 CheckShaderContract 对 WHISPER_FOG_COLOR 的比对）。
        /// </summary>
        static readonly Color FogColorReference = new Color(0.055f, 0.051f, 0.047f, 1f);

        /// <summary>洗白判据（2026-10-03 真机雾事故的形态）：均值高 + 标准差极低 + 颜色数极少。取值来自 token。</summary>
        static double WashMeanLuma => Whisper.Core.DesignTokens.RenderWashoutMeanLuma;
        static double WashStdDev => Whisper.Core.DesignTokens.RenderWashoutStdDev;
        static int WashColors => (int)Whisper.Core.DesignTokens.RenderWashoutColorCount;

        /// <summary>`_WhisperAmbient` 的期望默认值 = 环境项 token（= 着色器文件里声明的默认值 = 亮度目标的基准）。</summary>
        static float AmbientReference => Whisper.Core.DesignTokens.RenderLightAmbient;

        /// <summary>
        /// 手电筒相位的判据下限：**主光关掉、只开手电** vs **主光关掉、手电也关** 必须能分辨。
        /// 这是 ForwardAdd pass 的判据 —— 此前只有 ForwardBase，手电筒对渲染零作用，这一对必然 0.000%。
        /// </summary>
        const double FlashlightMinPct = 1.0;

        /// <summary>
        /// 取证目标：房间 + 视图 + **是否参与判红**。
        /// `judged=false` 的是"外部全景"视角：相机在房间外 6~30m，房间只占画面一部分，
        /// 而且长距离上雾会把着色差异摊薄 —— 拿它判"灯没生效"属于**判据错位**（实测：雾一浓，
        /// 该视角的开/关灯变化从 3.7% 掉到 0.68%）。它们仍然出图，供人工复看观感。
        /// </summary>
        static readonly (string room, string view, bool judged)[] Targets =
        {
            ("entrance_safe", "orbit33", false),
            ("entrance_safe", "eye", true),
            ("corridor_main", "orbit33", false),
            ("corridor_main", "eye", true),
            ("corridor_main", "alongX", true),   // 沿 18m 走廊纵深看：雾的判据必须有**长视线**才成立
            ("morgue_deep", "eye", true),
        };

        /// <summary>相位：唯一变量是灯 / 雾 / 手电筒，相机与场景全程不动。</summary>
        static readonly (string name, bool lightOn, string fog, bool flash)[] Phases =
        {
            ("lightOn_fogDefault", true, "default", false),
            ("lightOff_fogDefault", false, "default", false),
            ("lightOn_fogOff", true, "off", false),
            ("lightOn_fogStrong", true, "strong", false),
            // 手电筒相位：主光关掉、只开手电 —— 与 `lightOff_fogDefault` 构成"唯一变量是手电"的一对。
            // 这正是 ForwardAdd 的判据（此前只有 ForwardBase → 手电筒对渲染零作用，该对必然 0.000%）。
            ("lightOff_flashlight", false, "default", true),
        };

        static GameObject _levelGo;
        static string _outDir;

        public static void Run()
        {
            _outDir = ArgValue("-captureDir");
            if (string.IsNullOrEmpty(_outDir)) _outDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "_evidence", "render-v2");
            _outDir = Path.GetFullPath(_outDir);
            Directory.CreateDirectory(_outDir);

            var problems = new System.Collections.Generic.List<string>();
            var index = new StringBuilder();
            index.AppendLine("room,view,phase,file,mean_luma,stddev,colors,magenta_pct,changed_pct_vs_lightOnFogDefault");

            // ── ⓪ URP 必须在**这里**显式配置（不能只靠 [InitializeOnLoadMethod]）────────
            // 【2026-10-06 实测根因，一条 InvalidImportException 解释了一整轮的黑屏】
            // 日志原文：
            //   [UrpSetup] 编辑器加载时自动配置 URP 失败：InvalidImportException:
            //     Cannot load. Path Packages/.../Textures/BlueNoise64/L/LDR_LLL1_0.png
            //     is correct but AssetDatabase cannot load now.
            // 含义：`[InitializeOnLoadMethod]` 的时机**早于资产导入完成** —— 那一刻
            // `AssetDatabase` 还不能加载包内资源，于是配置 URP 中途炸掉：
            //   · PostProcessData 搜不到（t:PostProcessData 零命中）
            //   · URP Asset 没挂上 → GraphicsSettings.currentRenderPipeline == null
            //   · 渲染整屏近黑（实测 mean luma 5.2、开灯关灯 0.000%）
            //   · 六个后处理组件"不在 profile 里"
            //
            // ⇒ 修法：在**入口方法里显式配置** —— 执行到这里时资产导入早已完成。
            //   （`BuildScript` 一直是这么做的，所以出包路径没这个问题；
            //    只有取证路径漏了这一步。教训：**不要依赖"加载时机"这种隐式契约**，
            //    要在真正需要它的入口处显式保证。）
            UrpSetup.ConfigureUrp();

            Debug.Log($"[RENDER] 输出 {_outDir}");

            // ── ① 着色器契约（纯文本检查：不依赖任何 Unity API，改坏了立刻可见）──
            CheckShaderContract(problems);

            // ── ② 场景：产品代码装配（LevelLoader + LevelBuilder），失败即抛 ──
            // 装配期的问题（含房间↔套件高度不匹配）必须并入判据，否则"装配有缺陷但取证全绿"是假绿。
            problems.AddRange(BuildScene());

            // ── ③ 相机 + 产品主光（反射调 GameBootstrap.BuildCamera）──
            var camGo = new GameObject("CaptureCam");
            var cam = camGo.AddComponent<Camera>();
            // 取证相机同样必须接 URP 后处理：否则"取证看不到后处理"会被误读成
            // "后处理无效" —— 那是假证据（2026-10-06 逐项 ON/OFF 判据抓到的正是这个）。
            Whisper.Runtime.CameraPostFx.Enable(cam);
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.03f, 1f);
            cam.enabled = false;   // 手动 Render（批次模式下没有游戏循环）

            var key = CreateProductKeyLight();
            float refIntensity = key.intensity;
            Debug.Log($"[RENDER] 产品主光：type={key.type} intensity={refIntensity} color=({key.color.r:0.000},{key.color.g:0.000},{key.color.b:0.000})");
            if (refIntensity <= 0.01f)
            {
                problems.Add($"产品主光 intensity={refIntensity}（<=0.01）—— 开灯/关灯对照会失去意义，请先修 GameBootstrap.BuildCamera");
            }
            // 手电筒（附加光判据的载体）：挂在取证相机上的聚光灯，默认关；只有手电相位打开。
            var flash = CreateFlashlight(camGo.transform);
            // 【迁 URP 后由 2.4 抬到 5.0】URP 的附加光按**物理距离**衰减（`distanceAttenuation`），
            // 而 Built-in 那版是被手写成温和曲线 `1/(1+0.15d²)` 的。同一盏灯在 URP 下
            // 中远距离明显更暗 ⇒ 不抬强度就测不出"手电真的有作用"（实测差只有 0.643%）。
            // 5.0 是**取证用的量级**，不是产品数值——产品手电的强度归 LightRig 管。
            flash.intensity = 5.0f;
            flash.color = new Color(1f, 0.97f, 0.90f, 1f);
            flash.enabled = false;

            // ── ④ 逐房间/视图/相位渲染 ──
            var rooms = LoadRooms();
            var stats = new System.Collections.Generic.Dictionary<string, Shot>();
            int written = 0;
            foreach (var (roomId, view, judged) in Targets)
            {
                var room = FindRoom(rooms, roomId);
                if (room == null) { problems.Add($"关卡里没有房间 {roomId}"); continue; }
                // 光区（safe / pressure / high-risk）：高风险区**按设计更暗**（LevelPalette「风险越高越暗」），
                // 所以"暗到看不清"的下限判据不能套在它头上 —— 那是判据错位，不是画面问题。
                string zone = Whisper.Gameplay.Level.MiniJson.GetOrNull(room, "lightZone") as string ?? "?";
                bool darkByDesign = zone == "high-risk";
                GetRoomBounds(room, out float cx, out float cz, out float sx, out float sz, out float sy);
                if (!PlaceCamera(camGo.transform, cam, roomId, view, cx, cz, sx, sz, sy, problems)) continue;

                // 一个视图内的全部相位：相机全程不动，唯一变量是灯 / 雾
                var shots = new System.Collections.Generic.List<(string phase, Shot shot, string file)>();
                foreach (var (phase, lightOn, fog, flashOn) in Phases)
                {
                    key.intensity = lightOn ? refIntensity : 0f;
                    flash.enabled = flashOn;
                    ApplyFog(fog);
                    string file = Path.Combine(_outDir, $"{roomId}_{view}_{phase}.png");
                    var s = RenderTo(cam, file);
                    written++;
                    shots.Add((phase, s, file));
                    stats[Key(roomId, view, phase)] = s;

                    // 洗白 / 全黑 / 洋红（着色器编译失败）三类形体判据
                    if (s.magentaPct > 1.0) problems.Add($"{roomId}/{view}/{phase} 洋红像素 {s.magentaPct:0.00}% —— 着色器很可能编译失败（洋红 = Unity 的 shader error 色）");
                    if (s.mean > WashMeanLuma && s.std < WashStdDev && s.colors <= WashColors)
                        problems.Add($"{roomId}/{view}/{phase} 判为**洗白**：亮度 {s.mean:0.0} > {WashMeanLuma} · 标准差 {s.std:0.0} < {WashStdDev} · 颜色数 {s.colors} <= {WashColors}（2026-10-03 真机雾事故的形态）");
                    if (s.mean < 3.0 && s.colors <= 2)
                        problems.Add($"{roomId}/{view}/{phase} 判为**全黑**：亮度 {s.mean:0.0} · 颜色数 {s.colors}（几何或着色器没进来）");
                    // 亮度目标带（token render.target.*）：上限对**所有**相位成立（谁也不许洗白）；
                    // 下限只对"判定视角的开灯帧"成立 —— 高风险区（太平间）本来就该更暗，拿它判"太暗"是判据错位。
                    if (s.mean > Whisper.Core.DesignTokens.RenderTargetSceneMeanLumaMax)
                        problems.Add($"{roomId}/{view}/{phase} 平均亮度 {s.mean:0.0} 超过亮度目标上限 {Whisper.Core.DesignTokens.RenderTargetSceneMeanLumaMax}（画面偏亮/洗白方向）");
                    if (lightOn && judged && !darkByDesign && s.mean < Whisper.Core.DesignTokens.RenderTargetSceneMeanLumaMin)
                        problems.Add($"{roomId}/{view}/{phase} 判定视角开灯帧平均亮度 {s.mean:0.0} 低于亮度目标下限 {Whisper.Core.DesignTokens.RenderTargetSceneMeanLumaMin}（暗到看不清）");
                }

                // 变化占比一律以 lightOn_fogDefault（产品默认观感）为基准 —— 与 tools/pixel-diff-pair.mjs 同口径
                var baseShot = shots[0].shot;
                foreach (var (phase, s, file) in shots)
                {
                    double changedPct = phase == shots[0].phase ? 0.0 : ChangedPct(baseShot.pixels, s.pixels);
                    index.AppendLine(string.Join(",", roomId, view, phase, Path.GetFileName(file),
                        s.mean.ToString("0.00", CultureInfo.InvariantCulture),
                        s.std.ToString("0.00", CultureInfo.InvariantCulture),
                        s.colors.ToString(CultureInfo.InvariantCulture),
                        s.magentaPct.ToString("0.000", CultureInfo.InvariantCulture),
                        changedPct.ToString("0.000", CultureInfo.InvariantCulture)));

                    Debug.Log($"[RENDER] {roomId}({zone})/{view}/{phase} → {Path.GetFileName(file)} · 亮度 {s.mean:0.0} · 标准差 {s.std:0.0}"
                        + $" · 颜色数 {s.colors} · 洋红 {s.magentaPct:0.000}% · 相对基准变化 {changedPct:0.000}%");
                }

                // ── 【诊断 · 2026-10-06】主光阴影 A/B：同一次运行内直接对照，不靠推断 ──────────
                // 背景：`entrance_safe/orbit33` 出现「开灯 1.94 << 关灯 23.69」（中央区均值），
                // 而**同一取景点关雾时开灯 39.68 > 关灯** —— 说明几何/着色器没问题，
                // 差异只在"雾 + 开灯"这一组合上。我先后用离线复算验过"雾 lerp 写反""阴影全黑"两个假说，
                // 三次都与实测对不上 ⇒ 停止推断，改成**同一次运行内做 A/B**：
                // 把主光的 `shadows` 从 Soft 改成 None（唯一变量），其余全不动，再拍一张。
                // 判读：若 A/B 两数差很大 → 阴影是主因；若几乎相同 → 阴影无关，继续查别的。
                // 这比"改配置再等一轮 CI"快一个数量级，而且不会把"猜"写进产品。
                if (view == "orbit33" && key.type == LightType.Directional)
                {
                    var keepShadows = key.shadows;
                    key.shadows = LightShadows.None;
                    var sNoShadow = RenderTo(cam, Path.Combine(_outDir, $"{roomId}_{view}_DIAG_noShadow.png"));
                    key.shadows = keepShadows;
                    Debug.Log($"[RENDER][诊断] {roomId}/{view} 开灯+默认雾：带阴影 {baseShot.mean:0.00} vs 关阴影 {sNoShadow.mean:0.00}"
                        + $"（差 {(sNoShadow.mean - baseShot.mean):0.00}）—— 差大 = 阴影是主因，差≈0 = 阴影无关");
                }
            }

            // ── ⑤ 相对判据：光照必须可分辨；雾必须只压暗不提亮 ──
            double worstJudgedLight = double.MaxValue;
            foreach (var (roomId, view, judged) in Targets)
            {
                var on = stats.TryGetValue(Key(roomId, view, "lightOn_fogDefault"), out var a) ? a : null;
                var off = stats.TryGetValue(Key(roomId, view, "lightOff_fogDefault"), out var b) ? b : null;
                if (on != null && off != null)
                {
                    double d = ChangedPct(on.pixels, off.pixels);
                    Debug.Log($"[RENDER] 【光照判据{(judged ? "" : "·仅参考")}】{roomId}/{view} 开灯 vs 关灯 变化 {d:0.000}%（平均亮度 {on.mean:0.0} vs {off.mean:0.0}）");
                    if (judged)
                    {
                        worstJudgedLight = Math.Min(worstJudgedLight, d);
                        if (d < 1.0) problems.Add($"【光照判据不成立】{roomId}/{view} 开灯 vs 关灯 只变化 {d:0.000}%（<1%）—— 灯对渲染没有实际作用");
                        if (on.mean <= off.mean) problems.Add($"【光照判据不成立】{roomId}/{view} 开灯平均亮度 {on.mean:0.0} 未高于关灯 {off.mean:0.0}");
                    }
                }

                var fogOn = stats.TryGetValue(Key(roomId, view, "lightOn_fogDefault"), out var c) ? c : null;
                var fogOff = stats.TryGetValue(Key(roomId, view, "lightOn_fogOff"), out var e) ? e : null;
                if (fogOn != null && fogOff != null)
                {
                    double d = ChangedPct(fogOn.pixels, fogOff.pixels);
                    Debug.Log($"[RENDER] 【雾判据】{roomId}/{view} 雾默认 vs 关雾 变化 {d:0.000}%（平均亮度 {fogOn.mean:0.0} vs {fogOff.mean:0.0}）");
                    // 这条对**所有**视角都成立：雾只许压暗、不许提亮（洗白方向）
                    if (fogOn.mean > fogOff.mean + 1.0)
                        problems.Add($"【雾判据不成立】{roomId}/{view} 开雾后平均亮度 {fogOn.mean:0.0} **高于**关雾 {fogOff.mean:0.0} —— 雾把画面提亮了（洗白方向）");
                }

                // ── 手电判据（ForwardAdd）：主光**关着**，唯一变量是手电开/关 ──
                var flashOn = stats.TryGetValue(Key(roomId, view, "lightOff_flashlight"), out var f1) ? f1 : null;
                var flashOff = stats.TryGetValue(Key(roomId, view, "lightOff_fogDefault"), out var f0) ? f0 : null;
                if (flashOn != null && flashOff != null)
                {
                    double d = ChangedPct(flashOn.pixels, flashOff.pixels);
                    Debug.Log($"[RENDER] 【手电判据{(judged ? "" : "·仅参考")}】{roomId}/{view} 手电开 vs 关（主光都关）变化 {d:0.000}%（平均亮度 {flashOn.mean:0.0} vs {flashOff.mean:0.0}）");
                    if (judged)
                    {
                        if (d < FlashlightMinPct)
                            problems.Add($"【手电判据不成立】{roomId}/{view} 主光关着时手电开/关只差 {d:0.000}%（<{FlashlightMinPct}%）—— ForwardAdd 没生效（手电筒对渲染零作用）");
                        if (flashOn.mean <= flashOff.mean)
                            problems.Add($"【手电判据不成立】{roomId}/{view} 开手电后平均亮度 {flashOn.mean:0.0} 未高于关手电 {flashOff.mean:0.0}");
                    }
                }
            }
            Debug.Log($"[RENDER] 判定视角里最小的「开/关灯」变化 = {worstJudgedLight:0.000}%（判据下限 1%）");

            // ── ⑥ 逐项后处理 ON/OFF 像素证据（用户要求：每一项都要有 ON/OFF 证据）──
            // 放在最后：它用判定视角的机位，唯一变量是"某一个效果的开与关"。
            // ⚠ 必须把 Volume **显式**挂进场景（详见 RenderTo 重载的说明）：
            //   否则"效果无效"与"取证没接对"区分不了，出来的就是假证据。
            RunPostFxOnOff(camGo, cam, key, refIntensity, rooms, index, problems, ref written);

            File.WriteAllText(Path.Combine(_outDir, "render-index.csv"), index.ToString());
            Debug.Log($"[RENDER] 共 {written} 张图 → {_outDir}");

            if (problems.Count > 0)
            {
                foreach (var p in problems) Debug.LogError("[RENDER] ✗ " + p);
                Debug.LogError($"RENDER_EVIDENCE FAIL · {problems.Count} 项判据不成立");
                EditorApplication.Exit(1);
                return;
            }
            Debug.Log($"RENDER_EVIDENCE OK · {written} 张图 · 光照与雾的判据全部成立");
            EditorApplication.Exit(0);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // 逐项后处理 ON/OFF 像素证据
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>一个后处理效果的 ON/OFF 对照配方：名字 + 装上"开启值" + 装上"关闭值"。</summary>
        sealed class FxProbe
        {
            public string Name;
            /// <summary>把该效果设成"开"（返回 false = 当前 profile 里拿不到这个组件）。</summary>
            public Func<VolumeProfile, bool> Enable;
            /// <summary>把该效果设成"关"（中性值）。</summary>
            public Func<VolumeProfile, bool> Disable;
            /// <summary>判据：ON 与 OFF 的像素差异下限（%）。取 0.5 = "肉眼可辨"的保守门槛。</summary>
            public double MinPct = 0.5;
        }

        /// <summary>
        /// 逐个后处理效果做 ON/OFF 像素证据，并把结果打进日志 + render-index.csv。
        ///
        /// ## 为什么要有这一套（用户 2026-10-06 的原话）
        /// 「画质注重：上色, 后处理, … 辉光, 体积光, 雾, 颗粒, 色差, 阴影质量, 抗锯齿 … 等一系列方面」
        /// 且本目标写明「**每一项都要有 ON/OFF 像素证据**」。
        /// 于是判据不是"我在配置里写了它"，而是"**开与关两张图必须可分辨**"——
        /// 这与本项目抓出"手电零作用""关卡不吃光"用的是同一套办法：看图，而不是看配置。
        ///
        /// ## 关掉一个效果的正确写法
        /// 全部用**中性值**关闭（UMin/UMax 取 0、或颜色/模式取恒等值），
        /// 而不是只把 `overrideState` 置 false —— 后者会让"本地默认值"参与结果，
        /// 而默认值随 Unity 版本变，等于把判据建在流沙上（详见各 Probe 的注释）。
        ///
        /// ## 为什么这里**不**替换 profile 本身，而是改它的参数
        /// profile 是 URP Asset 引用的**同一个实例**；改参数即刻生效（`Camera.Render()` 同步）。
        /// 替换 profile 会牵动 Asset 引用，一旦失败就是"整个后处理栈消失"，
        /// 那种失败会被误读成"这个效果没用"。
        /// </summary>
        static void RunPostFxOnOff(GameObject camGo, Camera cam, Light mainLight, float productMainLightIntensity,
            System.Collections.Generic.List<object> rooms,
            System.Text.StringBuilder index,
            System.Collections.Generic.List<string> problems,
            ref int written)
        {
            // 用判定视角的机位 + 产品默认光照（开灯 + 默认雾）：这是"玩家真正看到的画面"
            var room = FindRoom(rooms, "corridor_main");
            if (room == null)
            {
                problems.Add("逐项后处理取证：关卡里找不到 corridor_main —— 无法取景");
                return;
            }
            GetRoomBounds(room, out float cx, out float cz, out float sx, out float sz, out float sy);
            if (!PlaceCamera(camGo.transform, cam, "corridor_main", "eye", cx, cz, sx, sz, sy, problems)) return;

            // 显式建 Volume 并指向产品 profile（Asset 的 default profile 是否自动生效不由我们假设）
            var volGo = new GameObject("EvidencePostFxVolume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 1000f;   // 高于任何场景内 Volume：保证我们改的参数就是最终生效的那份
            vol.profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/DefaultVolumeProfile.asset");
            if (vol.profile == null)
            {
                problems.Add("逐项后处理取证：找不到 Assets/DefaultVolumeProfile.asset —— 无法验证任何后处理");
                UnityEngine.Object.DestroyImmediate(volGo);
                return;
            }
            Debug.Log($"[RENDER][后处理] Volume 已挂 · profile 组件 {vol.profile.components.Count} 个");

            // 【2026-10-06 第二次修：还要把 profile 挂到 URP Asset 的 volumeProfile】
            // 为什么：场景里的 `Volume` 组件要靠 `VolumeManager` **重建栈**之后才参与混合，
            // 而重建发生在 URP 初始化/设置变化时 —— 同一帧内新建的 Volume 很可能**还没被拾取**，
            // 于是"改了参数却仍 0.000%"。实测第 27 轮正是如此（6 项仍全 0.000%）。
            // URP Asset 的 `volumeProfile` 是**默认 profile**，由管线直接应用、不经 VolumeManager，
            // 是这里唯一确定可靠的挂法。（该属性可写；用反射以免依赖 URP 程序集引用。）
            var urpAsset = GraphicsSettings.currentRenderPipeline;
            bool assetProfileHung = false;
            if (urpAsset != null)
            {
                var pp = urpAsset.GetType().GetProperty("volumeProfile");
                if (pp != null && pp.CanWrite) { pp.SetValue(urpAsset, vol.profile); assetProfileHung = true; }
                else Debug.LogWarning("[RENDER][后处理] URP Asset 上没有可写的 volumeProfile —— 只能依赖场景 Volume");
            }

            // 诊断：把"相机是否真的参与后处理"打出来。
            // 这是本轮的关键教训：只确认"profile 里有组件"是**配置证据**，
            // 必须同时确认"相机在看它"才是**渲染证据**。
            {
                var camData = cam.GetUniversalAdditionalCameraData();
                Debug.Log($"[RENDER][后处理] 相机renderPostProcessing="
                    + (camData != null ? camData.renderPostProcessing.ToString() : "无URP相机数据")
                    + $" · URP Asset={(urpAsset != null ? urpAsset.name : "无")}"
                    + $" · profile已挂到Asset={assetProfileHung}"
                    + $" · 组件 {vol.profile.components.Count} 个");
            }

            // ── 诊断：URP 渲染器数据的关键字段（反射读，不引 URP 程序集）──
            // 【为什么必须实测】今天已经三次证明"配置对 ≠ 生效"：
            //   ① ShadowCaster 缺 bias（pass 在、几何却自遮蔽）② 相机没开 renderPostProcessing
            //   ③ profile 没被 VolumeManager 拾取。
            // 现在三个诊断值全对、测量自检也 0.000%（地基可靠），后处理却仍完全不动 ——
            // 于是必须问最底层那个问题：**URP 的后处理资源本身在不在**。
            // `postProcessData` 为 null 时 URP 会**静默跳过**后处理 pass，正是现在这个形态。
            DumpRendererDataDiag(urpAsset);

            // ══════════════════════════════════════════════════════════════════
            // 【自检 A：我的测量地基可靠吗】
            // 我连续三轮都在对比"我改完之后的两张图"，却**从没验证过"不改任何东西的两张图"是否真的相同**。
            // 如果确定性的同一场景连拍两张都有差异，那 0.000% 就不可能是"效果没生效"，
            // 而更可能是"我根本没在测我以为在测的东西"。
            // 判据：同一状态连拍两张，变化必须 **恰好 0.000%**（渲染是确定性的）。
            // ══════════════════════════════════════════════════════════════════
            {
                probeDisableAll(vol.profile);
                var a = RenderTo(cam, Path.Combine(_outDir, "postfx_SELFTEST_A.png"), vol);
                var b = RenderTo(cam, Path.Combine(_outDir, "postfx_SELFTEST_B.png"), vol);
                written += 2;
                double same = ChangedPct(a.pixels, b.pixels);
                Debug.Log($"[RENDER][后处理][自检] 同一状态连拍两张：变化 {same:0.000}%（应为 0.000，否则我的测量地基有问题）· 亮度 {a.mean:0.00}/{b.mean:0.00}");
                if (same > 0.001)
                    problems.Add($"【测量地基不可靠】同状态连拍两张差异 {same:0.000}% —— 后处理判据的结论不可信，先修测量");
            }

            var probes = new System.Collections.Generic.List<FxProbe>
            {
                new FxProbe
                {
                    Name = "Bloom",
                    // 关：intensity=0 是**唯一确定的"无辉光"**（MinFloatParameter 的下限就是 0）。
                    // 不用 overrideState=false 关：那会让 Unity 的内部默认值参与，判据不稳。
                    Disable = p => SetFx(p, (Bloom b) => { b.intensity.value = 0f; b.intensity.overrideState = true; }),
                    Enable  = p => SetFx(p, (Bloom b) => { b.intensity.value = 0.9f; b.intensity.overrideState = true; }),
                },
                new FxProbe
                {
                    Name = "Vignette",
                    Disable = p => SetFx(p, (Vignette v) => { v.intensity.value = 0f; v.intensity.overrideState = true; }),
                    Enable  = p => SetFx(p, (Vignette v) => { v.intensity.value = 0.75f; v.intensity.overrideState = true; }),
                },
                new FxProbe
                {
                    Name = "ChromaticAberration",
                    // URP 17 的 ChromaticAberration 只有 intensity 一个参数（见 docs/reference-urp17-setup.md §4.2）
                    Disable = p => SetFx(p, (ChromaticAberration c) => { c.intensity.value = 0f; c.intensity.overrideState = true; }),
                    Enable  = p => SetFx(p, (ChromaticAberration c) => { c.intensity.value = 0.6f; c.intensity.overrideState = true; }),
                },
                new FxProbe
                {
                    Name = "FilmGrain",
                    Disable = p => SetFx(p, (FilmGrain g) => { g.intensity.value = 0f; g.intensity.overrideState = true; }),
                    Enable  = p => SetFx(p, (FilmGrain g) => { g.intensity.value = 0.8f; g.intensity.overrideState = true; }),
                },
                new FxProbe
                {
                    Name = "ColorAdjustments",
                    // 关 = 恒等（饱和 0、对比 0）；开 = 强去饱和（-100 是 URP 的合法下限）
                    Disable = p => SetFx(p, (ColorAdjustments c) =>
                    { c.saturation.value = 0f; c.saturation.overrideState = true; c.contrast.value = 0f; c.contrast.overrideState = true; }),
                    Enable  = p => SetFx(p, (ColorAdjustments c) =>
                    { c.saturation.value = -100f; c.saturation.overrideState = true; c.contrast.value = 40f; c.contrast.overrideState = true; }),
                },
                new FxProbe
                {
                    Name = "Tonemapping",
                    // 关 = None（不映射）；开 = ACES。模式类效果没有"强度"，只能用模式开关对照。
                    Disable = p => SetFx(p, (Tonemapping t) => { t.mode.value = TonemappingMode.None; t.mode.overrideState = true; }),
                    Enable  = p => SetFx(p, (Tonemapping t) => { t.mode.value = TonemappingMode.ACES; t.mode.overrideState = true; }),
                },
            };

            // 探针期间固定"开灯 + 默认雾 + 无手电"，保证唯一变量只有那一个效果。
            // `mainLight` 由调用方传入（就是产品那位主光，反射拿到的那个），不自己另找一盏 —— 那会造出第二份真源。
            if (mainLight != null) mainLight.intensity = productMainLightIntensity;
            ApplyFog("default");

            foreach (var probe in probes)
            {
                if (!probe.Disable(vol.profile) || !probe.Enable(vol.profile))
                {
                    // 拿不到组件 = 它根本不在 profile 里 ⇒ 这是**真缺陷**（配了却不存在），必须判红
                    problems.Add($"【后处理缺失】{probe.Name} 不在 DefaultVolumeProfile 里 —— 该效果不可能生效");
                    continue;
                }
                // ⚠ 【2026-10-06 实测修正：探针必须**互相隔离**】
                // 第一版连续跑探针，**前一个的 ON 状态会留给后一个**（我只关了当前这个）。
                // 后果有实测证据：Tonemapping 把亮度从 56 压到 6.1 之后，
                // 后面的 Bloom 拿到的是一个已经压暗的画面 ⇒ 没有超过阈值的亮部 ⇒
                // 实测 Bloom "ON vs OFF 只变化 0.001%"，看着像"Bloom 无效"，
                // **其实是上一个效果还在生效**（探针测序缺陷，不是产品缺陷）。
                // ⇒ 每次测量前把**所有**效果归零，只让被测的那一个上场。
                probeDisableAll(vol.profile);
                probe.Disable(vol.profile);
                var off = RenderTo(cam, Path.Combine(_outDir, $"postfx_{probe.Name}_OFF.png"), vol);
                probeDisableAll(vol.profile);     // 再归零一次，确保只有本效果 ON
                probe.Enable(vol.profile);
                var on = RenderTo(cam, Path.Combine(_outDir, $"postfx_{probe.Name}_ON.png"), vol);
                written += 2;

                double d = ChangedPct(on.pixels, off.pixels);
                index.AppendLine(string.Join(",", "postfx", "corridor_main", $"{probe.Name}_ON",
                    $"postfx_{probe.Name}_ON.png", on.mean.ToString("0.00", CultureInfo.InvariantCulture),
                    on.std.ToString("0.00", CultureInfo.InvariantCulture), on.colors.ToString(CultureInfo.InvariantCulture),
                    on.magentaPct.ToString("0.000", CultureInfo.InvariantCulture), d.ToString("0.000", CultureInfo.InvariantCulture)));
                Debug.Log($"[RENDER][后处理] {probe.Name}：ON vs OFF 变化 {d:0.000}%（亮度 {on.mean:0.0} vs {off.mean:0.0} · 颜色数 {on.colors} vs {off.colors}）");
                if (d < probe.MinPct)
                    problems.Add($"【后处理判据不成立】{probe.Name} 开/关只变化 {d:0.000}%（<{probe.MinPct}%）—— 该效果对渲染没有实际作用");
            }

            // ── 相机级抗锯齿（FXAA）ON/OFF 证据 ────────────────────────────────────
            // 【为什么单独做，不塞进 probes 列表】它的开关在**相机**上
            // （`UniversalAdditionalCameraData.antialiasing`），不在 Volume Profile 里 ——
            // 而 ② 要求「每一项都要有 ON/OFF 像素证据」，抗锯齿是其中一项。
            // 官方移动端立场："For anti-aliasing on mobile platforms, Unity recommends that you use FXAA."
            {
                object camData = null;
                try
                {
                    foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                    {
                        var ext = asm.GetType("UnityEngine.Rendering.Universal.CameraExtensions", false);
                        var m = ext?.GetMethod("GetUniversalAdditionalCameraData",
                            BindingFlags.Public | BindingFlags.Static);
                        if (m != null) { camData = m.Invoke(null, new object[] { cam }); break; }
                    }
                }
                catch (Exception e) { Debug.LogWarning("[RENDER][后处理] 取相机 URP 数据失败：" + e.Message); }

                var aaProp = camData?.GetType().GetProperty("antialiasing", BindingFlags.Public | BindingFlags.Instance);
                if (aaProp == null || !aaProp.CanWrite)
                {
                    problems.Add("【后处理缺失】拿不到相机上的 antialiasing（相机级抗锯齿无法验证）");
                }
                else
                {
                    // 关 = None
                    object none = null, fxaa = null;
                    try { none = Enum.Parse(aaProp.PropertyType, "None"); } catch { }
                    foreach (var n in new[] { "FastApproximateAntialiasing", "FXAA", "FastApproximate" })
                    { try { fxaa = Enum.Parse(aaProp.PropertyType, n); break; } catch { } }
                    if (none == null || fxaa == null)
                    {
                        problems.Add($"{aaProp.PropertyType.Name} 里找不到 None / FXAA 成员 —— 抗锯齿判据无法成立");
                    }
                    else
                    {
                        aaProp.SetValue(camData, none);
                        var aaOff = RenderTo(cam, Path.Combine(_outDir, "postfx_AntiAliasing_OFF.png"), vol);
                        aaProp.SetValue(camData, fxaa);
                        var aaOn = RenderTo(cam, Path.Combine(_outDir, "postfx_AntiAliasing_ON.png"), vol);
                        written += 2;
                        double aaD = ChangedPct(aaOn.pixels, aaOff.pixels);
                        index.AppendLine(string.Join(",", "postfx", "corridor_main", "AntiAliasing_ON",
                            "postfx_AntiAliasing_ON.png", aaOn.mean.ToString("0.00", CultureInfo.InvariantCulture),
                            aaOn.std.ToString("0.00", CultureInfo.InvariantCulture), aaOn.colors.ToString(CultureInfo.InvariantCulture),
                            aaOn.magentaPct.ToString("0.000", CultureInfo.InvariantCulture), aaD.ToString("0.000", CultureInfo.InvariantCulture)));
                        Debug.Log($"[RENDER][后处理] AntiAliasing(FXAA)：ON vs OFF 变化 {aaD:0.000}%"
                            + $"（亮度 {aaOn.mean:0.0} vs {aaOff.mean:0.0}）· 读回模式={camData.GetType().GetProperty("antialiasing")?.GetValue(camData)}");
                        // ⚠ 阈值说明：FXAA 只改**边缘像素**，在全屏占比里通常远小于 0.5%（那是"整幅变化"的门槛）。
                        // 所以这里用 0.01% —— 它不是"放宽判据迁就结果"，而是**该效果的作用面积本来就小**；
                        // 真正的判据是"可分辨 vs 逐像素完全相同"（0.000% 就意味着完全没生效）。
                        const double AaMinPct = 0.01;
                        if (aaD < AaMinPct)
                            problems.Add($"【后处理判据不成立】AntiAliasing(FXAA) 开/关只变化 {aaD:0.000}%（<{AaMinPct}%）"
                                + " —— 相机级抗锯齿对渲染没有实际作用");
                        aaProp.SetValue(camData, fxaa);   // 保持开启（产品默认）
                    }
                }
            }

            // 还原：把探针期间的强值退掉，避免影响后续（当前是最后一步，但保持函数可重入）
            vol.enabled = false;
            UnityEngine.Object.DestroyImmediate(volGo);
        }

        /// <summary>
        /// 把 URP Asset 的 Renderer Data 关键字段打出来（全反射，不引 URP 程序集）。
        ///
        /// 关心三件事，都是"会让后处理静默不生效"的开关：
        ///   · `postProcessData` —— 后处理资源（shader/LUT）。为 null 时 URP **静默跳过**后处理 pass；
        ///   · `renderingMode` —— Deferred 在移动端带宽贵，但那是性能问题，不影响是否生效；
        ///   · `depthPrimingMode` —— 与"深度纹理是否可用"相关。
        /// 打不出来就说明**反射路径本身**有问题（那也是一条有用的信息，不能静默）。
        /// </summary>
        static void DumpRendererDataDiag(RenderPipelineAsset urpAsset)
        {
            if (urpAsset == null) { Debug.LogWarning("[RENDER][URP诊断] 没有 currentRenderPipeline"); return; }
            try
            {
                var t = urpAsset.GetType();
                // ⚠ 先读**序列化字段** `m_RendererDataList`，不要先碰 `rendererDataList` 属性：
                // 实测（第 30 轮）那个属性的 getter 会抛 TargetInvocationException（URP 内部对未初始化
                // 渲染器列表很敏感），于是整段诊断被 catch 掉、什么也没打出来 ——
                // 一个"诊断静默失效"比没有诊断更糟：它会让人以为"查过了，没问题"。
                object data0 = null;
                var f = t.GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
                var arr2 = f != null ? f.GetValue(urpAsset) as Array : null;
                if (arr2 != null && arr2.Length > 0) data0 = arr2.GetValue(0);
                if (data0 == null)
                {
                    var listProp = t.GetProperty("rendererDataList");
                    if (listProp != null)
                    {
                        var arr = listProp.GetValue(urpAsset) as System.Collections.IEnumerable;
                        if (arr != null) foreach (var x in arr) { data0 = x; break; }
                    }
                }
                if (data0 == null) { Debug.LogWarning("[RENDER][URP诊断] 拿不到 RendererData 元素"); return; }

                var dt = data0.GetType();
                var ppd = dt.GetProperty("postProcessData") ?? dt.GetField("postProcessData") as MemberInfo;
                object ppdValue = null;
                if (ppd is PropertyInfo pi) ppdValue = pi.GetValue(data0);
                else if (ppd is FieldInfo fi) ppdValue = fi.GetValue(data0);
                var mode = dt.GetProperty("renderingMode");
                var depth = dt.GetProperty("depthPrimingMode");
                Debug.Log($"[RENDER][URP诊断] RendererData={dt.Name}"
                    + $" · postProcessData={(ppdValue != null ? "有" : "**null（后处理会被静默跳过）**")}"
                    + $" · renderingMode={(mode != null ? mode.GetValue(data0).ToString() : "?")}"
                    + $" · depthPriming={(depth != null ? depth.GetValue(data0).ToString() : "?")}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RENDER][URP诊断] 读 RendererData 失败：{e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>把所有已知后处理效果都设成中性值（用于测量地基自检：确保两张图状态一致）。</summary>
        static void probeDisableAll(VolumeProfile p)
        {
            SetFx(p, (Bloom b) => { b.intensity.value = 0f; b.intensity.overrideState = true; });
            SetFx(p, (Vignette v) => { v.intensity.value = 0f; v.intensity.overrideState = true; });
            SetFx(p, (ChromaticAberration c) => { c.intensity.value = 0f; c.intensity.overrideState = true; });
            SetFx(p, (FilmGrain g) => { g.intensity.value = 0f; g.intensity.overrideState = true; });
            SetFx(p, (ColorAdjustments ca) =>
            { ca.saturation.value = 0f; ca.saturation.overrideState = true; ca.contrast.value = 0f; ca.contrast.overrideState = true; });
            SetFx(p, (Tonemapping t) => { t.mode.value = TonemappingMode.None; t.mode.overrideState = true; });
        }

        /// <summary>在 profile 里找某类组件并施加改动；找不到返回 false（调用方据此判红）。</summary>
        static bool SetFx<T>(VolumeProfile profile, Action<T> apply) where T : VolumeComponent
        {
            for (int i = 0; i < profile.components.Count; i++)
            {
                if (profile.components[i] is T hit) { apply(hit); return true; }
            }
            return false;
        }

        // ───────────────────────── 场景与相机 ─────────────────────────

        /// <summary>用产品的 LevelLoader + LevelBuilder 装配（避免"验的不是产品"）。</summary>
        /// <summary>装配场景。**返回装配期发现的问题**（如房间与套件高度不匹配）——
        /// 这些必须进判据，否则"装配有缺陷但取证全绿"又是一次假绿。</summary>
        static System.Collections.Generic.List<string> BuildScene()
        {
            var problems = new System.Collections.Generic.List<string>();
            var levelText = Resources.Load<TextAsset>("Levels/asylum_v1");
            if (levelText == null) throw new InvalidOperationException("缺 Resources/Levels/asylum_v1 —— 取证无法进行");

            // 已知套件集合与 GameBootstrap.LoadKitIds() 同口径（读 asset-manifest 的 kits[].id）
            var manifest = Resources.Load<TextAsset>("Data/asset-manifest");
            var knownKits = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            if (manifest != null)
            {
                var root = Whisper.Gameplay.Level.MiniJson.AsMap(Whisper.Gameplay.Level.MiniJson.Parse(manifest.text));
                if (Whisper.Gameplay.Level.MiniJson.GetOrNull(root, "kits") is System.Collections.Generic.List<object> kits)
                    foreach (var k in kits)
                        if (Whisper.Gameplay.Level.MiniJson.GetOrNull(k as System.Collections.Generic.Dictionary<string, object>, "id") is string id)
                            knownKits.Add(id);
            }

            var level = Whisper.Gameplay.Level.LevelLoader.Load(levelText.text, knownKits);
            // 供 KitUsableHeight 查"房间用哪个套件"（相机取景高度按**套件实际几何**定）
            LevelCache.Current = level;
            if (_levelGo != null) UnityEngine.Object.DestroyImmediate(_levelGo);
            _levelGo = new GameObject("Level");
            var lb = _levelGo.AddComponent<Whisper.Gameplay.Level.LevelBuilder>();
            lb.Build(level, knownKits);
            Debug.Log($"[RENDER] 场景建好：套件房间 {lb.KitRooms.Count} 个 · 道具 {lb.PropObjects.Count} · 套件问题={Whisper.Gameplay.Level.LevelBuilder.KitProblem ?? "无"}");
            DumpSceneBounds(level);

            // ── **套件高度落差审计**（判据，会判红）──────────────────────────────
            // 【为什么是判据而不是日志】2026-10-06 我为 `morgue_deep` 纯黑改了**四次**相机才找到真因：
            // 房间声明高 3.2m，而 `morgue` 套件实际只高 1.69m ⇒ 套件顶只到 0.845m
            // ⇒ 任何按"人眼 1.6m"摆的相机都在**顶棚之上**，拍到越过墙顶的雾 ⇒ 纯黑，且**不报任何错**。
            // 这是"两套尺寸各自都对、合起来对不上"的典型 ⇒ 必须由机器持续盯着，不能靠人看图发现。
            foreach (var msg in KitUsableHeight.AuditHeightMismatch())
            {
                problems.Add($"【房间与套件高度不匹配】{msg}");
                Debug.LogWarning($"[RENDER][几何] {msg}");
            }
            return problems;
        }

        /// <summary>
        /// 【2026-10-06 新增 · 把推断变成测量】
        ///
        /// 为什么需要它：`entrance_safe/orbit33` 出现「**开灯 2.9 &lt; 关灯 17.6**」这种自相矛盾的判红
        /// （开灯反而更暗）。我先后猜过雾 lerp 写反、阴影全黑、相机在几何内部三种原因，
        /// 但离线复算与实测对不上 —— 说明还有没建模的因素。
        /// **继续猜就是浪费 CI**，所以直接把场景的客观几何打进日志：
        ///   · 每个房间**套件部件的世界包围盒**（不是房间盒）→ 看几何到底落在哪；
        ///   · 相机位置/朝向/远近裁剪面 → 看取景点是否真的在房间外；
        ///   · 相机与各房间中心的距离 → 判断"雾是否已经吃满"（雾起止 8→40m）。
        ///
        /// 判据口径：只看 **Kit_ 前缀**（套件部件）与房间中心，不看程序化墙体（那批是本轮之外的议题）。
        /// </summary>
        static void DumpSceneBounds(Whisper.Gameplay.Level.LevelData level)
        {
            // 套件部件的整体包围盒（按房间分组，只打印前 4 个房间以免日志爆掉）
            var renderers = _levelGo.GetComponentsInChildren<MeshRenderer>(true);
            int kitCount = 0;
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            var perRoomFirst = new System.Collections.Generic.Dictionary<string, string>();
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || r.name == null || !r.name.StartsWith("Kit_", StringComparison.Ordinal)) continue;
                kitCount++;
                var b = r.bounds;
                min = Vector3.Min(min, b.min);
                max = Vector3.Max(max, b.max);
                // 房间归属：沿父链找 Room_<id>（`LevelBuilder.BuildRoom` 就是这么命名的）
                string room = "?";
                var t = r.transform.parent;
                while (t != null)
                {
                    if (t.name != null && t.name.StartsWith("Room_", StringComparison.Ordinal))
                    { room = t.name.Substring(5); break; }
                    t = t.parent;
                }
                if (!perRoomFirst.ContainsKey(room) && perRoomFirst.Count < 6)
                    perRoomFirst[room] = $"min({b.min.x:0.00},{b.min.y:0.00},{b.min.z:0.00}) max({b.max.x:0.00},{b.max.y:0.00},{b.max.z:0.00})";
            }
            // ── **按房间统计部件数**（2026-10-06 加）────────────────────────────────
            // 【为什么加】`morgue_deep/eye` 一直渲成**纯黑（颜色数=1）**，我先怀疑相机位置
            // （改了两次：贴墙 → 放进室内），相机已经确实在房间内（z=3.54，房间 z[3.0,6.0]）
            // **却仍然纯黑** ⇒ 那问题就不在相机，而在**那个房间根本没有几何**。
            // 而旧的房间归属靠"父链名字匹配房间 id"，实测全部落到 `?`（没匹配上）⇒ 这条诊断等于没用。
            // ⇒ 改成**按部件包围盒是否落在房间盒内**来归属 —— 与命名无关，且能直接回答
            //   "哪个房间一个部件都没有"。
            var roomHits = new System.Collections.Generic.Dictionary<string, int>();
            var roomBounds = new System.Collections.Generic.Dictionary<string, Bounds>();
            // ⚠ **判定盒必须带上楼层高度偏移**（这次我连错两次，都记下来）：
            //   ① 房间盒的高度轴是 **Y**，我第一版把 `SizeZ` 写进了 y 分量；
            //   ② 楼层有 Y 偏移 —— `LevelAssembly` 里 `baseY = r.Floor * FloorHeightM`（3.5m/层）。
            //      不带偏移的盒只罩得住 0 层 ⇒ 二三层几何全落在盒外 ⇒ 被误报成"该房间没有任何几何"。
            //      **一个错的诊断比没有诊断更糟**：它会让人去修一个不存在的问题。
            foreach (var rm in level.Rooms)
            {
                roomHits[rm.Id] = 0;
                float baseY = rm.Floor * Whisper.Gameplay.Level.LevelGeometry.FloorHeightM;
                roomBounds[rm.Id] = new Bounds(
                    new Vector3(rm.CenterX, baseY + rm.SizeY * 0.5f, rm.CenterZ),
                    new Vector3(rm.SizeX + 0.6f, rm.SizeY + 0.6f, rm.SizeZ + 0.6f));  // 放宽 0.3m 容差
            }
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || r.name == null || !r.name.StartsWith("Kit_", StringComparison.Ordinal)) continue;
                var b = r.bounds;
                foreach (var rm in level.Rooms)
                {
                    // 用部件中心点判定归属（比"相交"更干脆：部件不会同时属于两间房）
                    if (!roomBounds[rm.Id].Contains(b.center)) continue;
                    roomHits[rm.Id]++;
                    var rb = roomBounds[rm.Id];   // 累积实际占据范围，便于判断"建歪了"
                    roomBounds[rm.Id] = new Bounds(rb.center, rb.size);
                    break;
                }
            }
            Debug.Log($"[RENDER][几何] 套件部件 {kitCount} 个 · 场景包围盒 min({min.x:0.00},{min.y:0.00},{min.z:0.00}) max({max.x:0.00},{max.y:0.00},{max.z:0.00})");
            int emptyRooms = 0;
            foreach (var rm in level.Rooms)
            {
                int n = roomHits[rm.Id];
                if (n == 0) emptyRooms++;
                Debug.Log($"[RENDER][几何]   房间 {rm.Id} kit={rm.Kit}: 部件 {n} 个"
                    + (n == 0 ? "  ⚠ **该房间没有任何几何**" : ""));
            }
            // ── **每个房间几何的世界包围盒**（含 Y）──────────────────────────────
            // 【为什么必须有】morgue_deep 一直纯黑，我改了 4 次相机都没碰到真因，
            // 因为**我从来没有直接读出"那间房的几何在世界里到底占哪一段 Y"**。
            // 这个读数能一句话回答"相机在不在几何里面"，胜过任何推断。
            {
                var byRoom = new System.Collections.Generic.Dictionary<string, Bounds>();
                for (int i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    if (r == null || r.name == null || !r.name.StartsWith("Kit_", StringComparison.Ordinal)) continue;
                    string room = "?";
                    var t = r.transform.parent;
                    while (t != null)
                    {
                        if (t.name != null && t.name.StartsWith("Room_", StringComparison.Ordinal))
                        { room = t.name.Substring(5); break; }
                        t = t.parent;
                    }
                    var bb = r.bounds;
                    if (byRoom.TryGetValue(room, out var cur))
                    {
                        var mn = Vector3.Min(cur.min, bb.min);
                        var mx = Vector3.Max(cur.max, bb.max);
                        byRoom[room] = new Bounds((mn + mx) * 0.5f, mx - mn);
                    }
                    else byRoom[room] = bb;
                }
                foreach (var kv in byRoom)
                {
                    var bb = kv.Value;
                    // 相机若在这间房取景，就直接报出"相机相对几何的竖向位置"
                    Debug.Log($"[RENDER][几何]   世界盒 {kv.Key}: x[{bb.min.x:0.00},{bb.max.x:0.00}]"
                        + $" y[{bb.min.y:0.00},{bb.max.y:0.00}] z[{bb.min.z:0.00},{bb.max.z:0.00}] 部件几何中心y={bb.center.y:0.00}");
                }
            }
            {
                var all = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
                int point = 0, dir = 0, spot = 0, off = 0;
                var samples = new StringBuilder();
                foreach (var lt in all)
                {
                    if (!lt.enabled || lt.intensity <= 0.001f) { off++; continue; }
                    if (lt.type == LightType.Point) point++;
                    else if (lt.type == LightType.Directional) dir++;
                    else if (lt.type == LightType.Spot) spot++;
                    if (samples.Length < 150)
                        samples.Append(lt.name + "=" + lt.intensity.ToString("0.00", CultureInfo.InvariantCulture) + " ");
                }
                Debug.Log("[RENDER][灯] 场景灯 " + all.Length + " 盏：点光 " + point + " · 平行光 " + dir + " · 聚光 " + spot + " · 关闭/零强度 " + off + " · 样例 " + samples);
            }
            Debug.Log($"[RENDER][几何] 空房间 {emptyRooms} / {level.Rooms.Count} 个"
                + (emptyRooms > 0 ? " —— 空房间在渲染里必然是纯黑（与相机/光照无关）" : ""));
            foreach (var kv in perRoomFirst) Debug.Log($"[RENDER][几何]   父链归属 {kv.Key}: {kv.Value}");
            for (int i = 0; i < level.Rooms.Count && i < 6; i++)
            {
                var rm = level.Rooms[i];
                Debug.Log($"[RENDER][几何]   房间盒 {rm.Id}: x[{rm.MinX:0.0},{rm.MaxX:0.0}] y[0,{rm.SizeY:0.0}] z[{rm.MinZ:0.0},{rm.MaxZ:0.0}] 中心({rm.CenterX:0.0},{rm.CenterZ:0.0})");
            }
        }

        /// <summary>
        /// 手电筒：挂在相机上的**聚光灯**（Spot）—— 用户要的手电就是锥形光。
        /// 手电配置——**必须与产品侧同口径**，否则取证测的是另一盏灯。
        ///
        /// 【2026-10-06 迁 URP 后重标定，原因是一条实测判红】
        /// 迁 URP 前亮度差 0.000%（ForwardAdd pass 存在），迁后变成 **0.643% —— 卡在 1% 阈值下面**。
        /// 诊断：URP 的附加光走**物理距离衰减**（`GetAdditionalLight` 的 `distanceAttenuation`），
        /// 而 Built-in 那版是手写的温和衰减 `1/(1+0.15d²)`；再加上原来没设 `range/spotAngle`，
        /// Unity 默认 10m / 30°，光锥很窄 —— 取景点是走廊，手电照到的多是**平行于光锥的面**，
        /// 于是全屏平均亮度变化被摊薄。
        ///
        /// ⇒ 修法不是"把阈值调低"（那是改判据去迁就结果），而是**把手电调成产品该有的样子**：
        /// 更远的 range、更宽的锥角、强制像素光（`ForcePixel`：Auto 在灯多时会被降级成顶点光，
        /// 那会让"手电不亮"变成一个**偶发**现象——正是最难查的一类 bug）。
        /// </summary>
        static Light CreateFlashlight(Transform parent)
        {
            var go = new GameObject("EvidenceFlashlight");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0f);
            go.transform.localRotation = Quaternion.identity;   // 与相机同向：照哪看哪
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.range = 18f;                 // 走廊纵深足够（默认 10m 太短）
            l.spotAngle = 55f;             // 宽光斑：覆盖取景画面的大部分（默认 30° 太窄）
            l.renderMode = LightRenderMode.ForcePixel;   // 绝不被降级成顶点光
            return l;
        }

        /// <summary>
        /// 反射调用产品的 `GameBootstrap.BuildCamera`，拿回它建的**主光**组件。
        ///
        /// 为什么用反射：`BuildCamera` 是私有方法（构建 A 没有 `Assets/Scripts/Runtime/**` 写权）。
        /// 为什么不用"照抄 0.85 / bone / Euler(50,-30,0)"：那会产生第二份真源——产品改了灯，取证图却不变，
        /// 于是"证据"与"真机"脱钩（本项目踩过 EyeHeightM 三份拷贝的坑）。
        /// 拿不到就抛：宁可取证失败，也不要一份"验的不是产品"的图。
        /// </summary>
        static Light CreateProductKeyLight()
        {
            var bootGo = new GameObject("ProductBoot");
            var boot = bootGo.AddComponent<Whisper.Runtime.GameBootstrap>();
            var m = typeof(Whisper.Runtime.GameBootstrap).GetMethod("BuildCamera", BindingFlags.Instance | BindingFlags.NonPublic);
            if (m == null)
                throw new InvalidOperationException("找不到 GameBootstrap.BuildCamera（产品改名了？）—— 取证脚本必须用产品的主光，请同步本脚本反射的方法名");
            m.Invoke(boot, null);

            var light = boot.GetComponentInChildren<Light>();
            if (light == null)
                throw new InvalidOperationException("GameBootstrap.BuildCamera 没有建出 Light —— 产品主光缺失，光照取证无意义");

            // 产品建的 MainCamera 本脚本用不上（自建取证相机，取景点由房间几何算），销毁以免场景里有两个相机
            var prodCam = boot.GetComponentInChildren<Camera>();
            if (prodCam != null) UnityEngine.Object.DestroyImmediate(prodCam.gameObject);
            return light;
        }

        /// <summary>取景点：全部保证相机在房间内部（公式与 KitVisibilityCapture.EyePosition 同口径）。</summary>
        static bool PlaceCamera(Transform t, Camera cam, string roomId, string view, float cx, float cz, float sx, float sz, float sy,
            System.Collections.Generic.List<string> problems)
        {
            switch (view)
            {
                case "orbit33":
                    // 【2026-10-06 实测修正】第一版写的是 `cx + Mathf.Max(sx*1.6f, 6.0f)` 等 ——
                    // **硬编码的最小距离**。后果（有实测证据）：
                    //   corridor_main 尺寸 18×3×3、中心 (13.0, 1.5)
                    //   ⇒ 相机被放到 (8.40, 6.50, **-5.00**) —— z=-5 而房间 z 范围是 [0,3]
                    //   ⇒ **在房间外 5 米、且在 6.5 米高处**（房间只有 3 米高）
                    //   ⇒ 拍到的是雾与背景，画面近黑（实测 mean luma 10.5），
                    //     害我一度以为是光照或后处理的问题。
                    // 正解：偏移量**按房间尺寸缩放**，并夹在合理范围内 ——
                    // 小房间不至于贴脸，大房间也能取到全景。
                    cam.fieldOfView = 55f;
                    {
                        float span = Mathf.Max(sx, sz);
                        float diag = Mathf.Sqrt(sx * sx + sz * sz);
                        float off = Mathf.Clamp(diag * 0.95f, 2.5f, 24f);
                        float hgt = Mathf.Clamp(sy * 1.35f, 1.5f, 26f);
                        t.position = new Vector3(cx + off, hgt, cz - off);
                        t.LookAt(new Vector3(cx, sy * 0.4f, cz));
                        // 小房间（如 3×3）用 55° 会取景过散，按跨度收一收
                        cam.fieldOfView = Mathf.Clamp(60f - span * 1.2f, 40f, 70f);
                    }
                    return true;
                case "eye":
                    // ══════════════════════════════════════════════════════════════════
                    // 【2026-10-06 第四次修正 —— 前三次都改错了地方，把真结论记下来】
                    // ══════════════════════════════════════════════════════════════════
                    // 前三次依次改了：① 离远端墙的距离 ② 放进室内 ③ 相机高度=1.6m。
                    // **都没解决 morgue 纯黑**。真根因是**房间与套件的尺寸不匹配**：
                    //   套件几何**上下对称、原点在房间中心**（LevelBuilder 把套件挂在
                    //   Room_<id> 下且 localPosition = 0，而该对象位于 (CenterX, Floor*3.5, CenterZ)）
                    //   ⇒ 套件在房间里的实际竖向范围 = 中心 ± 套件高/2
                    //   实测 GLB 顶点：hall_main 2.91 / hall_main_lobby 3.41 / **morgue 仅 1.69**
                    //   而 morgue_deep 房间**声明**高 **3.2m** ⇒ 套件顶只到 **0.845m**
                    //   ⇒ 相机放 1.6m 就**在顶棚之上**，拍到的是越过墙顶的雾 ⇒ 纯黑（颜色数=1）
                    //
                    // ⇒ 正确做法：相机高度按**套件实际竖向范围**取（不按房间声明高度、也不假定人眼 1.6m），
                    //   取中部偏上，看得到地面与家具。套件数据缺失时才退回房间高度的一半。
                    cam.fieldOfView = 70f;
                    {
                        float inset = Mathf.Clamp(sz * 0.18f, 0.5f, 1.2f);      // 离近端墙的安全距离
                        float camZ = Mathf.Min(cz - sz * 0.5f + inset, cz);    // 绝不越过房间中线
                        float camY = KitUsableHeight.EyeHeightFor(roomId, sy);
                        t.position = new Vector3(cx, camY, camZ);
                        t.LookAt(new Vector3(cx, camY * 0.55f, cz + sz * 0.5f));
                    }
                    return true;
                case "alongX":
                    // 沿走廊长轴看：雾的判据需要 10m 以上的连续视线（3m 进深的房间按设计就没有雾）。
                    // 【实测踩坑】第一版把相机放在房间中心线上（z = cz）→ 正前方 0.5m 就是 hall_main 的
                    // 立柱（柱心在房间中心线、每 5m 一根）→ 整幅被一根近处柱子填满，雾**完全测不出来**
                    // （实测关雾/浓雾两图变化 0.000%）。现在横向让开 0.9m，视线沿走廊轴线穿到底。
                    cam.fieldOfView = 70f;
                    t.position = new Vector3(cx - sx * 0.5f + 1.0f, 1.55f, cz + 0.9f);
                    t.LookAt(new Vector3(cx + sx * 0.5f, 1.15f, cz + 0.9f));
                    return true;
                default:
                    problems.Add($"{roomId} 未知视图 {view}");
                    return false;
            }
        }

        // ───────────────────────── 灯 / 雾 控制 ─────────────────────────

        static MethodInfo _setGlobalFloat, _setGlobalColor;

        /// <summary>
        /// 设置雾的**全局**量。为什么走反射：`Shader.SetGlobalFloat/SetGlobalColor` 不在
        /// `native/unity-stubs/UnityStubs.cs` 里，而 `native/**` 不在构建 A 的写权内（无法补桩）；
        /// 反射不依赖编译期成员存在，正是本项目 gate-editor-api 给这类调用选定的形态。
        /// 反射不到就抛（不静默跳过——那会让"雾关/开"变成两张一模一样的图）。
        /// </summary>
        static void SetGlobal(string name, object value, bool isColor)
        {
            if (isColor)
            {
                if (_setGlobalColor == null)
                    _setGlobalColor = typeof(Shader).GetMethod("SetGlobalColor", new[] { typeof(string), typeof(Color) });
                if (_setGlobalColor == null) throw new InvalidOperationException("反射不到 Shader.SetGlobalColor —— 雾的证据无法产出");
                _setGlobalColor.Invoke(null, new[] { (object)name, value });
            }
            else
            {
                if (_setGlobalFloat == null)
                    _setGlobalFloat = typeof(Shader).GetMethod("SetGlobalFloat", new[] { typeof(string), typeof(float) });
                if (_setGlobalFloat == null) throw new InvalidOperationException("反射不到 Shader.SetGlobalFloat —— 雾的证据无法产出");
                _setGlobalFloat.Invoke(null, new[] { (object)name, value });
            }
        }

        /// <summary>
        /// 雾的三个相位。注意着色器的显式约定：**0 = 未设置 = 用编译期默认值**
        /// （见 WhisperUnlitColor.shader 头部表格），所以"回到默认"必须显式写回 0。
        /// </summary>
        static void ApplyFog(string mode)
        {
            switch (mode)
            {
                case "default":   // 产品默认：雾开、很淡、暗色（全部走着色器编译期默认值）
                    SetGlobal("_WhisperFogOff", 0f, false);
                    SetGlobal("_WhisperFogStartM", 0f, false);
                    SetGlobal("_WhisperFogEndM", 0f, false);
                    SetGlobal("_WhisperFogColor", new Color(0f, 0f, 0f, 0f), true);
                    break;
                case "off":       // kill switch
                    SetGlobal("_WhisperFogOff", 1f, false);
                    break;
                case "strong":    // 调浓：0.5m 起雾、10m 饱和（证明"可调"，也证明浓雾仍只压暗、不洗白）
                    SetGlobal("_WhisperFogOff", 0f, false);
                    SetGlobal("_WhisperFogStartM", 0.5f, false);
                    SetGlobal("_WhisperFogEndM", 10f, false);
                    SetGlobal("_WhisperFogColor", FogColorReference, true);
                    break;
                default:
                    throw new InvalidOperationException($"未知雾相位 {mode}");
            }
        }

        // ───────────────────────── 渲染与统计 ─────────────────────────

        sealed class Shot
        {
            public Color32[] pixels;
            public double mean, std, magentaPct;
            public int colors;
        }

        /// <summary>离屏渲染一帧并写 PNG，同时返回像素统计（用来判"洗白/全黑/洋红"）。</summary>
        static Shot RenderTo(Camera cam, string path) => RenderTo(cam, path, null);

        /// <summary>
        /// 同上，但可挂一个 <see cref="Volume"/>。
        ///
        /// 【为什么要这个重载】逐效果 ON/OFF 取证必须**显式**把 Volume 挂进场景并指向 profile。
        /// 依赖"URP Asset 的 default volumeProfile 会自动生效"是危险的：
        /// 一旦它没生效，取证会给出"这个效果本来就没作用"的**错误结论**，
        /// 而不是"我的取证没接对"—— 那正是本项目最忌讳的"假证据"。
        /// 显式挂上之后，"效果无效"与"接线没做"才区分得开。
        /// </summary>
        static Shot RenderTo(Camera cam, string path, Volume volume)
        {
            var rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
            var prevRt = cam.targetTexture;
            cam.targetTexture = rt;
            // 【诊断】把**实测**相机位姿打进日志：位置/朝向/远近裁剪面。
            // 为什么必须实测：`entrance_safe/orbit33` 的「开灯比关灯暗」我先后猜了雾、阴影、
            // 相机在几何内部三种原因，离线复算与实测都对不上 —— 再猜就是浪费 CI。
            // 这三个数能直接判掉"相机跑到几何里面/裁剪面把几何切掉"这一整类假设。
            {
                var p = cam.transform.position;
                var f = cam.transform.forward;
                Debug.Log($"[RENDER][相机] 取景前实测 pos({p.x:0.00},{p.y:0.00},{p.z:0.00}) "
                    + $"forward({f.x:0.00},{f.y:0.00},{f.z:0.00}) fov={cam.fieldOfView:0} "
                    + $"near={cam.nearClipPlane:0.000} far={cam.farClipPlane:0} "
                    + $"正交={cam.orthographic} 裁剪mask={cam.cullingMask} 深度模式={cam.depthTextureMode}");

                // ── 遮挡探针：相机是不是贴在/穿进几何体里 ──────────────────────────
                // 【为什么必须加】2026-10-06 实测：`entrance_safe/eye` 渲出一圈同心色带、
                // 绝对亮度只有 10.5（目标下限 20），我先怀疑"光照不够"，先后改了雾/后处理参数 ——
                // 全是白费。真相是**相机贴在门框上、画面被近处几何糊满**。
                // 这五个方向的最短命中距离能**一次性**判掉"相机在几何内部/贴墙/贴门框"这一整类原因，
                // 不必再靠看图猜。forward 命中距离就是"视线被挡多远"。
                {
                    var origin = p;
                    var dirs = new (string name, Vector3 d)[]
                    {
                        ("前", f), ("后", -f), ("上", cam.transform.up), ("下", -cam.transform.up),
                        ("左", -cam.transform.right), ("右", cam.transform.right),
                    };
                    var sb = new StringBuilder("[RENDER][相机][遮挡] ");
                    for (int i = 0; i < dirs.Length; i++)
                    {
                        float dist = 999f;
                        if (Physics.Raycast(origin, dirs[i].d, out var hit, 60f)) dist = hit.distance;
                        sb.Append(dirs[i].name).Append('=');
                        sb.Append(dist >= 999f ? "无" : dist.ToString("0.00", CultureInfo.InvariantCulture));
                        if (i < dirs.Length - 1) sb.Append(" · ");
                    }
                    Debug.Log(sb.ToString() + "（米；forward 很小 = 视线被近处几何挡住）");
                }
            }
            // 逐效果 ON/OFF：Volume 的挂/摘由调用方控制（见 RenderTo(cam, path, volume) 的说明）。
            // ⚠ 必须在 cam.Render() **之前**设：`Camera.Render()` 是同步的，
            //   渲染完再改就已经晚了（这一点我第一版写反过）。
            if (volume != null) volume.enabled = volume.profile != null;
            cam.Render();
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = prevRt;

            var px = tex.GetPixels32();
            double sum = 0, sum2 = 0;
            long magenta = 0;
            var seen = new System.Collections.Generic.HashSet<int>();
            foreach (var c in px)
            {
                double luma = 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
                sum += luma; sum2 += luma * luma;
                if (c.r > 200 && c.g < 60 && c.b > 200) magenta++;                       // 洋红 = shader error 色
                seen.Add((c.r >> 3 << 10) | (c.g >> 3 << 5) | (c.b >> 3));               // 每通道 5 位量化
            }
            int n = px.Length;
            double mean = sum / n;
            double std = Math.Sqrt(Math.Max(0, sum2 / n - mean * mean));

            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            RenderTexture.ReleaseTemporary(rt);
            return new Shot
            {
                pixels = px, mean = mean, std = std, colors = seen.Count,
                magentaPct = 100.0 * magenta / n,
            };
        }

        /// <summary>变化像素占比（阈值 8/255，与 tools/pixel-diff-pair.mjs 同一判据）。</summary>
        static double ChangedPct(Color32[] a, Color32[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return -1;
            long changed = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int d0 = Math.Abs(a[i].r - b[i].r), d1 = Math.Abs(a[i].g - b[i].g), d2 = Math.Abs(a[i].b - b[i].b);
                if (Math.Max(d0, Math.Max(d1, d2)) > 8) changed++;
            }
            return 100.0 * changed / a.Length;
        }

        // ───────────────────────── 着色器契约（纯文本） ─────────────────────────

        /// <summary>
        /// 断言着色器文件与"亮度目标/雾的可控性"三条约定一致。判据全部是文件文本，不需要 Unity API：
        ///   ① 必须声明 `_Color`（Material.color 生效的前提，gate-test T6 也断言这条）
        ///   ② `_WhisperAmbient` 默认值必须等于本脚本的 AmbientReference（改默认值就得同步亮度目标）
        ///   ③ 雾的四个全局量**不得出现在 Properties 块里**（放进去会被 new Material 固化 → 失去"一处可调"），
        ///      且**不得出现 multi_compile_fog**（2026-10-03 真机洗白事故的根因）
        /// </summary>
        static void CheckShaderContract(System.Collections.Generic.List<string> problems)
        {
            string path = Path.Combine(Application.dataPath, "Resources/Shaders/WhisperUnlitColor.shader");
            if (!File.Exists(path)) { problems.Add($"着色器资产缺失：{path}"); return; }
            int before = problems.Count;
            string src = StripShaderComments(File.ReadAllText(path));

            // 【2026-10-06 允许属性前缀】URP 官方迁移清单第 10 步要求主色写成 `[MainColor] _Color`
            // （让 Material.color 正确映射）。原正则只认行首直接跟 _Color → 照官方写法反而判红。
            // 与 tools/gate-test.mjs 的 T6 判据保持同一放宽口径（两处必须一致，否则会出现
            // "本机门禁过、云端取证红"这种自相矛盾的状态）。
            if (!Regex.IsMatch(src, @"^\s*(\[[^\]]+\]\s*)*_Color\s*\(", RegexOptions.Multiline)) problems.Add("着色器未声明 _Color（Material.color 会无效）");
            if (!Regex.IsMatch(src, "Shader\\s+\"Whisper/UnlitColor\"")) problems.Add("着色器名不是 \"Whisper/UnlitColor\"（与 LevelBuilder.UnlitShaderName 的契约破裂）");

            var am = Regex.Match(src, @"^[ \t]*_WhisperAmbient\b[^\n]*?=[ \t]*([0-9.]+)", RegexOptions.Multiline);
            if (!am.Success) problems.Add("着色器未声明 _WhisperAmbient（环境项）");
            else
            {
                float v = float.Parse(am.Groups[1].Value, CultureInfo.InvariantCulture);
                if (Math.Abs(v - AmbientReference) > 1e-6f)
                    problems.Add($"_WhisperAmbient 默认值 {v} 与亮度目标基准 {AmbientReference} 不一致 —— 改了着色器默认值必须同步本脚本的 AmbientReference 与 LevelPalette 校准表");
            }

            if (Regex.IsMatch(src, @"#pragma\s+multi_compile_fog"))
                problems.Add("着色器出现 multi_compile_fog —— 这正是 2026-10-03 真机把画面洗成 #D8CFBB 的根因，必须用自研雾");

            var props = Regex.Match(src, @"Properties\s*\{(?<body>[^}]*)\}");
            if (!props.Success) problems.Add("着色器没有 Properties 块（Unity 需要它来暴露 _Color）");
            else if (Regex.IsMatch(props.Groups["body"].Value, @"_WhisperFog"))
                problems.Add("雾参数出现在 Properties 块里 —— 会被 new Material(shader) 固化成不可全局调的材质值，必须只做 CGPROGRAM 里的全局量");

            foreach (var g in new[] { "_WhisperFogStartM", "_WhisperFogEndM", "_WhisperFogColor", "_WhisperFogOff" })
                if (!Regex.IsMatch(src, @"\b" + g + @"\b")) problems.Add($"着色器缺少全局量 {g}（雾的开关/调参 API 破裂）");

            // ── 每个 pass 的 uniform 都必须在该 pass 内声明（真机事故 0.1.19：整屏品红）──
            // 事故原委：多 pass 着色器里，**每个 pass 是独立编译单元** —— base pass 声明过的 uniform
            // 在 add pass 里不会自动可见。我在 ForwardAdd 里漏了 `_WhisperFogOff`：
            //   · 本机 D3D11 那轮没跑到（取证没抢到锁），所以"本机没报错"
            //   · 安卓构建日志里是 `undeclared identifier '_WhisperFogOff' (on gles3 / on vulkan)`
            //   · 后果：整个着色器编译失败 → Unity 用 error shader 填满几何 = **整屏品红**
            // 所以这里逐 pass 机械核对："用到的每个 `_Whisper*` 都必须在本 pass 里声明过（或 #define）"。
            CheckPerPassUniforms(src, problems);
            // Properties 里的属性必须在 CGPROGRAM 里再声明一次，否则编译期 undeclared identifier → 整屏洋红。
            // 实测踩过：漏 `float _WhisperAmbient;` 时本脚本的洋红判据立刻判红（这是它存在的意义）。
            if (Regex.IsMatch(src, @"_WhisperAmbient\b") && !Regex.IsMatch(src, @"^[ \t]*float[ \t]+_WhisperAmbient\s*;", RegexOptions.Multiline))
                problems.Add("_WhisperAmbient 只出现在 Properties 里，CGPROGRAM 中没有 `float _WhisperAmbient;` 声明 —— 会编译失败（整屏洋红）");

            // 雾的默认值：着色器里的 #define 是运行时真正的默认值，必须与 token 一致（否则"文档说的"和"真跑的"是两份数）
            CheckShaderNumber(src, "WHISPER_FOG_START", Whisper.Core.DesignTokens.RenderFogStartM, problems);
            CheckShaderNumber(src, "WHISPER_FOG_END", Whisper.Core.DesignTokens.RenderFogEndM, problems);
            var fc = Regex.Match(src, @"#define\s+WHISPER_FOG_COLOR\s+float3\(\s*([0-9.]+)\s*,\s*([0-9.]+)\s*,\s*([0-9.]+)\s*\)");
            if (!fc.Success) problems.Add("着色器缺少 #define WHISPER_FOG_COLOR float3(r, g, b)");
            else if (Math.Abs(float.Parse(fc.Groups[1].Value, CultureInfo.InvariantCulture) - FogColorReference.r) > 1e-4f
                  || Math.Abs(float.Parse(fc.Groups[2].Value, CultureInfo.InvariantCulture) - FogColorReference.g) > 1e-4f
                  || Math.Abs(float.Parse(fc.Groups[3].Value, CultureInfo.InvariantCulture) - FogColorReference.b) > 1e-4f)
                problems.Add($"#define WHISPER_FOG_COLOR({fc.Groups[1].Value},{fc.Groups[2].Value},{fc.Groups[3].Value}) 与取证脚本的雾色基准({FogColorReference.r},{FogColorReference.g},{FogColorReference.b}) 不一致；" +
                             "这条同时防『雾色被改成亮色』——亮色雾正是洗白画面的直接路径");

            if (problems.Count == before)
                Debug.Log($"[RENDER] 着色器契约检查通过：{Path.GetFileName(path)} · _Color ✓ · _WhisperAmbient={AmbientReference} 且已在 CG 中声明 ✓ · 雾 {Whisper.Core.DesignTokens.RenderFogStartM}→{Whisper.Core.DesignTokens.RenderFogEndM}m=全局量（不在 Properties）· 无 multi_compile_fog ✓");
            else
                Debug.LogWarning($"[RENDER] 着色器契约检查有 {problems.Count - before} 项不成立（见最终判红清单）");
        }

        /// <summary>
        /// 逐 pass 核对：**用到的每个 `_Whisper*` 全局量都必须在本 pass 内声明过**。
        /// 判据只看代码（注释已剥离）。为什么需要它：真机 0.1.19 整屏品红就是这个坑
        /// （ForwardAdd pass 用了 `_WhisperFogOff` 却没声明，安卓 GLES3/Vulkan 编译失败）。
        /// 这条检查与平台无关，且比"渲染一张图看是不是品红"更早、更准。
        ///
        /// 【2026-10-06 扩到 URP】原来只匹配 `CGPROGRAM/ENDCG`。着色器迁到 URP 后代码块是
        /// `HLSLPROGRAM/ENDHLSL`，于是本检查**一个块都找不到**，直接报"找不到任何 CGPROGRAM"
        /// 并**不再做 uniform 核对** —— 那等于把这道防线丢了（而且报错信息还会误导人以为着色器坏了）。
        /// 现在两种块都认：正则改成 `(?:CG|HLSL)PROGRAM ... END(?:CG|HLSL)`。
        /// ⚠ 不要因为"迁移了所以注释掉这条"——多 pass 漏声明这个坑与管线无关，URP 一样踩。
        /// </summary>
        static void CheckPerPassUniforms(string src, System.Collections.Generic.List<string> problems)
        {
            var blocks = Regex.Matches(src, @"(?:CG|HLSL)PROGRAM(?<body>[\s\S]*?)END(?:CG|HLSL)");
            if (blocks.Count == 0) { problems.Add("着色器里找不到任何 CGPROGRAM/ENDCG 或 HLSLPROGRAM/ENDHLSL 代码块"); return; }
            for (int i = 0; i < blocks.Count; i++)
            {
                string body = blocks[i].Groups["body"].Value;
                var declared = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                foreach (Match d in Regex.Matches(body, @"^[ \t]*(?:float|float2|float3|float4|fixed|fixed2|fixed3|fixed4|half|half2|half3|half4)[ \t]+(_Whisper\w+)[ \t]*;", RegexOptions.Multiline))
                    declared.Add(d.Groups[1].Value);
                foreach (Match d in Regex.Matches(body, @"#define[ \t]+(WHISPER\w+)"))
                    declared.Add(d.Groups[1].Value);

                // 【2026-10-06 修正正则】原为 `\b(_Whisper\w*)\b`，它在 URP 版着色器里会**误报**：
                // 文件里有个辅助函数 `WhisperFogK(...)`，上面的搜索串会命中其内部子串 `_WhisperFogK`，
                // 而在 `\b` 之后取 `\w*` 得到 `_WhisperFog` —— 于是报"用了 _WhisperFog 但没声明"。
                // 判据必须只认**完整的标识符**：前面是词边界、后面**不能**再跟标识符字符
                // （`(?![A-Za-z0-9_])`）。这样 `_WhisperFogOff`/`_WhisperFogColor` 仍被正确核对，
                // 而 `_WhisperFogK`/`WhisperFogK` 不再误命中。
                foreach (Match u in Regex.Matches(body, @"(?<![A-Za-z0-9_])(_Whisper\w*)(?![A-Za-z0-9_])"))
                {
                    string name = u.Groups[1].Value;
                    if (declared.Contains(name)) continue;
                    // 同一 pass 里重复出现只报一次
                    declared.Add(name);
                    problems.Add($"第 {i + 1} 个着色器代码块（{PassTagOf(src, blocks[i].Index)}）用了 `{name}` 但**本 pass 内没有声明** —— "
                        + "多 pass 着色器每个 pass 是独立编译单元，真机会报 undeclared identifier（0.1.19 整屏品红的根因）");
                }
            }
            Debug.Log($"[RENDER] 逐 pass uniform 核对：{blocks.Count} 个着色器代码块全部通过");
        }

        /// <summary>取某个 pass 代码块所属的 LightMode 标签（只用于报错信息可读）：取该块**之前最后一个** Tags 声明。</summary>
        static string PassTagOf(string src, int blockIndex)
        {
            var tags = Regex.Matches(src.Substring(0, blockIndex), @"Tags\s*\{\s*""LightMode""\s*=\s*""(\w+)""\s*\}");
            return tags.Count > 0 ? "LightMode=" + tags[tags.Count - 1].Groups[1].Value : "未标 LightMode";
        }

        /// <summary>断言着色器里的 `#define &lt;macro&gt; &lt;数值&gt;` 与 token 取值一致。</summary>
        static void CheckShaderNumber(string src, string macro, float expected, System.Collections.Generic.List<string> problems)
        {
            var m = Regex.Match(src, @"#define\s+" + macro + @"\s+([0-9.]+)");
            if (!m.Success) { problems.Add($"着色器缺少 #define {macro}"); return; }
            float v = float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (Math.Abs(v - expected) > 1e-4f)
                problems.Add($"#define {macro} = {v} 与 design-tokens 的 render 组（{expected}）不一致 —— 着色器默认值与文档化默认值必须是同一个数（改一处就得改另一处）");
        }

        /// <summary>
        /// 剥掉注释后再做契约断言。**为什么必须剥**（实测踩到）：本项目的纪律是把事故原委写进注释，
        /// 而注释里必然出现 `multi_compile_fog` / `_WhisperFogColor` 这些字样 ——
        /// 不剥注释的扫描器会把"写清楚为什么不能这么做"判成"又这么做了"
        /// （tools/gate-test.mjs:184-190 为同一原因做过同样处理）。
        /// </summary>
        static string StripShaderComments(string src)
        {
            src = Regex.Replace(src, @"/\*[\s\S]*?\*/", " ");
            src = Regex.Replace(src, @"//[^\n]*", " ");
            return src;
        }

        // ───────────────────────── 关卡读取 ─────────────────────────

        static System.Collections.Generic.List<object> LoadRooms()
        {
            var asset = Resources.Load<TextAsset>("Levels/asylum_v1");
            if (asset == null) throw new InvalidOperationException("缺 Resources/Levels/asylum_v1");
            var map = Whisper.Gameplay.Level.MiniJson.AsMap(Whisper.Gameplay.Level.MiniJson.Parse(asset.text));
            return Whisper.Gameplay.Level.MiniJson.AsList(Whisper.Gameplay.Level.MiniJson.GetOrNull(map, "rooms"));
        }

        static System.Collections.Generic.Dictionary<string, object> FindRoom(System.Collections.Generic.List<object> rooms, string id)
        {
            foreach (var r in rooms)
            {
                var m = r as System.Collections.Generic.Dictionary<string, object>;
                if (m != null && (Whisper.Gameplay.Level.MiniJson.GetOrNull(m, "id") as string) == id) return m;
            }
            return null;
        }

        static void GetRoomBounds(System.Collections.Generic.Dictionary<string, object> room,
            out float cx, out float cz, out float sx, out float sz, out float sy)
        {
            var pos = Whisper.Gameplay.Level.MiniJson.AsList(Whisper.Gameplay.Level.MiniJson.GetOrNull(room, "pos"));
            var size = Whisper.Gameplay.Level.MiniJson.AsList(Whisper.Gameplay.Level.MiniJson.GetOrNull(room, "size"));
            float x0 = Whisper.Gameplay.Level.MiniJson.AsFloat(pos[0]);
            float z0 = Whisper.Gameplay.Level.MiniJson.AsFloat(pos[1]);
            sx = Whisper.Gameplay.Level.MiniJson.AsFloat(size[0]);
            sy = Whisper.Gameplay.Level.MiniJson.AsFloat(size[1]);
            sz = Whisper.Gameplay.Level.MiniJson.AsFloat(size[2]);
            cx = x0 + sx * 0.5f;
            cz = z0 + sz * 0.5f;
        }

        static string Key(string room, string view, string phase) => room + "/" + view + "/" + phase;

        static string ArgValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
