using UnityEngine;
using UnityEngine.UI;
using Whisper.Core;
using Whisper.Core.Contracts;
using Whisper.Gameplay.Config;
using Whisper.Gameplay.Level;
using Whisper.Gameplay.Progression;   // Progression / Shop / TaskSystem（等级-商店-任务，恐鬼症对齐）
using Whisper.Gameplay.Objectives;    // ObjectiveSystem（**局内任务**：合同日志里的可选目标，与每日任务是两套）
using Whisper.Net;
using Whisper.Net.Direct;   // LanSession / LanAddress / RoomCode（零信令直连层）
using Whisper.Audio;
using Whisper.Backend;

namespace Whisper.Runtime
{
    /// <summary>
    /// Boot 场景的唯一组件（V9 §19.1 C1「场景零手工」）—— **组合根**。
    ///
    /// 为什么它必须存在：三接口的实现在各自模块（Net/Audio/Backend），而"谁来在启动期注入"
    /// 这个问题此前没有答案（独立验证轨 A7 警告：全仓库 Services.Install 只在测试里出现）。
    /// 本类即那个答案，且是**唯一**允许同时引用各模块实现的地方。
    ///
    /// 启动链（下方 Awake → Start 逐步兑现，不再是注释里的空头承诺）：
    ///   ① 读配置表（Assets/Data/config.json → GameConfig，数值唯一真源）
    ///   ② 组装并注入三接口（Local* 桩 ↔ 将来 Fusion/Vivox/Firebase 实现，接口不变）
    ///   ③ 装载并校验关卡（Level DSL）
    ///   ④ 进入 Play 循环（每帧 Tick，并把状态显示在代码构建的 HUD 上 —— C2）
    ///
    /// 说明：本类引用 UnityEngine，因此**不在本机 .NET 跑手覆盖范围内**（那 38 项断言覆盖的是
    /// 零引擎依赖的 Core/Gameplay 层）；本类的路径由 Unity Test Framework（PlayMode）与真机验证覆盖。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class GameBootstrap : MonoBehaviour
    {
        [Tooltip("Level DSL 资源路径（发布构建走 Addressables；此处保留 Resources 形态以便 Play/Edit 测试）")]
        public string LevelResourcePath = "Levels/asylum_v1";

        /// <summary>当前地图 id（与 config.maps.list[].id 对应）。默认 asylum_v1。</summary>
        public string MapId { get; private set; } = "asylum_v1";

        /// <summary>
        /// 选图：按 `config.maps.list` 解析 `resource` 并切换关卡路径。
        /// 为什么走注册表：多地图是用户要求（"接入多地图选项"），而资源路径属**数值真源**
        /// （V9 §19.5：改数值不碰代码）。
        /// 返回 false = 该图未实现或不在注册表 —— **不静默换图**（换错图比报错更糟）。
        /// </summary>
        public bool SelectMap(string mapId)
        {
            if (string.IsNullOrEmpty(mapId)) return false;
            var list = GameConfig.Get("maps.list") as System.Collections.Generic.List<object>;
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                var m = MiniJson.AsMap(list[i]);
                if (m == null) continue;
                var id = MiniJson.AsString(MiniJson.Get(m, "id"));
                if (id != mapId) continue;
                var res = MiniJson.AsString(MiniJson.Get(m, "resource"));
                if (string.IsNullOrEmpty(res)) return false;
                LevelResourcePath = res;
                MapId = id;
                return true;
            }
            return false;
        }

