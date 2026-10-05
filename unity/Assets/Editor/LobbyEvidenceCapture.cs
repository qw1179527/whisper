using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Whisper.Editor
{
    /// <summary>
    /// 大厅（`HallScene`）的像素取证。
    ///
    /// ## 为什么必须单独写一个（而不是复用 `RenderEvidenceCapture`）
    /// `RenderEvidenceCapture` 按**关卡房间**取景（`LoadRooms()` 读关卡 DSL → `Targets`），
    /// 而**大厅是代码搭的、不属于任何关卡** —— 于是它对大厅无能为力。
    /// 结果就是：P1（大厅套件重做）**改之前没有基线图、改之后无法比对**。
    /// 本类补上这一块。
    ///
    /// ## 取帧方式：离屏 RenderTexture（**不用** ScreenCapture）
    /// 照抄 `RenderEvidenceCapture` 的实测结论：`ScreenCapture.CaptureScreenshot` 在
    /// `-batchmode` 下依赖帧末回调，**不可靠**；`Camera.Render()` → `RenderTexture` →
    /// `ReadPixels` → `EncodeToPNG` 是同步、确定的。
    ///
    /// ⚠ **不要加 `-nographics`** —— `Camera.Render()` 在无图形设备下会崩 `0xC0000005`。
    /// 云上必须 `xvfb-run`（见 `unity-agent.yml` 的 `run_xvfb`）。
    ///
    /// ## 判据（两层，缺一即红 —— 照 render-evidence.sh 的教训）
    /// ① 真 Unity 退出码；② **日志里必须出现 `LOBBY_EVIDENCE OK`**。
    /// 只看退出码不够：`executeMethod` 名字写错会"静默成功"（本仓已有先例）。
    /// </summary>
    public static class LobbyEvidenceCapture
    {
        const int W = 960, H = 600;

        /// <summary>取景点：名字 + 相对大厅中心的偏移 + 朝向（yaw）。</summary>
        struct View { public string name; public Vector3 pos; public float yaw; public bool judged; }

        public static void Run()
        {
            var problems = new List<string>();
            string outDir = ArgValue("-captureDir");
            if (string.IsNullOrEmpty(outDir))
                outDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "_evidence", "lobby");
            outDir = Path.GetFullPath(outDir);
            Directory.CreateDirectory(outDir);
            Debug.Log($"[LOBBY] 输出 {outDir}");

            // ── 场景：根 + 相机 + HallScene ─────────────────────────────────
            // 为什么自己建相机：HallScene 的构造签名要求 Camera（它要把菜单板的拾取面挂给相机）。
            var root = new GameObject("LobbyEvidenceRoot");
            var camGo = new GameObject("EvidenceCamera", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;

            var hall = new Whisper.Runtime.HallScene(root.transform, cam);
            hall.Build();
            Debug.Log($"[LOBBY] 大厅建好：{hall.WidthM:F1}m × {hall.LengthM:F1}m");

            // ── 取景点（覆盖 P1 要看的四件事）────────────────────────────────
            // 坐标一律由大厅实测尺寸推导，不写死 —— 大厅改尺寸后取景点自动跟着走。
            float hx = hall.WidthM * 0.5f, hz = hall.LengthM * 0.5f;
            var views = new[]
            {
                // 入口视角：官方要求"玩家一进来就面对菜单板"
                new View { name = "entrance", pos = new Vector3(0f, 1.7f, -hz * 0.75f), yaw = 0f, judged = true },
                // 俯视全景：看整体布局与套件拼接
                new View { name = "orbit",    pos = new Vector3(hx * 0.55f, hz * 0.55f, -hz * 0.85f), yaw = 205f, judged = true },
                // 货车特写（§三 的重点对象）
                new View { name = "truck",    pos = new Vector3(-hx * 0.25f, 1.8f, -hz * 0.15f), yaw = 155f, judged = true },
                // 菜单板正视
                new View { name = "board",    pos = new Vector3(0f, 1.6f, hz * 0.15f), yaw = 180f, judged = true },
            };

            // ── 灯光相位：唯一变量是灯 ──────────────────────────────────────
            // HallScene.BuildLights 建的光存在 root 下；关灯相位把它们 enabled=false。
            var lights = root.GetComponentsInChildren<Light>(true);
            if (lights.Length == 0) problems.Add("大厅一个 Light 都没有 —— 光照相位无从对照（HallScene.BuildLights 没跑到？）");
            Debug.Log($"[LOBBY] 场景内光源 {lights.Length} 个");

            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var index = new StringBuilder("view,phase,file,mean,stddev\n");
            int written = 0;

            foreach (var v in views)
            {
                foreach (var lightOn in new[] { true, false })
                {
                    foreach (var l in lights) l.enabled = lightOn;
                    camGo.transform.position = v.pos;
                    camGo.transform.rotation = Quaternion.Euler(0f, v.yaw, 0f);

                    string phase = lightOn ? "lightOn" : "lightOff";
                    string file = Path.Combine(outDir, $"lobby_{v.name}_{phase}.png");
                    var stats = Shoot(cam, rt, tex, file);
                    index.AppendLine($"{v.name},{phase},{Path.GetFileName(file)},{stats.mean:F1},{stats.std:F1}");
                    if (stats.mean == 0f && stats.std == 0f)
                        problems.Add($"{v.name}/{phase} 判为**全黑**（亮度 0 · 标准差 0）—— 几何或着色器没进来");
                    written++;
                    Debug.Log($"[LOBBY] {v.name}/{phase} → {Path.GetFileName(file)} · 亮度 {stats.mean:F1} · 标准差 {stats.std:F1}");
                }
            }

            // ── 判据：开灯 vs 关灯必须可分辨 ────────────────────────────────
            // 这是 P1 的核心判据之一（大厅的光照有没有真的生效）。
            foreach (var v in views)
            {
                if (!v.judged) continue;
                string on = Path.Combine(outDir, $"lobby_{v.name}_lightOn.png");
                string off = Path.Combine(outDir, $"lobby_{v.name}_lightOff.png");
                if (!File.Exists(on) || !File.Exists(off)) continue;
                float diff = PixelDiffPct(on, off);
                Debug.Log($"[LOBBY] {v.name} 开灯 vs 关灯 变化 {diff:F3}%");
                if (diff < 1.0f) problems.Add($"{v.name} 开灯 vs 关灯只变化 {diff:F3}%（<1%）—— 灯对大厅渲染没有实际作用");
            }

            File.WriteAllText(Path.Combine(outDir, "lobby-index.csv"), index.ToString());
            rt.Release();
            Object.DestroyImmediate(tex);

            if (problems.Count == 0)
                Debug.Log($"LOBBY_EVIDENCE OK · {written} 张图 · 判据全部成立");
            else
            {
                foreach (var p in problems) Debug.LogError("[LOBBY] ✗ " + p);
                Debug.LogError($"LOBBY_EVIDENCE FAIL · {problems.Count} 项判据不成立");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        /// <summary>离屏渲染一帧并存 PNG，返回亮度统计（用于"全黑"判据）。</summary>
        static (float mean, float std) Shoot(Camera cam, RenderTexture rt, Texture2D tex, string file)
        {
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = prev;

            var px = tex.GetPixels32();
            double sum = 0, sum2 = 0;
            foreach (var c in px)
            {
                double l = (0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b);
                sum += l; sum2 += l * l;
            }
            int n = px.Length;
            double mean = sum / n;
            double var = sum2 / n - mean * mean;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            return ((float)mean, (float)System.Math.Sqrt(var < 0 ? 0 : var));
        }

        /// <summary>两张 PNG 的变化像素占比（%，阈值口径与 tools/pixel-diff-pair.mjs 一致）。</summary>
        static float PixelDiffPct(string a, string b)
        {
            var ta = new Texture2D(2, 2, TextureFormat.RGB24, false);
            var tb = new Texture2D(2, 2, TextureFormat.RGB24, false);
            ta.LoadImage(File.ReadAllBytes(a));
            tb.LoadImage(File.ReadAllBytes(b));
            if (ta.width != tb.width || ta.height != tb.height) return 100f;
            var pa = ta.GetPixels32(); var pb = tb.GetPixels32();
            int diff = 0;
            for (int i = 0; i < pa.Length; i++)
            {
                int d = System.Math.Abs(pa[i].r - pb[i].r) + System.Math.Abs(pa[i].g - pb[i].g) + System.Math.Abs(pa[i].b - pb[i].b);
                if (d > 12) diff++;   // 每通道均差 4 —— 与 pixel-diff-pair.mjs 的同口径
            }
            Object.DestroyImmediate(ta); Object.DestroyImmediate(tb);
            return 100f * diff / pa.Length;
        }

        static string ArgValue(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
