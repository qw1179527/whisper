using System.Collections.Generic;
using Whisper.Core;                 // Services 定位器在 Whispers.Core（写成同文件内引用会 CS0103）
using Whisper.Core.Contracts;       // MatchPhase
using Whisper.Gameplay.Level;        // MiniJson（地图注册表读取用）           // LanSession / RoomReachJudge（零信令直连：可达范围与会话编排）
using UnityEngine;
using UnityEngine.UI;
using Whisper.Net.Direct;

namespace Whisper.Runtime
{
    // 2026-10-06 从 MenuScene.cs 拆出（gate-code C5：单文件 ≤600 行）。
    // 按**方法边界**机械分块，方法体一行未改 —— 行为与拆分前等价。
    // MenuScene 是 partial（见 MenuScene.cs）。
    public sealed partial class MenuScene
    {
        void RebuildRenderState()
        {
            if (_isRebuilding) return;
            _isRebuilding = true;
            try
            {
                if (_cam != null)
                {
                    _cam.enabled = true;
                    _cam.clearFlags = CameraClearFlags.SolidColor;
                    _cam.backgroundColor = new Color(0.012f, 0.014f, 0.020f);
                    _cam.targetTexture = null;   // 关键：清掉可能已被释放的 RT 绑定
                }
                // 画质档重新施加（targetFrameRate/vSync/阴影/MSAA 在后台会被系统改掉）
                if (_boot != null) _boot.ApplyRenderQuality();
                // 后处理：把临时缓冲丢掉，OnRenderImage 会按需重建
                if (_boot != null && _boot.PostFx != null) _boot.PostFx.RebuildBuffers();
                _lastTouch = "已从后台恢复";
            }
            catch (System.Exception e) { Debug.LogWarning("[Whisper] 回前台重建失败：" + e.Message); }
            finally { _isRebuilding = false; }
        }

        bool _isRebuilding;

        void Update()
        {
            PumpJoinKeyboard();   // 加入房间：键盘关闭即提交
            // 实时输入状态：mousePosition 与 touchCount 是判断"输入有没有到游戏"的唯一客观依据。
            // 用 Input.touches（原始数组）而不是 touchCount：PlayerController 走的就是这条，
            // 0.1.21 真机验证过能读到触摸；而我先前用的 GetMouseButton* 在触摸屏上恒为 0。
            var touches = Input.touches;
            int tc = touches != null ? touches.Length : 0;
            string tinfo = "";
            if (tc > 0) { var t0 = touches[0]; tinfo = " T0 " + t0.position.x.ToString("F0") + "," + t0.position.y.ToString("F0") + " " + t0.phase; }
            _inputState = "touches " + tc + tinfo + " mp " + Input.mousePosition.x.ToString("F0") + "," + Input.mousePosition.y.ToString("F0");
            LogTouchesOnce();
            // 先更新相机与 UI 位置，再做点击判定 —— 否则拾取射线用的是上一帧的相机（过渡期间会点偏）
            UpdateBoardCamera(Time.deltaTime);
            UpdateBoardUi();
            if (_root == null || _paused) return;
            // 键盘是**每帧事件**：必须无条件每帧检查，不能挂在"有点击的那一帧"里
            // （否则纯按键永远走不到 —— 真机实测与读码确认，见 fix-keyboard-per-frame.mjs 头注释）。
            HandleMenuKeyboard();
            HandleSelfDrawClick();

            // 【倒计时已删除】用户："就用按钮，不要倒计时"。所以这里不再有任何绕过输入的路径 ——
            // 正确做法是让按钮真的能收到点击（见 HandleSelfDrawClick 的双输入源实现）。
            float dt = Time.deltaTime;

            // 大厅面板 0.5s 刷一次（每帧拼字符串会白白产生 GC）
            // 大厅环境动效（灵球漂浮等）
            if (_hall != null) _hall.Tick(dt);

            _nextLobbyRefresh -= dt;
            if (_nextLobbyRefresh <= 0f) { _nextLobbyRefresh = 0.5f; RefreshLobby(); RefreshCamInfo(); }

            // 灯光闪烁：**骤暗脉冲**（不是正弦呼吸 —— 正弦看起来像呼吸灯，不像坏灯管）
            // 【兼容旧逻辑】旧走廊的"天花板灯骤暗"用 _ceilingLight；大厅用多盏点光，
            // 没有单一 _ceilingLight → 下面整段在没有灯时直接跳过（否则空引用会把 Update 打断）。
            if (_ceilingLight == null) { _dipLeft = 0f; _dipTimer = 0f; }
            if (_ceilingLight != null && _dipLeft > 0f)
            {
                _dipLeft -= dt;
                // 骤暗期间强度剧烈抖动
                _ceilingLight.intensity = LightBase * (0.06f + 0.5f * Next01());
                if (_dipLeft <= 0f) _ceilingLight.intensity = LightBase;
            }
            else if (_ceilingLight != null)
            {
                _dipTimer -= dt;
                // 平时有轻微抖动（旧灯管），让"不停闪烁"这句话成立
                _ceilingLight.intensity = LightBase * (0.90f + 0.10f * Mathf.PerlinNoise(Time.time * 7f, 0.3f));
                if (_dipTimer <= 0f)
                {
                    _dipTimer = 0.6f + Next01() * 1.4f;
                    if (Next01() < DipChancePerSec * 0.35f)
                    {
                        _dipLeft = 0.10f + Next01() * 0.16f;   // 骤暗 0.10~0.26 秒
                        TrySpawnGhost(false);                  // 用户在「闪烁时」看到鬼
                    }
                }
            }

            if (_hint != null)
                // 把诊断与玩法信息**都**放到这一行：release 下这是唯一可靠的取证通道。
                // 诊断行（含触摸/输入/相机/按钮矩形）——受总开关控制，见字段注释。
            if (ShowDiagnostics)
            _hint.text = string.Format("灯闪中 · 鬼 {0}/刷出 {1} · 触摸 {2} · 输入 [{3}]\n{4}\n{5}",
                    GhostCount, GhostSpawned, _lastTouch, _inputState, _camInfo, _btnRects);
        }

