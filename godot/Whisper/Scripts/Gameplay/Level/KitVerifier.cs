using System;
using System.Collections.Generic;
using System.IO;

namespace Whisper.Gameplay.Level
{
    /// <summary>
    /// 套件资产加载与校验（**纯逻辑，不引用 UnityEngine**）。
    ///
    /// ## 为什么要有这个类，而不是直接在 KitMeshLibrary 里读文件
    /// 本项目反复出现"**验证过 ≠ 在产品里**"（几何层 / 内容管线 / 怪物实例化 / 理智系统，
    /// 四次）。防它的唯一办法是让**产品与验证走同一条代码路径**：
    ///   · 产品：<c>KitMeshLibrary</c> 用 <c>Application.streamingAssetsPath</c> 作为根目录调这里；
    ///   · 本机跑手（native/csharp-verify）：用仓库里的 <c>unity/Assets/StreamingAssets</c> 作为根目录调这里。
    /// 于是"本机断言绿"与"真机能读出来"是同一段代码的同一个结果，而不是两套实现各自自证。
    ///
    /// ## 为什么落 StreamingAssets 而不是 Resources（2026-10-04 实测踩坑）
    /// Resources 下的 <c>.glb</c> 会被 Unity 用 **ModelImporter** 导入成"模型资产"——Unity 原生
    /// 不支持 glTF，于是既没有网格、**也不是文本资产**，按文本资产去取**永远拿到 null**。
    /// StreamingAssets 不经导入器：原始字节原样进包，运行时直接按字节读
    /// （Android 上就是 APK 内的 assets 条目，同样可读）。
    /// </summary>
    public static class KitVerifier
    {
        /// <summary>套件在包内的相对路径（与 tools/gen-kit-resources.mjs 的 resPath 一致）。</summary>
        public const string KitDirInPackage = "Kits";

        /// <summary>单个套件的加载结果。</summary>
        public sealed class KitLoad
        {
            public string Id;
            public bool Ok;
            public string Problem;
            public int Bytes;
            public int Parts;
            public int Vertices;
            public int Triangles;
            public GlbReaderPure.Model Model;
            public override string ToString() =>
                Ok ? $"{Id}: {Parts} 部件 · {Vertices} 顶点 · {Triangles} 面 · {Bytes} B"
                   : $"{Id}: 失败（{Problem}）";
        }

        /// <summary>
        /// 从给定根目录加载清单里的全部套件。
        /// <paramref name="rootDir"/> = 产品里传 Application.streamingAssetsPath，跑手里传仓库路径。
        /// **从不抛异常**：单个套件坏掉只让那一条 Ok=false，不影响其余（资产坏不该炸掉启动）。
        /// </summary>
        public static List<KitLoad> LoadAll(string rootDir, string manifestJson)
        {
            var results = new List<KitLoad>();
            if (string.IsNullOrEmpty(rootDir)) { results.Add(new KitLoad { Id = "<root>", Problem = "根目录为空" }); return results; }
            if (string.IsNullOrEmpty(manifestJson)) { results.Add(new KitLoad { Id = "<manifest>", Problem = "清单为空" }); return results; }

            Dictionary<string, object> manifest;
            try { manifest = MiniJson.AsMap(MiniJson.Parse(manifestJson)); }
            catch (Exception ex) { results.Add(new KitLoad { Id = "<manifest>", Problem = "清单解析失败：" + ex.Message }); return results; }

            var kits = MiniJson.GetOrNull(manifest, "kits") as List<object>;
            if (kits == null) { results.Add(new KitLoad { Id = "<manifest>", Problem = "清单缺 kits" }); return results; }

            foreach (var kitObj in kits)
            {
                var kit = kitObj as Dictionary<string, object>;
                if (kit == null) continue;
                string id = MiniJson.GetOrNull(kit, "id") as string;
                string resPath = MiniJson.GetOrNull(kit, "resPath") as string;
                var load = new KitLoad { Id = id };
                results.Add(load);

                if (string.IsNullOrEmpty(id)) { load.Problem = "清单条目缺 id"; continue; }
                // resPath 形如 "Kits/hall_main.glb"，落点固定在 StreamingAssets 下
                string rel = !string.IsNullOrEmpty(resPath) ? resPath : KitDirInPackage + "/" + id + ".glb";
                string full = Path.Combine(rootDir, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full)) { load.Problem = "文件不存在：" + rel; continue; }

                byte[] bytes;
                try { bytes = File.ReadAllBytes(full); }
                catch (Exception ex) { load.Problem = "读取失败：" + ex.Message; continue; }

                load.Bytes = bytes.Length;
                if (!GlbReaderPure.TryRead(bytes, out var model, out var reason)) { load.Problem = "解析失败：" + reason; continue; }
                load.Model = model;
                load.Parts = model.Primitives.Count;
                load.Vertices = model.VertexCount;
                load.Triangles = model.TriangleCount;
                load.Ok = load.Parts > 0 && load.Vertices > 0 && load.Triangles > 0;
                if (!load.Ok) load.Problem = "几何为空";
            }
            return results;
        }

        /// <summary>一行摘要（HUD / 日志 / 自检都打这一行，口径统一）。</summary>
        public static string Summary(List<KitLoad> loads)
        {
            int ok = 0;
            var bad = new List<string>();
            foreach (var l in loads) { if (l.Ok) ok++; else bad.Add(l.Id + "(" + l.Problem + ")"); }
            string line = $"套件 {ok}/{loads.Count} 可加载";
            if (bad.Count > 0) line += " · 失败：" + string.Join("；", bad);
            return line;
        }
    }
}
