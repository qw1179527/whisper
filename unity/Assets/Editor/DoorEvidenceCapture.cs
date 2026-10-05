using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Whisper.Editor
{
    /// <summary>
    /// 门系统取证：把"门真的会开合"拍成**像素证据**，并核对"一个物理洞口只有一块门扇"。
    ///
    /// ## 为什么门也要像素取证（而不只是几何断言）
    /// 几何断言能证明"关着挡路、开着能过"，但证明不了**玩家看得见门在动**：
    /// 旧实现给每个 DSL 条目建一块填满门洞的实心板 → ① 10 对完全同位共面（z-fighting）
    /// ② 门永远是"关着的样子"，开门只改了碰撞、画面纹丝不动。
    /// 这两条都只能靠像素看出来，所以判据是：**同机位、同场景，唯一变量是门的开合进度**。
    ///
    /// ## 判据（任一不成立即 exit 1，日志写 `[DOOR] ✗ …`）
    ///   ① 结构：门扇根对象数 == 物理洞口数（DSL 20 个键 → 10 个洞口；相等才说明没有同位重复）
    ///   ② 每扇门的**门格数 > 0**（门洞真的凿出来了；为 0 就是"登记不上"的老毛病）
    ///   ③ 每种门型：该型**全部实例 × 全部机位**里最大的「关门 vs 开门」变化 ≥ 1%
    ///   ④ 同上，「关门 vs 半开」≥ 0.3%（证明动画有真实中间姿态，不是两态硬切）
    ///   ⑤ 洋红 = 0（着色器编译正常）· 非全黑 · 非洗白（沿用渲染取证的判据）
    ///
    /// ## 三个实测踩坑（都写进判据里了）
    ///   · 固定 3m 机位：小房间两侧都落在墙带里 → 判红"无处可放"。现在按 1.4~3.0m × 两侧逐个试。
    ///   · 用 TryFindFreeCell 找机位：它会向外找最多 12 环，**可能把相机放到隔壁房间**（隔着两堵墙），
    ///     拍出一整幅均匀背光墙，"关门 vs 开门"变化 0.000% —— 看着像"门没动"，其实是门不在画面里。
    ///     现在要求候选点**到门洞前方有直线视野**（逐点采样判定）。
    ///   · 只拍一种机位/一个实例：本关很暗，同型不同实例可辨识度差很多（实测 sliding 两实例
    ///     0.457% vs 15.995%）。现在每型枚举全部实例 × 正面/斜侧两档取最大者，
    ///     并把每个实例的数值都写进 CSV（可复核谁好谁差）。
    ///
    /// 用法：
    ///   Unity -quit -batchmode -projectPath "项目" -executeMethod Whisper.Editor.DoorEvidenceCapture.Run -captureDir "目录" -logFile "日志"
    ///   注意：-projectPath 必须带引号；不要加 -nographics（Camera.Render 会崩 0xC0000005）。
    ///   一条命令复现：bash tools/door-evidence.sh（它会拿工程锁；第二层判据用 tools/pixel-diff-pair.mjs）
    /// </summary>
    public static class DoorEvidenceCapture
    {
        const int Width = 960, Height = 600;

        /// <summary>关门 vs 开门的最小变化占比。</summary>
        const double ClosedVsOpenMinPct = 1.0;
        /// <summary>关门 vs 半开的最小变化占比（半开门只转 45°，变化小一些但仍应明显）。</summary>
        const double ClosedVsMidMinPct = 0.3;

        /// <summary>
        /// 门区判据的画面比例区域：相机正对门洞 1.4~3.0m、FOV 60° → 门洞落在画面中间这一块。
        /// 与 tools/door-evidence.sh 传给 pixel-diff-pair 的 --region 必须一致。
        /// </summary>
        const double RegionX0 = 0.25, RegionY0 = 0.20, RegionX1 = 0.75, RegionY1 = 0.85;

        static GameObject _levelGo;
        static string _outDir;

        public static void Run()
        {
            _outDir = ArgValue("-captureDir");
            if (string.IsNullOrEmpty(_outDir)) _outDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "_evidence", "build-a", "doors");
            _outDir = Path.GetFullPath(_outDir);
            Directory.CreateDirectory(_outDir);

            var problems = new System.Collections.Generic.List<string>();
            var index = new StringBuilder();
            index.AppendLine("door_key,type,phase,file,mean_luma,stddev,colors,magenta_pct,changed_pct_full_frame,changed_pct_door_region");
            var pairs = new StringBuilder();   // 给独立工具 pixel-diff-pair 的比对清单：base|other|下限%|标签|区域

            Debug.Log($"[DOOR] 输出 {_outDir}");

            // ── 场景：产品代码装配（LevelLoader + LevelBuilder）──
            var builder = BuildScene();

            // ── 判据①②：结构（没有同位重复的门板）与门格 ──
            int openings = builder.Geometry.DoorOpeningCount;
            int keys = builder.Geometry.DoorCount;
            int roots = builder.DoorObjects.Count;
            int leaves = builder.DoorLeaves.Count;
            Debug.Log($"[DOOR] 结构：DSL 门键 {keys} · 物理洞口 {openings} · 门扇根对象 {roots} · 门叶 {leaves}（双开门两个叶）");
            if (roots != openings)
                problems.Add($"门扇根对象数 {roots} != 物理洞口数 {openings} —— 说明有同位重复的门板（z-fighting 源）或漏建");
            foreach (var leaf in builder.DoorLeaves)
            {
                int cells = builder.Geometry.DoorCellCount(leaf.Key);
                if (cells <= 0) problems.Add($"门 {leaf.Key} 的门格数为 0 —— 门洞没凿出来（关门时无从挡起）");
            }

            // ── 相机 ──
            var camGo = new GameObject("DoorCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.03f, 1f);
            cam.enabled = false;

            // ── 逐型：每种门型枚举它的全部实例，机位取正面/斜侧两档，判据取最大者 ──
            var views = new (string name, float tangent, float normal)[]
            {
                ("front", 0f, 0f),
                ("side", 1.5f, 0.6f),
            };
            var byType = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>(StringComparer.Ordinal);
            foreach (var leaf in builder.DoorLeaves)
            {
                if (!byType.TryGetValue(leaf.Type, out var list)) { list = new System.Collections.Generic.List<string>(); byType[leaf.Type] = list; }
                // 双开门有两片门叶、同一个洞口键 → 去重（否则同一扇门会被拍两遍，计数也虚高）
                if (!list.Contains(leaf.Key)) list.Add(leaf.Key);
            }
            int shotDoors = 0;
            foreach (var kv in byType) shotDoors += kv.Value.Count;

            int written = 0;
            foreach (var typeKv in byType)
            {
                string type = typeKv.Key;
                double bestOpen = -1, bestMid = -1;
                bool bestOpenRegion = false, bestMidRegion = false;
                string bestOpenBase = null, bestOpenOther = null, bestMidBase = null, bestMidOther = null, bestWho = null;
                foreach (var key in typeKv.Value)
                {
                    if (!FindDoor(builder, key, out var d)) { problems.Add($"几何层里找不到门 {key}"); continue; }
                    string tag = $"{type}_{Sanitize(key)}";
                    var phases = new (string name, bool open, float t)[]
                    {
                        ("closed", false, 0f),
                        ("open", true, 1f),
                        ("mid", true, 0.5f),      // 动画中间姿态：几何门开着，但门扇只转了 45°
                    };
                    foreach (var (vname, vt, vn) in views)
                    {
                        if (!PlaceCamera(camGo.transform, cam, builder, d, vt, vn)) continue;
                        Color32[] closedPixels = null;
                        double dOpen = -1, dMid = -1;
                        bool dOpenRegion = false, dMidRegion = false;
                        foreach (var (name, open, t) in phases)
                        {
                            builder.SetDoorOpen(key, open, instant: true);
                            builder.SetDoorVisualProgress(key, t);
                            string file = Path.Combine(_outDir, $"{tag}_{vname}_{name}.png");
                            var s = RenderTo(cam, file);
                            written++;
                            if (name == "closed") closedPixels = s.pixels;
                            // 判据取「整幅」与「门区」的较大者 —— 两个口径各有一类盲区，实测都碰到过：
                            //   · 整幅：门在暗房里只占中间一块 → 某 sliding 实例只量到 0.457%（<1%）
                            //   · 门区：平开门转到 45° 时门叶扫到门洞区域之外（盖住旁边的墙），
                            //           门区里几乎没变（0.000%）而整幅变了 2.083%
                            // 两者都写进 CSV（可复核），判红取 max —— 它表达的是"画面确实能分辨"。
                            double changed = closedPixels == null ? 0 : ChangedPct(closedPixels, s.pixels);
                            double changedRegion = closedPixels == null ? 0 : ChangedPct(closedPixels, s.pixels, RegionX0, RegionY0, RegionX1, RegionY1);
                            double metric = changed > changedRegion ? changed : changedRegion;
                            bool regionWon = changedRegion >= changed;
                            if (name == "open") { dOpen = metric; dOpenRegion = regionWon; }
                            if (name == "mid") { dMid = metric; dMidRegion = regionWon; }
                            index.AppendLine(string.Join(",", key, type, vname + "_" + name, Path.GetFileName(file),
                                s.mean.ToString("0.00", CultureInfo.InvariantCulture),
                                s.std.ToString("0.00", CultureInfo.InvariantCulture),
                                s.colors.ToString(CultureInfo.InvariantCulture),
                                s.magentaPct.ToString("0.000", CultureInfo.InvariantCulture),
                                changed.ToString("0.000", CultureInfo.InvariantCulture),
                                changedRegion.ToString("0.000", CultureInfo.InvariantCulture)));
                            Debug.Log($"[DOOR] {key}({type})/{vname}/{name} → {Path.GetFileName(file)} · 亮度 {s.mean:0.0}"
                                + $" · 颜色数 {s.colors} · 洋红 {s.magentaPct:0.000}% · 整幅变化 {changed:0.000}% · 门区变化 {changedRegion:0.000}% · 判据取 {metric:0.000}%");

                            if (s.magentaPct > 1.0) problems.Add($"{tag}/{vname}/{name} 洋红像素 {s.magentaPct:0.00}% —— 着色器很可能编译失败");
                            if (s.mean < 3.0 && s.colors <= 2) problems.Add($"{tag}/{vname}/{name} 判为全黑（几何没进来？）");
                            if (s.mean > 150.0 && s.std < 5.0 && s.colors <= 2) problems.Add($"{tag}/{vname}/{name} 判为洗白（均值 {s.mean:0.0} · 标准差 {s.std:0.0} · 颜色数 {s.colors}）");
                        }
                        if (dOpen > bestOpen)
                        {
                            bestOpen = dOpen; bestWho = $"{key}/{vname}";
                            bestOpenBase = $"{tag}_{vname}_closed.png"; bestOpenOther = $"{tag}_{vname}_open.png";
                            bestOpenRegion = dOpenRegion;   // 记录"哪个口径赢的"（供清单决定要不要带 region）
                        }
                        if (dMid > bestMid)
                        {
                            bestMid = dMid;
                            bestMidBase = $"{tag}_{vname}_closed.png"; bestMidOther = $"{tag}_{vname}_mid.png";
                            bestMidRegion = dMidRegion;
                        }
                    }
                    builder.SetDoorOpen(key, false, instant: true);   // 复原，避免影响后续门
                }

                if (bestOpen < 0) { problems.Add($"门型 {type} 的实例一个机位都放不下相机 —— 取证无法进行"); continue; }
                Debug.Log($"[DOOR] 【门开合判据·{type}】最佳实例 {bestWho}：开门变化 {bestOpen:0.000}% · 半开变化 {bestMid:0.000}%（取整幅/门区较大者）");
                if (bestOpen < ClosedVsOpenMinPct)
                    problems.Add($"【门开合判据不成立·{type}】全部实例/机位里最大的「关门 vs 开门」变化只有 {bestOpen:0.000}%（<{ClosedVsOpenMinPct}%）—— 门扇没动");
                if (bestMid < ClosedVsMidMinPct)
                    problems.Add($"【门动画判据不成立·{type}】全部实例/机位里最大的「关门 vs 半开」变化只有 {bestMid:0.000}%（<{ClosedVsMidMinPct}%）—— 动画没有中间姿态");
                // 清单里的**区域字段只在"区域口径赢"时才写** —— 两层判据必须同口径：
                // C# 取 max(整幅, 门区)，那么外壳的 pixel-diff-pair 也必须用同一个口径判，
                // 否则会出现"脚本说通过、独立工具说 0.000%"的自相矛盾（实测 swing/半开 就是这样）。
                string openRegion = bestOpenRegion ? $"{RegionX0},{RegionY0},{RegionX1},{RegionY1}" : "";
                string midRegion = bestMidRegion ? $"{RegionX0},{RegionY0},{RegionX1},{RegionY1}" : "";
                pairs.AppendLine($"{bestOpenBase}|{bestOpenOther}|{ClosedVsOpenMinPct}|{type} 最佳实例 {bestWho} 关门 vs 开门|{openRegion}");
                pairs.AppendLine($"{bestMidBase}|{bestMidOther}|{ClosedVsMidMinPct}|{type} 最佳实例 {bestWho} 关门 vs 半开|{midRegion}");
            }

            File.WriteAllText(Path.Combine(_outDir, "door-index.csv"), index.ToString());
            File.WriteAllText(Path.Combine(_outDir, "door-pairs.txt"), pairs.ToString());
            Debug.Log($"[DOOR] 共 {written} 张图 · 门型 {byType.Count} 种 / 门扇 {shotDoors} 扇 → {_outDir}");

            if (problems.Count > 0)
            {
                foreach (var p in problems) Debug.LogError("[DOOR] ✗ " + p);
                Debug.LogError($"DOOR_EVIDENCE FAIL · {problems.Count} 项判据不成立");
                EditorApplication.Exit(1);
                return;
            }
            Debug.Log($"DOOR_EVIDENCE OK · {written} 张图 · 门型 {byType.Count} 种 / 门扇 {shotDoors} 扇 · 结构与开合判据全部成立");
            EditorApplication.Exit(0);
        }

        // ───────────────────────── 场景与相机 ─────────────────────────

        static Whisper.Gameplay.Level.LevelBuilder BuildScene()
        {
            var levelText = Resources.Load<TextAsset>("Levels/asylum_v1");
            if (levelText == null) throw new InvalidOperationException("缺 Resources/Levels/asylum_v1 —— 门取证无法进行");
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
            if (_levelGo != null) UnityEngine.Object.DestroyImmediate(_levelGo);
            _levelGo = new GameObject("Level");
            var lb = _levelGo.AddComponent<Whisper.Gameplay.Level.LevelBuilder>();
            lb.Build(level, knownKits);
            Debug.Log($"[DOOR] 场景建好：房间 {level.Rooms.Count} · {lb.DescribeDoors()}");
            return lb;
        }

        static bool FindDoor(Whisper.Gameplay.Level.LevelBuilder b, string key, out Whisper.Gameplay.Level.LevelGeometry.DoorRect rect)
        {
            foreach (var d in b.Geometry.Doors)
                if (string.Equals(d.Key, key, StringComparison.Ordinal)) { rect = d; return true; }
            rect = default;
            return false;
        }

        /// <summary>
        /// 把相机放在门洞法线上（可带切向偏移做斜侧机位），并要求看得见门。
        /// 候选 = 距离 {3.0, 2.4, 1.8, 1.4} × 两侧，逐个试 ① 自身可走 ② 到"门心前方 0.5m"有直线视野。
        /// 某个机位放不下只记一条 DBG（**不记问题**）—— 可选视角不可用不该把证据判死；
        /// 只有"某型所有实例所有机位都放不下"才由调用方判红。
        /// </summary>
        static bool PlaceCamera(Transform t, Camera cam, Whisper.Gameplay.Level.LevelBuilder b,
            in Whisper.Gameplay.Level.LevelGeometry.DoorRect d, float tangentOff, float normalOff)
        {
            float nx = d.AlongX ? 0f : 1f, nz = d.AlongX ? 1f : 0f;   // 门洞法线
            float tx = d.AlongX ? 1f : 0f, tz = d.AlongX ? 0f : 1f;   // 沿墙切向（斜侧机位用）
            float[] dists = { 3.0f, 2.4f, 1.8f, 1.4f };
            float cx = 0f, cz = 0f;
            bool ok = false;
            for (int i = 0; i < dists.Length && !ok; i++)
            {
                for (int s = 1; s >= -1 && !ok; s -= 2)
                {
                    float px = d.CenterX + nx * (dists[i] * s + normalOff) + tx * tangentOff;
                    float pz = d.CenterZ + nz * (dists[i] * s + normalOff) + tz * tangentOff;
                    if (!b.Geometry.Passable(px, pz)) continue;
                    // 视线目标：门心 + 法线朝相机侧 0.5m —— 斜侧机位本来就不正对门心
                    float lx = d.CenterX + nx * 0.5f * s, lz = d.CenterZ + nz * 0.5f * s;
                    if (!LineOfSightClear(b, px, pz, lx, lz)) continue;
                    cx = px; cz = pz; ok = true;
                }
            }
            if (!ok)
            {
                Debug.Log($"[DOOR-DBG] {d.Key}({d.Type}) 机位(切{tangentOff}/法{normalOff}) 放不下，跳过该视角");
                return false;
            }
            cam.fieldOfView = 60f;
            t.position = new Vector3(cx, d.BaseY + 1.6f, cz);
            t.LookAt(new Vector3(d.CenterX, d.BaseY + 1.2f, d.CenterZ));
            float ddx = cx - d.CenterX, ddz = cz - d.CenterZ;
            Debug.Log($"[DOOR-DBG] {d.Key}({d.Type}) 门=({d.CenterX:0.00},{d.CenterZ:0.00}) AlongX={d.AlongX}"
                + $" 机位(切{tangentOff}/法{normalOff})=({cx:0.00},{cz:0.00}) 距门={Math.Sqrt(ddx * ddx + ddz * ddz):0.00}m");
            return true;
        }

        /// <summary>相机到目标点是否一路可走（门关着时门格本身不可走，故只判到目标点为止）。</summary>
        static bool LineOfSightClear(Whisper.Gameplay.Level.LevelBuilder b, float x0, float z0, float x1, float z1)
        {
            float dx = x1 - x0, dz = z1 - z0;
            float len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len <= 0.01f) return false;
            int steps = Math.Max(4, (int)(len / 0.25f));
            for (int i = 1; i < steps; i++)
            {
                float f = (float)i / steps;
                if (!b.Geometry.Passable(x0 + dx * f, z0 + dz * f)) return false;
            }
            return true;
        }

        // ───────────────────────── 渲染与统计 ─────────────────────────

        sealed class Shot
        {
            public Color32[] pixels;
            public double mean, std, magentaPct;
            public int colors;
        }

        static Shot RenderTo(Camera cam, string path)
        {
            var rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
            var prevRt = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = prevRt;

            var px = tex.GetPixels32();
            double sum = 0, sum2 = 0;
            long magenta = 0;
            var seen = new System.Collections.Generic.HashSet<int>();
            foreach (var c in px)
            {
                double luma = 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
                sum += luma; sum2 += luma * luma;
                if (c.r > 200 && c.g < 60 && c.b > 200) magenta++;
                seen.Add((c.r >> 3 << 10) | (c.g >> 3 << 5) | (c.b >> 3));
            }
            int n = px.Length;
            double mean = sum / n;
            double std = Math.Sqrt(Math.Max(0, sum2 / n - mean * mean));

            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            RenderTexture.ReleaseTemporary(rt);
            return new Shot { pixels = px, mean = mean, std = std, colors = seen.Count, magentaPct = 100.0 * magenta / n };
        }

        /// <summary>整幅的变化像素占比（阈值 8/255，与 tools/pixel-diff-pair.mjs 同口径）。</summary>
        static double ChangedPct(Color32[] a, Color32[] b) => ChangedPct(a, b, 0.0, 0.0, 1.0, 1.0);

        /// <summary>变化像素占比（可限定画面比例区域）。</summary>
        static double ChangedPct(Color32[] a, Color32[] b, double rx0, double ry0, double rx1, double ry1)
        {
            if (a == null || b == null || a.Length != b.Length) return -1;
            int x0 = (int)(rx0 * Width), x1 = (int)(rx1 * Width);
            int y0 = (int)(ry0 * Height), y1 = (int)(ry1 * Height);
            if (x1 <= x0) x1 = x0 + 1;
            if (y1 <= y0) y1 = y0 + 1;
            long changed = 0, n = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    int i = y * Width + x;
                    int d0 = Math.Abs(a[i].r - b[i].r), d1 = Math.Abs(a[i].g - b[i].g), d2 = Math.Abs(a[i].b - b[i].b);
                    if (Math.Max(d0, Math.Max(d1, d2)) > 8) changed++;
                    n++;
                }
            return n == 0 ? -1 : 100.0 * changed / n;
        }

        static string Sanitize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s) sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
            return sb.ToString();
        }

        static string ArgValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
