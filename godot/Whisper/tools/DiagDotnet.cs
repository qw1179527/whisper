#if TOOLS
using Godot;

/// <summary>诊断：把 Godot 看到的 dotnet 项目设置与它在找的路径打出来。</summary>
[Tool]
public partial class DiagDotnet : EditorScript
{
    public override void _Run()
    {
        GD.Print("=== Godot 的 dotnet 项目设置 ===");
        string[] keys = {
            "dotnet/project/assembly_name",
            "dotnet/project/solution_directory",
            "dotnet/project/place_solution_and_project_in_same_directory",
        };
        foreach (var k in keys)
            GD.Print($"  {k} = {(ProjectSettings.HasSetting(k) ? ProjectSettings.GetSetting(k).ToString() : "(未设置)")}");

        GD.Print("=== 磁盘上的项目文件 ===");
        foreach (var f in new[] { "res://Whisper.csproj", "res://Whisper.sln", "res://project.godot" })
        {
            bool abs = FileAccess.FileExists(f);
            GD.Print($"  {f} 存在: {abs}");
        }
        GD.Print($"res:// 实际路径: {ProjectSettings.GlobalizePath("res://")}");
    }
}
#endif
