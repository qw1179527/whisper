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
        /// <summary>
        /// **诊断显示总开关**（默认关）。开启后同时显示：
        ///   · HUD 状态小字（`Update` 里的 `_status` 文本）
        ///   · **机内诊断叠层**（`GameBootstrap.BootOverlay.cs` 的 `OnGUI`：
        ///     启动日志/错误/管线名/地图列表 + 一个"开始对局"按钮）
        /// 【为什么合并成一个开关】真机黑屏时两处都要看（小字给运行态、叠层给启动日志）；
        /// 分成两个开关会让"开了这个没开那个"变成新的排查成本。
        /// 【为什么默认关】用户要求去掉所有小字；诊断信息只在排查时需要。
        /// 【怎么在真机上打开】左上角连点 5 次（黑屏时无反馈手势）——见 BootOverlay 类注释。
        /// </summary>
        [Tooltip("诊断显示总开关（HUD 小字 + 机内诊断叠层）。真机排查时开；可在左上角连点 5 次切换。")]
        // ⚠ **排查构建期间默认 true**（2026-10-07 真机黑屏）。
        // 定稿前改回 false —— 用户明确要求"去掉所有小字"。
        public bool ShowDiagnostics = true;
        Canvas _canvas;
        Camera _camera;
        float _nextHudRefresh;



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

                // ── **把 render 段真正应用到引擎**（2026-10-07）────────────────────────
                // 此前 `render.tiers`（两档 × 约 22 个旋钮）与 `frameRates` **从未被读过**：
                //   `Application.targetFrameRate` 全仓 0 处、`renderScale` 0 处。
                // ⇒ 帧率、分辨率缩放、各向异性过滤这些旋钮一直是"配了但没生效"。
                // 放在配置载入之后、关卡之前：此时 cfg 可用，且早于任何渲染设置被消费。
                EnsureConfig();   // 是 void（只为建 reader）⇒ 必须先调、再传 _cfg
                int applied = _cfg != null ? RenderTierApplier.Apply(_cfg) : 0;
                lines.AppendLine($"渲染档已应用：{RenderTierApplier.LastApplied}"
                    + (applied == 0 ? "  ⚠ **0 项** —— 配置没读到，渲染设置全部是硬编码默认值" : ""));
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
                    // ── 跳脸接线（2026-10-06）────────────────────────────────────────
                    // 【为什么必须显式订阅】`JumpscareView` 与 `OnPlayerKilled` 早就存在，
                    // 但全仓**零调用者** —— 跳脸永远不会播。这正是本项目记录在案的失效模式
                    // （工具/系统做完却没人引用：几何层、内容管线、怪物实例化、10 个大厅道具）。
                    // 纯逻辑层（GameSession）不能引用 Unity 视图，所以走事件；订阅放在这里，
                    // 因为它是**组合根**：只有它同时知道 Session 与 JumpscareView。
                    Session.PlayerKilled += OnPlayerCaught;
                    lines.AppendLine("跳脸接线：GameSession.PlayerKilled → JumpscareView ✓（被追击杀到即播）");
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
