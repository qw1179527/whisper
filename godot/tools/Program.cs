using System;
using System.IO;
using Whisper.Gameplay.Level;

// 用**纯 C# 的 GlbReaderPure** 去解析仓库里全部真实套件 ——
// 判据：每个 .glb 都要能解析出 部件>0 · 顶点>0 · 三角形>0。
// 这件事过去只有 Unity 端能做（539 行 GlbReader），现在引擎无关层自己就能验证。
static class Program
{
    static int Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : "unity/Assets/ThirdParty/CC0/kits";
        if (!Directory.Exists(dir)) { Console.WriteLine("目录不存在: " + dir); return 2; }
        var files = Directory.GetFiles(dir, "*.glb", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);
        int ok = 0, bad = 0;
        foreach (var f in files)
        {
            var bytes = File.ReadAllBytes(f);
            if (!GlbReaderPure.TryRead(bytes, out var m, out var why))
            {
                bad++; Console.WriteLine($"  ✗ {Path.GetFileName(f)} → {why}"); continue;
            }
            bool good = m.Primitives.Count > 0 && m.VertexCount > 0 && m.TriangleCount > 0;
            if (good) ok++; else bad++;
            Console.WriteLine($"  {(good ? "✓" : "✗")} {Path.GetFileName(f),-44} {GlbReaderPure.Describe(m)}");
        }
        Console.WriteLine($"\n套件 {ok}/{files.Length} 可解析" + (bad > 0 ? $" · **{bad} 个失败**" : ""));
        return bad == 0 ? 0 : 1;
    }
}
