using Godot;

namespace Whisper.Runtime
{
    /// <summary>
    /// **Godot 端最小探针**：证明"C# 核心能在 Godot 运行时里跑"。
    ///
    /// 它不是渲染层，也不做任何装配 —— 只做一件事：
    /// 调用**搬过来的引擎无关核心**（`Whisper.Gameplay.*`），把真实关卡数据解析出来，
    /// 并把结果显示在屏幕上。
    ///
    /// 判据（屏幕上直接可读）：
    ///   · `MiniJson` 能否解析真实关卡 JSON
    ///   · `LevelLoader` 能否通过校验并给出房间/走廊/事件计数
    ///   · `GlbReaderPure` 能否解析真实套件
    /// 这三条过去只有 Unity 端能跑；现在在 Godot 里跑 —— 那就证明**核心真的可移植**。
    /// </summary>
    public partial class MainProbe : Node3D
    {
        public override void _Ready()
        {
            var lines = new System.Text.StringBuilder();
            lines.Append($"Godot {Engine.GetVersionInfo()["string"]} · 引擎无关核心探针\n");

            // ── ① 读真实关卡 JSON（从工程外的工作区路径读；本阶段只验证逻辑）────────
            const string levelPath = "/data/user/0/app.dsh.mobile/files/dsh-home/whisper/unity/Assets/Levels/asylum_v1.json";
            const string manifestPath = "/data/user/0/app.dsh.mobile/files/dsh-home/whisper/unity/Assets/Data/asset-manifest.json";
            try
            {
                if (!System.IO.File.Exists(levelPath))
                {
                    lines.Append($"✗ 关卡文件不存在：{levelPath}\n");
                }
                else
                {
                    var json = System.IO.File.ReadAllText(levelPath);
                    var kits = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
                    if (System.IO.File.Exists(manifestPath))
                    {
                        var mf = System.IO.File.ReadAllText(manifestPath);
                        foreach (System.Text.RegularExpressions.Match m in
                            System.Text.RegularExpressions.Regex.Matches(mf, "\"id\"\\s*:\\s*\"([^\"]+)\""))
                            kits.Add(m.Groups[1].Value);
                    }
                    var level = Whisper.Gameplay.Level.LevelLoader.Load(json, kits);
                    lines.Append($"✓ 关卡 {level.LevelId}：房间 {level.Rooms.Count} · 走廊 {level.Corridors.Count} · 事件 {level.Events.Count}\n");
                    lines.Append($"  套件清单 {kits.Count} 个 · 撤离点 {(level.Extraction != null ? level.Extraction.Standard : "-")}\n");
                }
            }
            catch (System.Exception e)
            {
                lines.Append($"✗ 关卡加载失败：{e.GetType().Name}: {e.Message}\n");
            }

            // ── ② 用纯 C# 解析器读一个真实套件 ────────────────────────────────────
            try
            {
                const string kit = "/data/user/0/app.dsh.mobile/files/dsh-home/whisper/unity/Assets/ThirdParty/CC0/kits/morgue.glb";
                if (System.IO.File.Exists(kit))
                {
                    var bytes = System.IO.File.ReadAllBytes(kit);
                    if (Whisper.Gameplay.Level.GlbReaderPure.TryRead(bytes, out var model, out var why))
                        lines.Append($"✓ 套件 morgue.glb：{Whisper.Gameplay.Level.GlbReaderPure.Describe(model)}\n");
                    else
                        lines.Append($"✗ 套件 morgue.glb 解析失败：{why}\n");
                }
                else lines.Append("✗ 找不到 morgue.glb\n");
            }
            catch (System.Exception e)
            {
                lines.Append($"✗ 套件解析异常：{e.GetType().Name}: {e.Message}\n");
            }

            var text = lines.ToString();
            GD.Print("[Whisper] " + text.Replace("\n", " | "));

            // 屏幕上也要能看到（真机/截图判据；本工程暂无 UI，用 Label 直接画）
            var label = new Label();
            label.Text = text;
            label.Position = new Vector2(20, 20);
            label.AddThemeFontSizeOverride("font_size", 26);
            AddChild(label);

            // 一盏灯 + 一个方块：确认渲染管线在这台设备上真的出像素（与逻辑探针分开判定）
            AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, -30, 0) });
            var box = new MeshInstance3D { Mesh = new BoxMesh() };
            box.Position = new Vector3(0, 0, -4);
            AddChild(box);
            AddChild(new Camera3D { Position = new Vector3(0, 1, 2), Current = true });
        }
    }
}
