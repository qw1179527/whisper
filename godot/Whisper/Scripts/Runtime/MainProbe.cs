using Godot;
using System.Collections.Generic;

namespace Whisper.Runtime
{
    /// <summary>
    /// **Godot 端最小可看场景** —— 用搬过来的引擎无关核心 + Godot 的 GLB 导入，
    /// 把疗养院的房间**真的摆出来**。
    ///
    /// ══════════════════════════════════════════════════════════════════════════════
    /// 与 Unity 端 `LevelBuilder` 的口径对齐（这是复刻，不是重新设计）
    /// ══════════════════════════════════════════════════════════════════════════════
    /// Unity 侧（`LevelBuilder.BuildRoom`）：
    /// ```csharp
    /// roomGo.transform.position = new Vector3(r.CenterX, r.Floor * 3.5f, r.CenterZ);
    /// // 套件部件按**局部坐标原样**摆进去
    /// ```
    /// ⇒ 本文件照抄同一条：**房间根节点放在 (CenterX, Floor*层高, CenterZ)，套件局部坐标不动**。
    ///   `LevelAssembly.Build(level)` 算出的装配计划（墙段/门板/道具）本阶段只报数、不建。
    ///
    /// ⚠ **本阶段如实界定**：
    ///   · 只摆**套件几何**（地面/天花/立柱），不建程序化墙体、不建门板、不摆道具
    ///   · 层高 3.5m 是 Unity 侧常量，此处**同值复刻**（将来应进配置表）
    ///   · 未做房间裁剪（Unity 侧也有同样的已知不足：套件按房间中心整块摆）
    ///
    /// ══════════════════════════════════════════════════════════════════════════════
    /// 自证：**帧缓冲读回**（不靠人截图）
    /// ══════════════════════════════════════════════════════════════════════════════
    /// 等若干帧后把 viewport 纹理读成 Image，统计亮度与颜色数，存 PNG 并写报告 ——
    /// 于是"画面有没有出来"变成**本机可判的读数**，不必等人截图。
    /// </summary>
    public partial class MainProbe : Node3D
    {
        const float LevelHeightM = 3.5f;   // 与 Unity 侧 LevelBuilder 同值

        const string WorkDir = "/data/user/0/app.dsh.mobile/files/dsh-home/whisper";
        /// <summary>
        /// 输出目录：**真机上必须写共享存储**，否则我读不到图。
        /// 桌面/CI 没有这个路径 ⇒ 回落到工程目录（`_Ready` 里探测）。
        /// </summary>
        static string OutDir()
        {
            const string shared = "/storage/emulated/0/DSH专用/godot-probe";
            try { System.IO.Directory.CreateDirectory(shared); return shared; }
            catch { return WorkDir + "/godot"; }
        }
        const string LevelPath = WorkDir + "/unity/Assets/Levels/asylum_v1.json";
        const string ManifestPath = WorkDir + "/unity/Assets/Data/asset-manifest.json";
        const string KitsDir = WorkDir + "/unity/Assets/ThirdParty/CC0/kits";

        readonly List<string> _log = new List<string>();
        int _frame;
        bool _done;

