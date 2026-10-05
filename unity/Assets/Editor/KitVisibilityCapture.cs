using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Whisper.Editor
{
    /// <summary>
    /// 套件可见性取证：把接入的套件**用像素证据**拍下来，并做"存在/缺失"对照。
    ///
    /// ## 为什么必须做 A/B 对照，而不是只渲染一张好看图
    /// 一张图只能说明"画面里有东西"，不能说明"套件真的进了渲染"——程序化方块同样会出现在画面里。
    /// 本项目的失败模式是"验证过 ≠ 在产品里"，所以判据是**差异**：同一相机、同一场景、同一光照，
    /// 唯一变量是**套件文件在不在**。两组图可分辨地不同，才证明套件确实参与了渲染。
    ///
    /// ## 为什么 B 组是"删掉 StreamingAssets 里的文件"
    /// 这正是产品的真实失败路径：`KitMeshLibrary.GetParts` 读不到文件 → 返回 null →
    /// 关卡走 `if (!kitPlaced)` 兜底建程序化体块。所以 B 组不是假代码，是**产品自己的兜底分支**。
    /// 每次运行结束（含异常）都会还原文件，绝不把仓库留在删除状态。
    ///
    /// ## 取帧方式：离屏 RenderTexture（**不用** ScreenCapture）
    /// 【实测踩坑】最初用 `ScreenCapture.CaptureScreenshot`：在 `-batchmode` 下它依赖游戏循环
    /// 末尾写文件，脚本 `EditorApplication.Exit` 后一帧都没跑完 → **一张图都不产出**（而且不报错）。
    /// 改成 `Camera.Render()` 渲染到 `RenderTexture` 再 `ReadPixels` → `EncodeToPNG` → 写文件：
    /// 同步、确定、与是否 headless 无关。
    ///
    /// ## 用法
    ///   Unity -quit -batchmode -projectPath "&lt;proj&gt;" -executeMethod Whisper.Editor.KitVisibilityCapture.Run -captureDir "&lt;dir&gt;" -logFile "&lt;log&gt;"
    /// </summary>
    public static class KitVisibilityCapture
    {
        /// <summary>
        /// 拍哪些房间。**这里的 kit 必须与关卡 DSL 里该房间实际引用的套件一致**，否则取证拍的是
        /// "另一个套件在不在渲染"，证据无效。
        /// 【2026-10-04 更新】按房型出套件后，`entrance_safe` / `corridor_link` / `corridor_ward`
        /// 各自用**自己的** hall 变体（`hall_main_entrance_safe` 等），`corridor_main` 仍用 `hall_main`。
        /// 另外走廊段描述里的尺寸也从旧的 16m 改为实际的主廊 18m。
        /// </summary>
        static readonly (string room, string kit, string why)[] Targets =
        {
            ("corridor_main", "hall_main",                 "走廊主段：18m×3m 的 hall_main 壳体（立柱 + 顶梁 + 桥架）"),
            ("entrance_safe", "hall_main_entrance_safe",   "安全区：4m×3m 专属变体 + cabinet_a 机柜道具"),
            ("corridor_ward", "hall_main_corridor_ward",   "住院廊：15m×1m 专属变体（门洞对位的墙裙/门套）"),
            ("morgue_deep",   "morgue",                    "太平间深区：morgue 壳体（三层冷柜 + 排水沟格栅）"),
            ("ward_01",       "hospital_ward",             "病房 01：hospital_ward 壳体 + bed_b 病床"),
            ("ward_04",       "hospital_ward",             "病房 04：hospital_ward 壳体 + cabinet_a 机柜"),
        };

        const int Width = 960, Height = 600;

        static GameObject _levelGo;
        static Whisper.Gameplay.Level.LevelBuilder _lb;
        static string _kitDir;
        static string _stashDir;
        static string MarkerPath => Path.Combine(Path.GetTempPath(), "whisper-kits-stash.marker");

        public static void Run()
        {
            string outDir = ArgValue("-captureDir");
            if (string.IsNullOrEmpty(outDir))
                outDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "_evidence", "kit-view");
            outDir = Path.GetFullPath(outDir);
            Directory.CreateDirectory(outDir);

            _kitDir = Path.Combine(Application.streamingAssetsPath, "Kits");
            Debug.Log($"[CAPTURE] 输出 {outDir}");
            Debug.Log($"[CAPTURE] 套件目录 {_kitDir}（存在={Directory.Exists(_kitDir)}）");

            var levelAsset = Resources.Load<TextAsset>("Levels/asylum_v1");
            if (levelAsset == null) { Debug.LogError("[CAPTURE] 找不到 Resources/Levels/asylum_v1"); EditorApplication.Exit(2); return; }
            var levelMap = Whisper.Gameplay.Level.MiniJson.AsMap(Whisper.Gameplay.Level.MiniJson.Parse(levelAsset.text));
            var rooms = Whisper.Gameplay.Level.MiniJson.AsList(Whisper.Gameplay.Level.MiniJson.GetOrNull(levelMap, "rooms"));

            var camGo = new GameObject("CaptureCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.03f, 1f);
            cam.enabled = false;   // 手动 Render

            var index = new System.Text.StringBuilder();
            index.AppendLine("room,kit,shot,phase,file,mean_luma,stddev,distinct_colors");
            int written = 0;

            try
            {
                foreach (var (roomId, kitId, why) in Targets)
                {
                    var room = FindRoom(rooms, roomId);
                    if (room == null) { Debug.LogWarning($"[CAPTURE] 关卡里没有房间 {roomId}"); continue; }
                    GetRoomBounds(room, out float cx, out float cz, out float sx, out float sz, out float sy);

                    var views = new (string name, Vector3 pos, Vector3 look, float fov)[]
                    {
                        // 高角度 3/4 俯视：拉远到 8~14m 看整间壳体（第一版只拉 3~5m，相机直接埋进墙体里）
                        ($"orbit33", new Vector3(cx + Mathf.Max(sx * 1.6f, 6.0f), Mathf.Max(sy * 1.5f, 6.5f), cz - Mathf.Max(sz * 1.6f, 6.5f)), new Vector3(cx, 1.0f, cz), 55f),
                        // 平视：站在房间内看摆件落位
                        // 平视：相机**必须在房间内部**（见 EyePosition 的注释：第一版算到墙体内，产出空帧）
                        ($"eye",     EyePosition(cx, cz, sz),                                                                              new Vector3(cx, 1.15f, cz + sz * 0.5f), 70f),
                    };

                    for (int phase = 0; phase < 2; phase++)
                    {
                        bool withKits = phase == 0;
                        if (withKits) RestoreKits(); else HideKits();
                        Whisper.Gameplay.Level.KitMeshLibrary.Clear();
                        BuildScene();

                        // 每种视图拍两种光照：
                        //   · dark = 与游戏一致的光照（3D 视图的暗调，证据"观感像不像"）
                        //   · lit  = 取证专用的检视光照（把暗调压掉，证据"套件在不在、摆得对不对"）
                        // 两种都留，避免"用好看的图冒充游戏观感"。
                        foreach (var v in views)
                        {
                            cam.fieldOfView = v.fov;
                            camGo.transform.position = v.pos;
                            camGo.transform.LookAt(v.look);
                            foreach (var lit in new[] { false, true })
                            {
                                SetInspectionLights(camGo.transform, lit);
                                string suffix = (withKits ? "A-with" : "B-without") + (lit ? "_lit" : "_dark");
                                string file = Path.Combine(outDir, $"{roomId}_{kitId}_{v.name}_{suffix}.png");
                                var stats = RenderTo(cam, file);
                                written++;
                                index.AppendLine($"{roomId},{kitId},{v.name},{(withKits ? "A" : "B")},{(lit ? "lit" : "dark")},{Path.GetFileName(file)},{stats.luma:0.0},{stats.std:0.0},{stats.colors}");
                                Debug.Log($"[CAPTURE] {roomId}/{kitId}/{v.name} {(withKits ? "A-有套件" : "B-无套件")}/{(lit ? "检视光" : "游戏光")} → {Path.GetFileName(file)} · 亮度 {stats.luma:0.0} · 标准差 {stats.std:0.0} · 颜色数 {stats.colors}   （{why}）");
                            }
                        }
                    }
                    RestoreKits();
                }
            }
            finally
            {
                RestoreKits();   // 无论如何都还原：不能把仓库的套件留在删除状态
                Whisper.Gameplay.Level.KitMeshLibrary.Clear();
            }

            File.WriteAllText(Path.Combine(outDir, "capture-index.csv"), index.ToString());
            Debug.Log($"[CAPTURE] 完成：{written} 张图 → {outDir} · 套件已还原={File.Exists(Path.Combine(_kitDir, "hall_main.glb"))}");
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// 取证专用检视光照：挂在相机上、随相机走，把暗调压掉以便**看清几何**。
        /// 这是**证据照明，不是游戏观感** —— 所以每次都另出一张 dark（游戏光）图，两张并列供对照，
        /// 不用"好看的图"冒充游戏观感（本项目对"证据要诚实"有明确要求）。
        /// </summary>
        static void SetInspectionLights(Transform camT, bool bright)
        {
            if (_lights == null)
            {
                _lights = new GameObject[4];
                for (int i = 0; i < _lights.Length; i++)
                {
                    _lights[i] = new GameObject("CaptureLight" + i);
                    var l = _lights[i].AddComponent<Light>();
                    l.type = LightType.Directional;
                    _lights[i].transform.SetParent(camT, false);   // 挂相机上：相机对着哪就照亮哪
                }
                _lights[0].transform.localRotation = Quaternion.identity;                     // 相机正前方
                _lights[1].transform.localRotation = Quaternion.Euler(0f, 120f, 0f);
                _lights[2].transform.localRotation = Quaternion.Euler(0f, -120f, 0f);
                _lights[3].transform.localRotation = Quaternion.Euler(-70f, 0f, 0f);           // 自上而下
            }
            foreach (var go in _lights)
            {
                var l = go.GetComponent<Light>();
                l.intensity = bright ? 1.1f : 0f;
                l.color = Color.white;
            }
        }

        static GameObject[] _lights;

        /// <summary>
        /// 平视取景点：**必须保证相机在房间内部**。
        ///
        /// 【实测事故】第一版用 `cz - max(sz*0.42, 1.4)`，对 3 个房间算出的相机位置**落在南墙体内**
        /// （复核者按同一公式算出距南墙内面 −0.01 m），于是那些 `eye` 图是**空帧**（颜色数 1~2）。
        /// 我据此写下"玩家站在房间里看不出套件"——那是**归因错误**，不是产品问题。
        /// 现在改成：从房间中心沿 −Z 退到"距南墙内面仍有 margin"的位置。
        /// </summary>
        static Vector3 EyePosition(float cx, float cz, float sz)
        {
            const float margin = 0.6f;                       // 离墙留白，务必 > nearClipPlane
            float maxBack = Mathf.Max(sz * 0.5f - margin, 0.1f);
            return new Vector3(cx, 1.55f, cz - maxBack);
        }

        /// <summary>离屏渲染一帧并写 PNG，同时返回像素统计（亮度/标准差/颜色数）。</summary>
        static (double luma, double std, int colors) RenderTo(Camera cam, string path)
        {
            var rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = prev;

            var px = tex.GetPixels32();
            double sum = 0, sum2 = 0;
            var seen = new System.Collections.Generic.HashSet<int>();
            foreach (var c in px)
            {
                double luma = 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
                sum += luma; sum2 += luma * luma;
                seen.Add((c.r >> 3 << 10) | (c.g >> 3 << 5) | (c.b >> 3));   // 每通道 5 位量化
            }
            int n = px.Length;
            double mean = sum / n;
            double std = Math.Sqrt(Math.Max(0, sum2 / n - mean * mean));

            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            RenderTexture.ReleaseTemporary(rt);
            return (mean, std, seen.Count);
        }

        /// <summary>用产品的 LevelBuilder 装配场景（不是自己搭一套，避免"验的不是产品"）。</summary>
        static void BuildScene()
        {
            var levelText = Resources.Load<TextAsset>("Levels/asylum_v1");
            if (levelText == null) { Debug.LogError("[CAPTURE] 缺 Resources/Levels/asylum_v1"); return; }

            // 已知套件集合：与 GameBootstrap.LoadKitIds() 同一口径（读 asset-manifest 的 kits[].id）。
            // 这里照抄而不调用它，因为它是 GameBootstrap 的私有实例方法；关键路径
            // （LevelBuilder.Build → KitMeshLibrary.GetParts → GlbReader）仍全是产品代码。
            var manifest = Resources.Load<TextAsset>("Data/asset-manifest");
            var knownKits = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            if (manifest != null)
            {
                var root = Whisper.Gameplay.Level.MiniJson.AsMap(Whisper.Gameplay.Level.MiniJson.Parse(manifest.text));
                if (Whisper.Gameplay.Level.MiniJson.GetOrNull(root, "kits") is System.Collections.Generic.List<object> kits)
                    foreach (var k in kits)
                        if (Whisper.Gameplay.Level.MiniJson.GetOrNull(k as System.Collections.Generic.Dictionary<string, object>, "id") is string id)
                            knownKits.Add(id);
            }

            var level = Whisper.Gameplay.Level.LevelLoader.Load(levelText.text, knownKits);

            // 每次重建前销毁上一轮的关卡对象：LevelBuilder.Build 只清它自己建的那些子对象，
            // 但 A/B 两相之间换的是"套件在不在"，必须确保旧网格彻底消失（否则 B 组会看到 A 组的残影）。
            if (_levelGo != null) UnityEngine.Object.DestroyImmediate(_levelGo);
            _levelGo = new GameObject("Level");
            _lb = _levelGo.AddComponent<Whisper.Gameplay.Level.LevelBuilder>();
            _lb.Build(level, knownKits);
            Debug.Log($"[CAPTURE] 场景建好：用套件的房间 {_lb.KitRooms.Count} 个 · 道具 {_lb.PropObjects.Count}"
                    + $" · 套件问题={(Whisper.Gameplay.Level.LevelBuilder.KitProblem ?? "无")}");
        }

        /// <summary>
        /// 套件的**两个落点**都必须藏起来，否则 A/B 失效。
        ///
        /// 【实测事故 · 2026-10-04】第一版只删 `StreamingAssets/Kits/*.glb`，但
        /// `KitMeshLibrary.LoadBytes` 后来改成**优先读 `Resources/Kits/*.glb.bytes`** ——
        /// 于是 B 组照样能读到套件，A/B 变成"自己和自己比"（重跑会得到 20/20 全 0.0%）。
        /// 独立复核者用三相位渲染抓到了这一点：A 与 B1 五个视角**逐字节相同**。
        /// 教训：**"把被测对象关掉"必须针对被测代码的真实取值路径**，而不是我以为的那条。
        /// </summary>
        static readonly string[] KitDirs =
        {
            "Assets/StreamingAssets/Kits",   // 兜底路径（产物取证走这条）
            "Assets/Resources/Kits",         // 首选路径（运行时优先读这条）
        };

        static void HideKits()
        {
            if (_stashDir != null) return;
            RecoverIfNeeded();                       // 先处理上次崩溃留下的残留
            _stashDir = Path.Combine(Path.GetTempPath(), "whisper-kits-stash-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_stashDir);
            var moved = new System.Collections.Generic.List<string>();
            int n = 0;
            foreach (var rel in KitDirs)
            {
                string dir = Path.Combine(ProjectRoot, rel);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir, "*.glb*"))
                {
                    if (f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;   // 保留 .meta，避免 Unity 重新导入
                    string dst = Path.Combine(_stashDir, $"{Path.GetFileName(rel.Replace('/', '_'))}__{Path.GetFileName(f)}");
                    File.Copy(f, dst, true);        // ① 备份
                    moved.Add(dst);
                    File.Delete(f);                 // ③ 删原件
                    n++;
                }
            }
            File.WriteAllText(MarkerPath, _stashDir + "\n" + string.Join("\n", moved.ToArray()));   // ② marker（含清单，便于恢复）
            // 【实测事故 · 2026-10-04 第二次踩】只删磁盘文件**不够**：Unity 的资产数据库仍持有
            // 已导入的 TextAsset，`Resources.Load` 照样返回它 —— 于是 B 组日志仍是
            // "用套件的房间 11 个 · 套件问题=无"，A/B 又变成自己比自己。
            // 必须让 Unity 重新扫描资产目录，把"文件没了"这件事同步进资产数据库。
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"[CAPTURE] B 组：已藏起 {n} 个套件文件（两个落点都藏 + AssetDatabase.Refresh；暂存 {_stashDir}）");
        }

        static void RestoreKits()
        {
            if (_stashDir == null) { RecoverIfNeeded(); return; }
            int n = RestoreFrom(_stashDir);
            Directory.Delete(_stashDir, true);
            if (File.Exists(MarkerPath)) File.Delete(MarkerPath);
            _stashDir = null;
            // 同理：还原后也要让 Unity 重新扫描，否则 A 组可能拿到"资产不存在"的缓存状态
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"[CAPTURE] A 组：已还原 {n} 个套件文件（+ AssetDatabase.Refresh）");
        }

        /// <summary>按 marker 里记录的"来源目录"把文件放回原处。</summary>
        static int RestoreFrom(string dir)
        {
            int n = 0;
            foreach (var rel in KitDirs)
            {
                string target = Path.Combine(ProjectRoot, rel);
                Directory.CreateDirectory(target);
                string prefix = Path.GetFileName(rel.Replace('/', '_')) + "__";
                foreach (var f in Directory.GetFiles(dir, prefix + "*"))
                {
                    string name = Path.GetFileName(f).Substring(prefix.Length);
                    File.Copy(f, Path.Combine(target, name), true);
                    n++;
                }
            }
            return n;
        }

        /// <summary>若存在上次运行遗留的暂存（说明崩在 B 组），把套件恢复回仓库。</summary>
        static void RecoverIfNeeded()
        {
            if (!File.Exists(MarkerPath)) return;
            string first = File.ReadAllLines(MarkerPath)[0].Trim();
            if (!Directory.Exists(first)) { File.Delete(MarkerPath); return; }
            int n = RestoreFrom(first);
            Directory.Delete(first, true);
            File.Delete(MarkerPath);
            Debug.Log($"[CAPTURE] 从上一次崩溃中恢复 {n} 个套件文件（两个落点）");
        }

        static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        // ── 房间几何：与 LevelBuilder 同一口径（position = (x, floor*3.5, z)）──
        static System.Collections.Generic.Dictionary<string, object> FindRoom(System.Collections.Generic.List<object> rooms, string id)
        {
            foreach (var r in rooms)
            {
                var m = r as System.Collections.Generic.Dictionary<string, object>;
                if (m != null && (Whisper.Gameplay.Level.MiniJson.GetOrNull(m, "id") as string) == id) return m;
            }
            return null;
        }

        static void GetRoomBounds(System.Collections.Generic.Dictionary<string, object> room,
            out float cx, out float cz, out float sx, out float sz, out float sy)
        {
            var pos = Whisper.Gameplay.Level.MiniJson.AsList(Whisper.Gameplay.Level.MiniJson.GetOrNull(room, "pos"));
            var size = Whisper.Gameplay.Level.MiniJson.AsList(Whisper.Gameplay.Level.MiniJson.GetOrNull(room, "size"));
            cx = Whisper.Gameplay.Level.MiniJson.AsFloat(pos[0]);
            cz = Whisper.Gameplay.Level.MiniJson.AsFloat(pos[1]);
            sx = Whisper.Gameplay.Level.MiniJson.AsFloat(size[0]);
            sz = Whisper.Gameplay.Level.MiniJson.AsFloat(size[2]);
            sy = Whisper.Gameplay.Level.MiniJson.AsFloat(size[1]);
        }

        static string ArgValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
