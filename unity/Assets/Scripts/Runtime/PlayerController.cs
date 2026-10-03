using UnityEngine;
using UnityEngine.UI;
using Whisper.Core;
using Whisper.Gameplay.Level;
using Whisper.Gameplay.Session;

namespace Whisper.Runtime
{
    /// <summary>
    /// 玩家控制器（V9 §7 第一人称移动）—— **Unity 侧胶水层**。
    ///
    /// 职责分工（与本项目 LevelBuilder/LevelAssembly 的拆法一致）：
    ///   · 纯逻辑（速度取值、分离轴碰撞、子步进防穿墙、脚步刺激节拍）在
    ///     <see cref="Whisper.Gameplay.Session.PlayerMotion"/> —— 不引用 UnityEngine，本机 144 条断言覆盖；
    ///   · 本类只做引擎相关的事：读触摸、把结果写进 Transform 与相机。
    ///
    /// 为什么必须用触摸而不是键盘：这是 Android 游戏，玩家没有键盘。
    /// 采用**动态摇杆**（手指按下处即摇杆原点，右上角区域除外）—— 不需要任何 UI 元素即可操作，
    /// 且不做"固定位置摇杆"那种需要玩家低头找控件的手感。
    /// 摇杆推到底（≥0.85）自动进入奔跑；蹲行走右下角按钮切换（V9 §7 三种移动形态）。
    /// </summary>
    public sealed class PlayerController : MonoBehaviour
    {
        /// <summary>眼高（米）。第一人称相机高度。</summary>
        public const float EyeHeightM = 1.7f;
        /// <summary>摇杆最大半径（像素）；超出按满舵算。</summary>
        const float JoystickRadiusPx = 180f;
        /// <summary>推杆多少算奔跑（V9 §7：跑 / 走 / 蹲三形态）。</summary>
        const float RunThreshold = 0.85f;
        /// <summary>右下角留给按钮的宽度比例（该区域不当作摇杆）。</summary>
        const float ButtonZoneWidthRatio = 0.28f;

        PlayerMotion _motion;
        LevelGeometry _geo;
        Camera _camera;
        Text _hud;
        Button _crouchBtn;
        Text _crouchLabel;

        int _moveTouchId = -1;
        Vector2 _touchOrigin;
        Vector2 _touchCurrent;
        bool _crouch;

        public bool IsCrouching => _crouch;
        public PlayerMotion Motion => _motion;
        /// <summary>最近一步（供 HUD 与后续玩法读取）。</summary>
        public MoveStep LastStep { get; private set; }

        /// <summary>
        /// 初始化。必须在 <see cref="GameBootstrap"/> 装好几何之后调用。
        /// </summary>
        public void Initialize(PlayerMotion motion, LevelGeometry geo, Camera cam, Text hud)
        {
            _motion = motion;
            _geo = geo;
            _camera = cam;
            _hud = hud;
            BuildCrouchButton();
            ApplyToTransform();
        }

        /// <summary>C2：蹲行按钮也是代码构建（编辑器零参与）。</summary>
        void BuildCrouchButton()
        {
            var canvas = _hud != null ? _hud.canvas : null;
            if (canvas == null) return;

            var btnGo = new GameObject("CrouchButton", typeof(Image), typeof(Button));
            btnGo.transform.SetParent(canvas.transform, false);
            var img = btnGo.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.10f);
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(220f, 150f);
            rt.anchoredPosition = new Vector2(-40f, 40f);

            _crouchBtn = btnGo.GetComponent<Button>();
            _crouchBtn.onClick.AddListener(() => { _crouch = !_crouch; RefreshCrouchLabel(); });

            var labelGo = new GameObject("Label", typeof(Text));
            labelGo.transform.SetParent(btnGo.transform, false);
            _crouchLabel = labelGo.GetComponent<Text>();
            _crouchLabel.font = _hud.font;            // 复用已加载的字体（Text.font 为 null 时什么都不画）
            _crouchLabel.alignment = TextAnchor.MiddleCenter;
            _crouchLabel.fontSize = 34;
            _crouchLabel.color = HexToColor(DesignTokens.ColorPaper);
            _crouchLabel.raycastTarget = false;
            var lrt = _crouchLabel.rectTransform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            RefreshCrouchLabel();
        }

        void RefreshCrouchLabel()
        {
            if (_crouchLabel != null) _crouchLabel.text = _crouch ? "蹲行" : "站立";
        }

        static Color HexToColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.white;
            if (hex[0] == '#') hex = hex.Substring(1);
            if (hex.Length < 6) return Color.white;
            return new Color32(
                System.Convert.ToByte(hex.Substring(0, 2), 16),
                System.Convert.ToByte(hex.Substring(2, 2), 16),
                System.Convert.ToByte(hex.Substring(4, 2), 16), 255);
        }

        void Update()
        {
            if (_motion == null || _geo == null) return;

            var input = ReadTouchInput(out float magnitude);
            var mode = _crouch
                ? MoveMode.Crouch
                : (magnitude >= RunThreshold ? MoveMode.Run : MoveMode.Walk);

            LastStep = _motion.Step(input.x, input.y, mode, Time.unscaledDeltaTime, _geo);
            ApplyToTransform();
        }

        /// <summary>
        /// 动态摇杆：第一根落在左侧区域的触摸成为移动摇杆；右侧留白区（按钮区）不接管。
        /// </summary>
        Vector2 ReadTouchInput(out float magnitude)
        {
            magnitude = 0f;
            int count = Input.touchCount;
            float buttonZoneX = Screen.width * (1f - ButtonZoneWidthRatio);

            for (int i = 0; i < count; i++)
            {
                var t = Input.GetTouch(i);
                if (t.phase == TouchPhase.Began && _moveTouchId < 0 && t.position.x < buttonZoneX)
                {
                    _moveTouchId = t.fingerId;
                    _touchOrigin = t.position;
                    _touchCurrent = t.position;
                }
                else if (t.fingerId == _moveTouchId)
                {
                    if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) _moveTouchId = -1;
                    else _touchCurrent = t.position;
                }
            }

            if (_moveTouchId < 0) return Vector2.zero;      // 松手立即停下（不做惯性：恐怖游戏需要精确走位）

            var delta = _touchCurrent - _touchOrigin;
            float len = delta.magnitude;
            if (len < 1e-3f) return Vector2.zero;
            magnitude = Mathf.Min(1f, len / JoystickRadiusPx);
            // 屏幕坐标是 (右+, 上+)；映射到世界 XZ：右→+X、上→+Z
            return new Vector2(delta.x / len, delta.y / len) * magnitude;
        }

        /// <summary>把逻辑位置/朝向写进相机（第一人称）。</summary>
        void ApplyToTransform()
        {
            if (_camera == null) return;
            var t = _camera.transform;
            t.position = new Vector3(_motion.X, EyeHeightM, _motion.Z);
            // 位置由逻辑决定、朝向由 YawDeg 决定；本阶段不做俯仰（触摸摇杆只管移动）
            t.rotation = Quaternion.Euler(0f, _motion.YawDeg, 0f);
        }

        /// <summary>诊断用：把玩家状态拼进 HUD（证明"真的在动"，而不是挂了组件）。</summary>
        public string Describe()
        {
            if (_motion == null) return "玩家：未初始化";
            return string.Format("玩家 ({0:0.0}, {1:0.0}) · 朝向 {2:0}° · {3} · 累计 {4:0.0}m",
                _motion.X, _motion.Z, _motion.YawDeg,
                _crouch ? "蹲行" : "站立", _motion.DistanceTravelledM);
        }
    }
}
