using System.Collections.Generic;
using UnityEngine;
using Whisper.Gameplay.Render;

namespace Whisper.Runtime
{
    /// <summary>
    /// HallScene 的**灯光与陈设**部分（2026-10-06 从 HallScene.cs 拆出）。
    ///
    /// 为什么拆：gate-code 的 C5 规模纪律要求单文件 ≤600 行，而 HallScene.cs 到了 722 行。
    /// 拆的是**内聚的一块**（灯 + 陈设 + 白板灵球），不是按行数硬切 ——
    /// 这三者本来就是一回事：都属于"把仓库布置得像个仓库"。
    /// 用项目既有的 partial 惯例（同 LevelBuilder.Doors.cs / MonsterViews.Doors.cs）。
    /// </summary>
    public sealed partial class HallScene
    {
        // ════════════════════════════════════════════════════════════════════════
        // 灯光（官方：**光线偏暗**，主菜单板是最亮点）
        // ════════════════════════════════════════════════════════════════════════
        void BuildLights()
        {
            // 半球环境光（Built-in 的全局间接光近似）。
            // 为什么必须有：仓库 26m 跨度、层高 5.6m，而 Built-in 的 Point/Spot 是平方反比衰减，
            // 8~14m 的 range 在仓库尺度下照不到天花板与远处墙面 —— 只靠灯会得到"几何在但看不清"
            // （0.1.60/61 真机截图就是这个症状）。
            // 环境光给所有朝上的面一个底，点光负责节奏与焦点 —— 这也是官方大厅的实际做法。
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.30f, 0.33f, 0.40f);   // 偏冷的工业天光
            RenderSettings.fog = false;   // 雾在几何着色器里自研，不用 Unity 的全局雾（历史事故）

            float w = WidthM, d = LengthM, h = FloorHeightM;

            // 工业吊灯阵列（偏暗、暖白、间距大 → 地面有明暗节奏，不是均匀亮）
            for (int i = 0; i < 4; i++)
            {
                for (int k = 0; k < 2; k++)
                {
                    float x = -w * 0.34f + i * (w * 0.226f);
                    float z = -d * 0.22f + k * (d * 0.44f);
                    Light(_root, "HallLamp", new Vector3(x, h - 0.95f, z), LightType.Point,
                          new Color(1.0f, 0.90f, 0.76f), 3.2f, 14f, true);
                    // 灯罩（视觉锚点：让"灯在哪"看得出来）
                    Box(_root, "LampShade", new Vector3(x, h - 0.82f, z), new Vector3(0.62f, 0.10f, 0.62f),
                        Mat(new Color(0.30f, 0.31f, 0.34f), 0.45f, MaterialFamily.Metal), false);
                }
            }

            // 主方向光（很弱，只给一点总体方向感；照度主要靠点光 → 更像仓库）
            var keyGo = new GameObject("HallKey");
            keyGo.transform.SetParent(_root, false);
            keyGo.transform.localPosition = new Vector3(-6f, h + 2f, -4f);
            keyGo.transform.localRotation = Quaternion.Euler(52f, -28f, 0f);
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(0.62f, 0.66f, 0.78f);
            key.intensity = 0.55f;
            key.shadows = LightShadows.Soft;
            BuiltCount++;

            // 二层的红光（娱乐区）——与一层的暖白形成分区
            Light(_root, "MezzRed", new Vector3(0f, MezzanineYM + 1.9f, d * 0.30f), LightType.Point,
                  new Color(1.0f, 0.32f, 0.26f), 1.5f, 9f, false);
        }

        // ════════════════════════════════════════════════════════════════════════
        // 道具（官方大厅：喷漆罐可叠、篮球可投、白板附近有灵球粒子）
        // ════════════════════════════════════════════════════════════════════════
        // ════════════════════════════════════════════════════════════════════════
        // 真实套件道具的装载（2026-10-06）
        //
        // 这批 `Hall_*` 工业道具是工程里**早就做好、却从未被用上**的资产（四重断链，
        // 见 `docs/hall-props-orphaned-2026-10-06.md`）。补齐断链后，这里给它们一个统一入口。
        //
        // 走 `KitMeshLibrary` 而不是 `ModelLibrary.InstantiateSingleFile`，理由是**材质**：
        // 前者按 GLB 自带的 PBR 参数（baseColor/metallic/roughness）建材质；
        // 后者在 `ModelLibrary` 里把材质写死成 `MaterialFor("GEO-GhostTorso")`
        // ⇒ 道具会全变成"鬼的躯干材质"，是错的颜色。
        // ════════════════════════════════════════════════════════════════════════

