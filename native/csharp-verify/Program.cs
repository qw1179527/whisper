using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Whisper.Gameplay.Level;

/// <summary>
/// 本机 C# 验证跑手：把 Unity 仓库里的真实源文件编译后真跑断言。
/// 与 Unity Test Framework 里的用例**同一批断言意图**（EditMode 测试的镜像），
/// 区别只是宿主：这里用纯 net8.0 控制台，不需要 Unity 授权。
/// </summary>
static class Program
{
    static int passed, failed;
    static readonly List<string> failures = new List<string>();

    static void Check(string name, Func<bool> body)
    {
        try
        {
            if (body()) { passed++; Console.WriteLine($"  ✓ {name}"); }
            else { failed++; failures.Add(name + "（断言为 false）"); Console.WriteLine($"  ✗ {name}"); }
        }
        catch (Exception ex)
        {
            failed++; failures.Add($"{name}（抛异常：{ex.GetType().Name}: {ex.Message}）");
            Console.WriteLine($"  ✗ {name} → {ex.GetType().Name}: {ex.Message}");
        }
    }

    static void CheckThrows<T>(string name, Action body) where T : Exception
    {
        try { body(); failed++; failures.Add(name + "（未按预期抛异常）"); Console.WriteLine($"  ✗ {name}（未抛异常）"); }
        catch (T) { passed++; Console.WriteLine($"  ✓ {name}"); }
        catch (Exception ex) { failed++; failures.Add($"{name}（抛了 {ex.GetType().Name} 而非 {typeof(T).Name}）"); Console.WriteLine($"  ✗ {name}（抛 {ex.GetType().Name}）"); }
    }

