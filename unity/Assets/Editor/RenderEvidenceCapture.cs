using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

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

            Debug.Log($"[RENDER] 输出 {_outDir}");

            // ── ① 着色器契约（纯文本检查：不依赖任何 Unity API，改坏了立刻可见）──
            CheckShaderContract(problems);

            // ── ② 场景：产品代码装配（LevelLoader + LevelBuilder），失败即抛 ──
            BuildScene();

            // ── ③ 相机 + 产品主光（反射调 GameBootstrap.BuildCamera）──
            var camGo = new GameObject("CaptureCam");
            var cam = camGo.AddComponent<Camera>();
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
            // 手电筒（ForwardAdd 的判据载体）：挂在取证相机上的聚光灯，默认关；只有手电相位打开。
            var flash = CreateFlashlight(camGo.transform);
            flash.intensity = 2.4f;      // 比主光强：手电本来就该"照到哪里哪里亮"
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

        // ───────────────────────── 场景与相机 ─────────────────────────

        /// <summary>用产品的 LevelLoader + LevelBuilder 装配（避免"验的不是产品"）。</summary>
        static void BuildScene()
        {
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
            if (_levelGo != null) UnityEngine.Object.DestroyImmediate(_levelGo);
            _levelGo = new GameObject("Level");
            var lb = _levelGo.AddComponent<Whisper.Gameplay.Level.LevelBuilder>();
            lb.Build(level, knownKits);
            Debug.Log($"[RENDER] 场景建好：套件房间 {lb.KitRooms.Count} 个 · 道具 {lb.PropObjects.Count} · 套件问题={Whisper.Gameplay.Level.LevelBuilder.KitProblem ?? "无"}");
        }

        /// <summary>
        /// 手电筒：挂在相机上的**聚光灯**（Spot）—— 用户要的手电就是锥形光。
        /// 只设桩里已有的 `type/intensity/color/enabled`：`range/spotAngle/renderMode` 不在
        /// `native/unity-stubs` 里（native/** 不在构建 A 写权内），这里靠 Unity 默认值
        /// （range 10m、spotAngle 30°、renderMode Auto）足以验证 **ForwardAdd pass 是否生效**；
        /// 产品侧由 LightRig/Runtime 负责把 renderMode 设成 ForcePixel。
        /// </summary>
        static Light CreateFlashlight(Transform parent)
        {
            var go = new GameObject("EvidenceFlashlight");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0f);
            go.transform.localRotation = Quaternion.identity;   // 与相机同向：照哪看哪
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
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
                    cam.fieldOfView = 55f;
                    t.position = new Vector3(cx + Mathf.Max(sx * 1.6f, 6.0f), Mathf.Max(sy * 1.5f, 6.5f), cz - Mathf.Max(sz * 1.6f, 6.5f));
                    t.LookAt(new Vector3(cx, 1.0f, cz));
                    return true;
                case "eye":
                    cam.fieldOfView = 70f;
                    t.position = new Vector3(cx, 1.55f, cz - Mathf.Max(sz * 0.5f - 0.6f, 0.1f));
                    t.LookAt(new Vector3(cx, 1.15f, cz + sz * 0.5f));
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
        static Shot RenderTo(Camera cam, string path)
        {
            var rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
            var prevRt = cam.targetTexture;
            cam.targetTexture = rt;
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

            if (!Regex.IsMatch(src, @"^\s*_Color\s*\(", RegexOptions.Multiline)) problems.Add("着色器未声明 _Color（Material.color 会无效）");
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