        /// <summary>地图注册表摘要（地图板/HUD 用；明确标出"哪些已实现"）。</summary>
        public string DescribeMaps()
        {
            var list = GameConfig.Get("maps.list") as System.Collections.Generic.List<object>;
            if (list == null) return "地图注册表缺失";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                var m = MiniJson.AsMap(list[i]);
                if (m == null) continue;
                var id = MiniJson.AsString(MiniJson.Get(m, "id"));
                var name = MiniJson.AsString(MiniJson.Get(m, "name"));
                bool impl = MiniJson.AsBool(MiniJson.Get(m, "implemented"));
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(name).Append("(").Append(id).Append(")");
                if (id == MapId) sb.Append("★");
                if (!impl) sb.Append("（未实现）");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 联机意图：**默认单人**（行为与接入前完全一致 —— 不开 socket，走本机回环桩）。
        /// 菜单里点「多人联机」= Host（建房），点「加入房间」= Join（用 <see cref="NetJoinCode"/>）。
        /// 之所以做成显式意图而不是"自动联机"：真实现会 bind 端口，不该在大厅里偷偷开。
        /// </summary>
        public NetIntentKind NetIntent = NetIntentKind.Solo;

        /// <summary>加入时要输入的房间码（仅 <see cref="NetIntentKind.Join"/> 用）。</summary>
        public string NetJoinCode;

        /// <summary>建房成功后要发给朋友的房间码（UI 显示在菜单板右上角）；未建房为 null。</summary>
        public string NetRoomCode => LanSession.NetRoomCode;

        /// <summary>
        /// 货车安全区（世界 AABB）。**唯一数据源**是 `_hall.TruckSafeZone`。
        /// 用户《补充说明》§8：「货车及其周围是安全区：鬼无法进入，在车内不掉理智」。
        /// 暴露在组合根是为了**单一口径** —— 撤离判定、理智保护、怪物寻路都读它，
        /// 不再各自算一遍（本项目在 UI 与关卡生成器上已多次吃过"两套口径"的亏）。
        /// </summary>
        /// <summary>货车安全区数据（由 MenuScene 推送，见 SetTruckSafeZone）。默认在地底且零尺寸。</summary>
        Bounds _truckSafeZone = new Bounds(new Vector3(0f, -1000f, 0f), Vector3.zero);

        public Bounds TruckSafeZone => _truckSafeZone;

        /// <summary>
        /// 由**持有 HallScene 的一方**（MenuScene）把货车安全区推给组合根。
        /// 为什么用推送而不是让 GameBootstrap 去抓：`GameBootstrap` 没有 HallScene 引用
        /// （全仓只有 `MenuScene.cs:82 HallScene _hall;`），让它去抓等于反向依赖 UI 层、破坏分层。
        /// 推送的是**纯数据**（世界 AABB），玩法层因此不依赖任何几何实现。
        /// </summary>
        public void SetTruckSafeZone(Bounds zone)
        {
            _truckSafeZone = zone;
            // ⚠ 这里**无法**转交给玩法层：`GameBootstrap` 没有 `GameSession` 字段
            // （实测 grep 全仓 `GameSession` 仅 3 处命中，全在 GameSession.cs 自己文件里
            //  —— 即 **GameSession 全仓零实例化**，与"玩法层从未接进对局"是同一件事）。
            // 故此处只保存 Bounds；转交裸浮点给玩法层的逻辑，留到"玩法层接线"那一轮统一做
            // （见 docs/truck-and-session-wiring.md 的 P0 条目）。
            // 玩法层收到的是**裸浮点**而非 Bounds/Vector3：`Whisper.Gameplay` 不引用 UnityEngine。
        }

        /// <summary>玩家是否在货车安全区内（§8：安全区不掉理智、鬼进不来）。</summary>
        public bool PlayerInTruckSafeZone
        {
            get
            {
                if (_player == null) return false;
                var b = TruckSafeZone;
                if (b.size.x <= 0f || b.size.z <= 0f) return false;   // 货车未建 → 不保护（fail-safe）
                float dx = Mathf.Abs(_player.X - b.center.x);
                float dz = Mathf.Abs(_player.Z - b.center.z);
                return dx <= b.size.x * 0.5f && dz <= b.size.z * 0.5f;
            }
        }

        /// <summary>联机装配结果的一句话说明（失败原因也在这里，便于真机 HUD 取证）。</summary>
        public string NetStatus { get; private set; } = "单人模式";

        [Tooltip("配置表资源路径（数值唯一真源）")]
        public string ConfigResourcePath = "Data/config";

        /// <summary>主界面请求开始对局（由 MenuScene 调用）。</summary>
        /// <remarks>
        /// 为什么做成公开方法而不是让 MenuScene 直接操作本对象的字段：
        /// 组合根是**唯一**知道"对局怎么开始"的地方（要关主界面、要保证玩家/怪物/HUD 都就绪）。
        /// 主界面只负责"用户点了什么"，不负责"怎么开局" —— 否则两处都要改。
        /// </remarks>
        public void OnMenuStartRequested()
        {
            // 大厅里选的联机意图在这里才生效（Boot 时装的还是单人桩）——见 ApplyNetIntentAtMatchStart 注释。
            ApplyNetIntentAtMatchStart();
            if (_menu != null) _menu.gameObject.SetActive(false);
            // 猎杀/死亡状态复位：重开一局不能带着上一局的红闪与文字
            if (_jumpscare != null) _jumpscare.Reset();
            // 真正的对局从这里才建（玩家/怪物/跳脸）—— Boot 阶段只到"关卡已加载 + 主界面已出"。
            StartMatch();
        }

        /// <summary>猎杀致死入口（供怪物/网络层调用）：播跳脸。</summary>
        public void OnPlayerKilled(bool redEyes)
        {
            if (_jumpscare == null) return;
            _jumpscare.RedEyes = redEyes;
            _jumpscare.Play();
        }

        /// <summary>HUD/自检用：主界面与跳脸状态。</summary>
        public string DescribeMenuAndScare()
            => (_menu != null ? _menu.Describe() : "主界面：未构建") + " · "
             + (_jumpscare != null ? _jumpscare.Describe() : "跳脸：未构建");

        /// <summary>等级档案（主界面与结算读写）。</summary>
        public Progression Progression => _progression;
        /// <summary>商店（主界面买/装备）。</summary>
        public Shop Shop => _shop;
        /// <summary>任务（主界面任务板）。</summary>
        public TaskSystem Tasks => _tasks;
        /// <summary>渲染质量档位（主界面可切）。</summary>
        public Whisper.Gameplay.Render.RenderQuality Quality => _quality;
        /// <summary>后处理（HUD 取证用）。</summary>
        public PostFx PostFx => _postFx;
        /// <summary>局内任务（合同日志）。
        public ObjectiveSystem Objectives => _objectives;
        /// <summary>电力。</summary>
        public Whisper.Gameplay.Power.PowerSystem Power => _power;
        /// <summary>互动。</summary>
        public Whisper.Gameplay.Interaction.InteractionSystem Interaction => _interaction;

        public LevelData Level { get; private set; }

        /// <summary>
        /// **玩法层**（理智→猎杀→取证→撤离）。第 17 轮发现它此前**全仓零实例化**，
        /// 本类第一次把它接进对局。接线策略见 wire-game-session.mjs 头注释：
        /// 先并行驱动 + 可观测，再逐项替换本类的简化逻辑（避免"接线了但没验证"）。
        /// </summary>
        public Whisper.Gameplay.Session.GameSession Session { get; private set; }

        /// <summary>
        /// 玩法层状态一行文本（**单一口径**：HUD / 诊断面板 / 将来的结算页都读它）。
        /// 为什么必须有它：接线之后如果没人能看见它在跑，就等于没接线 —— 本项目已有
        /// SendLocalPlayer 零调用者、UdpV6NetService 从未构造、MenuScene 键盘检查挂在条件链里
        /// 从未生效这三条同类前科。判据：**理智数字随时间变化**，才证明 Tick 真被调用。
        /// </summary>
        public string SessionStatus
        {
            get
            {
                if (Session == null) return "玩法层：**未接线**（理智/猎杀/撤离不会推进）";
                var so = Session.Outcome;
                return "玩法层 已推进 " + so.ElapsedSeconds.ToString("F1") + "s"
                     + " · 理智 " + (Session.Sanity.Value * 100f).ToString("F1") + "%"
                     + "（" + Session.Sanity.Band.Label + "）"
                     + " · 证据 " + so.EvidenceCollected + "/" + so.EvidenceTotal
                     + " · 阶段 " + Session.Director.Stage
                     + (so.Ended ? " · 已结束" : "");
            }
        }
        /// <summary>配置读取器（与 Progression/Shop/TaskSystem 共用同一份，避免两套配置口径）。</summary>
        Whisper.Gameplay.Config.GameConfigReader _cfg;
        public bool Booted { get; private set; }
        public string LastError { get; private set; }
        /// <summary>Play 循环帧计数（用于证明"真的在跑"，而不是只挂了个组件）。</summary>
        public long Ticks { get; private set; }
        /// <summary>Boot 全流程耗时（毫秒）——真机验收用，证明"启动了"而不是"挂着"。</summary>
        public double BootMs { get; private set; }
        /// <summary>Boot 各阶段的人类可读记录（真机日志取证用：logcat -s Unity）。</summary>
        public string BootLog { get; private set; } = "";

        LevelBuilder _levelBuilder;
        PlayerController _player;
        MonsterViews _monsters;
        /// <summary>温度系统（刺骨寒温证据）。每帧推进；鬼所在房间持续降温。</summary>
        Whisper.Gameplay.Environment.TemperatureSystem _temperature;
        /// <summary>本局天气 id（决定基线室温）。</summary>
        string _weatherId;
        /// <summary>
        /// 本局种子：**天气与刮风都由它派生**，联机时各端必须用同一个值（由 host 下发）。
        /// 默认 0 → `PickIndexForMatch` 走确定性兜底（不依赖任何随机源或时钟，见 `gate-physics` 的两条判红）。
        /// 接入对局流程后，由 `MatchDirector` 在开局时写入这里。
        /// </summary>
        public int MatchSeed { get; set; }
        /// <summary>本局鬼是否带「刺骨寒温」证据（决定它把鬼房降到 [-8,-5] 还是 [-2,5]）。</summary>
        bool _ghostHasFreezingEvidence;
        /// <summary>HUD 用的温度行缓存（0.5s 刷新时算一次，别每帧拼字符串）。</summary>
        string _temperatureLine = "温度：—";
        /// <summary>玩家身体（模型）。</summary>
        PlayerBody _playerBody;
        /// <summary>跳脸视图（猎杀致死时播放）。</summary>
        JumpscareView _jumpscare;
        /// <summary>主界面（代码构建；arch-guard 只允许 Boot.unity 一个场景文件）。</summary>
        MenuScene _menu;
        /// <summary>等级/经验/声望/钱/碎片（恐鬼症对齐 · docs/spec/phasmophobia-alignment.md §3.1）。</summary>
        Progression _progression;
        /// <summary>商店与已装备（§3.2）。</summary>
        Shop _shop;
        /// <summary>每日/每周任务（§3.3）。</summary>
        TaskSystem _tasks;
        /// <summary>电力：单总闸 + 各房间灯（§3.4）。</summary>
        /// <summary>局内任务（每局按本局种子抽 N 条，结算时叠加奖励）。</summary>
        ObjectiveSystem _objectives;
        /// <summary>渲染质量档位（低/高/顶级 + 60/90/120 帧）。</summary>
        Whisper.Gameplay.Render.RenderQuality _quality;
        /// <summary>后处理执行器（挂在主相机上）。</summary>
        PostFx _postFx;
        Whisper.Gameplay.Power.PowerSystem _power;
        /// <summary>互动：鬼开关灯/扔物/敲击/关总闸（§3.5）。</summary>
        Whisper.Gameplay.Interaction.InteractionSystem _interaction;
        /// <summary>关卡灯光（由电力系统驱动；它已有平滑开关过渡，不重复实现）。</summary>
        Whisper.Gameplay.Level.LightRig _lightRig;
        /// <summary>互动节拍用的确定性随机源（禁 UnityEngine.Random；见 gate-physics）。</summary>
        uint _interactRng = 0x1BADB002u;
        float _ghostRoomDwellAcc;
        /// <summary>对局是否已开始（幂等保护：主界面按钮可能被连点）。</summary>
        bool _matchStarted;
        PlayerController _playerControllerRef;
        Text _status;

        /// <summary>
        /// HUD 诊断文字总开关。**默认 false = 不留任何字体**（用户 2026-10-05：「去除所有小字，不留字体」）。
        ///
        /// 为什么必须放在**这里**而不是 MenuScene：左上角那一大块（Tick/接口/关卡/几何着色器/
        /// 玩家/怪物/本局任务/温度/玩法层…）是**本类 Update() 每 0.5s 写 `_status.text`** 渲染的；
        /// 第 10 轮我把开关加在 MenuScene 上，管不到这条路径 —— 0.1.80 真机截图里整块小字照样在。
        ///
        /// 为什么用 SetActive(false) 而不只是清空文本：用户要的是"**不留字体**"（屏幕上不该有调试文字，
        /// 含空壳控件）；且空 Text 仍占位、仍可能留描边残影。
        ///
        /// 为什么保留开关而不是删代码：本项目真机取证**只能走 HUD**（release IL2CPP 下 Debug.Log 时有时无，
        /// 交接 §0.6 已记）。排查时把此字段置 true 即可恢复全部诊断行。
        /// </summary>
        public bool ShowDiagnostics = false;
        Canvas _canvas;
        Camera _camera;
        float _nextHudRefresh;

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
        void Boot()
        {
            var t0 = System.Diagnostics.Stopwatch.StartNew();
            var lines = new System.Text.StringBuilder();
            AppendBootHeader(lines);

            if (!TryLoadConfig(lines)) return;
            if (!TryInstallServices(lines)) return;
            if (!TryLoadLevel(lines)) return;

            // ── 主界面先出：对局**等玩家点「开始调查」再建** ──
            // 为什么必须分开（真机实测）：我第一版让 Boot 一路跑完（建玩家/怪物/HUD）**同时**再建主界面，
            // 结果是"主界面标题与 HUD/走廊叠在一起"—— 两套 UI 同时活着，玩家看到的是混乱。
            // 正解：Boot 到"关卡已加载"为止 → 建主界面 → **停在这里**等 OnMenuStartRequested()。
            // 等级/商店/任务/电力/互动必须在**建主界面之前**就绪：主界面要读它们（否则面板显示"档案未就绪"）。
            InitProgressionSystems(lines);
            BuildMenu(lines);
            // 【可见性】BootLog 原先只在失败路径赋值 → 成功启动时 lines 没人看得到，
            // "玩法层已接线"这类证据等于没留。这里在成功路径也把它固化下来。
            lines.AppendLine(SessionStatus);
            FinishBoot(t0, lines);
        }

        /// <summary>把一段文字同时写进 HUD 与日志（HUD 可能尚未建好，故日志是兜底而不是唯一出路）。</summary>
        void AppendStatus(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Debug.Log("[Whisper] " + text);
            if (_status != null) _status.text = text;
        }

        /// <summary>建主界面（3D 空间 + 闪烁灯 + 右下角手电筒 + 概率刷鬼 + 右侧玩法选项）。</summary>
        /// <summary>每帧推进：鬼互动节拍 + 任务进度（鬼房停留秒数）。</summary>
        /// <summary>
        /// 驱动玩法层（本轮为**并行驱动**：既有简化逻辑照旧跑，玩法层同时推进并暴露状态）。
        /// 玩家位置每帧喂进去 —— 玩法层需要知道玩家在哪（安全区、证据点邻近、房间停留都在用它）。
        /// </summary>
        void TickSession()
        {
            if (Session == null) return;
            if (_player != null) { Session.PlayerX = _player.X; Session.PlayerZ = _player.Z; }
            // 安全区：组合根存的是 Bounds（Runtime 有 Unity），玩法层收**裸浮点**
            // （Whisper.Gameplay 不引用 UnityEngine —— 实测 CS0246）。
            var safe = _truckSafeZone;
            Session.TruckSafeCenterX = safe.center.x;
            Session.TruckSafeCenterZ = safe.center.z;
            Session.TruckSafeSizeX = safe.size.x;
            Session.TruckSafeSizeZ = safe.size.z;
            Session.Tick(Time.deltaTime);
            // 【不要写 _status】曾用 AppendStatus(SessionStatus) 每帧写 _status，那会**覆盖既有 HUD 诊断行**
            // （接口/关卡/玩家/交互次数那套真机取证通道）。改为由 HUD 刷新处统一追加 —— 见 _status.text 的
            // string.Format 里末尾那条 SessionStatus。
        }

        void TickInteractionAndTasks()
        {
            if (_interaction == null || _power == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // 确定性随机源（xorshift32）—— 禁 UnityEngine.Random：它依赖全局种子，跨端不一致
            float Roll()
            {
                _interactRng ^= _interactRng << 13; _interactRng ^= _interactRng >> 17; _interactRng ^= _interactRng << 5;
                return (_interactRng & 0xFFFFFF) / 16777216f;
            }

            // ⚠ `_ghostRoom` 是 `GhostRoom` **对象**，不是字符串（房间 id 在 `RoomId` 属性上）。
            // 我第一版直接把它当 string 用 → CS0029。这类"跨文件字段类型"错误语法预检抓不到，
            // 只有真 Unity 编译会报 —— 所以每轮改动都要过一次 EditMode/出包。
            string ghostRoom = _ghostRoom != null ? _ghostRoom.RoomId : null;
            float gx = 0f, gz = 0f;
            if (_monsters != null && _monsters.LastViews != null && _monsters.LastViews.Length > 0)
            { gx = _monsters.LastViews[0].X; gz = _monsters.LastViews[0].Z; }

            var rooms = new System.Collections.Generic.List<string>();
            if (Level != null) foreach (var r in Level.Rooms) rooms.Add(r.Id);

            _interaction.Tick(dt, Roll, ghostRoom, gx, gz, rooms);

            // 任务：在鬼房停留秒数（用玩家位置与鬼房比对）
            if (_tasks != null && _player != null && !string.IsNullOrEmpty(ghostRoom)
                && RoomIdAt(_player.X, _player.Z) == ghostRoom)
            {
                _ghostRoomDwellAcc += dt;
                if (_ghostRoomDwellAcc >= 1f) { _tasks.ReportGhostRoomDwell(1f); _ghostRoomDwellAcc -= 1f; }
            }
        }

        /// <summary>初始化等级/商店/任务/电力/互动（一次性；Boot 阶段调用）。</summary>
        void InitProgressionSystems(System.Text.StringBuilder lines)
        {
            EnsureConfig();
            var cfg = _cfg;   // 复用唯一那份（EnsureConfig 幂等；不新建第二个 reader）
            _progression = new Progression(cfg);
            _shop = new Shop();
            _shop.Load(cfg);
            _tasks = new TaskSystem(cfg);
            // ⚠ 任务按**日期种子**生成，但 gate-physics 禁止 DateTime 参与玩法判定
            // （跨端不一致）→ 这里用"会话序号"作为 dayIndex 的**可信来源占位**：
            // 联机时它应由网络层同步；单机用 0 表示"今天"。取到官方正文后再决定真实日历口径。
            _tasks.RollForDay(0);
            _power = new Whisper.Gameplay.Power.PowerSystem(cfg);
            InitRenderQuality(cfg);
            _objectives = new ObjectiveSystem(cfg);
            // **主界面就要能看到本局任务**（恐鬼症里合同日志是出发前读的，不是进场后才知道）。
            // 所以这里就用当前 MatchSeed 抽一次；StartMatch 会再按当时的 MatchSeed 抽 ——
            // 同种子幂等，不会换任务，玩家看到的就是进局后的那三条。
            _objectives.BeginContract((uint)MatchSeed ^ 0x5F3759DFu);
            _interaction = new Whisper.Gameplay.Interaction.InteractionSystem(cfg, _power);

            lines.AppendLine(_progression.Describe());
            lines.AppendLine(_shop.Describe());
            lines.AppendLine(_tasks.Describe().Split('\n')[0]);
            lines.AppendLine(_power.Describe());
            lines.AppendLine(_interaction.Describe());
            lines.AppendLine(_objectives.Describe().Split('\n')[0]);
            WirePowerToLights();
            PlaceBreaker();
        }

        /// <summary>
        /// 初始化渲染质量与后处理（用户永久约束 §2/§6）。
        /// </summary>
        /// <remarks>
        /// 三条关键点，每条都是"不这么做就静默失效"：
        /// ① **vSyncCount 必须为 0** —— 移动端 vSync 会覆盖 targetFrameRate，
        ///    不关就会出现"设了 120 却锁在 60"（而且不报错）。
        /// ② **后处理必须挂在同一台相机上**，且 allowHDR 打开（辉光需要 >1 的亮部余量）。
        /// ③ **帧率与画质是两条独立的轴**（用户并列提出）：换画质不该动帧率，反之亦然。
        /// </remarks>
        void InitRenderQuality(Whisper.Gameplay.Config.GameConfigReader cfg)
        {
            _quality = new Whisper.Gameplay.Render.RenderQuality(cfg);
            foreach (var p in _quality.ConfigProblems) Debug.LogWarning("[Whisper] 画质配置：" + p);

            int fps = cfg.Int("render.defaultFrameRate", 60);
            _quality.SelectFrameRate(fps);
            string key = cfg.String("render.defaultTier", "high");
            _quality.Select(key == "low" ? Whisper.Gameplay.Render.QualityTier.Low
                        : key == "top" ? Whisper.Gameplay.Render.QualityTier.Top
                        : Whisper.Gameplay.Render.QualityTier.High);

            if (_camera != null)
            {
                _postFx = _camera.gameObject.GetComponent<PostFx>();
                if (_postFx == null) _postFx = _camera.gameObject.AddComponent<PostFx>();
                _postFx.Quality = _quality;
                _camera.allowHDR = true;   // 辉光要有 >1 的亮部余量，否则高光被截断成死白
            }
            ApplyRenderQuality();
        }

        /// <summary>把当前档位**真的**施加到 Unity 的全局设置上。</summary>
        public void ApplyRenderQuality()
        {
            if (_quality == null) return;
            var t = _quality.CurrentTier;

            Application.targetFrameRate = (int)_quality.FrameRate;
            // vSyncCount=0 是 targetFrameRate 生效的前提（见 InitRenderQuality 的注释①）
            QualitySettings.vSyncCount = 0;

            QualitySettings.pixelLightCount = t.PixelLightCount;
            QualitySettings.shadows = t.Shadows <= 0 ? ShadowQuality.Disable
                                    : t.Shadows == 1 ? ShadowQuality.HardOnly : ShadowQuality.All;
            QualitySettings.shadowResolution = t.ShadowResolution <= 0 ? ShadowResolution.Low
                                             : t.ShadowResolution == 1 ? ShadowResolution.Medium
                                             : t.ShadowResolution == 2 ? ShadowResolution.High
                                             : ShadowResolution.VeryHigh;
            QualitySettings.shadowDistance = t.ShadowDistanceM;
            QualitySettings.antiAliasing = t.AntiAliasing;
            QualitySettings.anisotropicFiltering = t.AnisotropicFiltering <= 0 ? AnisotropicFiltering.Disable
                                                 : t.AnisotropicFiltering == 1 ? AnisotropicFiltering.Enable
                                                 : AnisotropicFiltering.ForceEnable;
            if (_camera != null)
            {
                _camera.farClipPlane = t.FarClipM;
                _camera.allowHDR = true;
            }
            Debug.Log("[Whisper] 画质已施加：" + _quality.Describe());
            // 相机自检：0.1.47 开后处理变黑时，"相机到底在哪、清屏模式是什么"是第一批要问的问题。
            // 这些值平时不打印（日志噪声），只在施加画质时打一次。
            if (_camera != null)
            {
                var cp = _camera.transform.position;
                var cf = _camera.transform.forward;
                Debug.Log(string.Format(
                    "[Whisper] 相机自检：pos=({0:F1},{1:F1},{2:F1}) forward=({3:F2},{4:F2},{5:F2}) clear={6} bg={7} hdr={8} depthTex={9} postFx={10}",
                    cp.x, cp.y, cp.z, cf.x, cf.y, cf.z, _camera.clearFlags, _camera.backgroundColor,
                    _camera.allowHDR, _camera.depthTextureMode, _postFx != null ? _postFx.enabled : false));
            }
        }

        /// <summary>把电力系统接到灯光上：总闸与房间开关的事件 → LightRig 的平滑开关。</summary>
        /// <remarks>
        /// 为什么用事件而不是每帧轮询：LightRig 内部已有 On→Target 的平滑过渡（ToggleSpeed），
        /// 每帧硬设 intensity 会把过渡打掉（灯会"跳"而不是"亮起来"）。
        /// </remarks>
        void WirePowerToLights()
        {
            if (_power == null) return;
            _power.OnBreakerChanged += on =>
            {
                if (_lightRig != null) _lightRig.SetAllLights(on);
            };
            _power.OnRoomLightChanged += (roomId, on) =>
            {
                if (_lightRig != null) _lightRig.SetRoomLights(roomId, on);
            };
        }

        /// <summary>把总闸放到关卡数据给的位置（没有就退回入口区，保证玩家找得到）。</summary>
        void PlaceBreaker()
        {
            if (_power == null || Level == null) return;
            // 关卡里没有专门的"总闸位置"字段 → 用**最深的房间**（离入口最远）作为总闸位置：
            // 这正是官方"必须深入才有电"的意图，而且是从数据推出来的，不是硬编码坐标。
            string far = null; float best = -1f;
            var entrance = Level.Rooms.Find(r => r.Id == (Level.Extraction?.Standard ?? ""));
            float ex = entrance?.MinX ?? 0f, ez = entrance?.MinZ ?? 0f;
            foreach (var r in Level.Rooms)
            {
                float dx = (r.MinX + r.MaxX) * 0.5f - ex, dz = (r.MinZ + r.MaxZ) * 0.5f - ez;
                float d = dx * dx + dz * dz;
                if (d > best) { best = d; far = r.Id; }
            }
            var target = Level.Rooms.Find(r => r.Id == far);
            if (target != null)
            {
                _power.PlaceBreaker(target.Id, (target.MinX + target.MaxX) * 0.5f, (target.MinZ + target.MaxZ) * 0.5f);
            }
            // 每个房间都登记"有灯"（LightRig 是按房间建灯的；没有灯的房间自然收不到 SetRoomLights 的效果）
            foreach (var r in Level.Rooms) _power.RegisterRoomLight(r.Id, true);
            Debug.Log("[Whisper] 总闸放在 " + far + "（离入口最远的房间）· 房间灯 " + Level.Rooms.Count + " 个");
        }


        void BuildMenu(System.Text.StringBuilder lines)
        {
            try
            {
                _menu = gameObject.AddComponent<MenuScene>();
                // **显式把相机交给主界面**：本工程的相机是代码建的且**刻意不设 MainCamera tag**
                // （见 BuildCamera 的注释），所以主界面里用 `Camera.main` 会拿到 null ——
                // 那会导致每次"补建相机"把画面糊掉（真机实测）。
                _menu.Build(transform, _camera, this);
                lines.AppendLine(_menu.Describe());
            }
            catch (System.Exception e)
            {
                // 主界面坏了也必须让 HUD 说明白，而不是黑屏
                lines.AppendLine("主界面：构建失败 —— " + e.Message);
                Debug.LogWarning("[Whisper] 主界面构建失败：" + e);
            }
        }

        /// <summary>真正进入对局：建玩家、怪物、跳脸。由主界面「开始调查」触发（幂等）。</summary>
        /// <summary>联机玩家视图（上行发位姿 + 远端可见）。见 PlayerViews 的类注释。</summary>
        PlayerViews _playerViews;

        /// <summary>HUD/取证用：联机视图摘要（发了多少帧、看到几个远端）。</summary>
        public string DescribePlayerViews() => _playerViews != null ? _playerViews.Describe() : "联机视图：未建";

        /// <summary>理智系统可把 0..1 理智写进来，随位姿一起上报（联机同步用）。</summary>
        public void ReportSanity01(float sanity01)
        {
            if (_playerViews != null) _playerViews.SetSanity01(sanity01);
        }
        public void StartMatch()
        {
            if (_matchStarted) return;
            _matchStarted = true;
            // 局内任务按**本局种子**抽 —— 与天气/风向同一来源（MatchSeed）：联机可复现，
            // 且同一局重复进入不会换任务（BeginContract 对同种子幂等）。
            if (_objectives != null) _objectives.BeginContract((uint)MatchSeed ^ 0x5F3759DFu);
            var lines = new System.Text.StringBuilder();
            try
            {
                var spawn = TryBuildGeometry(lines);
                if (!spawn.HasValue) return;
                if (!TrySpawnPlayer(lines, spawn.Value)) return;
                if (!TrySpawnMonsters(lines)) return;
                AppendStatus(lines.ToString());
                Debug.Log("[Whisper] 对局开始\n" + lines);
            }
            catch (System.Exception e)
            {
                LastError = "对局启动失败：" + e.Message;
                Debug.LogError("[Whisper] " + LastError);
            }
        }

        void AppendBootHeader(System.Text.StringBuilder lines)
        {
            lines.AppendLine("Project Whisper · Boot");
            lines.AppendLine($"Unity {Application.unityVersion} · 60 fps 基线");
            // 着色器自检放在最前：真机上几何上不了色是最容易"看起来像死了"的故障
            // （全黑 + 无报错）。这里先把结论写进 HUD，一眼可辨。
            lines.AppendLine(LevelBuilder.GeometryShader != null
                ? $"几何着色器 ✓ {LevelBuilder.UnlitShaderName}"
                : $"几何着色器 ✗ 缺失（{LevelBuilder.UnlitShaderName}）");
        }

        /// <summary>① 配置表（数值唯一真源）。</summary>
        /// <summary>
        /// 确保全局唯一的配置读取器已建（**幂等**）。
        /// 为什么单独抽出来：启动流程有两处需要它 —— 配置加载与进度系统初始化，
        /// 而**关卡加载夹在中间**（它要建 GameSession）。抽出来 + 在最早的配置加载处调用，
        /// 就不会再出现"接线时 cfg 还是 null"这类依赖顺序事故。
        /// </summary>
        void EnsureConfig()
        {
            if (_cfg == null) _cfg = new Whisper.Gameplay.Config.GameConfigReader();
        }

        bool TryLoadConfig(System.Text.StringBuilder lines)
        {
            var cfgAsset = Resources.Load<TextAsset>(ConfigResourcePath);
            if (cfgAsset == null) { Fail(lines, $"配置表未找到：Resources/{ConfigResourcePath}.json"); return false; }
            try
            {
                GameConfig.LoadFromJson(cfgAsset.text);
                // 【依赖顺序】实例化 reader 供玩法层使用：TryLoadLevel（466 行）里的
                // new GameSession(Level, _cfg, …) 需要它，而它原先只在 InitProgressionSystems
                //（473 行，**晚于**关卡加载）里建 → 接线静默失败（0.1.79 真机 HUD 抓到）。
                EnsureConfig();
                lines.AppendLine($"配置已载入 · tickRate={GameConfig.GetInt("network.tickRate", 60)} · 怪物 {CountMonsters()} 种");
                return true;
            }
            catch (System.Exception ex) { Fail(lines, $"配置表解析失败（{ex.GetType().Name}）：{ex.Message}"); return false; }
        }

        /// <summary>
        /// 按当前 <see cref="NetIntent"/> 建联机服务（幂等，可在 Boot 与开局各调一次）。
        /// 失败**不抛异常**：回落传入的兜底实现，并把原因写进 status（真机 HUD 可读）。
        /// </summary>
        INetService BuildNetForIntent(INetService fallback, out string status)
        {
            var tickRate = GameConfig.GetInt("network.tickRate", 60);
            var batchEvery = GameConfig.GetInt("network.batchEveryTicks", 3);
            var sendHz = GameConfig.GetInt("network.transformSendHz", 10);
            var maxPlayers = GameConfig.GetInt("network.maxPlayers", 4);
            status = "单人模式（本机回环桩）";
            if (NetIntent == NetIntentKind.Host)
            {
                if (LanSession.TryHost(fallback, tickRate, batchEvery, sendHz, maxPlayers,
                                       out var hosted, out var code, out var why))
                {
                    status = $"已建房 · 房间码 {code} · {RoomReachJudge.Describe(LanSession.Reach)}（发给朋友即可加入）";
                    return hosted;
                }
                status = $"建房失败，已回落单人：{why}";
            }
            else if (NetIntent == NetIntentKind.Join)
            {
                if (LanSession.TryJoin(fallback, tickRate, batchEvery, sendHz, maxPlayers, NetJoinCode,
                                       out var joined, out var why))
                {
                    status = $"已加入房间 {LanSession.NetRoomCode} · {RoomReachJudge.Describe(LanSession.Reach)}（等待主机数据）";
                    return joined;
                }
                status = $"加入失败，已回落单人：{why}";
            }
            return fallback;
        }

        /// <summary>
        /// 开局时按**当前**意图重装 net（大厅里改的意图在 Boot 之后才生效，故必须有这一步）。
        /// 类型相符就不动 —— 单人路径因此零开销、行为不变。
        /// </summary>
        void ApplyNetIntentAtMatchStart()
        {
            bool wantReal = NetIntent != NetIntentKind.Solo;
            if (wantReal == LanSession.UsingRealNet) return;   // 意图与现状相符，不折腾
            var fallback = new LocalNetService(GameConfig.GetInt("network.tickRate", 60));
            var net = BuildNetForIntent(fallback, out var status);
            Services.Install(net, replace: true);   // Services 默认拒绝静默覆盖，此处是正当替换
            NetStatus = status;
            AppendStatus(status);
        }
        /// <summary>② 三接口注入（组合根的职责；换 SDK 只换实现，接口与玩法代码不动）。</summary>
        bool TryInstallServices(System.Text.StringBuilder lines)
        {
            try
            {
                var net = BuildNetForIntent(new LocalNetService(GameConfig.GetInt("network.tickRate", 60)), out var netStatus);
                NetStatus = netStatus;
                lines.AppendLine(netStatus);
                Services.Install(net);
                Services.Install(new LocalVoiceService());
                Services.Install(new LocalBackendService());
                Services.Net.OnHostMigration += started =>
                    Debug.Log($"[Whisper] Host 迁移 {(started ? "开始" : "结束")}（V9 §13.4：迁移期播「信号干扰」遮罩）");
                lines.AppendLine(DescribeServices());
                return true;
            }
            catch (System.Exception ex) { Fail(lines, $"接口注入失败（{ex.GetType().Name}）：{ex.Message}"); return false; }
        }

        /// <summary>③ 关卡（Level DSL）。</summary>
        bool TryLoadLevel(System.Text.StringBuilder lines)
        {
            var asset = Resources.Load<TextAsset>(LevelResourcePath);
            if (asset == null) { Fail(lines, $"Level DSL 未找到：Resources/{LevelResourcePath}.json"); return false; }
            try
            {
                Level = LevelLoader.Load(asset.text, LoadKitIds());
                lines.AppendLine($"关卡 {Level.LevelId}：房间 {Level.Rooms.Count} · 走廊 {Level.Corridors.Count} · 事件 {Level.Events.Count}");
                if (Level.Extraction != null)
                    lines.AppendLine($"撤离点：标准 {Level.Extraction.Standard} / 深处 {Level.Extraction.Deep}");
                return true;
            }
            catch (System.Exception ex) { Fail(lines, $"关卡加载失败（{ex.GetType().Name}）：{ex.Message}"); return false; }
        }

        /// <summary>
        /// ④ 几何装配（独立复核 F2b：几何层此前**没接进产品** —— 无场景、LevelBuilder 无人实例化）。
        /// 返回 null 表示已 Fail。
        /// </summary>
        Spawn? TryBuildGeometry(System.Text.StringBuilder lines)
        {
            try
            {
                var builderGo = new GameObject("LevelGeometry");
                builderGo.transform.SetParent(transform, false);
                _levelBuilder = builderGo.AddComponent<LevelBuilder>();
                _levelBuilder.Build(Level, LoadKitIds());
                lines.AppendLine($"几何已装配：房间 {_levelBuilder.RoomObjects.Count} · 门 {_levelBuilder.DoorObjects.Count}"
                    + $" · 道具 {_levelBuilder.PropObjects.Count} · 可走格 {_levelBuilder.Geometry.PassableCount()}");


                // 本局天气：从 8 种里取一种（天气决定基线室温，局与局不同 —— 用户第 7/8 条）。
                //
                // 【为什么不能用引擎自带的随机数生成器】`gate-physics` 判红过这一点，而且它是对的：
                // 天气决定基线室温，而这是 **60 Tick 同步的联机游戏** —— 各端必须选到**同一种**天气，
                // 否则主机的 −1°C 与客户端的 23°C 会对不上（寒温证据、吐寒气全都不同步）。
                // 引擎随机数依赖全局种子、跨端不一致；系统时钟取种子同样不可复现（另一条判红）。
                // 改为**由局种子纯函数派生**：同种子 → 同天气，各端一致，且不需要额外的同步字段。
                // 种子的真源是 `MatchDirector`（它已用同样思路调度事件）；本阶段 MatchSeed=0 走确定性兜底，
                // 接入对局流程后由 MatchDirector 把它下发进来（联机时 host 的值即各端的值）。
                lines.AppendLine(SetupGhostRoom());

                // ── P0 接线：把玩法层实例化进对局（第 17 轮发现此前零实例化）──────────
                // 放在这里的原因：此时 Level 已加载成功、cfg 已就绪、matchSeed 已定。
                // 用**同一个 cfg 与同一个 MatchSeed**，保证与 Progression/TaskSystem 的随机流派一致。
                if (Level != null && _cfg != null)
                {
                    Session = new Whisper.Gameplay.Session.GameSession(Level, _cfg, MatchSeed);
                    lines.AppendLine("玩法层：已接线（GameSession 实例化）· 房间 " + Level.Rooms.Count);
                }
                else
                {
                    // 不静默：接线失败必须看得见（本项目无数次"静默失效"的教训）
                    lines.AppendLine("玩法层：**未接线**（Level 或 cfg 为空）—— 理智/猎杀/撤离不会推进");
                }

                int weatherPick = Whisper.Gameplay.Environment.Weather.PickIndexForMatch(MatchSeed);
                _weatherId = Whisper.Gameplay.Environment.Weather.All[weatherPick];
                bool wind = Whisper.Gameplay.Environment.Weather.WindForMatch(MatchSeed);   // 刮风（用户第 9 条）
                // 本局鬼是否带「刺骨寒温」证据 → 决定鬼房降到 [-8,-5] 还是 [-2,5]
                _ghostHasFreezingEvidence = ResolveGhostFreezingEvidence();
                _temperature = new Whisper.Gameplay.Environment.TemperatureSystem(
                    new Whisper.Gameplay.Config.GameConfigReader(), _weatherId, wind);
                foreach (var room in Level.Rooms) _temperature.RegisterRoom(room.Id);
                lines.AppendLine($"温度系统：登记 {Level.Rooms.Count} 个房间 · 天气 {Whisper.Gameplay.Environment.Weather.Label(_weatherId)}"
                    + $"（基线 {_temperature.BaselineC:0.0}C）{(wind ? " · 刮风" : "")}");

                // 玩家与怪物都从**入口房间的空可走格**出生：房间中心常被家具占用
                // （ward_03 中心就是病床），直接用中心会把角色卡在家具里。
                var start = Level.Rooms.Count > 0 ? Level.Rooms[0] : null;
                var spawn = ResolveSpawn(start, lines);
                // 【终验发现并修复·真机实测】PlaceCamera 本来就把相机朝向了门口，但它在
                // TrySpawnPlayer **之前**执行；而 PlayerController 每帧会用
                // `Quaternion.Euler(-_motion.PitchDeg, _motion.YawDeg, 0)` **覆盖相机旋转**，
                // PlayerMotion 的初始 Yaw 是 0°（+Z）——于是"朝门口"被静默丢弃，
                // 玩家睁眼看到的是 +Z 方向 0.3m 处的墙：真机截屏整屏单色、
                // pixel-region-audit 判 empty（1 种颜色 · 标准差 0.0 · 边缘 0.000%）。
                // 所以这里必须把算出的朝向角交给玩家控制器，两边用**同一个角**。
                //
                // 【2026-10-04 再次修正】朝向不再由 `PlaceCamera`（朝门口）决定，而是用
                // `ResolveSpawn` 里 `SuggestSpawn` 给的**最长视线方向** —— 真机试玩发现
                // "朝门口"在 `entrance_safe`（门在 3m 窄边）等于**朝墙**，出生画面右 2/3 被墙占满。
                // 这里保留 `PlaceCamera` 的**相机摆位**（房间内、有纵深），只采用 spawn 自带的朝向角。
                if (spawn.Ok) PlaceCamera(start, spawn, lines);
                return spawn;
            }
            catch (LevelLoader.LevelValidationException ex) { Fail(lines, "Level DSL 校验失败：" + ex.Message); return null; }
            catch (System.Exception ex) { Fail(lines, $"关卡装载失败（{ex.GetType().Name}）：{ex.Message}"); return null; }
        }

        /// <summary>⑤ 玩家控制（V9 §7）—— 没有移动就不是游戏。纯逻辑在 PlayerMotion（本机断言覆盖）。</summary>
        bool TrySpawnPlayer(System.Text.StringBuilder lines, Spawn spawn)
        {
            if (!spawn.Ok) return true;   // 没有出生点也不该让整个 Boot 失败：HUD 仍要能显示
            try
            {
                var playerGo = new GameObject("Player", typeof(PlayerController));
                playerGo.transform.SetParent(transform, false);
                _player = playerGo.GetComponent<PlayerController>();
                _playerControllerRef = _player;
                var motion = new Whisper.Gameplay.Session.PlayerMotion(
                    new Whisper.Gameplay.Config.GameConfigReader(), spawn.X, spawn.Z);
                // 把几何阶段算好的朝向写进运动状态：PlayerController 每帧用
                // `Quaternion.Euler(-PitchDeg, YawDeg, 0)` 覆盖相机旋转，所以初始朝向
                // **必须**落在 PlayerMotion 上；否则 PlaceCamera 的"朝门口"会被静默丢弃
                // （真机实测：开局朝向 0° → 正前方 0.3m 是墙 → 整屏单色、判 empty）。
                motion.SetYaw(spawn.FacingYawDeg);
                _player.Initialize(motion, _levelBuilder.Geometry, _camera, _status);
                // 门交互（点门开合）需要门扇对象列表做射线判据 → 把 LevelBuilder 也注入
                _player.BindLevelBuilder(_levelBuilder);

                // 玩家身体（第一人称"能看到自己"）：挂在 **Player 根对象**上，不是相机上 ——
                // 相机每帧被 ApplyToTransform 覆写旋转，挂上去身体会跟着贴脸转。
                var body = playerGo.AddComponent<PlayerBody>();
                body.Build(playerGo.transform);
                _playerBody = body;
                lines.AppendLine(body.Describe());

                // 联机玩家视图：上行发本机位姿 + 把远端玩家画出来。
                // 建在 Player 根对象上（与身体同宿主，生命周期一致）。
                // 没有它时联机两端各断一半：我动了对面收不到，对面动了我看不见。
                var views = playerGo.AddComponent<PlayerViews>();
                views.Initialize(_player);
                _playerViews = views;
                lines.AppendLine(views.Describe());

                // ── 跳脸（用户要求：被鬼猎杀时鬼跳脸）──
                // 挂在组合根上、用**相机**做锚点：猎杀判定一成立就 Play()，晚一帧玩家会先看到"我死了但没反应"。
                var js = gameObject.AddComponent<JumpscareView>();
                js.Build(_camera, transform, _status != null ? _status.canvas.transform : null);
                _jumpscare = js;
                lines.AppendLine(js.Describe());
                lines.AppendLine(GhostModelPool.Describe());
                // 【已删除这里的重复建主界面】主界面统一由 BuildMenu 建（Boot 阶段），
                // 这里再建一次会让同一帧存在两个 MenuScene（两个 EventSystem/两套按钮）——
                // 那正是"按钮点不动 + 画面糊"的一个来源。
                lines.AppendLine($"玩家：速度 蹲 {motion.CrouchSpeedMps}/走 {motion.WalkSpeedMps}/跑 {motion.RunSpeedMps} m/s"
                    + " · 左半屏拖拽移动 · 右下角切换蹲行");
                return true;
            }
            catch (System.Exception ex) { Fail(lines, $"玩家控制初始化失败（{ex.GetType().Name}）：{ex.Message}"); return false; }
        }

        /// <summary>⑤b 怪物实例化（V9 §7）—— 在它之前，状态机与听觉判定都完备但**没有任何东西实例化它们**。</summary>
        bool TrySpawnMonsters(System.Text.StringBuilder lines)
        {
            try
            {
                var go = new GameObject("Monsters", typeof(MonsterViews));
                go.transform.SetParent(transform, false);
                _monsters = go.GetComponent<MonsterViews>();
                _monsters.Initialize(_levelBuilder.Geometry, Level, _player, _status);
                // 鬼开关门：注入门扇入口 + 按鬼种读 canOpenDoors（见 MonsterViews.Doors.cs）
                _monsters.BindDoors(_levelBuilder, new Whisper.Gameplay.Config.GameConfigReader());
                lines.AppendLine($"怪物：{_monsters.LastViews.Length} 只已实例化（缝匠/低语者/收殓人）");
                return true;
            }
            catch (System.Exception ex) { Fail(lines, $"怪物实例化失败（{ex.GetType().Name}）：{ex.Message}"); return false; }
        }

        /// <summary>收尾：进入 Play 循环并把摘要打进 logcat（真机验收唯一要 grep 的一行）。</summary>
        void FinishBoot(System.Diagnostics.Stopwatch t0, System.Text.StringBuilder lines)
        {
            Booted = true;
            BootMs = t0.Elapsed.TotalMilliseconds;
            _status.color = HexToColor(DesignTokens.ColorPaper);
            _status.text = lines.ToString();
            BootLog = lines.ToString();
            // 【为什么要把门/温度写进这一行】这些数值以前**只进 HUD 文本、不进日志**，
            // 于是"开局到底开了几扇门""温度系统有没有装配"在真机上**无法核验**——
            // 只能靠肉眼看截图，而那正是本工程反复踩的"汇报与事实不符"。
            // 现在收进同一行，`adb logcat -s Unity:I` 就能直接读。
            var geoSummary = _levelBuilder != null && _levelBuilder.Geometry != null
                ? $" · 门扇 {_levelBuilder.DoorObjects.Count} · 门键 {_levelBuilder.Geometry.DoorCount}"
                  + $" · 洞口 {_levelBuilder.Geometry.DoorOpeningCount}"
                  + $" · 已开 {CountOpenDoors(_levelBuilder.Geometry)}/{_levelBuilder.Geometry.DoorOpeningCount}"
                : "";
            Debug.Log($"[Whisper] BOOT OK · {BootMs:0} ms · 房间 {Level.Rooms.Count} · 门 {_levelBuilder.DoorObjects.Count}"
                + $" · 道具 {_levelBuilder.PropObjects.Count} · 可走格 {_levelBuilder.Geometry.PassableCount()}"
                + geoSummary
                + (_temperature != null ? " · 温度系统已装配" : " · 温度系统未装配")
                + $" · 着色器 {LevelBuilder.UnlitShaderName}"
                + (_player != null ? " · 玩家已就位" : ""));
        }

        int CountMonsters()
        {
            var m = GameConfig.Get("monsters");
            return m is System.Collections.Generic.Dictionary<string, object> map
                ? System.Linq.Enumerable.Count(map.Keys, k => !k.StartsWith("_"))
                : 0;
        }

        /// <summary>套件清单来自 asset-manifest（C3 资产清单驱动）；缺失时返回 null 表示跳过引用校验。</summary>
        System.Collections.Generic.HashSet<string> LoadKitIds()
        {
            var manifest = Resources.Load<TextAsset>("Data/asset-manifest");
            if (manifest == null) return null;
            var root = MiniJson.AsMap(MiniJson.Parse(manifest.text));
            if (!(MiniJson.Get(root, "kits") is System.Collections.Generic.List<object> kits)) return null;
            var set = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            foreach (var k in kits)
            {
                var km = MiniJson.AsMap(k);
                if (MiniJson.Get(km, "id") is string id) set.Add(id);
            }
            return set;
        }

        static string DescribeServices()
        {
            string net = Services.HasNet ? "已注入" : "未注入";
            string voice = Services.HasVoice ? "已注入" : "未注入";
            string backend = Services.HasBackend
                ? (Services.Backend.IsAvailable ? "已注入（可用）" : "已注入（无后端模式，§15.2）")
                : "未注入";
            return $"接口：INetService {net} · IVoiceService {voice} · IBackendService {backend}";
        }

        /// <summary>
        /// 本局的鬼是否带「刺骨寒温」证据。取**任意一只**怪即可 —— 本作同一局只有一种鬼种

        /// <summary>Play 循环：Tick 心跳 + 状态刷新 + 温度系统推进。</summary>
        void Update()
        {
            TickInteractionAndTasks();
            TickSession();

            if (!Booted) return;
            Ticks++;

            // 温度系统每帧推进（用真实 dt，不用 0.5s 的 HUD 节流 —— 温度是连续量，
            // 按 HUD 节流推进会让降温速率随帧率/刷新间隔漂移）。
            if (_temperature != null)
            {
                UpdateGhostRoomForTemperature();
                _temperature.Tick(Time.deltaTime);
            }

            if (Time.unscaledTime < _nextHudRefresh) return;
            _nextHudRefresh = Time.unscaledTime + 0.5f;
            // 【用户要求：去除所有小字，不留字体】关闭时**连写入都不做**，并把容器整个隐藏。
            // 这比"清空文本"彻底：空 Text 仍占位、仍可能留描边/阴影残影。
            if (!ShowDiagnostics)
            {
                if (_status != null && _status.gameObject.activeSelf) _status.gameObject.SetActive(false);
                return;
            }
            if (_status != null && !_status.gameObject.activeSelf) _status.gameObject.SetActive(true);
            _status.text = string.Format(
                "Project Whisper · 运行中\nTick {0} · {1} fps · tickRate={2}\n{3}\n关卡 {4}：房间 {5} · 走廊 {6}"
                + "\n几何着色器 {7} · 相机 {8} · Boot {9:0} ms"
                + "\n{10}\n{11}\n{12}\n{13}\n{14}",
                Ticks, (int)(1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f)),
                Services.HasNet ? Services.Net.TickRate : 0,
                DescribeServices(),
                Level != null ? Level.LevelId : "-",
                Level != null ? Level.Rooms.Count : 0,
                Level != null ? Level.Corridors.Count : 0,
                LevelBuilder.GeometryShader != null ? "✓" : "✗",
                _camera != null ? "✓" : "✗",
                BootMs,
                _player != null ? _player.Describe() : "玩家：—",
                _monsters != null ? _monsters.Describe() : "怪物：—",
                _objectives != null ? _objectives.OneLine() : "本局任务：—",
                DescribeTemperature(),
                // 【真机取证通道】玩法层状态追加在诊断末尾（**不覆盖**任何既有行）。
                // 判据：理智数字随时间变化 = Tick 真在跑，而不是只编译过。
                SessionStatus);
        }

        /// <summary>
        /// Boot 失败路径。真机事故教训：此前失败只是"把错误写进一行不起眼的文本"，
        /// 结果是**全黑屏幕 + 没有任何可读提示**，只能靠连电脑抓 logcat 才知道发生了什么。
        /// 现在失败一律：① 大号红字铺满屏幕 ② BootLog 落盘 ③ Debug.LogError 带统一前缀。
        /// </summary>
        void Fail(System.Text.StringBuilder lines, string message)
        {
            LastError = message;
            Booted = false;
            lines.Insert(0, "⚠ 启动失败 —— 详见下方\n\n");
            lines.AppendLine();
            lines.AppendLine("可能原因：① 着色器/资产未进包 ② Resources 路径写错 ③ 关卡 DSL 校验不通过");
            var body = lines.ToString();
            BootLog = body;
            _status.color = new Color(1f, 0.35f, 0.3f, 1f);
            _status.fontSize = 32;
            _status.text = body;
            Debug.LogError($"[Whisper] BOOT FAILED · {message}\n{body}");
        }
    }

    /// <summary>联机意图（见 <c>GameBootstrap.NetIntent</c> 注释）。</summary>
    public enum NetIntentKind
    {
        /// <summary>单人：本机回环桩，不开 socket（默认）。</summary>
        Solo = 0,
        /// <summary>建房：取本机地址编房间码并监听 UDP。</summary>
        Host = 1,
        /// <summary>加入：按 <c>NetJoinCode</c> 直连主机。</summary>
        Join = 2,
    }
}