        /// <summary>按概率在远处黑暗中刷一只鬼。`force` 用于开局保证有一只。</summary>
        void TrySpawnGhost(bool force)
        {
            if (_ghosts.Count >= MaxGhosts) return;
            if (!force && Next01() >= GhostChancePerDip) return;
            var go = BuildGhost();
            if (go == null) return;
            _ghosts.Add(go);
            GhostSpawned++;
        }

        GameObject BuildGhost()
        {
            // 位置：远处（-X 深处）、横向随机、贴地
            float t = Next01();
            float x = -Mathf.Lerp(GhostMinDistM, GhostMaxDistM, t);
            float z = (Next01() - 0.5f) * (RoomSizeM.z * 0.5f);
            // 红眼/白眼各半（用户点名要两种）
            bool red = Next01() < 0.5f;

            var go = new GameObject(red ? "MenuGhost_Red" : "MenuGhost_White");
            go.transform.SetParent(_root, false);
            go.transform.localPosition = new Vector3(x, 0f, z);
            // 面朝玩家（+X 方向）
            go.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

            // 优先用**新的整块人形模型**（ghostbody：男女 × 4 体型，元球生成的完整人形）。
            // 旧的按部件拼装模型（"ghost"）在暗场里就是一团黑，不作为首选。
            GameObject body = null;
            string picked = GhostModelPool.Pick(_rng ^ 0x51ED2701u, GhostSpawned);
            if (!string.IsNullOrEmpty(picked))
            {
                // picked 现在就是**完整的资源路径**（不含扩展名），别再拼前缀 ——
                // 我先前拼成 "Models/ghostbody/" + id 而池里给的是 resPath，于是路径重复、加载必失败。
                try { body = ModelLibrary.InstantiateSingleFile(picked, go.transform, picked); }
                catch (System.Exception e) { Debug.LogWarning("[Whisper] ghostbody 加载失败：" + e.Message); }
            }
            if (body == null)
            {
                // 诊断进 HUD：release 下 Debug.Log 会被剥离（实测 0.1.24 一条都没有），
                // 所以"模型到底加载没加载"这件事只能写在屏幕上看。
                _ghostModelInfo = (string.IsNullOrEmpty(picked) ? "池空" : picked + " 失败:" + ModelLibrary.LastProblem)
                                + " → 回退旧模型";
                try { body = ModelLibrary.InstantiateWhole("ghost", go.transform); }
                catch (System.Exception e) { Debug.LogWarning("[Whisper] 旧鬼模型也失败：" + e.Message); }
            }
            else _ghostModelInfo = picked;

            if (body == null)
            {
                // 降级：用胶囊占位（明确可见，不假装成功）
                var fb = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                fb.name = "GhostFallback";
                fb.transform.SetParent(go.transform, false);
                fb.transform.localPosition = new Vector3(0f, 0.95f, 0f);
                fb.transform.localScale = new Vector3(0.42f, 0.95f, 0.42f);
                var mr = fb.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = SceneMaterials.Make(new Color(0.055f, 0.058f, 0.062f), 0.88f);
            }

            // ── 眼睛：**加重亮处理**（用户原话），走 `_WhisperEmission`（自发光在雾后相加 → 远处也亮）──
            var eyeMat = SceneMaterials.Emissive(red ? new Color(1.0f, 0.09f, 0.04f) : new Color(0.90f, 0.95f, 1.0f),
                                                 red ? 4.2f : 3.4f);
            float eyeY = 1.62f;
            float eyeX = 0.085f;
            for (int s = -1; s <= 1; s += 2)
            {
                var e = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                e.name = "MenuGhostEye" + (s > 0 ? "L" : "R");
                e.transform.SetParent(go.transform, false);
                e.transform.localPosition = new Vector3(s * eyeX, eyeY, -0.075f);
                e.transform.localScale = new Vector3(0.055f, 0.055f, 0.030f);
                var mr = e.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = eyeMat;
                var col = e.GetComponent<Collider>();
                if (col != null) Destroy(col);            // 主界面不需要碰撞体
            }
            // 眼部点光：让"加重眼部亮处理"真的在暗场里照出一点光晕
            var el = new GameObject("MenuGhostEyeGlow");
            el.transform.SetParent(go.transform, false);
            el.transform.localPosition = new Vector3(0f, eyeY, -0.12f);
            var l = el.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 2.0f;
            l.intensity = red ? 0.55f : 0.42f;
            l.color = red ? new Color(1.0f, 0.16f, 0.08f) : new Color(0.85f, 0.92f, 1.0f);
            return go;
        }

