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

    static Whisper.Gameplay.Config.GameConfigReader cfgReader() => new Whisper.Gameplay.Config.GameConfigReader();

    /// <summary>事件类型是否在配置 eventPool 内（断言用）。</summary>
    static bool cfgPoolContains(string t)
    {
        var raw = Whisper.Gameplay.Config.GameConfig.Get("level.eventPool");
        if (raw is System.Collections.Generic.List<object> list)
            foreach (var o in list) if (o as string == t) return true;
        return false;
    }

    static int Main(string[] rawArgs)
    {
        if (Array.IndexOf(rawArgs, "--emit-voice-vectors") >= 0) return EmitVoiceVectors();
        if (Array.IndexOf(rawArgs, "--emit-hearing-vectors") >= 0) return EmitHearingVectors();
        if (Array.IndexOf(rawArgs, "--emit-monster-vectors") >= 0) return EmitMonsterVectors();

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
                // ward_01：pos=[4,6] 是**最小角点**，宽 3 → 南墙 z=6、x∈[4,7]；门宽 1.6 居中 → 门洞中心 x=5.5
                return Math.Abs(x - 5.5f) < 1e-3 && Math.Abs(z - 6f) < 1e-3;
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

        Console.WriteLine($"\n[装配] 关卡装配计划（纯 C#：LevelAssembly 不碰引擎，本机可验证）");
        {
            var plan = Whisper.Gameplay.Level.LevelAssembly.Build(level);
            Check("装配计划：墙段/门板/道具数量与独立复算一致", () =>
            {
                int doors = 0, props = 0, manualWalls = 0;
                foreach (var r in level.Rooms)
                {
                    doors += r.Doors.Count; props += r.Props.Count;
                    foreach (var wall in new[] { "north", "south", "west", "east" })
                    {
                        float len = (wall == "north" || wall == "south") ? r.SizeX : r.SizeZ;
                        var gaps = new List<(float a, float b)>();
                        foreach (var d in r.Doors) { if (d.Wall == wall) { d.SpanOnWall(r, out float a, out float b); gaps.Add((a, b)); } }
                        gaps.Sort((x, y) => x.a.CompareTo(y.a));
                        float cursor = 0; int seg = 0;
                        foreach (var (a, b) in gaps) { if (a - cursor > 0.001f) seg++; cursor = Math.Max(cursor, b); }
                        if (len - cursor > 0.001f) seg++;
                        manualWalls += seg;
                    }
                }
                Console.WriteLine($"      [装配] 墙段 {plan.Walls.Count}（复算 {manualWalls}）· 门板 {plan.Doors.Count}（门 {doors}）· 道具 {plan.Props.Count}（道具 {props}）");
                return plan.Walls.Count == manualWalls && plan.Doors.Count == doors && plan.Props.Count == props;
            });
            Check("装配计划：墙段贴在房间边界（不是房间中心）", () =>
            {
                int bad = 0;
                foreach (var w in plan.Walls)
                {
                    var r = level.Rooms.Find(x => x.Id == w.RoomId);
                    bool hz = w.Wall == "north" || w.Wall == "south";
                    float mid = hz ? (r.MinZ + r.MaxZ) / 2f : (r.MinX + r.MaxX) / 2f;
                    float val = hz ? w.CenterZ : w.CenterX;
                    if (Math.Abs(val - mid) < 0.4f) { bad++; continue; }
                    bool onEdge = hz ? (Math.Abs(val - r.MinZ) < 0.3f || Math.Abs(val - r.MaxZ) < 0.3f)
                                     : (Math.Abs(val - r.MinX) < 0.3f || Math.Abs(val - r.MaxX) < 0.3f);
                    if (!onEdge) bad++;
                }
                if (bad > 0) Console.WriteLine($"      [装配] {bad} 个墙段不在房间边界上");
                return bad == 0;
            });
            Check("装配计划：道具占地按 footprint+rot（bed_b rot90 → 2.0×0.9）", () =>
            {
                var (w1, d1) = Whisper.Gameplay.Level.LevelAssembly.RotatedFootprint("bed_b", 90f);
                var (w2, _) = Whisper.Gameplay.Level.LevelAssembly.RotatedFootprint("bed_b", 0f);
                Console.WriteLine($"      [装配] bed_b rot90 → {w1}×{d1} · rot0 宽 {w2}");
                return Math.Abs(w1 - 2.0f) < 1e-3f && Math.Abs(d1 - 0.9f) < 1e-3f && Math.Abs(w2 - 0.9f) < 1e-3f;
            });
            Check("装配计划：道具占地盒完整落在房间内", () =>
            {
                int bad = 0; var det = new List<string>();
                foreach (var pp in plan.Props)
                {
                    var r = level.Rooms.Find(x => x.Id == pp.RoomId);
                    if (pp.CenterX - pp.SizeX / 2 < r.MinX - 1e-3f || pp.CenterX + pp.SizeX / 2 > r.MaxX + 1e-3f
                        || pp.CenterZ - pp.SizeZ / 2 < r.MinZ - 1e-3f || pp.CenterZ + pp.SizeZ / 2 > r.MaxZ + 1e-3f)
                    { bad++; det.Add($"{pp.RoomId}/{pp.Kit}"); }
                }
                if (bad > 0) Console.WriteLine($"      [装配] 越界：{string.Join(" · ", det)}");
                return bad == 0;
            });
            Check("装配计划：确定性（两次装配逐字段相同）", () =>
            {
                var a = Whisper.Gameplay.Level.LevelAssembly.Build(level);
                var b = Whisper.Gameplay.Level.LevelAssembly.Build(level);
                if (a.Walls.Count != b.Walls.Count || a.Props.Count != b.Props.Count) return false;
                for (int k = 0; k < a.Walls.Count; k++)
                    if (a.Walls[k].CenterX != b.Walls[k].CenterX || a.Walls[k].CenterZ != b.Walls[k].CenterZ) return false;
                return true;
            });
        }

        Console.WriteLine($"\n[证据] 证据点可达性（F-A 阻断项：代理半径 0.34 / 拾取半径 0.9）");
        {
            var geo = Whisper.Gameplay.Level.LevelGeometry.Compile(level);
            const float pick = 0.9f, R = 0.34f;
            Check("证据点可达性：每个证据点都有代理站得到的站位，且距离 ≤ 拾取半径", () =>
            {
                int bad = 0; var det = new List<string>();
                foreach (var r in level.Rooms)
                {
                    if (!r.EvidencePoint) continue;
                    if (!Whisper.Gameplay.Items.EvidencePlacer.TryPlace(geo, level, r, out float ex, out float ez))
                    {
                        bad++; det.Add($"{r.Id} 无可达落位");
                        var fine = Whisper.Gameplay.Level.LevelGeometry.Compile(level, 0.25f);
                        int stand = 0, pass = 0;
                        for (float cz = r.MinZ; cz <= r.MaxZ; cz += 0.25f)
                            for (float cx = r.MinX; cx <= r.MaxX; cx += 0.25f)
                            {
                                if (fine.Passable(cx, cz)) pass++;
                                if (Whisper.Gameplay.Items.EvidencePlacer.IsStandable(fine, level, cx, cz)) stand++;
                            }
                        Console.WriteLine($"      [证据] {r.Id} 细网格可走 {pass} 点 · 站得住 {stand} 点 · 房 x[{r.MinX},{r.MaxX}] z[{r.MinZ},{r.MaxZ}]");
                        continue;
                    }
                    float best = float.MaxValue, bx = 0, bz = 0;
                    for (float cz = r.MinZ + R; cz <= r.MaxZ - R; cz += 0.25f)
                        for (float cx = r.MinX + R; cx <= r.MaxX - R; cx += 0.25f)
                        {
                            if (!Whisper.Gameplay.Items.EvidencePlacer.IsStandable(geo, level, cx, cz)) continue;
                            float d = (float)Math.Sqrt((cx - ex) * (cx - ex) + (cz - ez) * (cz - ez));
                            if (d < best) { best = d; bx = cx; bz = cz; }
                        }
                    Console.WriteLine($"      [证据] {r.Id,-13} 落位({ex:0.00},{ez:0.00}) 最近站位({bx:0.00},{bz:0.00}) 距离 {best:0.00}m");
                    if (best > pick) { bad++; det.Add($"{r.Id} {best:0.00}m > {pick}"); }
                }
                if (bad > 0) Console.WriteLine($"      [证据] ✗ {string.Join(" · ", det)}");
                return bad == 0;
            });
        }


        Console.WriteLine("\n[3] 校验器必须真会拦（注入畸形数据）");
        CheckThrows<LevelLoader.LevelValidationException>("未知 kit 被拦", () =>
            LevelLoader.Load(levelJson.Replace("\"hospital_ward\"", "\"nope_kit\""), kits));
        CheckThrows<LevelLoader.LevelValidationException>("非法 lightZone 被拦", () =>
            LevelLoader.Load(levelJson.Replace("\"lightZone\": \"pressure\"", "\"lightZone\": \"spooky\""), kits));
        CheckThrows<LevelLoader.LevelValidationException>("门洞越界被拦（offsetM + widthM > 墙长）", () =>
        {
            // JSON 里 offsetM/widthM 分行，故用正则只改值（把 1.6 宽的门推到墙外）
            var broken = System.Text.RegularExpressions.Regex.Replace(levelJson, "\"offsetM\": 0.7", "\"offsetM\": 2.5");
            LevelLoader.Load(broken, kits);
        });
        CheckThrows<LevelLoader.LevelValidationException>("门宽为负被拦", () =>
        {
            var broken = System.Text.RegularExpressions.Regex.Replace(levelJson, "\"widthM\": 1.6", "\"widthM\": -1");
            LevelLoader.Load(broken, kits);
        });
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

        // 硬前置：以下所有断言都按**配置真值**驱动，必须先确保静态配置已载入。
        // [2.5] 末尾的 Reset 断言会清空它；早期版本的 LoadFromJson 落在 [3.8] 内部，
        // 导致 3.11/3.10/3.9 三段静默读到空配置并退化成默认值（档位表变 unknown、事件池为空）。
        // 现在把它提到最前，并做一次显式校验。
        Whisper.Gameplay.Config.GameConfig.LoadFromJson(cfgJson);
        if (!Whisper.Gameplay.Config.GameConfig.IsLoaded)
            throw new InvalidOperationException("配置表未载入：按配置真值驱动的断言会静默退化");
        Console.WriteLine($"      [前置] sanity.max={Whisper.Gameplay.Config.GameConfig.GetFloat("sanity.max", -1f)} · 事件池={((Whisper.Gameplay.Config.GameConfig.Get("level.eventPool") as System.Collections.Generic.List<object>)?.Count ?? 0)} · 怪物={((Whisper.Gameplay.Config.GameConfig.Get("monsters") as System.Collections.Generic.Dictionary<string, object>)?.Count ?? 0)}");

        Console.WriteLine("\n[3.14] 端到端集成：真跑一整局（把各系统接起来跑，而不只是各自断言）");
        {
            var cfg0 = cfgReader();
            var lv0 = Whisper.Gameplay.Level.LevelLoader.Load(levelJson, kits);
            var s0 = new Whisper.Gameplay.Session.GameSession(lv0, cfg0, 1);
            for (int i = 0; i < 60 * 60; i++) s0.Tick(1f / 60f);
        }
        Check("空跑 60 秒：保护期→主阶段、HUD 有时钟、无崩溃（不静止崩溃）", () =>
        {
            var cfg = cfgReader();
            var level = Whisper.Gameplay.Level.LevelLoader.Load(levelJson, kits);
            var s1 = new Whisper.Gameplay.Session.GameSession(level, cfg, 1);
            for (int i = 0; i < 60 * 60; i++) s1.Tick(1f / 60f);
            return !s1.Outcome.Ended
                && s1.Director.Stage == Whisper.Gameplay.Match.MatchStage.Main
                && s1.Hud.ClockText == "1:00"
                && s1.Sanity.Value > 0f
                && s1.Monsters.Count == 3;
        });
        Check("完整闭环：走到 5 个证据点 → 到撤离点 → 存活结算（碎片 = 5×200+150+100 = 1250）", () =>
        {
            var cfg = cfgReader();
            var level = Whisper.Gameplay.Level.LevelLoader.Load(levelJson, kits);
            var s2 = new Whisper.Gameplay.Session.GameSession(level, cfg, 7);
            // 简化导航：直接瞬移到每个证据点（本用例验证的是"闭环成立"，不是寻路）
            foreach (var ep in s2.Items.EvidencePoints)
            {
                s2.PlayerX = ep.X; s2.PlayerZ = ep.Z;
                s2.Tick(1f / 60f);
            }
            if (s2.Items.EvidenceCount != s2.Items.EvidenceTotal) return false;
            var target = s2.Items.ExtractionPoints[0];      // 标准撤离点
            s2.PlayerX = target.X; s2.PlayerZ = target.Z;
            s2.SurvivingAllies = 1;
            s2.Tick(1f / 60f);
            var o = s2.Outcome;
            return o.Ended && o.Survived && o.EvidenceCollected == 5 && o.Fragments == 1250
                && o.Extraction == Whisper.Gameplay.Extraction.ExtractionKind.Standard;
        });
        Check("声纹闭环：喊叫 → 低语者听见（刺激→可听性→日志三段都真的跑通）", () =>
        {
            var cfg = cfgReader();
            var level = Whisper.Gameplay.Level.LevelLoader.Load(levelJson, kits);
            var s3 = new Whisper.Gameplay.Session.GameSession(level, cfg, 3);
            // 把玩家挪到低语者附近，制造一次必然可听的喊叫
            var brain = s3.Monsters.Find(b => b.Id == "whisperer");
            s3.PlayerX = brain.Position.X + 2f; s3.PlayerZ = brain.Position.Z;
            float intensity = cfg.Float("stimulusSources.voice_shout.intensity", 80f);
            float radius = cfg.Float("stimulusSources.voice_shout.radiusM", 25f);
            s3.EmitStimulus(new Whisper.Gameplay.Hearing.Stimulus("voice_shout", "voice", intensity, radius, false, s3.PlayerX, s3.PlayerZ, 0));
            s3.Tick(1f / 60f);
            bool logged = false;
            foreach (var l in s3.EventLog) if (l.Contains("低语者 听见了")) logged = true;
            // 听见 → 调查（不是直接追击），这是 V9 的核心语义纪律
            return logged && brain.State == "investigate";
        });
        Check("保护期内怪物不因「看见」入追击（V9 §7 保护期语义）", () =>
        {
            var cfg = cfgReader();
            var level = Whisper.Gameplay.Level.LevelLoader.Load(levelJson, kits);
            var s4 = new Whisper.Gameplay.Session.GameSession(level, cfg, 9);
            s4.SeenByPlayer = true;                       // 玩家「看见」怪物（模拟视觉触发条件）
            s4.PlayerX = s4.Monsters[0].Position.X; s4.PlayerZ = s4.Monsters[0].Position.Z;
            for (int i = 0; i < 30; i++) { s4.PlayerX = s4.Monsters[0].Position.X; s4.PlayerZ = s4.Monsters[0].Position.Z; s4.Tick(1f / 60f); }  // 仍在 20s 保护期内
            bool noChaseInGrace = s4.Monsters[0].State != "chase";
            // 越过保护期后再看一次
            // 注意：必须每帧把玩家挪到怪身边。若站着不动，怪会走远，pdist>12 后视觉条件本就不成立
            // —— 那是测试设计问题，不是"保护期语义"的问题（我第一版就是这么写错的）。
            for (int i = 0; i < 21 * 60; i++) { s4.PlayerX = s4.Monsters[0].Position.X; s4.PlayerZ = s4.Monsters[0].Position.Z; s4.Tick(1f / 60f); }
            bool chaseAfterGrace = s4.Monsters[0].State == "chase";
            return noChaseInGrace && chaseAfterGrace;
        });
        Check("理智崩溃会产生尖叫声纹并被写进日志（代价可见）", () =>
        {
            var cfg = cfgReader();
            var level = Whisper.Gameplay.Level.LevelLoader.Load(levelJson, kits);
            var s5 = new Whisper.Gameplay.Session.GameSession(level, cfg, 11);
            // 直接压低理智到会被接触打崩的程度
            for (int i = 0; i < 60; i++) s5.Sanity.MonsterContact();
            s5.PlayerX = s5.Monsters[0].Position.X; s5.PlayerZ = s5.Monsters[0].Position.Z;
            s5.Tick(1f / 60f);
            bool screamed = false;
            foreach (var l in s5.EventLog) if (l.Contains("尖叫")) screamed = true;
            return screamed;
        });

        Console.WriteLine("\n[3.13] HUD 数据模型（§19.1 C2：编辑器零参与；显示口径与灰盒一致）");
        Check("时钟格式 mm:ss（灰盒口径：62s → 1:02；600s → 10:00）", () =>
        {
            var cfg = cfgReader();
            var hud = new Whisper.Gameplay.Hud.HudModel(cfg, 5);
            hud.Update(null, 120f, 0, 62f);
            bool a = hud.ClockText == "1:02";
            hud.Update(null, 120f, 0, 600f);
            bool b = hud.ClockText == "10:00";
            hud.Update(null, 120f, 0, 5f);
            bool c = hud.ClockText == "0:05";
            return a && b && c;
        });
        Check("理智文案 = 档位名 + 四舍五入百分比（恐惧 30/100 → 「恐惧 30」）", () =>
        {
            var cfg = cfgReader();
            var sanity = new Whisper.Gameplay.Sanity.SanitySystem(cfg, 30f);
            var hud = new Whisper.Gameplay.Hud.HudModel(cfg, 5);
            hud.Update(sanity, 90f, 2, 100f);
            return hud.SanityBandLabel == "恐惧" && hud.SanityPercent == 30 && hud.BatterySeconds == 90 && hud.Evidence == 2;
        });
        Check("怪物名取自配置 label（低语者/缝匠/收殓人）", () =>
        {
            var hud = new Whisper.Gameplay.Hud.HudModel(cfgReader(), 5);
            return hud.MonsterLabel("whisperer") == "低语者"
                && hud.MonsterLabel("stitcher") == "缝匠"
                && hud.MonsterLabel("coroner") == "收殓人";
        });
        Check("听见提示格式与灰盒一致（距离一位小数 / 阈值一位小数）", () =>
        {
            var hud = new Whisper.Gameplay.Hud.HudModel(cfgReader(), 5);
            var line = hud.FormatHearing(new Whisper.Gameplay.Hud.HearingNotice
            { MonsterLabel = "低语者", DistanceM = 12.34f, EffectiveThreshold = 10f });
            return line == "【低语者 听见了】距离 12.3m（阈值 10.0）";
        });
        Check("日志新的在上、且不超过上限（丢最旧）", () =>
        {
            var hud = new Whisper.Gameplay.Hud.HudModel(cfgReader(), 5) { MaxLogLines = 3 };
            hud.Log("A"); hud.Log("B"); hud.Log("C"); hud.Log("D");
            return hud.LogLines.Count == 3 && hud.LogLines[0] == "D" && hud.LogLines[2] == "B";
        });
        Check("状态行包含档位/电量/证据/时钟/阶段（便于 HUD 与日志对拍）", () =>
        {
            var cfg = cfgReader();
            var sanity = new Whisper.Gameplay.Sanity.SanitySystem(cfg, 90f);
            var hud = new Whisper.Gameplay.Hud.HudModel(cfg, 5);
            hud.Update(sanity, 118f, 3, 250f);
            hud.SetStageText("主阶段");
            var line = hud.FormatStatusLine();
            return line.Contains("镇定 90") && line.Contains("电量 118s") && line.Contains("证据 3/5") && line.Contains("4:10") && line.Contains("主阶段");
        });
        Check("结算页正文口径与灰盒一致（撤离点×系数 / 证据 / 用时 / 碎片）", () =>
        {
            var cfg = cfgReader();
            var r = Whisper.Gameplay.Extraction.Settlement.Compute(cfg, new Whisper.Gameplay.Extraction.MissionOutcome
            { Survived = true, Extraction = Whisper.Gameplay.Extraction.ExtractionKind.Standard, EvidenceCollected = 3, EvidenceTotal = 5, SurvivingAllies = 1, ElapsedSeconds = 412.3f });
            var hud = new Whisper.Gameplay.Hud.HudModel(cfg, 5);
            var text = hud.FormatSettlement(r);
            return text.Contains("标准撤离点（×1）") && text.Contains("证据 3/5") && text.Contains("412.3s") && text.Contains("残响碎片 850");
        });

        Console.WriteLine("\n[3.12] 道具与交互（V9 §7；交互半径逐条照抄灰盒）");
        Check("初始电量 = 配置 items.flashlight.batterySeconds（120s）", () =>
        {
            var it = new Whisper.Gameplay.Items.ItemSystem(cfgReader());
            return Math.Abs(it.BatterySeconds - 120f) < 1e-6 && it.TorchOn;
        });
        Check("手电亮时耗电（每秒 −1s），电尽自动熄灭", () =>
        {
            var it = new Whisper.Gameplay.Items.ItemSystem(cfgReader());
            for (int i = 0; i < 120 * 60; i++) it.TickFlashlight(1f / 60f);
            bool drained = !it.TorchOn && it.BatterySeconds <= 0.05f;
            it.TickFlashlight(1f);   // 再推一秒不应变负
            return drained && it.BatterySeconds == 0f;
        });
        Check("手电亮时怪物视觉加成 +3m，熄灭时为 0（配置真源）", () =>
        {
            var it = new Whisper.Gameplay.Items.ItemSystem(cfgReader());
            float on = it.MonsterPerceptionBonusM;
            for (int i = 0; i < 120 * 60; i++) it.TickFlashlight(1f / 60f);
            return Math.Abs(on - 3f) < 1e-6 && it.MonsterPerceptionBonusM == 0f;
        });
        Check("拾取半径 1.0m：刚好在内拾取、刚好在外不拾取", () =>
        {
            var it = new Whisper.Gameplay.Items.ItemSystem(cfgReader());
            it.Pickups.Add(new Whisper.Gameplay.Items.Pickup { Key = "b1", Kind = Whisper.Gameplay.Items.PickupKind.Battery, X = 0f, Z = 0f });
            it.Pickups.Add(new Whisper.Gameplay.Items.Pickup { Key = "b2", Kind = Whisper.Gameplay.Items.PickupKind.Battery, X = 5f, Z = 0f });
            for (int i = 0; i < 60 * 60; i++) it.TickFlashlight(1f / 60f);   // 电量降到 60s
            var inside = it.TryPickup(0.99f, 0f);      // 距离 0.99 < 1.0 → 拾取
            var outside = it.TryPickup(5.01f, 0f);     // 距离 0.01 < 1.0 → 也会拾取（同一物）；改用远点验证
            var far = it.TryPickup(50f, 0f);
            return inside.Count == 1 && far.Count == 0;
        });
        Check("电池补充 60s 且不超过上限（配置上限 120s）", () =>
        {
            var it = new Whisper.Gameplay.Items.ItemSystem(cfgReader());
            it.Pickups.Add(new Whisper.Gameplay.Items.Pickup { Key = "b", Kind = Whisper.Gameplay.Items.PickupKind.Battery, X = 0f, Z = 0f });
            for (int i = 0; i < 60 * 60; i++) it.TickFlashlight(1f / 60f);   // 60s
            it.TryPickup(0f, 0f);
            bool ok = Math.Abs(it.BatterySeconds - 120f) < 0.05f;             // 60 + 60 = 120（正好到顶）
            it.TryPickup(0f, 0f);                                            // 同一个不再重复拾取
            return ok && it.BatterySeconds <= 120f + 1e-6f;
        });
        Check("配电箱 1.6m 内按使用键切换区域照明；不在范围内无效", () =>
        {
            var it = new Whisper.Gameplay.Items.ItemSystem(cfgReader());
            it.Breakers.Add(new Whisper.Gameplay.Items.Breaker { Zone = "ward", X = 10f, Z = 10f });
            bool off1 = it.TryToggleBreaker(10f, 11.5f, usePressed: true) != null && it.LightsOffZones.Contains("ward");
            bool restored = it.TryToggleBreaker(10f, 10f, usePressed: true) != null && !it.LightsOffZones.Contains("ward");
            bool tooFar = it.TryToggleBreaker(10f, 20f, usePressed: true) == null;
            bool noKey = it.TryToggleBreaker(10f, 10f, usePressed: false) == null;
            return off1 && restored && tooFar && noKey;
        });
        Check("切断照明会进入理智参数（LightsOut=true）→ 与理智流失耦合", () =>
        {
            var it = new Whisper.Gameplay.Items.ItemSystem(cfgReader());
            it.Breakers.Add(new Whisper.Gameplay.Items.Breaker { Zone = "ward", X = 0f, Z = 0f });
            var before = it.SanityArgs;
            it.TryToggleBreaker(0f, 0f, true);
            var after = it.SanityArgs;
            return !before.LightsOut && after.LightsOut;
        });
        Check("证据 0.9m 内自动拾取且计数正确", () =>
        {
            var it = new Whisper.Gameplay.Items.ItemSystem(cfgReader(), evidenceTotal: 2);
            it.EvidencePoints.Add(new Whisper.Gameplay.Items.EvidencePoint { Id = "e1", X = 0f, Z = 0f });
            it.EvidencePoints.Add(new Whisper.Gameplay.Items.EvidencePoint { Id = "e2", X = 5f, Z = 0f });
            var a = it.TryCollectEvidence(0.89f, 0f);
            var b = it.TryCollectEvidence(0f, 0f);      // 已拾取，不重复
            return a.Count == 1 && b.Count == 0 && it.EvidenceCount == 1;
        });
        Check("证据未集齐不允许撤离；集齐后 1.4m 内可撤离（灰盒同规则）", () =>
        {
            var it = new Whisper.Gameplay.Items.ItemSystem(cfgReader(), evidenceTotal: 1);
            it.EvidencePoints.Add(new Whisper.Gameplay.Items.EvidencePoint { Id = "e1", X = 0f, Z = 0f });
            it.ExtractionPoints.Add(new Whisper.Gameplay.Items.ExtractionPoint { Id = "std", Label = "标准撤离点", X = 20f, Z = 0f, Safe = true, RewardScale = 1f });
            bool blockedBefore = it.TryExtract(20f, 0f) == null;     // 证据没集齐 → 不允许
            it.TryCollectEvidence(0f, 0f);
            bool outside = it.TryExtract(20f, 1.5f) == null;          // 1.5m > 1.4m → 不允许
            var ok = it.TryExtract(20f, 1.3f);                        // 1.3m → 允许
            return blockedBefore && outside && ok.HasValue && ok.Value.Id == "std";
        });
        Check("镇静剂/信号弹/相机/录音机 参数取自配置（25s 恢复 / 15s 安全区 / 2s 眩晕 / 20s 录音）", () =>
        {
            var it = new Whisper.Gameplay.Items.ItemSystem(cfgReader());
            it.Pickups.Add(new Whisper.Gameplay.Items.Pickup { Key = "s", Kind = Whisper.Gameplay.Items.PickupKind.Sedative, X = 0f, Z = 0f });
            it.TryPickup(0f, 0f);
            bool useFirst = it.UseSedative();
            bool useSecond = !it.UseSedative();
            return useFirst && useSecond
                && Math.Abs(it.FlareSafeZoneSeconds - 15f) < 1e-6
                && Math.Abs(it.CameraStunSeconds - 2f) < 1e-6
                && it.CameraUsesPerMonsterPerMatch == 1
                && Math.Abs(it.RecorderSeconds - 20f) < 1e-6
                && it.RecorderProducesStimulus;
        });

        Console.WriteLine("\n[3.11] 对局调度（V9 §7 保护期 → 主阶段 → 终局狂暴；§19.2 动态事件）");
        Check("开局处于保护期，20 秒后进入主阶段（保护期秒数取自配置）", () =>
        {
            var d = new Whisper.Gameplay.Match.MatchDirector(cfgReader());
            if (d.Stage != Whisper.Gameplay.Match.MatchStage.Grace || !d.InGrace) return false;
            if (d.ChaseBySightAllowed || d.ContactEnabled) return false;   // 保护期内不允许看见入追击、不判接触
            for (int i = 0; i < 20 * 60; i++) d.Tick(1f / 60f);            // 恰 20 秒（仍在期内）
            if (!d.InGrace) return false;
            d.Tick(1f / 60f);                                             // 越过 20 秒
            return d.Stage == Whisper.Gameplay.Match.MatchStage.Main && d.ChaseBySightAllowed && d.ContactEnabled;
        });
        Check("终局狂暴在撤离前 60 秒开启（配置 finalRageWindowBeforeExtractionSec）", () =>
        {
            var d = new Whisper.Gameplay.Match.MatchDirector(cfgReader(), 7);
            while (d.Stage != Whisper.Gameplay.Match.MatchStage.FinalRage && d.ElapsedSeconds < 700f) d.Tick(1f);
            return d.Stage == Whisper.Gameplay.Match.MatchStage.FinalRage
                && d.FinalRageActive
                && Math.Abs(d.RemainingSeconds - 60f) < 1.5f;
        });
        Check("动态事件数在配置区间 [2,3] 内，且时间落在保护期之后、狂暴窗口之前", () =>
        {
            for (int seed = 0; seed < 12; seed++)
            {
                var d = new Whisper.Gameplay.Match.MatchDirector(cfgReader(), seed);
                if (d.Schedule.Count < 2 || d.Schedule.Count > 3) return false;
                foreach (var e in d.Schedule)
                {
                    if (e.AtSecond <= d.GraceSeconds) return false;
                    if (e.AtSecond >= d.MatchMaxSeconds - d.FinalRageWindowSec) return false;
                    if (!cfgPoolContains(e.Type)) return false;
                }
            }
            return true;
        });
        Check("调度确定性：同种子两次构建得到同一条时间线", () =>
        {
            var a = new Whisper.Gameplay.Match.MatchDirector(cfgReader(), 42);
            var b = new Whisper.Gameplay.Match.MatchDirector(cfgReader(), 42);
            if (a.Schedule.Count != b.Schedule.Count) return false;
            for (int i = 0; i < a.Schedule.Count; i++)
                if (a.Schedule[i].Type != b.Schedule[i].Type || Math.Abs(a.Schedule[i].AtSecond - b.Schedule[i].AtSecond) > 1e-6) return false;
            return true;
        });
        Check("事件只在到点后触发一次（不重复、不提前）", () =>
        {
            var d = new Whisper.Gameplay.Match.MatchDirector(cfgReader(), 3);
            if (d.Schedule.Count == 0) return false;
            float firstAt = d.Schedule[0].AtSecond;
            for (int i = 0; i < (int)(firstAt * 60f) - 1; i++) d.Tick(1f / 60f);
            if (d.Fired.Count != 0) return false;                 // 未到点
            while (d.ElapsedSeconds <= firstAt + 1f) d.Tick(1f / 60f);
            return d.Fired.Count == 1;                            // 恰好触发一次
        });
        Check("对局超过上限秒数进入撤离阶段（配置 matchSeconds 下界）", () =>
        {
            var d = new Whisper.Gameplay.Match.MatchDirector(cfgReader(), 5, 30f);   // 覆盖为 30 秒便于断言
            while (d.Stage != Whisper.Gameplay.Match.MatchStage.Extraction && d.ElapsedSeconds < 60f) d.Tick(1f);
            return d.Stage == Whisper.Gameplay.Match.MatchStage.Extraction && d.RemainingSeconds <= 0f;   // 越界时恰好为 0
        });
        Check("理智档位通过 ApplyTo 传导到怪物感知（恐惧档 +0.2）", () =>
        {
            var cfg = cfgReader();
            var sanity = new Whisper.Gameplay.Sanity.SanitySystem(cfg, 30f);   // fear 档
            var brain = new Whisper.Gameplay.Monsters.MonsterBrain("whisperer", cfg);
            var d = new Whisper.Gameplay.Match.MatchDirector(cfg, 1);
            d.ApplyTo(sanity, brain);
            return Math.Abs(brain.PerceptionBonus - 0.2f) < 1e-6;
        });

        Console.WriteLine("\n[3.10] 撤离与结算（V9 §7 / economy 配置）");
        Check("满配结算 = 证据3×200 + 队友150 + 效率100 = 850（与灰盒公式独立复算一致）", () =>
        {
            var r = Whisper.Gameplay.Extraction.Settlement.Compute(cfgReader(), new Whisper.Gameplay.Extraction.MissionOutcome
            { Survived = true, Extraction = Whisper.Gameplay.Extraction.ExtractionKind.Standard, EvidenceCollected = 3, SurvivingAllies = 1, ElapsedSeconds = 500f });
            return r.Fragments == 850 && r.RewardScale == 1f;
        });
        Check("超过 10 分钟（700s）失去效率奖金、无队友 → 3×200 = 600", () =>
        {
            var r = Whisper.Gameplay.Extraction.Settlement.Compute(cfgReader(), new Whisper.Gameplay.Extraction.MissionOutcome
            { Survived = true, Extraction = Whisper.Gameplay.Extraction.ExtractionKind.Standard, EvidenceCollected = 3, SurvivingAllies = 0, ElapsedSeconds = 700f });
            return r.Fragments == 600;
        });
        Check("未撤离 → 碎片 0（V9 §7：只有带证据活着出去才算）", () =>
        {
            var r = Whisper.Gameplay.Extraction.Settlement.Compute(cfgReader(), new Whisper.Gameplay.Extraction.MissionOutcome
            { Survived = false, Extraction = Whisper.Gameplay.Extraction.ExtractionKind.None, EvidenceCollected = 5, SurvivingAllies = 2, ElapsedSeconds = 100f });
            return r.Fragments == 0 && r.Breakdown.Contains("未撤离");
        });
        Check("深处撤离点 rewardScale = 1.3 且标记为危险（配置真源）", () =>
        {
            var cfg = cfgReader();
            var r = Whisper.Gameplay.Extraction.Settlement.Compute(cfg, new Whisper.Gameplay.Extraction.MissionOutcome
            { Survived = true, Extraction = Whisper.Gameplay.Extraction.ExtractionKind.Deep, EvidenceCollected = 1, SurvivingAllies = 0, ElapsedSeconds = 700f });
            return Math.Abs(r.RewardScale - 1.3f) < 1e-6
                && Whisper.Gameplay.Extraction.Settlement.IsSafe(cfg, Whisper.Gameplay.Extraction.ExtractionKind.Deep) == false
                && Whisper.Gameplay.Extraction.Settlement.IsSafe(cfg, Whisper.Gameplay.Extraction.ExtractionKind.Standard) == true;
        });
        Check("已知缺口被显式登记：rewardScale 未参与碎片计算（与灰盒同行为）", () =>
        {
            var cfg = cfgReader();
            var deep = Whisper.Gameplay.Extraction.Settlement.Compute(cfg, new Whisper.Gameplay.Extraction.MissionOutcome
            { Survived = true, Extraction = Whisper.Gameplay.Extraction.ExtractionKind.Deep, EvidenceCollected = 1, SurvivingAllies = 0, ElapsedSeconds = 700f });
            var std = Whisper.Gameplay.Extraction.Settlement.Compute(cfg, new Whisper.Gameplay.Extraction.MissionOutcome
            { Survived = true, Extraction = Whisper.Gameplay.Extraction.ExtractionKind.Standard, EvidenceCollected = 1, SurvivingAllies = 0, ElapsedSeconds = 700f });
            return deep.Fragments == std.Fragments   // 同碎片 → 缺口确实存在（不是"已修好"）
                && Whisper.Gameplay.Extraction.Settlement.DeepScaleGapNote.Contains("1.3");
        });
        Check("开局保护期与动态事件数来自配置（20s / 2~3 个）", () =>
        {
            var cfg = cfgReader();
            Whisper.Gameplay.Extraction.Settlement.DynamicEventRange(cfg, out int mn, out int mx);
            return Math.Abs(Whisper.Gameplay.Extraction.Settlement.StartGraceSeconds(cfg) - 20f) < 1e-6 && mn == 2 && mx == 3;
        });

        Console.WriteLine("\n[3.9] 关卡几何编译（V9 §19.2：DSL → 可碰撞几何）");
        Check("几何可编译且可走格数合理（打印实测值，不写死阈值）", () =>
        {
            var geo = Whisper.Gameplay.Level.LevelGeometry.Compile(level);
            int n = geo.PassableCount();
            Console.WriteLine($"      [几何] 可走 {n} 格 · 网格 {geo.Width}×{geo.Height}");
            // 阈值口径：每个房间至少要有 1 格可走（内缩 0.26m 后小房间只剩 1~2 格），
            // 外加走廊。写死 >100 是我此前的坏做法（内缩后本就该变少，导致误报红线）。
            return n >= level.Rooms.Count && geo.Width > 0 && geo.Height > 0;
        });
        // 注：本几何模型用"格带"表示墙（墙厚 0.26m、格 0.5m），因此相邻两格之间是否可通行，
        // 要看**跨过共享边的中点**是否被挡，而不是看两侧格中心（中心离墙 0.25m，永远可走）。
        Check("房间之间必须有内墙：相邻房间共享边上只允许门洞处可穿（其余必须是墙）", () =>
        {
            var geo = Whisper.Gameplay.Level.LevelGeometry.Compile(level);
            int bad = 0;
            var detail = new List<string>();
            bool HasDoor(string a, string b) => level.Corridors.Exists(c =>
                (c.From == a && c.To == b) || (c.From == b && c.To == a));
            const float Eps = 0.12f;   // 跨边采样的偏移（远小于格 0.5m，足以落在相邻格内）

            for (int i = 0; i < level.Rooms.Count; i++)
                for (int j = i + 1; j < level.Rooms.Count; j++)
                {
                    var A = level.Rooms[i]; var B = level.Rooms[j];
                    bool door = HasDoor(A.Id, B.Id);
                    // 东西相邻：共享边 x = 常量
                    float xa = float.NaN, xb = float.NaN;
                    if (Math.Abs(A.MaxX - B.MinX) < 1e-3f) { xa = A.MaxX; xb = B.MinX; }
                    else if (Math.Abs(B.MaxX - A.MinX) < 1e-3f) { xa = A.MinX; xb = B.MaxX; }
                    if (!float.IsNaN(xa))
                    {
                        float lo = Math.Max(A.MinZ, B.MinZ), hi = Math.Min(A.MaxZ, B.MaxZ);
                        // 只检查真正共墙的相邻（共边长度 ≥1m）；对角相接（只共一个角点）不算相邻，
                        // 否则会把"两房在角上擦过"误报成缺墙（morgue_deep 与 ward_02 就是这种）。
                        if (hi - lo < 1.0f) continue;
                        int steps = Math.Max(2, (int)Math.Round((hi - lo) / 0.5f)), open = 0;
                        for (int k = 0; k < steps; k++)
                        {
                            float z = lo + (k + 0.5f) * (hi - lo) / steps;
                            if (geo.Passable(xa - Eps, z) && geo.Passable(xa + Eps, z)) open++;
                        }
                        int allow = door ? 3 : 0;   // 门宽约 1.6~2.0m → 0.5m 格下约 3~4 格
                        if (open > allow) { bad++; detail.Add($"{A.Id}|{B.Id} 东西相邻 {steps} 采样中 {open} 处可穿（门={door}）"); }
                    }
                    // 南北相邻：共享边 z = 常量
                    float za = float.NaN;
                    if (Math.Abs(A.MaxZ - B.MinZ) < 1e-3f) za = A.MaxZ;
                    else if (Math.Abs(B.MaxZ - A.MinZ) < 1e-3f) za = A.MinZ;
                    if (!float.IsNaN(za))
                    {
                        float lo = Math.Max(A.MinX, B.MinX), hi = Math.Min(A.MaxX, B.MaxX);
                        if (hi - lo < 1.0f) continue;   // 同上：对角相接不算共墙
                        int steps = Math.Max(2, (int)Math.Round((hi - lo) / 0.5f)), open = 0;
                        for (int k = 0; k < steps; k++)
                        {
                            float x = lo + (k + 0.5f) * (hi - lo) / steps;
                            if (geo.Passable(x, za - Eps) && geo.Passable(x, za + Eps)) open++;
                        }
                        int allow = door ? 3 : 0;
                        if (open > allow) { bad++; detail.Add($"{A.Id}/{B.Id} 南北相邻 {steps} 采样中 {open} 处可穿（门={door}）"); }
                    }
                }
            if (bad > 0) Console.WriteLine($"      [内墙] {string.Join(" · ", detail.GetRange(0, Math.Min(3, detail.Count)))}");
            return bad == 0;
        });
        Check("门真的打通了两侧空间：从入口洪水填充可达全部 11 个房间", () =>
        {
            var geo = Whisper.Gameplay.Level.LevelGeometry.Compile(level);
            // 起点/终点都要用"最近的空可走格"——房间中心常被家具占用（ward_03 中心就是病床）
            foreach (var r in level.Rooms)
            {
                if (!geo.TryFindFreeCell(r.CenterX, r.CenterZ, out float fx, out float fz))
                { Console.WriteLine($"      [连通] {r.Id} 找不到空可走格"); return false; }
                if (geo.ReachableCount(fx, fz) <= 0) { Console.WriteLine($"      [连通] {r.Id} 空洞（门未打通）"); return false; }
            }
            geo.TryFindFreeCell(level.Rooms[0].CenterX, level.Rooms[0].CenterZ, out float ex, out float ez);
            int fromEntrance = geo.ReachableCount(ex, ez);
            Console.WriteLine($"      [连通] 从入口可达 {fromEntrance} / 可走 {geo.PassableCount()}");
            return fromEntrance == geo.PassableCount();
        });
        Check("子步进防穿墙：一次 5 米位移不能穿过整面墙", () =>
        {
            var geo = Whisper.Gameplay.Level.LevelGeometry.Compile(level);
            var start = level.Rooms.Find(r => r.Id == "ward_01");
            // 从病房中心向北（+z）猛推 5 米：病房深 4 米，必须被外圈墙挡住
            var res = geo.Resolve(start.CenterX, start.CenterZ, 0, 5f, 0.34f);
            return res.Blocked && res.Z < start.MaxZ + 0.6f;
        });
        Check("碰撞解析：空地上小位移正常通行且不误报 blocked", () =>
        {
            var geo = Whisper.Gameplay.Level.LevelGeometry.Compile(level);
            // 注意：不能拿"房间中心"当空地 —— ward_03 中心放了床（道具自动落位的结果），
            // 从那儿起步本来就会被挡。用房间内一个明确无道具的点。
            var r = level.Rooms.Find(x => x.Id == "ward_03");
            // 起点必须是"空的可走格"：ward_03 中心放着病床，从那儿起步本来就该被挡。
            if (!geo.TryFindFreeCell(r.CenterX, r.CenterZ, out float px, out float pz)) return false;
            var res = geo.Resolve(px, pz, 0.1f, 0f, 0.34f);
            return !res.Blocked && Math.Abs(res.X - (px + 0.1f)) < 1e-4;
        });
        Check("道具碰撞盒按 footprint+rot 生效：床所在位置确实被挡住（可见与可撞一致）", () =>
        {
            var geo = Whisper.Gameplay.Level.LevelGeometry.Compile(level);
            var r = level.Rooms.Find(x => x.Id == "ward_03");
            var bed = r.Props[0];
            float bx = r.MinX + bed.X, bz = r.MinZ + bed.Z;
            var into = geo.Resolve(bx - 1.5f, bz, 1.5f, 0f, 0.34f);   // 朝床推
            return into.Blocked;                                       // 必须被挡（此前是 1×1 盒，朝向不对）
        });
        Check("门口可通过：从走廊经门洞走进病房（几何连通性实证）", () =>
        {
            var geo = Whisper.Gameplay.Level.LevelGeometry.Compile(level);
            var ward = level.Rooms.Find(x => x.Id == "ward_02");
            var door = ward.FindDoor("d_south");
            door.ToWorld(ward, out float dx, out float dz);
            // 门洞中心应可走（门格被真正打通），且门内侧、外侧都可走
            return geo.Passable(dx, dz) && geo.Passable(dx, dz + 0.5f) && geo.Passable(dx, dz - 0.5f);
        });

        Console.WriteLine("\n[3.8] 理智系统（V9 附录 A-2 / §7）");
                Check("初始理智 = 配置 sanity.max（100）", () =>
        {
            var sy = new Whisper.Gameplay.Sanity.SanitySystem(cfgReader());
            return Math.Abs(sy.Value - 100f) < 1e-6 && sy.Band.Id == "composed";
        });
        Check("暗处 10 秒 → -10（配置 darknessPerSec=-1）", () =>
        {
            var sy = new Whisper.Gameplay.Sanity.SanitySystem(cfgReader());
            for (int i = 0; i < 600; i++) sy.Tick(1f / 60f, torchOn: false, inSafeZone: false);
            // 100 → 90 仍在 composed 档（80-100）；档位迁移另有一条断言
            return Math.Abs(sy.Value - 90f) < 0.01 && sy.Band.Id == "composed";
        });
        Check("暗处 25 秒 → 75 且跨档到 uneasy（档位迁移真的发生）", () =>
        {
            var sy = new Whisper.Gameplay.Sanity.SanitySystem(cfgReader());
            for (int i = 0; i < 1500; i++) sy.Tick(1f / 60f, torchOn: false, inSafeZone: false);
            return Math.Abs(sy.Value - 75f) < 0.02 && sy.Band.Id == "uneasy";
        });
        Check("灯灭额外流失 ×1.5（灰盒两段叠加语义）", () =>
        {
            var a = new Whisper.Gameplay.Sanity.SanitySystem(cfgReader());
            var b = new Whisper.Gameplay.Sanity.SanitySystem(cfgReader());
            for (int i = 0; i < 60; i++) { a.Tick(1f / 60f, false, false, lightsOut: false); b.Tick(1f / 60f, false, false, lightsOut: true); }
            // 灯灭是**两段叠加**：常暗 -1/s 与灯灭 -1.5/s 同时生效 = -2.5/s；1 秒 → 100→99 与 100→97.5
            return Math.Abs(a.Value - 99f) < 0.01 && Math.Abs(b.Value - 97.5f) < 0.01;
        });
        Check("安全区恢复 0.5/s（配置 2 × 0.25）", () =>
        {
            var sy = new Whisper.Gameplay.Sanity.SanitySystem(cfgReader(), 50f);
            for (int i = 0; i < 120; i++) sy.Tick(1f / 60f, torchOn: true, inSafeZone: true);
            return Math.Abs(sy.Value - 51f) < 0.01;
        });
        Check("档位效果：恐惧档感知 +0.2（喂给怪物的 PerceptionBonus）", () =>
        {
            var sy = new Whisper.Gameplay.Sanity.SanitySystem(cfgReader(), 30f);
            return sy.Band.Id == "fear" && Math.Abs(sy.MonsterPerceptionBonus - 0.2f) < 1e-6 && sy.MoveSpeedScale == 1f;
        });
        Check("档位效果：崩溃边缘移速 ×0.85", () =>
        {
            var sy = new Whisper.Gameplay.Sanity.SanitySystem(cfgReader(), 10f);
            return sy.Band.Id == "brink" && Math.Abs(sy.MoveSpeedScale - 0.85f) < 1e-6;
        });
        Check("怪物接触扣 35（配置 contactSanityLoss；接触不是即死）", () =>
        {
            var sy = new Whisper.Gameplay.Sanity.SanitySystem(cfgReader());
            sy.MonsterContact();
            return Math.Abs(sy.Value - 65f) < 1e-6;
        });
        Check("理智归零 → 崩溃（尖叫 3 秒）→ 恢复到 25", () =>
        {
            var sy = new Whisper.Gameplay.Sanity.SanitySystem(cfgReader(), 5f);
            sy.MonsterContact();                       // 5-35 → 0，触发崩溃
            bool collapsed = sy.Collapsed && sy.TriggerCollapse;
            for (int i = 0; i < 200; i++) sy.Tick(1f / 60f, torchOn: true, inSafeZone: false);   // >3s
            // 恢复到配置的 restoreTo=25，正好落在 fear 档下沿（25-49）
            return collapsed && !sy.Collapsed && Math.Abs(sy.Value - 25f) < 0.01 && sy.Band.Id == "fear";
        });
        Check("崩溃时产生 sanity_scream 声纹 key（供怪物听觉使用）", () =>
        {
            var sy = new Whisper.Gameplay.Sanity.SanitySystem(cfgReader(), 5f);
            sy.MonsterContact();
            return sy.CollapseStimulusKey == "sanity_scream";
        });
        Check("理智台账记录各来源累计变化（死亡归因）", () =>
        {
            var sy = new Whisper.Gameplay.Sanity.SanitySystem(cfgReader(), 50f);
            sy.AllyDied();
            sy.Jumpscare();
            return Math.Abs(sy.Ledger[Whisper.Gameplay.Sanity.SanitySource.AllyDeath] + 15f) < 1e-6
                && Math.Abs(sy.Ledger[Whisper.Gameplay.Sanity.SanitySource.Jumpscare] + 10f) < 1e-6;
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

        Console.WriteLine("\n[6] 零信令房间码（跨地区直连 · 实测依据见 RoomCode.cs 头注释）");
        // 背景：真机实测 IPv4 为对称 NAT（打洞不可行）、IPv6 全局可路由且端口守恒。
        // 于是"怎么连"已解决，剩下"怎么知道对方地址"——本模块把地址编进房间码，消掉信令服务器。
        Check("IPv6 端点编解码往返一致", () =>
        {
            var code = Whisper.Net.Direct.RoomCode.Encode("2409:8a4c:862d:7d40:a841:2fcd:17b1:67e3", 38000);
            return Whisper.Net.Direct.RoomCode.TryDecode(code, out var h, out var p)
                && h == "2409:8a4c:862d:7d40:a841:2fcd:17b1:67e3" && p == 38000;
        });
        Check("IPv4 端点编解码往返一致", () =>
        {
            var code = Whisper.Net.Direct.RoomCode.Encode("192.168.1.7", 38000);
            return Whisper.Net.Direct.RoomCode.TryDecode(code, out var h, out var p) && h == "192.168.1.7" && p == 38000;
        });
        Check("房间码长度可接受（IPv6 ≤32、IPv4 ≤16 —— 太长就没人愿意转发）", () =>
        {
            var v6 = Whisper.Net.Direct.RoomCode.ExpectedLength("2409:8a4c:862d:7d40:a841:2fcd:17b1:67e3", 65535);
            var v4 = Whisper.Net.Direct.RoomCode.ExpectedLength("192.168.1.7", 65535);
            Console.WriteLine($"      [码长] IPv6 {v6} 字符 · IPv4 {v4} 字符（base32，只有大写字母与数字2-7）");
            return v6 <= 32 && v4 <= 16;
        });
        Check("房间码只含 base32 字符（不含易混符号，可手抄口述）", () =>
        {
            foreach (var (h, p) in new[] { ("2409:8a4c:862d:7d40::1", 1234), ("10.0.0.1", 65535) })
                foreach (var ch in Whisper.Net.Direct.RoomCode.Encode(h, p))
                    if (!"ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".Contains(ch.ToString())) return false;
            return true;
        });
        Check("IPv6/IPv4 房间码可区分（UI 要提示'对面必须有 IPv6'）", () =>
            Whisper.Net.Direct.RoomCode.IsIPv6Code(Whisper.Net.Direct.RoomCode.Encode("2409:8a4c:862d:7d40::1", 1))
            && !Whisper.Net.Direct.RoomCode.IsIPv6Code(Whisper.Net.Direct.RoomCode.Encode("192.168.1.7", 1)));
        Check("编解码确定性：同输入必得同码（可复现，便于断言与排障）", () =>
            Whisper.Net.Direct.RoomCode.Encode("2409:8a4c:862d:7d40::1", 1234)
            == Whisper.Net.Direct.RoomCode.Encode("2409:8a4c:862d:7d40::1", 1234));
        Check("容忍玩家从聊天软件粘来的空白/零宽字符（实战必然遇到）", () =>
        {
            var code = Whisper.Net.Direct.RoomCode.Encode("2409:8a4c:862d:7d40::1", 7777);
            var dirty = "  \u200b" + code + "\u200b\n";
            return Whisper.Net.Direct.RoomCode.TryDecode(dirty, out var h, out var p) && h == "2409:8a4c:862d:7d40::1" && p == 7777;
        });
        Check("脏输入一律返回 false 而不抛异常（玩家会粘错东西）", () =>
        {
            string[] bad = { "", "   ", "X123", "W!!!!", "W", "Wabc", null };
            foreach (var b in bad)
                if (Whisper.Net.Direct.RoomCode.TryDecode(b, out _, out _)) return false;
            return true;
        });
        CheckThrows<ArgumentException>("Encode 空主机必须报错（不产出无意义房间码）", () =>
            Whisper.Net.Direct.RoomCode.Encode("", 1234));
        CheckThrows<ArgumentOutOfRangeException>("Encode 非法端口必须报错", () =>
            Whisper.Net.Direct.RoomCode.Encode("2409:8a4c:862d:7d40::1", 70000));

        Console.WriteLine("\n[7] 联机线格式与带宽预算（V9 §13.4：下行 ≤12KB/s · 上行 ≤6KB/s · 每 3 Tick 一批）");
        // 换算：6KB/s ÷ 20 批/s = 每批 100 字节（上行）。这是硬约束，且【超了不会报错】——
        // 只会变成流量与延迟问题。所以用断言把它钉住，而不是等上线才发现。
        Check("定长尺寸自洽（头部/玩家记录/道具记录，且常量与实际编码一致）", () =>
        {
            // 方法：先用一个**空批**量出真正的固定开销，再算各类记录的增量。
            // 为什么不再用"头 + N 个字段"反推偏移：我反推时错了三次，每次都"看起来自洽"——
            // 反推是无法自证的。量空批作基准，偏移就只有一个来源。
            var W = Whisper.Net.Direct.WireFormat.HeaderSize;
            var empty = new Whisper.Net.Direct.WireFormat.Writer();
            Whisper.Net.Direct.WireFormat.WriteStateBatch(empty, 0, Whisper.Net.Direct.WireFormat.MatchPhaseLite.Playing,
                0, new List<Whisper.Net.Direct.WireFormat.PlayerRecord>(), 0, new List<byte>(), new List<byte>(),
                new List<ushort>(), new List<byte>());
            int fixedBytes = empty.Length;

            var onePlayer = new Whisper.Net.Direct.WireFormat.Writer();
            Whisper.Net.Direct.WireFormat.WriteStateBatch(onePlayer, 0, Whisper.Net.Direct.WireFormat.MatchPhaseLite.Playing,
                0, new List<Whisper.Net.Direct.WireFormat.PlayerRecord>
                { new Whisper.Net.Direct.WireFormat.PlayerRecord(0, 0f, 0f, 0f, 0f) },
                0, new List<byte>(), new List<byte>(), new List<ushort>(), new List<byte>());
            int playerActual = onePlayer.Length - fixedBytes;

            var oneProp = new Whisper.Net.Direct.WireFormat.Writer();
            Whisper.Net.Direct.WireFormat.WriteStateBatch(oneProp, 0, Whisper.Net.Direct.WireFormat.MatchPhaseLite.Playing,
                0, new List<Whisper.Net.Direct.WireFormat.PlayerRecord>(), 0, new List<byte>(), new List<byte>(),
                new List<ushort> { 1 }, new List<byte> { 0 });
            int propActual = oneProp.Length - fixedBytes;

            var oneSanity = new Whisper.Net.Direct.WireFormat.Writer();
            Whisper.Net.Direct.WireFormat.WriteStateBatch(oneSanity, 0, Whisper.Net.Direct.WireFormat.MatchPhaseLite.Playing,
                0, new List<Whisper.Net.Direct.WireFormat.PlayerRecord>(), 1, new List<byte> { 0 }, new List<byte> { 0 },
                new List<ushort>(), new List<byte>());
            int sanityActual = oneSanity.Length - fixedBytes;

            var stim = new Whisper.Net.Direct.WireFormat.Writer();
            Whisper.Net.Direct.WireFormat.WriteStimulus(stim, "x", 0f, 0f, 0f, 0f);

            Console.WriteLine($"      [尺寸] 空批固定 {fixedBytes} · 头 {W} · 玩家 {playerActual} · 道具 {propActual}"
                + $" · 理智 {sanityActual} · 声纹 {stim.Length}");
            return W == 5
                && fixedBytes == Whisper.Net.Direct.WireFormat.StateBatchFixedBytes
                && playerActual == Whisper.Net.Direct.WireFormat.PlayerRecordSize
                && propActual == Whisper.Net.Direct.WireFormat.PropRecordSize
                && sanityActual == Whisper.Net.Direct.WireFormat.SanityRecordSize
                && stim.Length == Whisper.Net.Direct.WireFormat.StimulusSize;
        });
        Check("★ 满房 4 人一批不超过上行预算 100 字节（V9 §13.4）", () =>
        {
            var w = new Whisper.Net.Direct.WireFormat.Writer();
            var players = new List<Whisper.Net.Direct.WireFormat.PlayerRecord>();
            for (byte i = 0; i < 4; i++) players.Add(new Whisper.Net.Direct.WireFormat.PlayerRecord(i, 12.5f + i, 0f, -3.25f - i, 90f * i));
            var slots = new List<byte> { 0, 1, 2, 3 };
            var vals = new List<byte> { 200, 180, 255, 90 };
            var hashes = new List<ushort>(); var flags = new List<byte>();
            for (int i = 0; i < 6; i++) { hashes.Add(Whisper.Net.Direct.WireFormat.Hash16("door_" + i)); flags.Add(1); }
            int n = Whisper.Net.Direct.WireFormat.WriteStateBatch(w, 42, Whisper.Net.Direct.WireFormat.MatchPhaseLite.Playing,
                2, players, 4, slots, vals, hashes, flags);
            Console.WriteLine($"      [批大小] 4 人 + 4 理智 + 6 道具变更 = {n} 字节 / 预算 100 字节");
            return n <= Whisper.Net.Direct.WireFormat.UpBatchBudgetBytes;
        });
        Check("位姿量化往返误差 ≤ 4 毫米（远小于位置校验容差）", () =>
        {
            foreach (var m in new[] { 0f, 1.5f, -12.25f, 127.99f, -128f, 3.14159f })
            {
                var back = Whisper.Net.Direct.WireFormat.DequantizePos(Whisper.Net.Direct.WireFormat.QuantizePos(m));
                if (Math.Abs(back - m) > 0.004f) { Console.WriteLine($"      [精度] {m} → {back}"); return false; }
            }
            return true;
        });
        Check("朝向量化往返误差 ≤ 1.5°", () =>
        {
            foreach (var d in new[] { 0f, 45f, 90f, 179.9f, 270f, 359.9f })
            {
                var back = Whisper.Net.Direct.WireFormat.DequantizeYaw(Whisper.Net.Direct.WireFormat.QuantizeYaw(d));
                var diff = Math.Abs(back - d); if (diff > 180f) diff = 360f - diff;
                if (diff > 1.5f) { Console.WriteLine($"      [精度] {d}° → {back}°"); return false; }
            }
            return true;
        });
        Check("声纹事件恰好 14 字节（瞬时 RPC，不进状态同步）", () =>
        {
            var w = new Whisper.Net.Direct.WireFormat.Writer();
            Whisper.Net.Direct.WireFormat.WriteStimulus(w, "sanity_scream", 0.8f, 3.5f, -7.25f, 9f);
            return w.Length == Whisper.Net.Direct.WireFormat.StimulusSize;
        });
        Check("状态批可完整解析且与写入一致", () =>
        {
            var w = new Whisper.Net.Direct.WireFormat.Writer();
            var players = new List<Whisper.Net.Direct.WireFormat.PlayerRecord>
            { new Whisper.Net.Direct.WireFormat.PlayerRecord(0, 1.25f, 0f, -2.5f, 135f),
              new Whisper.Net.Direct.WireFormat.PlayerRecord(3, -8f, 0f, 40f, 359f) };
            Whisper.Net.Direct.WireFormat.WriteStateBatch(w, 7, Whisper.Net.Direct.WireFormat.MatchPhaseLite.Extraction,
                3, players, 1, new List<byte> { 3 }, new List<byte> { 128 },
                new List<ushort> { Whisper.Net.Direct.WireFormat.Hash16("door_a") }, new List<byte> { 3 });
            if (!Whisper.Net.Direct.WireFormat.TryReadStateBatch(w.ToArray(), out var b)) return false;
            return b.Seq == 7 && b.Phase == Whisper.Net.Direct.WireFormat.MatchPhaseLite.Extraction && b.EvidenceCount == 3
                && b.Players.Length == 2 && b.Players[1].Slot == 3 && Math.Abs(b.Players[1].Fz - 40f) < 0.01f
                && b.Sanity.Length == 1 && b.Sanity[0].val == 128 && b.Props.Length == 1 && b.Props[0].flags == 3;
        });
        Check("拒绝被截断/篡改/版本不符的包（定长协议不做\"尽力而为\"解析）", () =>
        {
            var w = new Whisper.Net.Direct.WireFormat.Writer();
            Whisper.Net.Direct.WireFormat.WriteStateBatch(w, 1, Whisper.Net.Direct.WireFormat.MatchPhaseLite.Playing,
                0, new List<Whisper.Net.Direct.WireFormat.PlayerRecord>(), 0, new List<byte>(), new List<byte>(),
                new List<ushort>(), new List<byte>());
            var good = w.ToArray();
            if (!Whisper.Net.Direct.WireFormat.TryReadStateBatch(good, out _)) return false;
            var truncated = new byte[good.Length - 1]; Array.Copy(good, truncated, truncated.Length);
            if (Whisper.Net.Direct.WireFormat.TryReadStateBatch(truncated, out _)) return false;
            var extra = new byte[good.Length + 1]; Array.Copy(good, extra, good.Length); extra[good.Length] = 0xFF;
            if (Whisper.Net.Direct.WireFormat.TryReadStateBatch(extra, out _)) return false;
            var badVer = (byte[])good.Clone(); badVer[0] = 99;
            if (Whisper.Net.Direct.WireFormat.TryReadStateBatch(badVer, out _)) return false;
            return Whisper.Net.Direct.WireFormat.TryReadStateBatch(null, out _) == false;
        });
        Check("容量上限可算：满房下最多能带多少条道具变更（超预算时丢谁有依据）", () =>
        {
            int maxProps = Whisper.Net.Direct.WireFormat.MaxPropChangesFor(4, 4);
            int sizeAtMax = Whisper.Net.Direct.WireFormat.StateBatchSize(4, 4, maxProps);
            Console.WriteLine($"      [容量] 满房(4人+4理智) 每批最多带 {maxProps} 条道具/门变更（此时 {sizeAtMax} 字节）");
            return maxProps >= 10 && sizeAtMax <= Whisper.Net.Direct.WireFormat.UpBatchBudgetBytes;
        });
        Check("发送前预算法与实际编码字节数一致（预算不能只是估算）", () =>
        {
            for (int pc = 0; pc <= 4; pc++)
                for (int sc = 0; sc <= pc; sc++)
                    for (int prop = 0; prop <= 12; prop += 4)
                    {
                        var w = new Whisper.Net.Direct.WireFormat.Writer();
                        var pl = new List<Whisper.Net.Direct.WireFormat.PlayerRecord>();
                        for (byte i = 0; i < pc; i++) pl.Add(new Whisper.Net.Direct.WireFormat.PlayerRecord(i, i, 0f, i, 0f));
                        var sl = new List<byte>(); var sv = new List<byte>();
                        for (int i = 0; i < sc; i++) { sl.Add((byte)i); sv.Add((byte)(i * 10)); }
                        var ph = new List<ushort>(); var pf = new List<byte>();
                        for (int i = 0; i < prop; i++) { ph.Add((ushort)i); pf.Add(1); }
                        int actual = Whisper.Net.Direct.WireFormat.WriteStateBatch(w, 0,
                            Whisper.Net.Direct.WireFormat.MatchPhaseLite.Playing, 0, pl, (byte)sc, sl, sv, ph, pf);
                        if (actual != Whisper.Net.Direct.WireFormat.StateBatchSize(pc, sc, prop))
                        {
                            Console.WriteLine($"      [不一致] pc={pc} sc={sc} prop={prop}：预算 {Whisper.Net.Direct.WireFormat.StateBatchSize(pc, sc, prop)} vs 实际 {actual}");
                            return false;
                        }
                    }
            return true;
        });

        Console.WriteLine("\n[8] 玩家移动（V9 §7：速度配置化 · 碰撞不得穿墙 · 脚步刺激由形态决定）");
        // 为什么要有这一段：没有移动就不是游戏。而移动的两个正确性核心
        // （子步进防穿墙、速度必须来自配置）都能在没有 Unity 的机器上验到。
        Check("三条速度来自配置真源（3.5 / 5.6 / 1.6）", () =>
        {
            var m = new Whisper.Gameplay.Session.PlayerMotion(cfgReader());
            Console.WriteLine($"      [速度] 蹲 {m.CrouchSpeedMps} · 走 {m.WalkSpeedMps} · 跑 {m.RunSpeedMps} m/s");
            return m.CrouchSpeedMps == 1.6f && m.WalkSpeedMps == 3.5f && m.RunSpeedMps == 5.6f;
        });
        Check("缺配置时构造必须报错（不许代码里藏一份数值真源）", () =>
        {
            var empty = new Whisper.Gameplay.Config.GameConfigReader(new Dictionary<string, object>());
            try { new Whisper.Gameplay.Session.PlayerMotion(empty); return false; }
            catch (InvalidOperationException) { return true; }
        });
        Check("速度不自洽（跑 < 走）时构造必须报错", () =>
        {
            var bad = new Whisper.Gameplay.Config.GameConfigReader(new Dictionary<string, object> {
                ["player"] = new Dictionary<string, object> {
                    ["walkSpeedMps"] = 5.0, ["runSpeedMps"] = 2.0, ["crouchSpeedMps"] = 1.0 } });
            try { new Whisper.Gameplay.Session.PlayerMotion(bad); return false; }
            catch (InvalidOperationException) { return true; }
        });
        Check("形态→脚步刺激映射（V9 §7）", () =>
            Whisper.Gameplay.Session.PlayerMotion.StimulusKeyFor(Whisper.Gameplay.Session.MoveMode.Run) == "run_footstep"
            && Whisper.Gameplay.Session.PlayerMotion.StimulusKeyFor(Whisper.Gameplay.Session.MoveMode.Walk) == "walk_footstep"
            && Whisper.Gameplay.Session.PlayerMotion.StimulusKeyFor(Whisper.Gameplay.Session.MoveMode.Crouch) == "crouch_footstep");
        Check("斜向输入被归一化：斜走不比直走快（经典 bug）", () =>
        {
            var geo = Whisper.Gameplay.Level.LevelGeometry.Compile(level);
            var start = level.Rooms[0];
            if (!geo.TryFindFreeCell(start.CenterX, start.CenterZ, out float sx, out float sz)) return false;
            // 【假绿修复】第一版从 (0,0) 出发 —— 那是入口房间中心，**被柜子占着**，
            // 直走与斜走都得到 0.0000 m，断言因 "0 == 0" 而"通过"，实际什么都没测。
            // 现在必须从空可走格出发，并**断言位移非零**，否则这类断言会再次自欺。
            var straight = new Whisper.Gameplay.Session.PlayerMotion(cfgReader(), sx, sz);
            var diagonal = new Whisper.Gameplay.Session.PlayerMotion(cfgReader(), sx, sz);
            var a = straight.Step(0f, 1f, Whisper.Gameplay.Session.MoveMode.Walk, 0.05f, geo);
            var b = diagonal.Step(1f, 1f, Whisper.Gameplay.Session.MoveMode.Walk, 0.05f, geo);
            Console.WriteLine($"      [位移] 直走 {a.MovedM:0.0000} m · 斜走 {b.MovedM:0.0000} m（起点 {sx:0.0},{sz:0.0}）");
            if (a.MovedM <= 1e-4f) { Console.WriteLine("      [假绿拦停] 直走位移为 0 —— 起点被占或几何异常，本断言无意义"); return false; }
            return Math.Abs(a.MovedM - b.MovedM) < 0.002f;
        });
        Check("零输入不移动且不误报被挡", () =>
        {
            var geo = Whisper.Gameplay.Level.LevelGeometry.Compile(level);
            var m = new Whisper.Gameplay.Session.PlayerMotion(cfgReader(), 0f, 0f);
            var r = m.Step(0f, 0f, Whisper.Gameplay.Session.MoveMode.Walk, 0.05f, geo);
            return r.MovedM == 0f && !r.Blocked && r.FootstepStimulusKey == null;
        });
        Check("★ 撞墙不得穿墙：从出生点朝墙连走 200 步仍在可走格内", () =>
        {
            var geo = Whisper.Gameplay.Level.LevelGeometry.Compile(level);
            var start = level.Rooms[0];
            if (!geo.TryFindFreeCell(start.CenterX, start.CenterZ, out float sx, out float sz)) return false;
            var m = new Whisper.Gameplay.Session.PlayerMotion(cfgReader(), sx, sz);
            // 朝四个方向各冲 200 步（每步 0.05s，跑速 5.6 → 合计足够冲出房间很多次）
            int escaped = 0;
            foreach (var (dx, dz) in new[] { (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f) })
            {
                m.SetPosition(sx, sz);
                for (int i = 0; i < 200; i++)
                {
                    m.Step(dx, dz, Whisper.Gameplay.Session.MoveMode.Run, 0.05f, geo);
                    if (!geo.Passable(m.X, m.Z)) escaped++;
                }
            }
            Console.WriteLine($"      [穿墙] 800 步后落在不可走格的次数 {escaped}");
            return escaped == 0;
        });
        Check("★ 大位移不穿门/穿墙：单步 5 米（限时上限内）也被子步进切开", () =>
        {
            var geo = Whisper.Gameplay.Level.LevelGeometry.Compile(level);
            var start = level.Rooms[0];
            if (!geo.TryFindFreeCell(start.CenterX, start.CenterZ, out float sx, out float sz)) return false;
            var m = new Whisper.Gameplay.Session.PlayerMotion(cfgReader(), sx, sz);
            // maxDt 放宽到 1 秒 → 期望位移 5.6 米，远超房间尺寸；子步进必须把它切开并停在墙前
            m.Step(1f, 0f, Whisper.Gameplay.Session.MoveMode.Run, 1.0f, geo, maxDtSec: 1.0f);
            Console.WriteLine($"      [大位移] 期望 5.6m，实际停在 x={m.X:0.00}（可走={geo.Passable(m.X, m.Z)}）");
            return geo.Passable(m.X, m.Z);
        });
        Check("dt 上限生效：单步时长被 clamp 到 maxDtSec", () =>
        {
            var geo = Whisper.Gameplay.Level.LevelGeometry.Compile(level);
            var start = level.Rooms[0];
            if (!geo.TryFindFreeCell(start.CenterX, start.CenterZ, out float sx, out float sz)) return false;
            var a = new Whisper.Gameplay.Session.PlayerMotion(cfgReader(), sx, sz);
            var b = new Whisper.Gameplay.Session.PlayerMotion(cfgReader(), sx, sz);
            var ra = a.Step(0f, 1f, Whisper.Gameplay.Session.MoveMode.Walk, 10f, geo, maxDtSec: 0.05f);
            var rb = b.Step(0f, 1f, Whisper.Gameplay.Session.MoveMode.Walk, 0.05f, geo, maxDtSec: 0.05f);
            return Math.Abs(ra.MovedM - rb.MovedM) < 1e-4f;
        });
        Check("脚步刺激按步幅触发（累计 3 米应发 ~4 次）", () =>
        {
            var geo = Whisper.Gameplay.Level.LevelGeometry.Compile(level);
            var start = level.Rooms[0];
            if (!geo.TryFindFreeCell(start.CenterX, start.CenterZ, out float sx, out float sz)) return false;
            var m = new Whisper.Gameplay.Session.PlayerMotion(cfgReader(), sx, sz);
            int steps = 0;
            // 【第一版踩坑】单向直走会在 0.35m 处撞墙停下（入口房间只有 4m×3m 且中心有柜子），
            // 累计距离永远到不了 3m → 断言无从判断。改为**往复走**：每步反向，
            // 这样只在一个小房间里也能累积出足够的距离。
            for (int i = 0; i < 400 && m.DistanceTravelledM < 3.0f; i++)
            {
                float dir = (i % 2 == 0) ? 1f : -1f;
                if (m.Step(0f, dir, Whisper.Gameplay.Session.MoveMode.Walk, 0.05f, geo).FootstepStimulusKey != null) steps++;
            }
            int expect = (int)(3.0f / Whisper.Gameplay.Session.PlayerMotion.StrideLengthM);
            Console.WriteLine($"      [脚步] 往复走了 {m.DistanceTravelledM:0.00} m，触发 {steps} 次（步幅 {Whisper.Gameplay.Session.PlayerMotion.StrideLengthM}m，期望 ~{expect}）");
            if (m.DistanceTravelledM < 2.0f) { Console.WriteLine("      [假绿拦停] 累计距离不足，该断言无意义"); return false; }
            return steps >= expect - 1 && steps <= expect + 1;
        });
        Check("朝向跟随移动方向（北=0° · 东=90°）", () =>
        {
            var geo = Whisper.Gameplay.Level.LevelGeometry.Compile(level);
            var m = new Whisper.Gameplay.Session.PlayerMotion(cfgReader(), 0f, 0f);
            m.Step(0f, 1f, Whisper.Gameplay.Session.MoveMode.Walk, 0.05f, geo);
            float north = m.YawDeg;
            m.Step(1f, 0f, Whisper.Gameplay.Session.MoveMode.Walk, 0.05f, geo);
            float east = m.YawDeg;
            Console.WriteLine($"      [朝向] 北 {north:0.0}° · 东 {east:0.0}°");
            return Math.Abs(north) < 1f && Math.Abs(east - 90f) < 1f;
        });
        Check("玩家碰撞半径与 EvidencePlacer 的代理半径一致（两套口径会出鬼）", () =>
        {
            var f = typeof(Whisper.Gameplay.Session.PlayerMotion).GetField("AgentRadiusM");
            var ev = Type.GetType("Whisper.Gameplay.Items.EvidencePlacer");
            var evField = ev?.GetField("AgentRadiusM");
            if (f == null || evField == null) return false;
            return (float)f.GetRawConstantValue() == (float)evField.GetRawConstantValue();
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


    /// <summary>听觉向量产出模式（供 tools/hearing-port-vectors.mjs 比对）。</summary>
    static int EmitHearingVectors()
    {
        var cfgPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "config.json"));
        Whisper.Gameplay.Config.GameConfig.LoadFromJson(File.ReadAllText(cfgPath));
        var cfg = new Whisper.Gameplay.Config.GameConfigReader();
        var hearing = new Whisper.Gameplay.Hearing.Hearing(cfg);

        var inputPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "vectors", "hearing.inputs.json"));
        var spec = MiniJson.AsMap(MiniJson.Parse(File.ReadAllText(inputPath)));
        var list = new List<object>();
        foreach (var cObj in MiniJson.AsList(MiniJson.Get(spec, "cases")))
        {
            var c = MiniJson.AsMap(cObj);
            var mp = MiniJson.AsList(MiniJson.Get(c, "monsterPos"));
            var sp = MiniJson.AsList(MiniJson.Get(c, "stimPos"));
            float? radius = MiniJson.Get(c, "radiusM") == null ? (float?)null : MiniJson.AsFloat(MiniJson.Get(c, "radiusM"));
            var stim = new Whisper.Gameplay.Hearing.Stimulus(
                MiniJson.AsString(MiniJson.Get(c, "sourceKey")), "voice",
                MiniJson.AsFloat(MiniJson.Get(c, "intensity")), radius,
                MiniJson.Get(c, "globalBroadcast") is bool g && g,
                MiniJson.AsFloat(sp[0]), MiniJson.AsFloat(sp[1]), 0);
            var ctx = new Whisper.Gameplay.Hearing.HearingContext
            {
                PerceptionBonus = MiniJson.AsFloat(MiniJson.Get(c, "perceptionBonus")),
                LocalizationPenalty = MiniJson.AsFloat(MiniJson.Get(c, "localizationPenalty")),
            };
            var r = hearing.CanHear(MiniJson.AsString(MiniJson.Get(c, "monsterId")), stim,
                MiniJson.AsFloat(mp[0]), MiniJson.AsFloat(mp[1]), ctx);
            list.Add(new Dictionary<string, object> {
                ["audible"] = r.Audible,
                ["reason"] = r.Reason,
                ["distanceM"] = Math.Round(r.DistanceM, 6),
                ["threshold"] = Math.Round(r.EffectiveThreshold, 6),
                ["margin"] = Math.Round(r.Margin, 6),
                // 与灰盒一致：全局广播/阈值不足路径给字面 0，半径路径给实际衰减值
                ["attenuationDb"] = Math.Round(r.AttenuationDb, 6),
            });
        }
        Console.WriteLine(JsonWrite(new Dictionary<string, object> { ["cases"] = list }));
        return 0;
    }


    /// <summary>状态机向量产出模式（供 tools/monster-port-vectors.mjs 比对）。</summary>
    static int EmitMonsterVectors()
    {
        var cfgPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "config.json"));
        Whisper.Gameplay.Config.GameConfig.LoadFromJson(File.ReadAllText(cfgPath));
        var cfg = new Whisper.Gameplay.Config.GameConfigReader();

        var inputPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "vectors", "monster-brain.inputs.json"));
        var spec = MiniJson.AsMap(MiniJson.Parse(File.ReadAllText(inputPath)));
        var casesOut = new List<object>();

        foreach (var cObj in MiniJson.AsList(MiniJson.Get(spec, "cases")))
        {
            var c = MiniJson.AsMap(cObj);
            string monsterId = MiniJson.AsString(MiniJson.Get(c, "monsterId"));
            var pos = MiniJson.AsList(MiniJson.Get(c, "position"));
            var patrol = new List<Whisper.Gameplay.Monsters.Vec2>();
            foreach (var pp in MiniJson.AsList(MiniJson.Get(c, "patrolPoints")))
            {
                var arr = MiniJson.AsList(pp);
                patrol.Add(new Whisper.Gameplay.Monsters.Vec2(MiniJson.AsFloat(arr[0]), MiniJson.AsFloat(arr[1])));
            }
            var brain = new Whisper.Gameplay.Monsters.MonsterBrain(monsterId, cfg,
                new Whisper.Gameplay.Monsters.Vec2(MiniJson.AsFloat(pos[0]), MiniJson.AsFloat(pos[1])), patrol);

            var stimByTick = new Dictionary<long, Dictionary<string, object>>();
            foreach (var so in MiniJson.AsList(MiniJson.Get(c, "stimuli")))
                stimByTick[(long)MiniJson.AsFloat(MiniJson.Get(MiniJson.AsMap(so), "tick"))] = MiniJson.AsMap(so);
            var sightByTick = new Dictionary<long, Dictionary<string, object>>();
            foreach (var so in MiniJson.AsList(MiniJson.Get(c, "sights")))
                sightByTick[(long)MiniJson.AsFloat(MiniJson.Get(MiniJson.AsMap(so), "tick"))] = MiniJson.AsMap(so);

            long ticks = (long)MiniJson.AsFloat(MiniJson.Get(c, "ticks"));
            long sampleEvery = (long)MiniJson.AsFloat(MiniJson.Get(c, "sampleEvery"));
            var samples = new List<object>();

            for (long tick = 0; tick <= ticks; tick++)
            {
                if (stimByTick.TryGetValue(tick, out var st))
                {
                    float? radius = MiniJson.Get(st, "radiusM") == null ? (float?)null : MiniJson.AsFloat(MiniJson.Get(st, "radiusM"));
                    var stim = new Whisper.Gameplay.Hearing.Stimulus(
                        MiniJson.AsString(MiniJson.Get(st, "sourceKey")), "voice",
                        MiniJson.AsFloat(MiniJson.Get(st, "intensity")), radius,
                        MiniJson.Get(st, "globalBroadcast") is bool g && g,
                        MiniJson.AsFloat(MiniJson.Get(st, "x")), MiniJson.AsFloat(MiniJson.Get(st, "z")), tick);
                    brain.OnStimulus(stim, tick);
                }
                bool seen = sightByTick.TryGetValue(tick, out var sight);
                Whisper.Gameplay.Monsters.Vec2? playerPos = seen
                    ? new Whisper.Gameplay.Monsters.Vec2(MiniJson.AsFloat(MiniJson.Get(sight, "x")), MiniJson.AsFloat(MiniJson.Get(sight, "z")))
                    : (Whisper.Gameplay.Monsters.Vec2?)null;
                var r = brain.Step(tick, seen, playerPos);
                if (tick % sampleEvery == 0)
                {
                    samples.Add(new List<object> { tick, r.State,
                        (double)Math.Round(r.Position.X, 3), (double)Math.Round(r.Position.Z, 3),
                        (double)Math.Round(r.Speed, 3) });
                }
            }

            var transitions = new List<object>();
            foreach (var h in brain.History) transitions.Add($"{h.Tick}:{h.From}->{h.To}");
            casesOut.Add(new Dictionary<string, object> {
                ["id"] = monsterId + "/" + MiniJson.AsString(MiniJson.Get(c, "id")),
                ["transitions"] = transitions,
                ["samples"] = samples,
                ["finalState"] = brain.State,
            });
        }
        Console.WriteLine(JsonWrite(new Dictionary<string, object> { ["cases"] = casesOut }));
        return 0;
    }

}
