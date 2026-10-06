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
            // ⚠ 【第一版的错，记下来】原来我按"大厅是矩形、四角取景"的思路写死了坐标，
            //   结果 `truck` 那张拍到的是菜单板那面墙 —— **车在对角线上，完全没入镜**。
            //   根因：写死坐标 = 我对场景布局的猜测；而 `HallScene` **本来就暴露了真实位置**
            //   （`MenuBoardPos` / `Truck.WorldBounds` / `ViewPos` / `ViewLookAt`）。
            //   ⇒ 现在一律**从场景实测位置反推相机位姿**，布局改了取景点自动跟着走。
            float hx = hall.WidthM * 0.5f, hz = hall.LengthM * 0.5f;

            // ① 入口：直接问大厅"玩家该站哪、该看哪" —— 这是它自己的语义，不是我猜的
            Vector3 entryPos = hall.ViewPos.sqrMagnitude > 0.01f
                ? hall.ViewPos
                : new Vector3(0f, 1.7f, -hz * 0.75f);
            Vector3 entryLook = hall.ViewPos.sqrMagnitude > 0.01f
                ? hall.ViewLookAt
                : new Vector3(0f, 1.6f, -hz);

            // ② 货车：用它的世界包围盒中心 —— 车挪到哪都能拍到
            Vector3 truckAt = hall.Truck != null
                ? hall.Truck.WorldBounds.center
                : new Vector3(hall.WidthM * 0.30f, 1.5f, hall.LengthM * 0.22f);

            // ③ 菜单板：同上
            Vector3 boardAt = hall.MenuBoardPos.sqrMagnitude > 0.01f
                ? hall.MenuBoardPos
                : new Vector3(0f, 2.55f, -hz);

            var views = new[]
            {
                // 入口视角：官方要求"玩家一进来就面对菜单板"
                new View { name = "entrance", pos = entryPos, yaw = YawTo(entryPos, entryLook), judged = true },
                // 俯视全景：从高处斜看整个仓库。
                // ⚠ 【我自己的回归，记下来】第一版写成 `y = hz * 0.85`（= 7.65m），
                //   而**仓库层高只有 5.6m ⇒ 相机在屋顶外面**，拍到的是全黑（亮度 7.0 · 变化 0.000%），
                //   判据直接判红。更糟的是**我当时没检查这一项的判定**，让它带着红了两个 run。
                //   ⇒ 高度改为**由大厅自己的层高推导**（0.72 × FloorHeightM ≈ 4.0m，稳在室内），
                //     不再写一个"看起来够高"的数 —— 层高改了它也自动跟着走。
                new View { name = "orbit",    pos = new Vector3(hx * 0.70f, hall.FloorHeightM * 0.72f, -hz * 0.95f),
                            yaw = YawTo(new Vector3(hx * 0.70f, 0f, -hz * 0.95f), Vector3.zero), judged = true },
                // 货车特写：站在车的斜前方回望它（距离按车长推，保证整车入镜）
                new View { name = "truck",    pos = truckAt + new Vector3(-hx * 0.55f, 1.4f, -hz * 0.70f),
                            yaw = YawTo(truckAt + new Vector3(-hx * 0.55f, 0f, -hz * 0.70f), truckAt), judged = true },
                // 菜单板正视：站到板前一段距离平视它
                new View { name = "board",    pos = boardAt + new Vector3(0f, -0.9f, hz * 0.85f),
                            yaw = YawTo(boardAt + new Vector3(0f, 0f, hz * 0.85f), boardAt), judged = true },
                // 货架区（左墙）：**这个取景点是补上的，理由值得记**
                // 前几轮把 10 个工业道具接进工程后，我**没有任何一张图能证明它们出现在画面里** ——
                // 因为既有的四个取景点都不覆盖左墙货架（`HallScene.BuildProps` 把货架放在
                // `x = -WidthM/2 + 0.9`），而唯一看全景的 `orbit` 当时正被我弄坏（相机在屋顶外）。
                // ⇒ **改了东西却没有能验证它的取景点，等于没改。** 取景点的覆盖范围要与改动范围对齐。
                new View { name = "storage",  pos = new Vector3(-hx * 0.5f, 1.9f, -hz * 0.10f),
                            yaw = YawTo(new Vector3(-hx * 0.5f, 0f, -hz * 0.10f),
                                        new Vector3(-hx * 0.92f, 0.9f, -hz * 0.10f)), judged = false },
            };
            Debug.Log($"[LOBBY] 取景点：入口({entryPos.x:F1},{entryPos.z:F1}) · 货车({truckAt.x:F1},{truckAt.z:F1}) · 菜单板({boardAt.x:F1},{boardAt.z:F1})");

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

        /// <summary>
        /// 从 <paramref name="from"/> 看向 <paramref name="to"/> 的水平朝向（度）。
        ///
        /// 为什么要有这个：第一版我把 yaw 写死成数字，结果相机朝着菜单板却起名 "truck"。
        /// **角度是人最容易写错、也最难一眼看出的东西** —— 而"从 A 看向 B"是场景自己就知道的事实。
        /// Unity 里相机前向是 +Z，故 yaw = atan2(dx, dz)。
        /// </summary>
        static float YawTo(Vector3 from, Vector3 to)
        {
            float dx = to.x - from.x, dz = to.z - from.z;
            if (Mathf.Abs(dx) < 1e-4f && Mathf.Abs(dz) < 1e-4f) return 0f;   // 重合：不转
            return Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
        }

        static string ArgValue(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