        /// <summary>HUD 摘要（自检/日志可核）。</summary>
        public string Describe()
            => string.Format("主界面：{0}×{1}×{2}m 房间 · 灯 {3} · 在场鬼 {4} · 累计刷出 {5} · 鬼模型 {6}",
                RoomSizeM.x, RoomSizeM.y, RoomSizeM.z, _ceilingLight != null ? "已建" : "缺",
                GhostCount, GhostSpawned, ModelLibrary.LastProblem ?? "正常");
    }

    /// <summary>
    /// 主界面/场景共用的材质与 UI 小工具。
    /// 单独放一个类是因为"每个 Box 都新建一份材质"会瞬间产生上百个材质实例（真机内存与合批都会受影响）。
    /// </summary>
    public static class SceneMaterials
    {
        /// <summary>场景基色（暗调偏冷）。</summary>
        public static readonly Color RoomTint = new Color(0.20f, 0.21f, 0.25f);

        static readonly Dictionary<string, Material> _cache = new Dictionary<string, Material>();

        /// <summary>标准不透明材质（Whisper 自有 shader 优先，缺失则退回 Standard）。</summary>
        public static Material Make(Color c, float roughness)
        {
            string key = ColorKey(c) + "|" + roughness.ToString("F2");
            if (_cache.TryGetValue(key, out var hit) && hit != null) return hit;
            var sh = Shader.Find("Whisper/UnlitColor");
            var m = sh != null ? new Material(sh) : new Material(Shader.Find("Standard"));
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 1f - roughness);
            if (m.HasProperty("_WhisperEmission")) m.SetColor("_WhisperEmission", new Color(0f, 0f, 0f, 0f));
            _cache[key] = m;
            return m;
        }