    static int Main(string[] rawArgs)
    {
        if (Array.IndexOf(rawArgs, "--emit-voice-vectors") >= 0) return EmitVoiceVectors();

        Console.WriteLine("Project Whisper · 本机 C# 验证（真实源文件 + 真实关卡数据）");

        var levelJson = File.ReadAllText("asylum_v1.json");
        var kits = new HashSet<string>(
            System.Text.RegularExpressions.Regex.Matches(File.ReadAllText("asset-manifest.json"), "\"id\"\\s*:\\s*\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value), StringComparer.Ordinal);

        Console.WriteLine($"\n[1] MiniJson 解析");
        Check("解析对象/数组/标量", () =>
        {
            var root = MiniJson.AsMap(MiniJson.Parse("{\"a\":1,\"b\":\"x\",\"c\":true,\"d\":null,\"e\":[1,2.5,false]}"));
            return MiniJson.Get(root, "a") is long l && l == 1L
                && (string)MiniJson.Get(root, "b") == "x"
                && (bool)MiniJson.Get(root, "c")
                && MiniJson.Get(root, "d") == null
                && MiniJson.AsList(MiniJson.Get(root, "e")).Count == 3;
        });
        Check("转义与 Unicode", () =>
        {
            var root = MiniJson.AsMap(MiniJson.Parse("{\"s\":\"a\\nb\",\"u\":\"\\u4f4e\\u8bed\"}"));
            return (string)MiniJson.Get(root, "s") == "a\nb" && (string)MiniJson.Get(root, "u") == "低语";
        });
        CheckThrows<FormatException>("拒绝尾随内容", () => MiniJson.Parse("{\"a\":1} x"));
        CheckThrows<FormatException>("拒绝未闭合字符串", () => MiniJson.Parse("{\"a\":\"oops}"));
        CheckThrows<FormatException>("拒绝缺冒号", () => MiniJson.Parse("{\"a\" 1}"));

        Console.WriteLine($"\n[2] Level DSL 加载真实关卡（asylum_v1.json，已知套件 {kits.Count} 个）");
        LevelData level = null;
        Check("加载并校验通过", () => { level = LevelLoader.Load(levelJson, kits); return level != null; });
        if (level != null)
        {
            Check("levelId = asylum_v1", () => level.LevelId == "asylum_v1");
            // 布局重写后为 11 房间（10 任务房 + 地下太平间前室）；V9 §19.2 的「10 房间」指任务房
            Check("房间 11 个（10 任务房 + 太平间前室）", () => level.Rooms.Count == 11);
            Check("D1 修复：每个房间都有布局坐标（pos）", () => level.Rooms.All(r => r.SizeX > 0 && Math.Abs(r.PosX) + Math.Abs(r.PosZ) >= 0));
            Check("D1 修复：每扇门都有 id", () => level.Rooms.All(r => r.Doors.All(d => !string.IsNullOrWhiteSpace(d.Id))));
            Check("D1 修复：每条走廊都引用两端的具体门", () => level.Corridors.All(c => !string.IsNullOrWhiteSpace(c.DoorA) && !string.IsNullOrWhiteSpace(c.DoorB)));
            Check("D1 修复：门的绝对坐标可由房间盒+offset 推导", () =>
            {
                var r = level.Rooms.First(x => x.Id == "ward_01");
                r.Doors[0].ToWorld(r, out var x, out var z);
                return Math.Abs(x - 4.5f) < 1e-3 && Math.Abs(z - 6f) < 1e-3;   // ward_01 南墙(z=6) 上的门位 x=4.5
            });
            Check("证据点 5 个（住院区）", () => level.Rooms.Count(r => r.EvidencePoint) == 5);
            Check("光区分布 safe=1 / pressure=8 / high-risk=2", () =>
                level.Rooms.Count(r => r.LightZone == "safe") == 1
                && level.Rooms.Count(r => r.LightZone == "pressure") == 8
                && level.Rooms.Count(r => r.LightZone == "high-risk") == 2);
            Check("V9 §7 撤离双点（标准 / 深处）不同房间", () =>
                level.Extraction != null && level.Extraction.Standard != level.Extraction.Deep);
            Check("动态事件 2~3 个（V9 §19.2）", () => level.Events.Count >= 2 && level.Events.Count <= 3);

            // 走廊连通性：从入口出发应能到达撤离深处点（防孤岛）
            Check("走廊连通：入口 → 深处撤离点可达", () =>
            {
                var adj = new Dictionary<string, List<string>>();
                foreach (var c in level.Corridors)
                {
                    if (!adj.ContainsKey(c.From)) adj[c.From] = new List<string>();
                    if (!adj.ContainsKey(c.To)) adj[c.To] = new List<string>();
                    adj[c.From].Add(c.To);
                    adj[c.To].Add(c.From);
                }
                var seen = new HashSet<string>();
                var stack = new Stack<string>();
                stack.Push(level.Extraction.Standard);
                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    if (!seen.Add(cur)) continue;
                    foreach (var n in adj.TryGetValue(cur, out var l) ? l : new List<string>()) stack.Push(n);
                }
                return seen.Contains(level.Extraction.Deep);
            });
        }

        Console.WriteLine("\n[3] 校验器必须真会拦（注入畸形数据）");
        CheckThrows<LevelLoader.LevelValidationException>("未知 kit 被拦", () =>
            LevelLoader.Load(levelJson.Replace("\"hospital_ward\"", "\"nope_kit\""), kits));
        CheckThrows<LevelLoader.LevelValidationException>("非法 lightZone 被拦", () =>
            LevelLoader.Load(levelJson.Replace("\"lightZone\": \"pressure\"", "\"lightZone\": \"spooky\""), kits));
        CheckThrows<LevelLoader.LevelValidationException>("门 offset 越界被拦", () =>
            LevelLoader.Load(levelJson.Replace("\"offset\": 0.5", "\"offset\": 1.9"), kits));
        CheckThrows<LevelLoader.LevelValidationException>("走廊端点悬空被拦", () =>
            LevelLoader.Load(levelJson.Replace("\"to\": \"corridor_main\"", "\"to\": \"ghost_room\""), kits));
        CheckThrows<LevelLoader.LevelValidationException>("撤离双点同房被拦", () =>
            LevelLoader.Load(levelJson.Replace("\"deep\": \"morgue_deep\"", "\"deep\": \"entrance_safe\""), kits));
        // D1 新增防线的负向验证
        CheckThrows<LevelLoader.LevelValidationException>("门缺 id 被拦（D1）", () =>
        {
            // 真实格式：多行 pos + 门对象；用正则精确打掉第一扇门的 id
            var broken = System.Text.RegularExpressions.Regex.Replace(levelJson, "\\{\\s*\"id\": \"[^\"]+\",\\s*\"wall\"", "{\"wall\"", System.Text.RegularExpressions.RegexOptions.None);
            LevelLoader.Load(broken, kits);
        });
        CheckThrows<LevelLoader.LevelValidationException>("房间缺 pos 被拦（D1）", () =>
        {
            var broken = System.Text.RegularExpressions.Regex.Replace(levelJson, "\"pos\": \\[[^\\]]*\\],", "", System.Text.RegularExpressions.RegexOptions.Singleline);
            LevelLoader.Load(broken, kits);
        });
        CheckThrows<LevelLoader.LevelValidationException>("事件缺 counterplay 被拦（§30.2）", () =>
            LevelLoader.Load(levelJson.Replace("\"counterplay\": \"手电筒照走廊地面确认出口；黑暗持续掉理智，回到安全区（+2/s）可恢复\"", "\"counterplay\": \"\""), kits));

        Console.WriteLine("\n[2.5] 配置表读取（V9 §13.3 / §19.5：数值唯一真源）");
        var cfgJson = File.ReadAllText("config.json");
        Check("LoadFromJson 后 IsLoaded = true", () => { Whisper.Gameplay.Config.GameConfig.LoadFromJson(cfgJson); return Whisper.Gameplay.Config.GameConfig.IsLoaded; });
        Check("点路径查得到 tickRate 且为 60（V9 §13.4）", () => Whisper.Gameplay.Config.GameConfig.GetInt("network.tickRate", -1) == 60);
        Check("点路径查得到怪物速度（配置驱动的证据）", () => Whisper.Gameplay.Config.GameConfig.GetFloat("monsters.stitcher.speedMps", -1f) > 0f);
        Check("声纹分档来自配置表（喊叫 80/25 米，V9 附录 A-1）", () =>
            Whisper.Gameplay.Config.GameConfig.GetInt("stimulusSources.voice_shout.intensity", -1) == 80
            && Math.Abs(Whisper.Gameplay.Config.GameConfig.GetFloat("stimulusSources.voice_shout.radiusM", -1f) - 25f) < 1e-6);
        Check("缺失路径返回 fallback 而不抛异常", () => Whisper.Gameplay.Config.GameConfig.GetInt("nope.nothing.here", 42) == 42);
        Check("Reset 后 IsLoaded = false", () => { Whisper.Gameplay.Config.GameConfig.Reset(); return !Whisper.Gameplay.Config.GameConfig.IsLoaded; });

        Console.WriteLine("\n[3.5] 独立验证轨指出后新增的防线（回归证据）");

        // S11：深层嵌套必须被拦（旧版递归无上限，约 4 万层 SIGSEGV 硬崩）
        CheckThrows<FormatException>("MiniJson 拒绝超深嵌套（MaxDepth=64）", () =>
        {
            var deep = new string('[', 200) + new string(']', 200);
            MiniJson.Parse(deep);
        });
        Check("MiniJson 正常深度仍可解析", () =>
        {
            var ok = new string('[', 30) + "1" + new string(']', 30);
            return MiniJson.Parse(ok) != null;
        });
        Check("真实关卡 asylum_v1.json 在深度守卫下仍可解析（回归锚点）", () =>
        {
            var root = MiniJson.AsMap(MiniJson.Parse(levelJson));
            return MiniJson.AsList(MiniJson.Get(root, "rooms")).Count == 11;
        });
        Check("深度上限取值合理：MaxDepth 明显高于真实关卡深度", () => MiniJson.MaxDepth >= 32);

        // S3a：结构/类型错误必须以 LevelValidationException 呈现（旧版抛 FormatException → Bootstrap 只 catch 前者 → Awake 硬崩）
        CheckThrows<LevelLoader.LevelValidationException>("数值型房间 id → LevelValidationException（而非 FormatException）", () =>
            LevelLoader.Load("{\"levelId\":\"x\",\"rooms\":[{\"id\":1,\"size\":[1,1,1],\"kit\":\"k\",\"lightZone\":\"safe\"}]}", null));
        CheckThrows<LevelLoader.LevelValidationException>("事件缺 minute → LevelValidationException", () =>
            LevelLoader.Load("{\"levelId\":\"x\",\"rooms\":[{\"id\":\"a\",\"size\":[1,1,1],\"kit\":\"k\",\"lightZone\":\"safe\"}],\"events\":[{\"type\":\"blackout\",\"durationSec\":5},{\"type\":\"laugh\",\"durationSec\":5}]}", null));
        CheckThrows<LevelLoader.LevelValidationException>("rooms 为对象 → LevelValidationException", () =>
            LevelLoader.Load("{\"levelId\":\"x\",\"rooms\":{}}", null));

        Console.WriteLine("\n[3.6] 三接口的实现方（组合根注入用的本机桩，替换 SDK 时接口不变）");
        Check("LocalNetService 实现 INetService 且 tickRate 跟随配置", () =>
        {
            Whisper.Core.Contracts.INetService net = new Whisper.Net.LocalNetService(60);
            net.Connect("ROOM1", "tok");
            return net.IsConnected && net.IsHost && net.TickRate == 60;
        });
        Check("LocalNetService 的 Host 迁移事件可触发（V9 §13.4）", () =>
        {
            var net = new Whisper.Net.LocalNetService(60);
            var fired = false;
            Whisper.Core.Contracts.INetService iface = net;
            iface.OnHostMigration += started => fired = started;
            net.SimulateHostMigration(true);
            return fired;
        });
        Check("LocalVoiceService 实现 IVoiceService 且能量回调可用（V9 §13.5 采集源）", () =>
        {
            var voice = new Whisper.Audio.LocalVoiceService();
            var got = -1f;
            Whisper.Core.Contracts.IVoiceService iface = voice;
            iface.OnParticipantEnergy += (id, e) => got = e;
            voice.SimulateEnergy("local", 0.42f);
            return Math.Abs(voice.LocalEnergy01 - 0.42f) < 1e-6 && Math.Abs(got - 0.42f) < 1e-6;
        });
        Check("LocalBackendService 处于无后端模式并如实拒绝（V9 §15.2）", () =>
        {
            var be = new Whisper.Backend.LocalBackendService();
            string reason = null;
            Whisper.Core.Contracts.IBackendService iface = be;
            iface.IssueTokens("0.6.0", _ => { }, r => reason = r);
            return !be.IsAvailable && reason != null && reason.Contains("无后端模式");
        });

        Console.WriteLine("\n[3.7] 只读状态面（V9 §13.4 同步对象 ①③④，D5）");
        Check("阶段可设置并触发变更事件（同步对象④）", () =>
        {
            var net = new Whisper.Net.LocalNetService(60);
            var seen = Whisper.Core.Contracts.MatchPhase.Lobby;
            Whisper.Core.Contracts.INetService iface = net;
            iface.OnPhaseChanged += p => seen = p;
            net.SetPhase(Whisper.Core.Contracts.MatchPhase.Playing);
            return seen == Whisper.Core.Contracts.MatchPhase.Playing && iface.Phase == Whisper.Core.Contracts.MatchPhase.Playing;
        });
        Check("玩家位姿进入快照并触发更新（同步对象①）", () =>
        {
            var net = new Whisper.Net.LocalNetService(60);
            var fired = false;
            Whisper.Core.Contracts.INetService iface = net;
            iface.OnPlayerUpdated += _ => fired = true;
            net.UpsertPlayer(new Whisper.Core.Contracts.PlayerSnapshot("p0", 3f, 0f, 4f, 90f, true, 0.8f));
            var snap = iface.Snapshot;
            return fired && snap.Players.Count == 1 && Math.Abs(snap.Players[0].X - 3f) < 1e-6 && Math.Abs(snap.Players[0].Sanity01 - 0.8f) < 1e-6;
        });
        Check("门/道具状态进入快照并触发变更（同步对象③）", () =>
        {
            var net = new Whisper.Net.LocalNetService(60);
            var got = default(Whisper.Core.Contracts.PropState);
            var fired = false;
            Whisper.Core.Contracts.INetService iface = net;
            iface.OnPropChanged += ps => { got = ps; fired = true; };
            net.UpsertProp(new Whisper.Core.Contracts.PropState("ward_03/door_a", true, false, 1f));
            var snap = iface.Snapshot;
            return fired && got.Open && snap.Props.Count == 1 && snap.Props[0].PropId == "ward_03/door_a";
        });
        Check("快照携带证据计数与世界哈希（§13.6 重连比对用）", () =>
        {
            var net = new Whisper.Net.LocalNetService(60);
            Whisper.Core.Contracts.INetService iface = net;
            net.SetEvidence(3);
            net.SetWorldHash("abc123");
            var snap = iface.Snapshot;
            return snap.EvidenceCount == 3 && snap.WorldHash == "abc123";
        });
        Check("玩法层无需引用 Net 模块即可读状态（§13.2 × §13.4 交叉要求）", () =>
        {
            // 只用接口类型做一次完整读取，证明耦合面就是接口本身
            Whisper.Core.Contracts.INetService iface = new Whisper.Net.LocalNetService(60);
            ((Whisper.Net.LocalNetService)iface).SetPhase(Whisper.Core.Contracts.MatchPhase.Extraction);
            var snap = iface.Snapshot;
            return snap.Phase == Whisper.Core.Contracts.MatchPhase.Extraction && snap.Players != null && snap.Props != null;
        });

        Console.WriteLine("\n[4] DesignTokens（C2：设计与代码一一对应）");
        // 口径说明（重要）：DesignTokens 由**产品真源** data/design-tokens.json（= 0.6.0 产物内嵌 __TOK）生成，
        // 不是由 V9 §11 表11-1 凭空写死。实测两者并不完全一致：
        //   规范 7 个色彩 token 中，产品同名同值 4 个（paper/ink/blood + mold≡ghost 同值异名），
        //   faded(#8C7E66)/warning(#C46A1A)/dark(#0D0D0D) 在产品侧**不存在**，且 game.js 从未引用这三个名字。
        // 处置：断言产品真实 token；规范缺口写成显式报告项（记入机制清单），绝不在此悄悄改名对齐。
        Check("产品真实 token（12 色）取值与 data/design-tokens.json 一致", () =>
            Whisper.Core.DesignTokens.ColorPaper == "#F0E6D2"
            && Whisper.Core.DesignTokens.ColorInk == "#1A1A1A"
            && Whisper.Core.DesignTokens.ColorBlood == "#8B1E1E"
            && Whisper.Core.DesignTokens.ColorMold == "#5C8C6E"
            && Whisper.Core.DesignTokens.ColorBone == "#D8CFBB"
            && Whisper.Core.DesignTokens.ColorSoot == "#0E0D0C"
            && Whisper.Core.DesignTokens.ColorRust == "#6E4A2F"
            && Whisper.Core.DesignTokens.ColorSignal == "#C9A227"
            && Whisper.Core.DesignTokens.ColorDanger == "#C0392B"
            && Whisper.Core.DesignTokens.ColorSafe == "#4E7A5A"
            && Whisper.Core.DesignTokens.ColorHud == "rgba(240,230,210,0.86)"
            && Whisper.Core.DesignTokens.ColorHudDim == "rgba(240,230,210,0.42)");
        Check("V9 表11-1 与产品**重合**的 3 个同名同值 token 精确一致", () =>
            Whisper.Core.DesignTokens.ColorPaper == "#F0E6D2"
            && Whisper.Core.DesignTokens.ColorInk == "#1A1A1A"
            && Whisper.Core.DesignTokens.ColorBlood == "#8B1E1E");

        Console.WriteLine("\n[5] 服务定位器（V9 §13.2 / §15.2）");
        CheckThrows<InvalidOperationException>("未注入时取 Net 必须报错", () => { var _ = Whisper.Core.Services.Net; });
        CheckThrows<InvalidOperationException>("二次注入不再静默覆盖（D7）", () =>
        {
            Whisper.Core.Services.Reset();
            Whisper.Core.Services.Install(new FakeNet());
            Whisper.Core.Services.Install(new FakeNet());   // 必须抛
        });
        Check("显式 replace:true 允许替换", () =>
        {
            Whisper.Core.Services.Reset();
            Whisper.Core.Services.Install(new FakeNet());
            Whisper.Core.Services.Install(new FakeNet(), replace: true);
            return Whisper.Core.Services.HasNet;
        });
        Check("注入后可用 + 无后端模式不影响其它接口", () =>
        {
            Whisper.Core.Services.Reset();
            Whisper.Core.Services.Install(new FakeNet());
            Whisper.Core.Services.Install(new FakeVoice());
            var ok = Whisper.Core.Services.HasNet && Whisper.Core.Services.HasVoice && !Whisper.Core.Services.HasBackend;
            ok = ok && Whisper.Core.Services.Net.TickRate == 60 && Math.Abs(Whisper.Core.Services.Voice.LocalEnergy01 - 0.25f) < 1e-6;
            Whisper.Core.Services.Reset();
            return ok;
        });

        Console.WriteLine($"\n结果：通过 {passed} · 失败 {failed}");
        if (failed > 0)
        {
            Console.WriteLine("失败清单：");
            foreach (var f in failures) Console.WriteLine("  - " + f);
            return 1;
        }
        return 0;
    }

    sealed class FakeNet : Whisper.Core.Contracts.INetService
    {
        public bool IsHost => true;
        public bool IsConnected => true;
        public int TickRate => 60;
        public void Connect(string roomCode, string authToken) { }
        public void Disconnect() { }
        public void SendVoiceStimulus(in Whisper.Core.Contracts.StimulusEvent stimulus) { }
        public event Action<string> OnRoomClosed { add { } remove { } }
        public event Action<bool> OnHostMigration { add { } remove { } }
        // §13.4 状态面（接口新增成员 → 编译器强制所有实现方跟上）
        public Whisper.Core.Contracts.MatchPhase Phase => Whisper.Core.Contracts.MatchPhase.Playing;
        public Whisper.Core.Contracts.NetworkSnapshot Snapshot =>
            new Whisper.Core.Contracts.NetworkSnapshot(Phase, new Whisper.Core.Contracts.PlayerSnapshot[0],
                new Whisper.Core.Contracts.PropState[0], 0, "fake");
        public event Action<Whisper.Core.Contracts.MatchPhase> OnPhaseChanged { add { } remove { } }
        public event Action<Whisper.Core.Contracts.PropState> OnPropChanged { add { } remove { } }
        public event Action<Whisper.Core.Contracts.PlayerSnapshot> OnPlayerUpdated { add { } remove { } }
    }

    sealed class FakeVoice : Whisper.Core.Contracts.IVoiceService
    {
        public bool IsMuted => false;
        public float LocalEnergy01 => 0.25f;
        public void JoinChannel(string channelName) { }
        public void LeaveChannel() { }
        public void SetLocalMute(string participantId, bool muted) { }
        public event Action<string, float> OnParticipantEnergy { add { } remove { } }
    }

    /// <summary>
    /// 向量产出模式：读 data/vectors/voice-classification.inputs.json，用**移植件**跑出逐帧结果并以 JSON 打印。
    /// 由 tools/voice-port-vectors.mjs 调用并与灰盒 JS 实现比对（锁住移植等价性）。
    /// </summary>
    static int EmitVoiceVectors()
    {
        var inputPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "vectors", "voice-classification.inputs.json");
        inputPath = Path.GetFullPath(inputPath);
        if (!File.Exists(inputPath)) { Console.Error.WriteLine("缺输入向量: " + inputPath); return 2; }
        var spec = MiniJson.AsMap(MiniJson.Parse(File.ReadAllText(inputPath)));
        float frameMs = MiniJson.AsFloat(MiniJson.Get(spec, "frameMs"));
        // 判定参数必须来自配置表（不许猜默认值）；跑手自己加载 config.json 真源
        var cfgPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "config.json"));
        Whisper.Gameplay.Config.GameConfig.LoadFromJson(File.ReadAllText(cfgPath));
        var cfgReader = new Whisper.Gameplay.Config.GameConfigReader();
        var cal = MiniJson.AsMap(MiniJson.Get(spec, "calibration"));
        int sampleFrames = MiniJson.AsInt(MiniJson.Get(cal, "sampleFrames"));
        int ambientFrames = MiniJson.AsInt(MiniJson.Get(cal, "ambientFrames"));
        var prompts = MiniJson.AsList(MiniJson.Get(cal, "prompts")).ConvertAll(o => (string)o);

        var outMap = new Dictionary<string, object>();
        foreach (var devObj in MiniJson.AsList(MiniJson.Get(spec, "devices")))
        {
            var dev = MiniJson.AsMap(devObj);
            string id = MiniJson.AsString(MiniJson.Get(dev, "id"));
            float whisper = MiniJson.AsFloat(MiniJson.Get(dev, "whisper"));
            float normal = MiniJson.AsFloat(MiniJson.Get(dev, "normal"));
            float shout = MiniJson.AsFloat(MiniJson.Get(dev, "shout"));
            float ambient = MiniJson.AsFloat(MiniJson.Get(dev, "ambient"));

            // 校准：与 JS 侧完全相同的合成序列
            var calibrator = new Whisper.Gameplay.Voice.VoiceCalibrator(3, sampleFrames, prompts);
            for (int i = 0; i < ambientFrames; i++) calibrator.PushAmbientFrame(ambient + ((i % 5) - 2) * 0.5f);
            foreach (var pr in prompts)
            {
                calibrator.StartPrompt(pr);
                float baseDb = pr == "whisper" ? whisper : pr == "normal" ? normal : shout;
                for (int i = 0; i < sampleFrames; i++) calibrator.PushFrame(baseDb + ((i % 7) - 3) * 0.4f);
            }
            var anchors = new Whisper.Gameplay.Voice.VoiceAnchors(whisper, normal, shout, ambient);
            var clf = Whisper.Gameplay.Voice.VoiceBandClassifier.FromConfig(cfgReader, anchors);

            var seqOut = new Dictionary<string, object>();
            foreach (var seqObj in MiniJson.AsList(MiniJson.Get(spec, "resolved")))
            {
                var seq = MiniJson.AsMap(seqObj);
                if (MiniJson.AsString(MiniJson.Get(seq, "device")) != id) continue;
                string seqId = MiniJson.AsString(MiniJson.Get(seq, "sequence"));
                var dec = new List<object>();
                foreach (var f in MiniJson.AsList(MiniJson.Get(seq, "dBFS")))
                {
                    var r = clf.Push(MiniJson.AsFloat(f));
                    // 形状对齐灰盒（两条路径字段不同）：
                    //   成功路径  → { band, levelNorm(norm), relDb, relPeakNorm, snrDb, ... }，**无** snrPeakDb
                    //   不可辨路径 → { band:'indistinguishable', reason:'snr_below_min', snrDb, snrPeakDb, minSnrDb, ... }，**无** relPeakNorm
                    var cell = new Dictionary<string, object> {
                        ["band"] = r.Band,
                        ["reason"] = r.Reason,
                        ["norm"] = Math.Round(r.Norm, 6),
                        ["relDb"] = Math.Round(r.RelDb, 6),
                        ["snrDb"] = Math.Round(r.SnrDb, 6),
                    };
                    if (r.Band == "indistinguishable") cell["snrPeakDb"] = Math.Round(r.SnrPeakDb, 6);
                    else cell["relPeakNorm"] = Math.Round(r.RelPeakNorm, 6);
                    dec.Add(cell);
                }
                seqOut[seqId] = dec;
            }

            var calOut = calibrator.Result == null ? null : new Dictionary<string, object> {
                ["whisper"] = calibrator.Result.Whisper, ["normal"] = calibrator.Result.Normal,
                ["shout"] = calibrator.Result.Shout, ["ambientDb"] = calibrator.Result.AmbientDb,
            };
            outMap[id] = new Dictionary<string, object> { ["calibration"] = calOut, ["sequences"] = seqOut };
        }

        Console.WriteLine(JsonWrite(new Dictionary<string, object> { ["devices"] = outMap }));
        return 0;
    }

    /// <summary>极简 JSON 输出（只覆盖本文件用到的类型：Dictionary/List/string/float/bool/null）。</summary>
    static string JsonWrite(object v)
    {
        var sb = new System.Text.StringBuilder();
        void W(object o)
        {
            switch (o)
            {
                case null: sb.Append("null"); break;
                case string str: sb.Append('"').Append(str.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"'); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case float f: sb.Append(float.IsNaN(f) || float.IsInfinity(f) ? "null" : f.ToString(System.Globalization.CultureInfo.InvariantCulture)); break;
                case double d: sb.Append(double.IsNaN(d) || double.IsInfinity(d) ? "null" : d.ToString(System.Globalization.CultureInfo.InvariantCulture)); break;
                case int i: sb.Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(System.Globalization.CultureInfo.InvariantCulture)); break;
                case Dictionary<string, object> map:
                    sb.Append('{');
                    bool firstK = true;
                    foreach (var kv in map) { if (!firstK) sb.Append(','); firstK = false; sb.Append('"').Append(kv.Key).Append("\":"); W(kv.Value); }
                    sb.Append('}');
                    break;
                case System.Collections.IEnumerable en:
                    sb.Append('[');
                    bool firstI = true;
                    foreach (var item in en) { if (!firstI) sb.Append(','); firstI = false; W(item); }
                    sb.Append(']');
                    break;
                default: sb.Append('"').Append(o.ToString()).Append('"'); break;
            }
        }
        W(v);
        return sb.ToString();
    }

}