        public override void _Ready()
        {
            _log.Add($"Godot {Engine.GetVersionInfo()["string"]} · 最小可看场景");
            try { BuildScene(); }
            catch (System.Exception e) { _log.Add($"✗ 装配异常：{e.GetType().Name}: {e.Message}"); }

            // 相机：**第一人称站在入口朝走廊**（与 Unity 侧 eye 取景同一意图）
            // 入口房间 entrance_safe 在 x[0,4] z[0,3]，走廊沿 +X 展开 ⇒ 站在入口往 +X 看。
            var cam = new Camera3D { Position = new Vector3(1.6f, 1.6f, 1.5f), Current = true, Fov = 70f };
            AddChild(cam);
            cam.LookAt(new Vector3(22f, 1.2f, 1.5f), Vector3.Up);

            // 环境光 + 方向光：先保证"有光"，氛围是后续阶段
            var env = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.05f, 0.06f, 0.08f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.55f, 0.58f, 0.65f),
                AmbientLightEnergy = 1.1f,
            };
            AddChild(new WorldEnvironment { Environment = env });
            AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55, -35, 0), LightEnergy = 1.2f });

            foreach (var l in _log) GD.Print("[Whisper] " + l);
        }

        void BuildScene()
        {
            if (!System.IO.File.Exists(LevelPath)) { _log.Add($"✗ 关卡文件不存在：{LevelPath}"); return; }
            var json = System.IO.File.ReadAllText(LevelPath);

            var kits = new HashSet<string>(System.StringComparer.Ordinal);
            if (System.IO.File.Exists(ManifestPath))
                foreach (System.Text.RegularExpressions.Match m in
                    System.Text.RegularExpressions.Regex.Matches(
                        System.IO.File.ReadAllText(ManifestPath), "\"id\"\\s*:\\s*\"([^\"]+)\""))
                    kits.Add(m.Groups[1].Value);

            var level = Whisper.Gameplay.Level.LevelLoader.Load(json, kits);
            _log.Add($"✓ 关卡 {level.LevelId}：房间 {level.Rooms.Count} · 走廊 {level.Corridors.Count}");

            var plan = Whisper.Gameplay.Level.LevelAssembly.Build(level);
            _log.Add($"✓ 装配计划：墙段 {plan.Walls.Count} · 门板 {plan.Doors.Count} · 道具 {plan.Props.Count}");

            int placed = 0, missing = 0, meshNodes = 0;
            var kitCache = new Dictionary<string, Node3D>();
            foreach (var room in level.Rooms)
            {
                string kitPath = $"{KitsDir}/{room.Kit}.glb";
                if (!kitCache.TryGetValue(room.Kit, out var proto))
                {
                    if (!System.IO.File.Exists(kitPath)) { missing++; continue; }
                    var doc = new GltfDocument();
                    var st = new GltfState();
                    var err = doc.AppendFromFile(kitPath, st);
                    if (err != Error.Ok) { _log.Add($"  ✗ 套件加载失败 {room.Kit}：{err}"); missing++; continue; }
                    // ⚠ `GenerateScene` 返回 **Node**（不是 PackedScene）——
                    //   我第一版按 `PackedScene` 缓存 + `Instantiate()` ⇒ CS0029。
                    //   正确做法：缓存这个 Node 原型，每次用 `Duplicate()` 造一份。
                    proto = doc.GenerateScene(st) as Node3D;
                    if (proto == null) { _log.Add($"  ✗ 套件 {room.Kit} 生成的不是 Node3D"); missing++; continue; }
                    kitCache[room.Kit] = proto;
                }
                var inst = proto.Duplicate() as Node3D;
                if (inst == null) { missing++; continue; }
                inst.Name = "Room_" + room.Id;
                // ── 与 Unity 侧同一口径 ──
                inst.Position = new Vector3(room.CenterX, room.Floor * LevelHeightM, room.CenterZ);
                AddChild(inst);
                placed++;
                meshNodes += CountMeshInstances(inst);
            }
            _log.Add($"✓ 已摆房间 {placed} 个（缺套件 {missing}）· 套件缓存 {kitCache.Count} 种 · 网格节点 {meshNodes}");
            _log.Add("  相机 (1.6, 1.6, 1.5) 第一人称朝 +X（沿走廊）");
        }

        static int CountMeshInstances(Node n)
        {
            int c = n is MeshInstance3D ? 1 : 0;
            foreach (var ch in n.GetChildren()) c += CountMeshInstances(ch);
            return c;
        }

        public override void _Process(double delta)
        {
            if (_done) return;
            if (++_frame < 10) return;      // 等渲染稳定
            _done = true;

            string verdict;
            var vp = GetViewport();
            var tex = vp?.GetTexture();
            if (tex == null) verdict = "✗ 拿不到 viewport 纹理（headless 下正常；需真机/带显示环境）";
            else
            {
                var img = tex.GetImage();
                if (img == null) verdict = "✗ GetImage() 返回 null";
                else
                {
                    long n = 0; double sum = 0; var seen = new HashSet<int>();
                    for (int y = 0; y < img.GetHeight(); y += 4)
                        for (int x = 0; x < img.GetWidth(); x += 4)
                        {
                            var c = img.GetPixel(x, y);
                            sum += 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
                            n++;
                            seen.Add(((int)(c.R * 31) << 10) | ((int)(c.G * 31) << 5) | (int)(c.B * 31));
                        }
                    double mean = n > 0 ? sum / n : 0;
                    verdict = $"帧缓冲 {img.GetWidth()}x{img.GetHeight()} · 平均亮度 {mean:0.0} · 颜色数 {seen.Count}"
                        + (mean > 1.0 ? " ⇒ **有画面**" : " ⇒ **近黑（没出画面）**");
                    img.SavePng(OutDir() + "/probe-frame.png");
                    verdict += $" · 已存 {OutDir()}/probe-frame.png";
                }
            }
            _log.Add(verdict);
            GD.Print("[Whisper] " + verdict);
            WriteProbeFile();
        }

        void WriteProbeFile()
        {
            try
            {
                using (var w = new System.IO.StreamWriter(OutDir() + "/probe-report.txt", false))
                    foreach (var l in _log) w.WriteLine(l);
            }
            catch (System.Exception e) { GD.Print("[Whisper] 写报告失败：" + e.Message); }
        }
    }
}