        /// <summary>
        /// **吃光 + PBR** 材质（响应场景灯光、带细节贴图与金属/粗糙区分）。
        /// </summary>
        /// <remarks>
        /// 三条历史都写在这里，避免再走一遍：
        ///  ① 本项目默认材质走 `Whisper/UnlitColor`（**不吃光**）→ 主界面房间永远全黑
        ///     （0.1.22~0.1.26 白烧一轮构建）。
        ///  ② 后来用 `Shader.Find("Standard")` → 但 Standard 在本工程**没有 .mat 引用**，
        ///     有被剥离的风险（2026-10-03 真机全黑事故就是 `new Material(null)`）。
        ///  ③ 现在统一用**自研 PBR**（`Whisper/LitPbr`）：它放在 Resources/ 下**无条件进包**，
        ///     既有 PBR 观感又没有剥离风险。回退链：LitPbr → UnlitColor → 报错。
        /// </remarks>
        public static Material Lit(Color c, float roughness, Whisper.Gameplay.Render.MaterialFamily family
            = Whisper.Gameplay.Render.MaterialFamily.Plaster)
        {
            string key = "P" + ColorKey(c) + "|" + roughness.ToString("F2") + "|" + (int)family;
            if (_cache.TryGetValue(key, out var hit) && hit != null) return hit;

            var sh = Shader.Find("Whisper/LitPbr") ?? Shader.Find("Whisper/UnlitColor") ?? Shader.Find("Standard");
            var m = new Material(sh);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", Mathf.Clamp01(1f - roughness));
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            // 程序化贴图：给主界面几何也加上细节（不然还是"纯色矩形方体"）
            if (m.HasProperty("_DetailTex"))
            {
                var set = Whisper.Gameplay.Render.ProceduralTextures.Get(family);
                if (set != null)
                {
                    m.SetTexture("_DetailTex", set.Detail);
                    m.SetTexture("_BumpMap", set.Normal);
                    m.SetTexture("_OcclusionMap", set.Occlusion);
                    if (m.HasProperty("_DetailScale")) m.SetFloat("_DetailScale", set.DetailScale);
                    if (m.HasProperty("_DetailStrength")) m.SetFloat("_DetailStrength", set.DetailStrength);
                    if (m.HasProperty("_DirtAmount")) m.SetFloat("_DirtAmount", set.DirtAmount);
                }
            }
            _cache[key] = m;
            return m;
        }

        /// <summary>自发光材质（rgb=颜色 · **a=强度**，与本项目 `_WhisperEmission` 的契约一致）。</summary>
        public static Material Emissive(Color c, float strength)
        {
            string key = "E" + ColorKey(c) + "|" + strength.ToString("F1");
            if (_cache.TryGetValue(key, out var hit) && hit != null) return hit;
            var sh = Shader.Find("Whisper/UnlitColor");
            var m = sh != null ? new Material(sh) : new Material(Shader.Find("Standard"));
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_WhisperEmission")) m.SetColor("_WhisperEmission", new Color(c.r, c.g, c.b, strength));
            else if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * strength); }
            _cache[key] = m;
            return m;
        }

        static string ColorKey(Color c)
            => ((int)(c.r * 255)).ToString() + "_" + ((int)(c.g * 255)).ToString() + "_" + ((int)(c.b * 255)).ToString();

        /// <summary>建一个长方体（墙/地/顶/家具都用它）。</summary>
        public static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && mat != null) mr.sharedMaterial = mat;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);     // 主界面不参与物理
            return go;
        }

        /// <summary>建一段文字。</summary>
        public static Text Label(Transform parent, string name, string text, Vector2 anchorMin, Vector2 anchorMax,
                                 int size, TextAnchor align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.text = text;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size;
            t.alignment = align;
            t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(720f, size * 1.6f);
            return t;
        }

        /// <summary>建一个按钮（右侧玩法选项用）。</summary>
        public static Button Button(Transform parent, string name, string label, Vector2 anchor, Vector2 size,
                                    UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.10f, 0.12f, 0.16f, 0.88f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(onClick);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
            Label(go.transform, name + "Label", label, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), 24, TextAnchor.MiddleCenter);
            return btn;
        }

    }
}