        readonly List<string> _kitMisses = new List<string>();
        int _kitParts;

        /// <summary>
        /// 用真实套件 GLB 摆一个道具。**返回 null 表示套件不可用**，调用方应回退到程序化几何。
        ///
        /// 与 `TruckScene.TryBuildFromKit` 同一套做法（含非凸 `MeshCollider`）：
        /// 静态陈设也要有碰撞体，否则玩家自由行走时会**直接穿过去**。
        /// 非凸对静态物体是允许的，且只有它能表达"有外壳、内有空腔"的形状。
        /// </summary>
        GameObject PlaceKit(string kitId, string name, Transform parent, Vector3 localPos, float yawDeg = 0f)
        {
            var parts = Whisper.Gameplay.Level.KitMeshLibrary.GetParts(kitId);
            if (parts == null || parts.Length == 0)
            {
                if (!_kitMisses.Contains(kitId)) _kitMisses.Add(kitId);
                return null;
            }
            var matIdx = Whisper.Gameplay.Level.KitMeshLibrary.GetPartMaterials(kitId);
            var mats = Whisper.Gameplay.Level.KitMeshLibrary.GetMaterials(kitId);

            var root = new GameObject(string.IsNullOrEmpty(name) ? kitId : name);
            root.transform.SetParent(parent != null ? parent : _root, false);
            root.transform.localPosition = localPos;
            root.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);

            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == null) continue;
                var go = new GameObject(kitId + "_" + i);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = parts[i];
                go.AddComponent<MeshRenderer>().sharedMaterial = KitMat(matIdx, mats, i);
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = parts[i];
                mc.convex = false;
                _kitParts++;
            }
            return root;
        }

        /// <summary>部件材质：优先用套件自带 PBR 参数；缺则中性灰兜底（不出怪色）。</summary>
        static Material KitMat(int[] matIdx, Whisper.Gameplay.Level.GlbReader.KitMaterial[] mats, int part)
        {
            if (matIdx == null || mats == null || part >= matIdx.Length) return KitFallback();
            int mi = matIdx[part];
            if (mi < 0 || mi >= mats.Length) return KitFallback();
            var km = mats[mi];
            return SceneMaterials.Lit(new Color(km.R, km.G, km.B, 1f),
                                      Mathf.Clamp(km.Roughness, 0.05f, 1f),
                                      FamilyOfKit(km.Name));
        }

        static Material KitFallback()
            => SceneMaterials.Lit(new Color(0.50f, 0.50f, 0.52f), 0.6f, MaterialFamily.Metal);

        /// <summary>
        /// 套件材质名 → 本产品材质族。未知名字落 `Metal`（中性、不会出怪色）。
        /// 用**名字**而不是下标：下标随导出顺序变，名字稳定（这条是 `TruckScene` 用血换来的）。
        /// </summary>
        static MaterialFamily FamilyOfKit(string name)
        {
            if (string.IsNullOrEmpty(name)) return MaterialFamily.Metal;
            string n = name.ToLowerInvariant();
            if (n.Contains("wood") || n.Contains("crate") || n.Contains("pallet") || n.Contains("plank")) return MaterialFamily.Wood;
            if (n.Contains("rust")) return MaterialFamily.RustMetal;
            if (n.Contains("concrete") || n.Contains("cement") || n.Contains("plaster")) return MaterialFamily.Concrete;
            if (n.Contains("tile")) return MaterialFamily.Tile;
            if (n.Contains("fabric") || n.Contains("cloth") || n.Contains("canvas")) return MaterialFamily.Fabric;
            return MaterialFamily.Metal;   // 金属是这批工业道具的默认语义
        }

        void BuildProps()
        {
            var canMat = Mat(new Color(0.55f, 0.18f, 0.16f), 0.40f, MaterialFamily.Metal);
            var canMat2 = Mat(new Color(0.20f, 0.35f, 0.55f), 0.40f, MaterialFamily.Metal);
            var ballMat = Mat(new Color(0.62f, 0.30f, 0.10f), 0.65f, MaterialFamily.Fabric);
            var crateMat = Mat(new Color(0.34f, 0.26f, 0.17f), 0.80f, MaterialFamily.Wood);

            // ── 货架（沿左墙）：用**真实工业道具 GLB**，不再方盒子拼 ────────────
            //
            // 【为什么换】此前是 4 块 `Box(...)` 板 + 方盒子箱子 —— 这正是用户说的
            // 「谁家医院这样」「看着还是几十个版本前的样子」的直接来源。
            // 而工程里**早就做好了** `Hall_RackUpright` / `Hall_RackBeam` / `Hall_CrateWood`，
            // 只因四重断链（清单没登记 / 目录不对 / 代码没引用 / 扩展名不是 .glb.bytes）从未被用上。
            // 断链已在前几轮补齐（见 `docs/hall-props-orphaned-2026-10-06.md`）。
            //
            // **保留 Box 回退**：道具若某个设备上加载失败，退回方盒子至少"有货架"，
            // 而不是空一片 —— 与货车 `TryBuildFromKit` 的取舍一致（宁可降级，不可缺件）。
            for (int s = 0; s < 3; s++)
            {
                float z = -LengthM * 0.25f + s * (LengthM * 0.22f);
                float x = -WidthM * 0.5f + 0.9f;

                // 立柱（1.802m 高）×4 + 横梁（2.406m 长）×2 —— 尺寸取自实测包围盒，不是估的
                var rackRoot = new GameObject("RackRow");
                rackRoot.transform.SetParent(_root, false);
                rackRoot.transform.localPosition = new Vector3(x, 0f, z);
                bool ok = true;
                for (int e = 0; e < 2; e++)
                for (int sgn = -1; sgn <= 1; sgn += 2)
                    ok &= PlaceKit("Hall_RackUpright", "RackUpright", rackRoot.transform,
                                   new Vector3(sgn * 0.55f, 0f, e * 3.0f - 1.5f)) != null;
                for (int lv = 0; lv < 2; lv++)
                    ok &= PlaceKit("Hall_RackBeam", "RackBeam", rackRoot.transform,
                                   new Vector3(0f, lv == 0 ? 0.9f : 1.7f, 0f), 0f) != null;

                if (!ok)
                {
                    // 回退：与旧版几何等价，保证"看得见货架"
                    Box(_root, "Shelf", new Vector3(x, 0.9f, z), new Vector3(1.2f, 0.08f, 3.4f), crateMat);
                    Box(_root, "Shelf", new Vector3(x, 1.7f, z), new Vector3(1.2f, 0.08f, 3.4f), crateMat);
                    Box(_root, "ShelfSide", new Vector3(x - 0.56f, 1.0f, z), new Vector3(0.08f, 2.0f, 3.4f), crateMat);
                    Box(_root, "ShelfSide", new Vector3(x + 0.56f, 1.0f, z), new Vector3(0.08f, 2.0f, 3.4f), crateMat);
                }

                // 架上的木箱（0.604³ 实测）—— 用真实货箱，回退方盒子
                for (int c = 0; c < 3; c++)
                {
                    var cp = new Vector3(x + (c % 2 == 0 ? -0.2f : 0.25f),
                                         1.06f + (c == 2 ? 0.8f : 0f),
                                         z - 1.1f + c * 1.1f);
                    float cy = c == 2 ? 30f : -20f + c * 25f;   // 确定性朝向（不用 Random：gate-physics 要求可复现）
                    if (PlaceKit("Hall_CrateWood", "Crate", _root, cp, cy) == null)
                        Box(_root, "Crate", cp, new Vector3(0.7f, 0.55f, 0.7f), crateMat);
                }
            }

            // 喷漆罐堆（官方彩蛋：可以叠喷漆罐）
            for (int i = 0; i < 7; i++)
            {
                float px = 3.6f + (i % 3) * 0.12f;
                float py = 0.14f + (i / 3) * 0.26f;
                float pz = -5.2f + (i % 3) * 0.10f;
                var can = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                can.name = "SprayCan";
                can.transform.SetParent(_root, false);
                can.transform.localPosition = new Vector3(px, py, pz);
                can.transform.localScale = new Vector3(0.09f, 0.12f, 0.09f);
                var mr = can.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = (i % 2 == 0) ? canMat : canMat2;
                BuiltCount++;
            }

            // 二层的篮球（官方：得分 666 触发闪电彩蛋 —— 这里先放好球与篮筐，彩蛋后续接）
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Basketball";
            ball.transform.SetParent(_root, false);
            ball.transform.localPosition = new Vector3(-4.5f, MezzanineYM + 0.30f, LengthM * 0.30f);
            ball.transform.localScale = new Vector3(0.48f, 0.48f, 0.48f);
            var bmr = ball.GetComponent<MeshRenderer>();
            if (bmr != null) bmr.sharedMaterial = ballMat;
            BuiltCount++;

            Box(_root, "HoopBoard", new Vector3(-4.5f, MezzanineYM + 1.55f, LengthM * 0.5f - 0.30f),
                new Vector3(1.5f, 0.95f, 0.06f), Mat(new Color(0.78f, 0.76f, 0.70f), 0.75f, MaterialFamily.Wood), false);
            Box(_root, "HoopRim", new Vector3(-4.5f, MezzanineYM + 1.05f, LengthM * 0.5f - 0.52f),
                new Vector3(0.72f, 0.05f, 0.72f), Mat(new Color(0.75f, 0.35f, 0.12f), 0.45f, MaterialFamily.Metal), false);

            // 白板（官方：白板附近有灵球粒子漂浮）
            BuildWhiteboardAndOrbs();
        }

        void BuildWhiteboardAndOrbs()
        {
            var frame = Mat(new Color(0.70f, 0.70f, 0.70f), 0.40f, MaterialFamily.Metal);
            var face = Mat(new Color(0.86f, 0.88f, 0.88f), 0.30f, MaterialFamily.Tile);
            float z = LengthM * 0.5f - WallThicknessM - 0.10f;
            float x = -WidthM * 0.28f;
            float y = 2.0f;
            Box(_root, "WhiteboardFrame", new Vector3(x, y, z), new Vector3(4.6f, 2.4f, 0.10f), frame, false);
            Box(_root, "WhiteboardFace", new Vector3(x, y, z - 0.03f), new Vector3(4.4f, 2.2f, 0.05f), face, false);
            Light(_root, "WhiteboardLight", new Vector3(x, FloorHeightM - 1.0f, z - 1.2f), LightType.Point,
                  new Color(0.95f, 0.97f, 1.0f), 1.1f, 6f, false);

            // 灵球粒子：用**自发光小球** + 缓慢上下浮动（Update 里驱动）。
            // 为什么不用 ParticleSystem：粒子系统的默认材质依赖内置资源（可能被剥离），
            // 而本项目已经踩过"内置资源被剥离 → 真机全黑"的事故。用几何体最稳。
            _orbs = new List<Transform>();
            for (int i = 0; i < 9; i++)
            {
                var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                orb.name = "WillOrb";
                orb.transform.SetParent(_root, false);
                // 确定性分布（整数哈希，不用 Random）
                float ox = x - 2.0f + H01(i * 7 + 1) * 4.0f;
                float oy = y - 1.0f + H01(i * 13 + 3) * 2.0f;
                float oz = z - 2.6f + H01(i * 19 + 5) * 1.6f;
                orb.transform.localPosition = new Vector3(ox, oy, oz);
                float s = 0.045f + H01(i * 23 + 7) * 0.035f;
                orb.transform.localScale = new Vector3(s, s, s);
                var col = orb.GetComponent<Collider>(); if (col != null) Object.Destroy(col);
                var mr = orb.GetComponent<MeshRenderer>();
                var glow = SceneMaterials.Emissive(new Color(0.55f, 0.85f, 1.0f), 1.6f + H01(i * 29 + 11));
                if (mr != null && glow != null) mr.sharedMaterial = glow;
                _orbs.Add(orb.transform);
                BuiltCount++;
            }
        }
    }
}
