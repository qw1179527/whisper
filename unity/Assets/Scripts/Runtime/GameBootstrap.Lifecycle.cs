using UnityEngine;
using UnityEngine.UI;
using Whisper.Core;
using Whisper.Core.Contracts;
using Whisper.Gameplay.Config;
using Whisper.Gameplay.Level;
using Whisper.Net;
using Whisper.Audio;
using Whisper.Backend;

namespace Whisper.Runtime
{
    // 2026-10-06 从 GameBootstrap.cs 拆出（gate-code C5：单文件 ≤600 行）。
    // 拆的是**内聚的一块**（生命周期与构建），不是按行数硬切：Awake/Start/BuildCamera/BuildUi/字体与颜色助手 —— 启动时那一串。
    // 用项目既有的 partial 惯例（同 GameBootstrap.Spawn.cs / .Temperature.cs）。
    public sealed partial class GameBootstrap
    {
        void Awake()
        {
            Application.targetFrameRate = 60;   // V9 §13.4 固定 60 Tick/s 的客户端帧率基线
            // C2：uGUI 全部代码构建。注意顺序——相机与 HUD 必须在 Boot 之前就绪，
            // 否则 boot 失败时连"为什么失败"都看不见（真机黑屏事故的教训之一）。
            BuildCamera();
            BuildUi();
        }

        void Start()
        {
            // 【防"失焦即停渲染"】移动端默认 false：失焦会停渲染并释放 Surface，
            // 而本工程相机/UI 全是代码建的、不会自建回来（真机日志：APP_CMD_TERM_WINDOW → destroySurface）。
            // 运行期也设一遍，防 ProjectSettings 被覆盖或换机后丢设置。
            Application.runInBackground = true;
            Boot();
        }

        /// <summary>
        /// 相机（V9 §19.1 C1 的隐含前提）。
        /// 真机事故教训：最初 Boot 场景里只有 GameBootstrap 一个组件，**没有任何相机**——
        /// 就算几何装配成功，屏幕上也不会有任何东西。相机同样由代码创建。
        /// </summary>
        void BuildCamera()
        {
            var camGo = new GameObject("MainCamera", typeof(Camera));
            camGo.transform.SetParent(transform, false);
            // 刻意不设 tag="MainCamera"：渲染不需要它，而它依赖 TagManager
            // （本工程同样没有 ProjectSettings 真源）——启动期不为零收益的东西引入风险。
            _camera = camGo.GetComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = HexToColor(DesignTokens.ColorInk);   // 墨色背景，走廊尽头不至于惨白
            _camera.fieldOfView = 70f;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 120f;
            _camera.transform.position = new Vector3(0f, PlayerController.EyeHeightM, -6f);   // 眼高引用单一真源
            _camera.transform.rotation = Quaternion.identity;

            // 一盏方向光：为将来接 Lit 材质/真美术资产预留（当前是 Unlit，光照不影响观感）
            var lightGo = new GameObject("KeyLight", typeof(Light));
            lightGo.transform.SetParent(transform, false);
            var light = lightGo.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.85f;
            light.color = HexToColor(DesignTokens.ColorBone);
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // ── 雾：**着色器全局量**（不是材质属性）────────────────────────────
            // 为什么走全局量：本工程的材质由 LevelBuilder 运行时批量 `new Material(shader)` 创建，
            // 拿不到引用；若把雾做成 `Properties`，值会被固化进每个材质 → 运行时就没法"一处调"。
            // 设成全局量后，任何时候改一次就全体生效（也便于停电/事件时整屏关雾）。
            //
            // 契约（由构建智能体 A 冻结；数值是他**实测驱动**修正过的）：
            //   _WhisperFogStartM  8       雾开始距离(m)
            //   _WhisperFogEndM    36      雾饱和距离(m)；<= Start 即完全无雾
            //   _WhisperFogColor   (0.055,0.051,0.047,1)  近黑 ink 系雾色
            //   _WhisperFogOff     0       1 = 彻底关雾（kill switch）
            //
            // 为什么是 8/36 而不是拍脑袋的 12/60：A 用 `pixel-diff-pair.mjs` 的 8/255 判据实测发现，
            // 12→60 时走廊尽头（17m）的混合系数只有 10%，**像素差低于判据 → 判"无变化"**，
            // 等于做了一条看不出来的雾。改成 8→36 后：房间进深 3~4m 完全无雾（小房间观感不动），
            // 走廊 12m→14%、17m→32%、最长视线 22m→50% —— 看得见纵深，又只压暗不提亮。
            //
            // ⚠️ 两个坑（A 实测踩到，写在这里免得后人重踩）：
            //   ① `Shader.SetGlobalColor` 的 **alpha 必须 > 0**，否则着色器按"未设置"处理，
            //      表现为"设了雾色但完全没生效"；
            //   ② 关雾有两条路：`_WhisperFogOff=1`，或把 `_WhisperFogEndM` 设成 ≤ `_WhisperFogStartM`。
            Shader.SetGlobalFloat(FogStartProp, 8f);
            Shader.SetGlobalFloat(FogEndProp, 40f);
            Shader.SetGlobalColor(FogColorProp, new Color(0.055f, 0.051f, 0.047f, 1f));   // alpha=1（见坑①）
            Shader.SetGlobalFloat(FogOffProp, 0f);
        }

