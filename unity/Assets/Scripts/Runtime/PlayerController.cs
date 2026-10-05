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
    public sealed partial class PlayerController : MonoBehaviour
    {
        /// <summary>眼高（米）。第一人称相机高度。**单一真源**（`GameBootstrap` 摆相机也引用这里）。</summary>
        public const float EyeHeightM = 1.7f;

        /// <summary>推杆多少算奔跑（V9 §7：跑 / 走 / 蹲三形态）。</summary>
        const float RunThreshold = 0.85f;
        /// <summary>
        /// 视角灵敏度换算因子：把 token `touch.lookSensitivityDefault`（0.0032，比例常数）
        /// 换成角度。实测标定 **0.0032 × 45 = 0.144°/像素**（拖 50px 转 7.2°、拖满半屏 1400px 转 201°）。
        /// 【2026-10-04 真机实测修正】历史值是 120 → **0.384°/像素**，实测"向右拖 500px 使
        /// `朝向 94° → 277°`"即**转了 183°（半圈）**，过快；玩家会分不清"方向反了"还是"转太快"。
        /// **调手感只动这个数**，不要改 token（token 是全 UI 共用的真源）。
        /// </summary>
        const float LookDegreesScale = 45f;

        PlayerMotion _motion;
        LevelGeometry _geo;
        Camera _camera;
        Text _hud;
        Button _crouchBtn;
        /// <summary>平滑后的摇杆输出（治"生硬"：一步一抖的原始位移直接进运动学，手感是顿的）。</summary>
        Vector2 _smoothStick;
        Image _wheelBase;
        Image _wheelRing;
        Image _wheelKnob;
        Text _crouchLabel;

        int _moveTouchId = -1;
        Vector2 _touchCurrent;
        /// <summary>右半屏拖拽 = 转视角（真机反馈：首版完全没有转头操作）。</summary>
        int _lookTouchId = -1;
        /// <summary>最近一次**真的看到** `_lookTouchId` 的帧号（用于"僵尸手指"自愈，见 ReadTouchInput）。</summary>
        int _lookSeenFrame = -1;
        /// <summary>在蹲下按钮矩形内按下、但还没拖出阈值的触摸（拖出去就升级为转视角）。</summary>
        int _pendingLookId = -1;
        Vector2 _pendingLookOrigin;
        Vector2 _lookLast;
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

            // ── 从相机**采纳**初始朝向（yaw 与 pitch 都要）────────────────────────
            // 【2026-10-04 真机实测】`GameBootstrap.PlaceCamera` 精心算好了"朝门口 + 略微俯视"，
            // 但 `PlayerMotion` 的 Yaw/Pitch 初始都是 0，而本控制器每帧用
            // `Quaternion.Euler(-PitchDeg, YawDeg, 0)` **覆盖**相机旋转 —— 于是那套朝向被静默丢弃。
            // 上次只给 yaw 打了补丁（`GameBootstrap` 传 `FacingYawDeg`），**pitch 一直没人接**：
            // 真机开局实测 `俯仰 -31°`（相机被摆在玩家后下方 + 俯角），看起来像"一进游戏就在看地板"。
            // 现在改成本控制器**直接采纳相机的当前朝向**，让"摆相机"只有一处真源
            // （以后 `PlaceCamera` 想怎么摆就怎么摆，这里自动跟着，不会再出现"算了但没用上"）。
            if (_camera != null)
            {
                var e = _camera.transform.rotation.eulerAngles;
                _motion.SetYaw(e.y);
                // 相机的 X 角为负 = 抬头，而 PitchDeg **正 = 低头**（见 ApplyToTransform 的取负）
                // → 两者符号相反。eulerAngles 会把负角折成 0~360，所以先归一到 (-180,180]。
                float camPitch = e.x > 180f ? e.x - 360f : e.x;
                _motion.Look(0f, -camPitch);
            }

            BuildCrouchButton();
            BuildMoveWheel();
            ApplyToTransform();
        }


        // ── 移动轮盘（可见的虚拟摇杆）────────────────────────────────────────
        //
        // 【为什么要做】用户反馈"给方向移动加一个轮盘移动"。此前是**隐式摇杆**：左半屏任意处
        // 按下就当作原点，屏幕上没有任何可视参照——玩家不知道该往哪推、推多远，
        // 也不知道自己到底推出了多大力度（第一人称恐怖游戏里"轻推慢走"很重要）。
        //
        // 做法：固定位置的**底盘 + 随手指移动的推杆**，只在底盘范围内响应触摸；
        // 力度 = 推杆偏离底盘中心的比例，**满舵点 = 圆环**（判定与视觉同尺度）。
        // 好处：① 有可视反馈 ② 触摸归属明确（底盘外不抢触摸，右半屏看视角不受影响）
        //       ③ 与旧手感一致，不改变 `PlayerMotion` 的任何数值契约。
        const float WheelBaseSizePx = 300f;    // 底盘**直径**
        const float WheelKnobSizePx = 130f;    // 推杆**直径**
        const float WheelRingWidthPx = 7f;     // 圆环线宽（视觉边界 = 判定边界，见下）
        const float WheelMarginPx = 40f;       // 距屏幕左/下边缘

        /// <summary>
        /// 轮盘中心的**默认**位置（屏幕像素）。按钮区在右下，两者不重叠。
        /// 位置调整史（都是真机实测反馈）：
        ///   ① 原 `(0.16W, 0.28H)` → 2800×1280 下 `(448, 922)`，**偏右偏高**，且比例定位在宽屏上会推离左下角
        ///   ② 改边距 26 → 贴左下角，但实测**太低**
        ///   ③ 边距 40 + X 额外右移 30 → `(220, 190)`（实测反馈：再往右上微调一点）
        /// **固定**：轮盘中心永远在这个位置，不跟手。
        /// （我一度做成"浮动轮盘"——按下处即中心、圆盘跟着手跑——用户明确要求"轮盘跟着手动"要改掉。）
        /// 宽容度不靠移动轮盘实现，而是靠**认领范围 = 整个左半屏**（见 `ReadTouchInput`）。
        /// </summary>
        Vector2 WheelCenterPx => new Vector2(
            WheelMarginPx + WheelBaseSizePx * 0.5f + 30f,
            WheelMarginPx + WheelBaseSizePx * 0.5f);

        /// <summary>
        /// 生成一张**圆形**贴图（内部实心 + 外圈略亮的圆环）。
        /// 为什么必须自己生成：`Image` 不给 `sprite` 时画的是**默认白色方块** ——
        /// 于是"想要的圆盘"在屏幕上是个方框，而触摸判定用的是圆形距离，
        /// 两者对不上（用户反馈的"实际判定范围与轮盘范围不符"正是这个）。
        /// 用代码生成而不是导入美术资源：本工程是"uGUI 全部代码构建、编辑器零参与"（C2），
        /// 导入的贴图会被资产管线剥离/需要 .meta，反而多一处失效点。
        /// </summary>
        static Sprite MakeWheelSprite(int size, bool ring)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float r = size * 0.5f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // 像素中心到圆心的距离；用 4x 超采样做边缘抗锯齿，避免圆看起来是锯齿方块
                    float d = 0f;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                        {
                            float dx = x + 0.25f + sx * 0.5f - r;
                            float dy = y + 0.25f + sy * 0.5f - r;
                            d += Mathf.Sqrt(dx * dx + dy * dy);
                        }
                    d *= 0.25f;

                    float a;
                    if (ring)
                    {
                        // 圆环：外沿稍亮、内侧快速淡出 —— 给玩家一个"这就是判定边界"的可视提示
                        float edge = r - WheelRingWidthPx;
                        a = d <= r && d >= edge ? 1f : 0f;
                        if (d > r - 1f) a = 0f;                    // 最外 1px 收掉，防硬锯齿
                    }
                    else
                    {
                        a = d <= r - 1f ? 1f : 0f;                 // 底盘：实心圆
                    }
                    // 各向异性过滤起不了作用，改为在圆心附近保持实心、边缘 1px 渐隐
                    if (!ring && d > r - 2f) a *= Mathf.Clamp01(r - d);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        void BuildMoveWheel()
        {
            var canvas = _hud != null ? _hud.canvas : null;
            if (canvas == null) return;

            var baseGo = new GameObject("MoveWheelBase", typeof(Image));
            baseGo.transform.SetParent(canvas.transform, false);
            _wheelBase = baseGo.GetComponent<Image>();
            _wheelBase.sprite = MakeWheelSprite(256, ring: false);   // 圆形底盘（不给 sprite 会画成方块）
            _wheelBase.color = new Color(1f, 1f, 1f, 0.10f);
            _wheelBase.raycastTarget = false;      // 自己按屏幕坐标判定，不走 uGUI 事件（避免与视角拖拽抢触摸）

            var ringGo = new GameObject("MoveWheelRing", typeof(Image));
            ringGo.transform.SetParent(baseGo.transform, false);
            _wheelRing = ringGo.GetComponent<Image>();
            _wheelRing.sprite = MakeWheelSprite(256, ring: true);     // 环 = 判定边界的可见表达
            _wheelRing.color = new Color(1f, 1f, 1f, 0.22f);
            _wheelRing.raycastTarget = false;

            var knobGo = new GameObject("MoveWheelKnob", typeof(Image));
            knobGo.transform.SetParent(baseGo.transform, false);
            _wheelKnob = knobGo.GetComponent<Image>();
            _wheelKnob.sprite = MakeWheelSprite(128, ring: false);
            _wheelKnob.color = new Color(1f, 1f, 1f, 0.20f);
            _wheelKnob.raycastTarget = false;

            LayoutMoveWheel(Vector2.zero);
        }

        /// <summary>按当前分辨率摆放底盘、圆环与推杆；<paramref name="offsetPx"/> 是推杆相对底盘中心的偏移。</summary>
        void LayoutMoveWheel(Vector2 offsetPx)
        {
            if (_wheelBase == null || _wheelKnob == null) return;
            var c = WheelCenterPx;
            float half = WheelBaseSizePx * 0.5f;

            var brt = _wheelBase.rectTransform;
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.zero; brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(WheelBaseSizePx, WheelBaseSizePx);
            brt.anchoredPosition = c;

            if (_wheelRing != null)
            {
                var rrt = _wheelRing.rectTransform;
                rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one; rrt.pivot = new Vector2(0.5f, 0.5f);
                rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;   // 与底盘完全同心同径
            }

            var krt = _wheelKnob.rectTransform;
            krt.anchorMin = new Vector2(0.5f, 0.5f); krt.anchorMax = new Vector2(0.5f, 0.5f); krt.pivot = new Vector2(0.5f, 0.5f);
            krt.sizeDelta = new Vector2(WheelKnobSizePx, WheelKnobSizePx);
            // 推杆限制在底盘内（半径 = 底盘半径 - 推杆半径），超出按满舵夹住
            float maxOff = Mathf.Max(1f, half - WheelKnobSizePx * 0.5f);
            float len = offsetPx.magnitude;
            if (len > maxOff) offsetPx = offsetPx * (maxOff / len);
            krt.anchoredPosition = offsetPx;
        }

        /// <summary>
        /// 手指是否落在**视角区**（用户要求：整个右半屏都能转视角）。
        /// 【2026-10-04 修正】原实现是"右半屏**再挖掉右侧 28% 宽的一条**"（`InButtonZone`），
        /// 于是屏幕最右 28% 竖条**既不能走也不能看** —— 一个真实存在的死区，
        /// 正是用户说的"有时不知为何转不动视角"（手指落在死区里，什么都不会发生）。
        /// 现在只避开蹲下按钮的**实际矩形**（右下角 220×150 + 边距），且允许"从按钮上按下后
        /// 拖出去转视角"（见 `LookDragSlopPx`）。
        /// </summary>
        static bool InLookZone(Vector2 screenPos)
        {
            if (screenPos.x < Screen.width * 0.5f) return false;      // 左半屏归移动轮盘
            return !InCrouchButtonRect(screenPos);
        }

        /// <summary>蹲下按钮的实际矩形（右下角，与 `BuildCrouchButton` 的锚点/尺寸保持一致）。</summary>
        static bool InCrouchButtonRect(Vector2 p)
        {
            const float w = 220f, h = 150f, margin = 40f;
            return p.x >= Screen.width - margin - w && p.y <= margin + h;
        }

        /// <summary>
        /// 从按钮上按下后，手指移动超过这个距离就切换成"转视角"（而不是点按钮）。
        /// 目的：既不牺牲"按蹲下"的可靠性，又不让按钮成为视角死区。
        /// </summary>
        const float LookDragSlopPx = 28f;

        /// <summary>
        /// 摇杆输出的指数平滑时间常数（秒）。取 ~90ms：既够顺，又不至于"松手还在走"
        /// （松手时 `_moveTouchId` 立刻清空、`_smoothStick` 复位，所以没有惯性漂移）。
        /// 治用户反馈的"轮盘移动依旧很生硬"：原始位移直接喂运动学，方向会一步一跳。
        /// </summary>
        const float StickSmoothSeconds = 0.09f;

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
            ScanNearDoor();     // 节流 0.2s：决定「开门/关门」按钮出不出、显示什么字
        }

        /// <summary>
        /// 动态摇杆 + 视角拖拽：左半屏任意处按下即移动摇杆；右半屏拖拽转动视角。
        /// 两侧互不干扰（各自记住 fingerId），因为手机上"边跑边看"是常态。
        /// </summary>
        Vector2 ReadTouchInput(out float magnitude)
        {
            // 【2026-10-04 拆分】原函数 ~137 行，被 `gate-code` 的 C4 规模纪律判红（>120）。
            // 现在只做调度：认领手指 → 自愈僵尸手指 → 算摇杆输出。
            // 三段各自的实现与实测依据都在被调用的方法里（注释随代码一起搬走了）。
            ClaimTouches();
            if (_moveTouchId < 0) { magnitude = 0f; return Vector2.zero; }
            // 屏幕坐标是 (右+, 上+)；返回的是**视角相对**输入（上 = 朝前），
            // 由 PlayerMotion 按当前 Yaw 旋转到世界方向（WASD 语义）。
            return StickOutput(out magnitude);
        }


        /// <summary>
        /// 认领本帧的手指：左半屏归移动轮盘，右半屏（除蹲下按钮矩形）归视角，按钮矩形内先挂起。
        /// 【提取原因】`gate-code` 的 C4 规模纪律判红过 `ReadTouchInput` 约 137 行（&gt;120）——
        /// 那是有效的红线：触摸认领这套分支一旦混进输出计算，改任何一处都要通读整段。
        /// </summary>
        void ClaimTouches()
        {
            int count = Input.touchCount;
            for (int i = 0; i < count; i++)
            {
                var t = Input.GetTouch(i);
                if (t.phase == TouchPhase.Began)
                {
                    // 移动：**左半屏任意处按下都接管，但轮盘中心固定不动**
                    //
                    // 【2026-10-04 两次修正的结论】
                    //   ① 最初只在"圆盘半径内"才认领手指 → 左半屏圆外**既不是移动也不是视角**，
                    //      整个触摸被丢弃 → 用户报"有时划没反应"。
                    //   ② 我一度改成"浮动轮盘"（按下处即中心，圆盘跟着手跑）→ 用户反馈"**轮盘跟着手动**"，
                    //      要求固定。浮动轮盘虽然解决了丢输入，却毁掉了固定摇杆的肌肉记忆
                    //      （拇指闭着眼也知道盘在哪），而且盘会在屏幕上乱跑。
                    //   ③ 现在这条是**两者的正解**：中心**钉死在 `WheelCenterPx`**，
                    //      但**认领范围 = 整个左半屏**。手指落在哪都算移动（不丢输入），
                    //      输入 = 手指相对**固定中心**的偏移 —— 离盘远时等于"推到满舵"，
                    //      方向仍然由手指方位决定，符合直觉。
                    // 记录这次按下的起点：抬起时用来区分"点击"与"拖拽"（门交互需要真点击）
                    if (_tapTouchId < 0) { _tapTouchId = t.fingerId; _tapStart = t.position; }
                    // 【2026-10-04 真机试玩发现并修的 bug】**互斥判定缺了另一半**。
                    // 旧写法 `if (左半屏) {...} else if (_lookTouchId < 0) {...}` ——
                    // 那个 `else` 绑的是**内层条件** `_moveTouchId < 0`，于是：
                    //   手指落在左半屏 → 第一分支认领成功（`_moveTouchId = 0`），
                    //   但 `_lookTouchId` 仍是 -1 → `else if` 依然成立 →
                    //   同一根手指**同时**被认领为移动与视角 → 走轮盘时画面也在转。
                    // 实测证据：左半屏 (220,190)→(220,40) 一次拖拽，`朝向 295°→115°`、`俯仰 -3°→-28°`。
                    // 现在用显式 `claimed`：一次按下只能归一个用途。
                    bool claimed = _moveTouchId < 0 && t.position.x < Screen.width * 0.5f;
                    if (claimed)
                    {
                        _moveTouchId = t.fingerId;
                        _touchCurrent = t.position;
                        LayoutMoveWheel(Vector2.zero);   // 推杆回中
                    }
                    // 视角：整个右半屏（`InLookZone`），只避开蹲下按钮的**实际矩形**。
                    // 为什么不是"点不到按钮就当视角"：`Image.raycastTarget` 走 uGUI 事件，
                    // 而这里读原始 `Input.touches` —— 两者互不阻塞。若按钮矩形内的触摸直接判成视角，
                    // 就会出现"想按蹲下、手指稍滑就转头"。所以按钮矩形内先**挂起**（`_pendingLookId`），
                    // 拖出 `LookDragSlopPx` 才升级为转视角；原地抬起则交给 uGUI 派发 onClick。
                    else if (!claimed && _lookTouchId < 0)
                    {
                        if (InLookZone(t.position))
                        {
                            _lookTouchId = t.fingerId;
                            _lookLast = t.position;
                            _lookSeenFrame = Time.frameCount;
                        }
                        else if (InCrouchButtonRect(t.position))
                        {
                            _pendingLookId = t.fingerId;
                            _pendingLookOrigin = t.position;
                        }
                    }
                    continue;
                }

                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                    OnTouchReleasedForDoor(t.fingerId, t.position);
                if (t.fingerId == _pendingLookId) UpdatePendingLook(t);
                if (t.fingerId == _moveTouchId) UpdateMoveTouch(t);
                else if (t.fingerId == _lookTouchId) UpdateLookTouch(t);
            }

            // 「僵尸手指」自愈：手指早抬起、但 `_lookTouchId` 没被清掉 → 之后**所有** `Began`
            // 都因 `_lookTouchId >= 0` 进不了视角分支 → 视角彻底转不动（用户："有时不知为何转不动"）。
            // 判据：本帧没有任何 touch 匹配它 → 说明它已经不在了 → 立即释放。
            if (_lookTouchId >= 0 && _lookSeenFrame != Time.frameCount) _lookTouchId = -1;
        }

        /// <summary>挂起中的"可能在蹲下按钮上"的触摸：拖出阈值即升级为视角。</summary>
        void UpdatePendingLook(Touch t)
        {
            if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) { _pendingLookId = -1; return; }
            if (_lookTouchId < 0 && (t.position - _pendingLookOrigin).magnitude > LookDragSlopPx)
            {
                _lookTouchId = t.fingerId;
                _lookLast = t.position;     // 从当前位置起算，避免升级瞬间跳一大格
                _lookSeenFrame = Time.frameCount;
                _pendingLookId = -1;
            }
        }

        /// <summary>移动手指：松手回中并复位平滑状态；否则更新当前位置。</summary>
        void UpdateMoveTouch(Touch t)
        {
            if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
            {
                _moveTouchId = -1;
                _smoothStick = Vector2.zero;     // 复位，避免下次按下"带旧速度"
                LayoutMoveWheel(Vector2.zero);   // 推杆回中（可视反馈）
            }
            else _touchCurrent = t.position;
        }

        /// <summary>视角手指：按灵敏度转动视角（含"上下方向"与"灵敏度标定"两处实测修正）。</summary>
        void UpdateLookTouch(Touch t)
        {
            if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) { _lookTouchId = -1; return; }

            _lookSeenFrame = Time.frameCount;
            var d = t.position - _lookLast;
            _lookLast = t.position;
            // 【上下方向】屏幕坐标 y 向下为正 → 向上拖得到 Δy<0 是**抬头**，所以这里用 `+d.y`。
            // （旧式写 `-d.y` 会把方向反掉：往上拖变成低头。用户实测反馈过这条。）
            // 【水平方向】`+d.x`：Yaw=0 朝 +Z 时，(1,0) 经 PlayerMotion 的 cos/sin 转到世界是 +X，
            // 与 `Quaternion.Euler(_, +Yaw, _)` 同向 → "向右拖 = 看向右"成立。
            // 【灵敏度】token 0.0032 × `LookDegreesScale`(45) = **0.144°/像素**。
            // 旧值 120（=0.384°/像素）实测"拖 500px 转 183°"，过快；调手感只动这个常量。
            _motion.Look(d.x * DesignTokens.TouchLookSensitivityDefault * LookDegreesScale,
                          d.y * DesignTokens.TouchLookSensitivityDefault * LookDegreesScale);
        }

        /// <summary>
        /// 摇杆输出：按半径夹取 → 死区 → 指数平滑。
        /// 【提取原因】同 `ClaimTouches` —— 原函数过长，且这三步各自有独立的实测依据。
        /// </summary>
        Vector2 StickOutput(out float magnitude)
        {
            magnitude = 0f;
            if (_moveTouchId < 0) return Vector2.zero;   // 松手立即停下（不做惯性：恐怖游戏要精确走位）

            // 按半径夹取后归一化（用户反馈"轮盘判定很生硬"的根因）：
            // 旧式 `delta` 未夹取，手指拖到边界外时方向会**反向**（拇指在边缘一晃"前进"变"后退"）；
            // 且旧尺度 `JoystickRadiusPx=180` 比底盘半径 150 还大，与看到的圆环不一致。
            float radius = WheelBaseSizePx * 0.5f;
            var delta = _touchCurrent - WheelCenterPx;
            float raw = delta.magnitude;

            // 死区：手指静止时的微抖不该变成角色抖动（token touch.deadZonePx = 8，此前一直没被用）
            const float dead = 8f;
            float eff = raw <= dead ? 0f : (raw - dead);

            var target = Vector2.zero;
            if (eff > 1e-3f && raw > 1e-6f)
            {
                var unit = new Vector2(delta.x / raw, delta.y / raw);
                target = unit * Mathf.Clamp01(Mathf.Min(raw, radius) / radius);   // 满舵 = 手指到圆环
            }
            // 指数平滑：原始位移直接喂运动学会让方向一步一跳（时间常数 ~90ms；松手即归零，无惯性漂移）
            float k = 1f - Mathf.Exp(-Time.deltaTime / StickSmoothSeconds);
            _smoothStick = Vector2.Lerp(_smoothStick, target, k);

            magnitude = _smoothStick.magnitude;
            return magnitude < 1e-3f ? Vector2.zero : _smoothStick;
        }

        /// <summary>
        /// 蹲行眼高。**为什么必须有**（终验真机试玩发现）：蹲下原本只改了移动速度与脚步刺激，
        /// 相机高度写死 `EyeHeightM` —— 玩家按了「蹲行」，画面**一点没变**，
        /// 体感上等于"蹲下没生效"。蹲行在恐怖游戏里是躲视线的主要手段，眼高必须跟着降。
        /// 取值 1.0m：约站立眼高的 59%，与"贴地潜行"的观感一致，且仍高于地面避免穿地。
        /// </summary>
        public const float CrouchEyeHeightM = 1.0f;

        /// <summary>当前眼高（按蹲/站插值由调用方决定，这里给目标值）。</summary>
        public float CurrentEyeHeightM => _crouch ? CrouchEyeHeightM : EyeHeightM;

        /// <summary>把逻辑位置/视角写进相机（第一人称：位置=眼高，旋转=偏航+俯仰）。</summary>
        void ApplyToTransform()
        {
            if (_camera == null) return;
            var t = _camera.transform;
            // 眼高按蹲/站切换；再做一次**平滑**过渡，避免蹲下瞬间画面"跳"一下。
            float target = CurrentEyeHeightM;
            _eyeHeight = Mathf.MoveTowards(_eyeHeight <= 0f ? target : _eyeHeight, target, EyeSmoothMps * Time.deltaTime);
            t.position = new Vector3(_motion.X, _eyeHeight, _motion.Z);
            // 俯仰取负：Unity 相机绕 X 轴正方向是"低头"
            t.rotation = Quaternion.Euler(-_motion.PitchDeg, _motion.YawDeg, 0f);
            // 推杆跟随手指（可视反馈）。放在这里而不是 Update：位置每帧都要跟，
            // 放这里能保证"输入→显示"同帧一致，不会出现推杆滞后一帧的抖动感。
            if (_wheelKnob != null) LayoutMoveWheel(_moveTouchId >= 0 ? _touchCurrent - WheelCenterPx : Vector2.zero);
        }

        /// <summary>眼高过渡速度（m/s）：0.7m 的落差约 0.25s 走完，既不跳也不拖沓。</summary>
        const float EyeSmoothMps = 2.8f;
        float _eyeHeight;

        /// <summary>诊断用：把玩家状态拼进 HUD（证明"真的在动"，而不是挂了组件）。</summary>
        /// <summary>玩家世界坐标（供 GameBootstrap 的温度 HUD 判断"此处是哪个房间"）。</summary>
        public float X => _motion != null ? _motion.X : 0f;
        public float Z => _motion != null ? _motion.Z : 0f;

        public string Describe()
        {
            if (_motion == null) return "玩家：未初始化";
            // 俯仰角也打出来：否则"往上拖到底是抬头还是低头"只能靠肉眼猜暗图
            // （真机取证时吃过这个亏：画面全黑，光看图分不出 +30° 还是 −30°）。
            // 符号约定见 `ApplyToTransform`：PitchDeg 为**正 = 低头**，为负 = 抬头。
            return string.Format("玩家 ({0:0.0}, {1:0.0}) · 朝向 {2:0}° · 俯仰 {3:0}° · {4} · 累计 {5:0.0}m",
                _motion.X, _motion.Z, _motion.YawDeg, _motion.PitchDeg,
                _crouch ? "蹲行" : "站立", _motion.DistanceTravelledM);
        }
    }
}
