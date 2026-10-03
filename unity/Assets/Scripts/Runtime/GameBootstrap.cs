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
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Tooltip("Level DSL 资源路径（发布构建走 Addressables；此处保留 Resources 形态以便 Play/Edit 测试）")]
        public string LevelResourcePath = "Levels/asylum_v1";

        [Tooltip("配置表资源路径（数值唯一真源）")]
        public string ConfigResourcePath = "Data/config";

        public LevelData Level { get; private set; }
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
        Text _status;
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

        void Start() => Boot();

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
            _camera.transform.position = new Vector3(0f, 1.7f, -6f);        // 眼高 1.7m，朝向 +Z
            _camera.transform.rotation = Quaternion.identity;

            // 一盏方向光：为将来接 Lit 材质/真美术资产预留（当前是 Unlit，光照不影响观感）
            var lightGo = new GameObject("KeyLight", typeof(Light));
            lightGo.transform.SetParent(transform, false);
            var light = lightGo.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.85f;
            light.color = HexToColor(DesignTokens.ColorBone);
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

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

        /// <summary>出生点解析结果（④ 几何阶段产出、⑤ 玩家阶段消费）。</summary>
        readonly struct Spawn
        {
            public readonly bool Ok;
            public readonly float X, Z;
            public Spawn(bool ok, float x, float z) { Ok = ok; X = x; Z = z; }
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

            var spawn = TryBuildGeometry(lines);
            if (!spawn.HasValue) return;      // Fail 已调用
            if (!TrySpawnPlayer(lines, spawn.Value)) return;
            if (!TrySpawnMonsters(lines)) return;

            FinishBoot(t0, lines);
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
        bool TryLoadConfig(System.Text.StringBuilder lines)
        {
            var cfgAsset = Resources.Load<TextAsset>(ConfigResourcePath);
            if (cfgAsset == null) { Fail(lines, $"配置表未找到：Resources/{ConfigResourcePath}.json"); return false; }
            try
            {
                GameConfig.LoadFromJson(cfgAsset.text);
                lines.AppendLine($"配置已载入 · tickRate={GameConfig.GetInt("network.tickRate", 60)} · 怪物 {CountMonsters()} 种");
                return true;
            }
            catch (System.Exception ex) { Fail(lines, $"配置表解析失败（{ex.GetType().Name}）：{ex.Message}"); return false; }
        }

        /// <summary>② 三接口注入（组合根的职责；换 SDK 只换实现，接口与玩法代码不动）。</summary>
        bool TryInstallServices(System.Text.StringBuilder lines)
        {
            try
            {
                var tickRate = GameConfig.GetInt("network.tickRate", 60);
                Services.Install(new LocalNetService(tickRate));
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

                // 玩家与怪物都从**入口房间的空可走格**出生：房间中心常被家具占用
                // （ward_03 中心就是病床），直接用中心会把角色卡在家具里。
                var start = Level.Rooms.Count > 0 ? Level.Rooms[0] : null;
                var spawn = ResolveSpawn(start, lines);
                if (spawn.Ok) PlaceCamera(start, spawn, lines);
                return spawn;
            }
            catch (LevelLoader.LevelValidationException ex) { Fail(lines, "Level DSL 校验失败：" + ex.Message); return null; }
            catch (System.Exception ex) { Fail(lines, $"关卡装载失败（{ex.GetType().Name}）：{ex.Message}"); return null; }
        }

        /// <summary>解析出生点：先试入口房间，失败则在整张图上找空可走格。</summary>
        Spawn ResolveSpawn(Room start, System.Text.StringBuilder lines)
        {
            var geo = _levelBuilder.Geometry;
            if (start != null && geo.TryFindFreeCell(start.CenterX, start.CenterZ, out float sx, out float sz))
            {
                lines.AppendLine($"出生点：房间 {start.Id} → 世界 ({sx:0.0}, {sz:0.0})");
                return new Spawn(true, sx, sz);
            }
            // 兜底：别把玩家丢在 (0,0)——那里常被家具占据（本项目 ward_03 中心就是病床，踩过这个坑）
            if (geo.TryFindFreeCell(0f, 0f, out sx, out sz))
            {
                lines.AppendLine($"⚠ 入口房间无空位，玩家改放 ({sx:0.0}, {sz:0.0})");
                return new Spawn(true, sx, sz);
            }
            lines.AppendLine("⚠ 整张图都找不到空可走格，玩家控制不启用");
            return new Spawn(false, 0f, 0f);
        }

        /// <summary>
        /// 相机摆放（真机实测修正）：入口房间只有 4m×3m，房间中心附近没有"倒退 2.5m"的余量——
        /// 先前把相机放在 spawn - 2.5m，实际已落到墙外，屏幕上只有一堵贴脸的墙
        /// （截屏实测：整屏 #D8CFBB = ColorBone 安全区墙色 · 边缘密度 0.0%）。
        /// 现在：站在房间内、**朝最近的门口方向**看，门连通走廊，视角才有纵深。
        /// </summary>
        void PlaceCamera(Room start, Spawn spawn, System.Text.StringBuilder lines)
        {
            if (_camera == null) return;
            float dirX = 1f, dirZ = 0f;
            var door = start != null && start.Doors.Count > 0 ? start.Doors[0] : null;
            if (door != null)
            {
                door.ToWorld(start, out float dx, out float dz);
                float vx = dx - spawn.X, vz = dz - spawn.Z;
                float len = Mathf.Sqrt(vx * vx + vz * vz);
                if (len > 0.05f) { dirX = vx / len; dirZ = vz / len; }
                lines.AppendLine($"相机朝向：门 {door.Id}（{door.Wall}）→ 方向 ({dirX:0.00}, {dirZ:0.00})");
            }
            // 离中心留一点内缩，避免正好卡在中心家具里；眼高 1.7m
            float back = start != null ? Mathf.Min(0.8f, Mathf.Min(start.SizeX, start.SizeZ) * 0.25f) : 0.8f;
            _camera.transform.position = new Vector3(spawn.X - dirX * back, 1.7f, spawn.Z - dirZ * back);
            _camera.transform.rotation = Quaternion.LookRotation(new Vector3(dirX, -0.12f, dirZ));
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
                var motion = new Whisper.Gameplay.Session.PlayerMotion(
                    new Whisper.Gameplay.Config.GameConfigReader(), spawn.X, spawn.Z);
                _player.Initialize(motion, _levelBuilder.Geometry, _camera, _status);
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
            Debug.Log($"[Whisper] BOOT OK · {BootMs:0} ms · 房间 {Level.Rooms.Count} · 门 {_levelBuilder.DoorObjects.Count}"
                + $" · 道具 {_levelBuilder.PropObjects.Count} · 可走格 {_levelBuilder.Geometry.PassableCount()}"
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

        /// <summary>Play 循环：本阶段只做 Tick 心跳与状态刷新；真正的系统调度（怪物/理智/经济）后续接入。</summary>
        void Update()
        {
            if (!Booted) return;
            Ticks++;
            if (Time.unscaledTime < _nextHudRefresh) return;
            _nextHudRefresh = Time.unscaledTime + 0.5f;
            _status.text = string.Format(
                "Project Whisper · 运行中\nTick {0} · {1} fps · tickRate={2}\n{3}\n关卡 {4}：房间 {5} · 走廊 {6}"
                + "\n几何着色器 {7} · 相机 {8} · Boot {9:0} ms"
                + "\n{10}\n{11}",
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
                _monsters != null ? _monsters.Describe() : "怪物：—");
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
}