        // 雾的全局属性名：写成常量，避免散落的字符串字面量拼错（拼错不会报错，只会静默无效）
        const string FogStartProp = "_WhisperFogStartM";
        const string FogEndProp = "_WhisperFogEndM";
        const string FogColorProp = "_WhisperFogColor";
        const string FogOffProp = "_WhisperFogOff";

        /// <summary>运行时开关雾（停电/事件等）。1 = 关。</summary>
        public static void SetFogOff(bool off) => Shader.SetGlobalFloat(FogOffProp, off ? 1f : 0f);

        /// <summary>C2：uGUI 全部代码构建，编辑器零参与。</summary>
        void BuildUi()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // 【终验·真机试玩发现】uGUI 的 Button **必须有 EventSystem 才会派发 onClick**。
            // 全仓此前**没有任何 EventSystem**（`EventSystem|InputModule` 命中数 0），
            // 于是 `PlayerController.BuildCrouchButton()` 里注册的
            // `_crouchBtn.onClick.AddListener(...)` **永远不触发** —— 玩家能走能看，却蹲不下来。
            // 移动/转向之所以正常，是因为它们走 `Input.touchCount` 直接读取，不依赖 uGUI 事件。
            // 模块选 `StandaloneInputModule`：本工程 `activeInputHandler: 0`（旧 Input Manager），
            // 与玩家控制器用的是同一个输入源，不引入新输入系统的依赖。
            var esGo = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.EventSystems.StandaloneInputModule));
            esGo.transform.SetParent(transform, false);

            var textGo = new GameObject("BootStatus", typeof(Text));
            textGo.transform.SetParent(canvasGo.transform, false);
            _status = textGo.GetComponent<Text>();
            // 字体：Text.font 为空时 uGUI **什么都不画**（真机事故教训之二——屏幕全黑却没有任何报错）。
            _status.font = LoadDefaultFont();
            _status.alignment = TextAnchor.UpperLeft;
            _status.fontSize = 28;
            _status.color = HexToColor(DesignTokens.ColorPaper);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
            _status.verticalOverflow = VerticalWrapMode.Overflow;
            _status.raycastTarget = false;   // HUD 不吞触摸事件
            var rt = _status.rectTransform;
            rt.anchorMin = new Vector2(0.03f, 0.03f);
            rt.anchorMax = new Vector2(0.97f, 0.97f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// 取一个可用的内置字体。Unity 6 的内置 UI 字体是 LegacyRuntime.ttf；
        /// 更老的版本用 Arial.ttf。两条都试，最后回落到系统字体——**不允许返回 null**，
        /// 因为 Text.font == null 意味着 HUD 完全不可见。
        /// </summary>
        static Font LoadDefaultFont()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font == null) font = Font.CreateDynamicFontFromOSFont("sans-serif", 28);
            if (font == null) Debug.LogError("[Whisper] 一个可用字体都没找到，HUD 将不可见");
            return font;
        }

        static Color HexToColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.white;
            if (hex[0] == '#') hex = hex.Substring(1);
            if (hex.Length < 6) return Color.white;
            byte r = System.Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = System.Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = System.Convert.ToByte(hex.Substring(4, 2), 16);
            return new Color32(r, g, b, 255);
        }

        /// <summary>
        /// 出生点解析结果（④ 几何阶段产出、⑤ 玩家阶段消费）。
        /// <see cref="FacingYawDeg"/> 也由几何阶段（<c>PlaceCamera</c>，它知道门口在哪）算出，
        /// 玩家阶段必须把它写进 <c>PlayerMotion</c> —— 否则控制器会用默认 0° 覆盖相机朝向。
        /// </summary>
        readonly struct Spawn
        {
            public readonly bool Ok;
            public readonly float X, Z;
            /// <summary>初始朝向（度）：0° = +Z，90° = +X（与 Unity 的 Yaw 约定一致）。</summary>
            public readonly float FacingYawDeg;
            public Spawn(bool ok, float x, float z, float facingYawDeg = 0f)
            {
                Ok = ok; X = x; Z = z; FacingYawDeg = facingYawDeg;
            }
        }

        /// <summary>
        /// 启动编排（只做顺序与失败短路；每个阶段的具体工作在下方各 Try* 方法里）。
        /// 为什么拆开：本方法一度长到 135 行，被 gate-code 的 C5 规模纪律判红——
        /// 那是有效的红线，"启动链"这种东西一旦混成一坨，出问题就只能靠通读。
        /// </summary>
    }
}
