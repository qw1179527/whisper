using System;
using System.Collections.Generic;
using UnityEngine;
// 注：DesignTokens 的直接引用已移到 LevelPalette（同命名空间）——配色决策搬去纯逻辑层后，
// 本文件不再需要 Whisper.Core。（历史上这里漏过 using Whisper.Core，导致 DesignTokens 解析不到、
// 几何配色全部落到兜底色；现在这条依赖由 LevelPalette.cs 持有，那边的 using 同样不能少。）

namespace Whisper.Gameplay.Level
{
    /// <summary>
    /// 关卡装配器（V9 §19.2 / §19.1 C1「场景零手工」）。
    ///
    /// 职责：把 Level DSL **实例化**成可运行的场景几何——
    ///   ① 编译几何（LevelGeometry：格子/门洞/碰撞盒；纯 C#，本机可断言）
    ///   ② 每个房间生成地板/天花板/墙（盒体，按 lightZone 上色，材质全部代码构建 —— C2）
    ///   ③ 每个门洞生成门框（门板与开合状态由玩法层驱动）
    ///   ④ 每个道具生成占位盒（真美术资产接入后由资产管线替换）
    ///
    /// 为什么在 Boot 时必须先跑一次**完整性校验**：V9 §19.2 要求"关卡即数据"，
    /// 而数据出错（门对不上、套件缺失）在运行时表现为"怪卡在门口/穿墙"这类难查症状
    /// （灰盒首轮真实事故）。这里宁可早失败，也不带病开跑。
    ///
    /// 注意：本类引用 UnityEngine，**不在本机 .NET 跑手覆盖范围内**——它依赖的纯逻辑
    /// （几何编译/校验）已由 LevelGeometry 与 LevelLoader 在本机被断言覆盖；
    /// 本类自身由 PlayMode 测试与真机验收覆盖。
    /// </summary>
    public sealed partial class LevelBuilder : MonoBehaviour
    {
        public LevelData Level { get; private set; }
        public LevelGeometry Geometry { get; private set; }
        /// <summary>装配计划（纯 C# 计算，可本机断言；本类按它建对象）。</summary>
        public LevelAssembly.Plan Plan { get; private set; }
        public readonly List<GameObject> RoomObjects = new List<GameObject>();
        public readonly List<GameObject> DoorObjects = new List<GameObject>();
        public readonly List<GameObject> PropObjects = new List<GameObject>();

        /// <summary>构建（幂等：重复调用会先清掉上一次的产物）。</summary>
        public void Build(LevelData level, ISet<string> knownKits = null)
        {
            Clear();
            Level = level;
            var problems = LevelLoader.Validate(level, knownKits);
            if (problems.Count > 0)
                throw new LevelLoader.LevelValidationException(problems);

            // ⚠ **必须逐层编译，不能把三层压成一张 2D 格网**。实测（`_evidence/build-a/overlap-probe`）：
            //   合编三层 → 门键 26 · 物理洞口 11 · `entrance_safe/d_east` **门格 0** · 门前 0.5m 判**可走**
            //   只编 F0 → 门键 20 · 物理洞口 10 · 同门 **门格 8** · 门前 0.5m 判不可走 ✓
            // 原因：上层走廊与一层走廊**同 (x,z) 上下对齐**（竖井要落在每层可走区里），它的内缩凿空
            // 覆盖了一层门带格 → 门格清成 0：渲染层门扇照动，结构层"门洞没凿开"（第 26 步真红）。
            // 所以 `Geometry` 只代表玩家所在的 0 层（既有运行时行为不变），上层各编各的，
            // 跨层由 `LevelWorld`（竖井）负责 —— 那才是"层间只能走竖井"的正确模型。
            Geometry = LevelGeometry.CompileFloor(level, 0);
            Floors.Clear();
            int maxFloor = 0;
            foreach (var r in level.Rooms) if (r.Floor > maxFloor) maxFloor = r.Floor;
            for (int f = 0; f <= maxFloor; f++) Floors.Add(LevelGeometry.CompileFloor(level, f));
            World = LevelWorld.Build(level);
            // 装配计划由 LevelAssembly（纯 C#）计算 —— 本类只负责"把计划变成 GameObject"。
            // 为什么这样拆：装配计算必须能**在本机无引擎环境断言**（V9 §19 代码优先），
            // 而 GameObject 创建只能靠引擎。独立复核 F2b 指出几何层此前根本没接进产品，
            // 拆分后计算部分有 5 条本机断言覆盖（墙段数/贴边界/占地/越界/确定性）。
            Plan = LevelAssembly.Build(level);
            foreach (var room in level.Rooms) BuildRoom(room);
            FlushWalls();   // 墙段收集完毕 → 按同板矩形并集合并后统一建（见 AddWallSegment 注释）
            // 门**不按 DSL 条目建**：DSL 里同一门洞在走廊侧与房间侧各登记一次，按条目建会得到
            // 10 对完全同位共面的门板（z-fighting）。几何层已按几何重合合并，这里按它的洞口清单建。
            BuildDoorLeaves();
            foreach (var room in level.Rooms) BuildProps(room);
        }

        public void Clear()
        {
            foreach (var go in RoomObjects) if (go != null) DestroyImmediate(go);
            foreach (var go in DoorObjects) if (go != null) DestroyImmediate(go);
            foreach (var go in PropObjects) if (go != null) DestroyImmediate(go);
            RoomObjects.Clear(); DoorObjects.Clear(); PropObjects.Clear();
        }

        void BuildRoom(Room r)
        {
            var roomGo = new GameObject("Room_" + r.Id);
            roomGo.transform.SetParent(transform, false);
            roomGo.transform.position = new Vector3(r.CenterX, r.Floor * 3.5f, r.CenterZ);
            RoomObjects.Add(roomGo);

            // 配色决策全部搬到 LevelPalette（纯逻辑 · 可本机断言"不溢出""风险越高越暗"）。
            // 真机事故：门框曾写 `LightZoneColor * 1.3f`，bone 216×1.3 = 280.8 被截顶成 255,255,243，
            // 门框变惨白、色相被削平（整屏平均亮度 213/255，恐怖游戏看着像曝光过度）。
            // 乘法在浅色上必然截顶 —— 所以门框改用向墨色**混合**。
            var zone = r.LightZone;
            // 套件网格（包内 Kits/<kit>.glb，落点在 Assets/StreamingAssets）——**"套件真的在产品里"的落点**。
            // 为什么按部件接、并分别上色：套件的部件语义不同（楼板 / 天花板灯槽 / 立柱），
            // 合并成一个网格整块白色（GLB 顶点色是白的，实测过）；分部件才能按分区着色。
            // 墙体仍走程序化的"按门洞切段"路径（套件墙体是实心壳，会挡门洞的视觉）。
            // ⚠️ 已知不足（独立复核者实测，待后续修）：套件按房间中心整块摆、不做裁剪 →
            //    6/11 房间楼板与房间不符（悬挑/缺口）、11/11 房间因此没有天花板、9 对共面重叠 z-fighting。
            bool kitPlaced = false;
            var kitParts = KitMeshLibrary.GetParts(r.Kit);
            if (kitParts != null && kitParts.Length > 0)
            {
                for (int i = 0; i < kitParts.Length; i++)
                {
                    var part = kitParts[i];
                    if (part == null) continue;
                    var b = part.bounds;
                    // 部件语义按尺寸判定（确定性、可复核）：
                    //   水平铺满整房 → 楼板/天花板；高瘦 → 立柱；其余 → 门框等小件
                    bool spansRoom = b.size.x >= r.SizeX - 0.05f && b.size.z >= r.SizeZ - 0.05f;
                    bool isTall = b.size.y > 1.5f && b.size.x < 0.6f && b.size.z < 0.6f;
                    Color color;
                    if (spansRoom) color = ToColor(LevelPalette.Floor(zone));
                    else if (isTall) color = ToColor(LevelPalette.Wall(zone));
                    else color = ToColor(LevelPalette.Ceiling(zone));
                    var go = new GameObject($"Kit_{r.Kit}_{i}");
                    go.transform.SetParent(roomGo.transform, false);
                    go.transform.localPosition = Vector3.zero;
                    var mf = go.AddComponent<MeshFilter>();
                    mf.sharedMesh = part;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = FlatMaterial(color);
                }
                kitPlaced = true;
                KitRooms.Add(r.Id);
            }

            // 程序化楼板/天花板只在**套件缺失**时补：套件楼板是整块矩形、与房型同尺寸，
            // 两者同时存在会 z-fighting（同一平面上两张面）。套件缺失时宁可观感差一点，
            // 也不能让房间没地板（真机表现是"掉进虚空"）。
            if (!kitPlaced)
            {
                AddBox(roomGo, "Floor", new Vector3(0f, -0.05f, 0f),
                    new Vector3(r.SizeX, 0.1f, r.SizeZ), ToColor(LevelPalette.Floor(zone)));
                AddBox(roomGo, "Ceiling", new Vector3(0f, r.SizeY, 0f),
                    new Vector3(r.SizeX, 0.1f, r.SizeZ), ToColor(LevelPalette.Ceiling(zone)));
            }
            // 四面墙：按门洞切段（门洞处留缺口，几何上由 LevelGeometry 保证可走）
            BuildWallsWithDoorGaps(roomGo, r, zone);
        }

        /// <summary>已用套件网格的房型 id（诊断与断言用：非空才说明"套件真的在产品里"）。</summary>
        public readonly List<string> KitRooms = new List<string>();

        /// <summary>套件网格库最近一次失败原因（null = 全部命中）。</summary>
        public static string KitProblem => KitMeshLibrary.LastProblem;

        /// <summary>纯逻辑 Rgb → UnityEngine.Color。</summary>
        static Color ToColor(in Rgb c) => new Color(c.R, c.G, c.B, 1f);

        void BuildWallsWithDoorGaps(GameObject parent, Room r, string zone)
        {
            const float thickness = 0.22f;
            float h = r.SizeY;

            // 收集每面墙上的门洞区间（沿墙、局部坐标）
            var north = new List<(float a, float b)>();
            var south = new List<(float a, float b)>();
            var west = new List<(float a, float b)>();
            var east = new List<(float a, float b)>();
            foreach (var d in r.Doors)
            {
                d.SpanOnWall(r, out float a, out float b);
                switch (d.Wall)
                {
                    case "north": north.Add((a, b)); break;
                    case "south": south.Add((a, b)); break;
                    case "west": west.Add((a, b)); break;
                    default: east.Add((a, b)); break;
                }
            }

            AddWallRun(parent, "N", north, r.SizeX, h, thickness, zone, true, true, r.SizeX, r.SizeZ);
            AddWallRun(parent, "S", south, r.SizeX, h, thickness, zone, true, false, r.SizeX, r.SizeZ);
            AddWallRun(parent, "W", west, r.SizeZ, h, thickness, zone, false, false, r.SizeX, r.SizeZ);
            AddWallRun(parent, "E", east, r.SizeZ, h, thickness, zone, false, true, r.SizeX, r.SizeZ);
        }

        /// <summary>把一面墙按门洞切成若干段（门洞位置来自 DSL 的 offsetM/widthM）。</summary>
        void AddWallRun(GameObject parent, string tag, List<(float a, float b)> gaps,
            float wallLen, float h, float thickness, string zone, bool horizontal, bool atPositive,
            float roomSizeX, float roomSizeZ)
        {
            gaps.Sort((x, y) => x.a.CompareTo(y.a));
            float cursor = 0f;
            int seg = 0;
            foreach (var (a, b) in gaps)
            {
                if (a > cursor) AddWallSegment(parent, tag, seg++, cursor, a, wallLen, h, thickness, zone, horizontal, atPositive, roomSizeX, roomSizeZ);
                cursor = Mathf.Max(cursor, b);
            }
            if (cursor < wallLen) AddWallSegment(parent, tag, seg, cursor, wallLen, wallLen, h, thickness, zone, horizontal, atPositive, roomSizeX, roomSizeZ);
        }

        void AddWallSegment(GameObject parent, string tag, int idx, float from, float to,
            float wallLen, float h, float thickness, string zone, bool horizontal, bool atPositive,
            float roomSizeX, float roomSizeZ)
        {
            float len = to - from;
            if (len <= 0.001f) return;
            float mid = (from + to) / 2f - wallLen / 2f;   // 房间局部坐标（中心为 0）
            // 墙线到房间中心的距离：横向墙（南北）用**深度**的一半，纵向墙（东西）用**宽度**的一半。
            // 早期这里写成两个返回 1f 的占位函数，墙会全部塌到房间中心——已改为显式传参。
            float half = (horizontal ? roomSizeZ : roomSizeX) / 2f;
            Vector3 local = horizontal
                ? new Vector3(mid, h / 2f, atPositive ? half : -half)
                : new Vector3(atPositive ? half : -half, h / 2f, mid);
            Vector3 size = horizontal
                ? new Vector3(len, h, thickness)
                : new Vector3(thickness, h, len);

            // ── 共享墙去重 + **墙段合并**（2026-10-04 修·独立复核 B2 量化 → 构建A 复审加合并）──
            // 墙盒**正中摆在房间边界线上**（上面 `half` 就是房间半尺寸），于是相邻两房各建一份。
            // 独立审计（纯 C# `GeometryOverlapAudit` + `_evidence/build-a/overlap-probe`）实测装配计划：
            //   · 完全同位 10 对（如 ward_01 east ↔ ward_02 west，0.880 m² × 全高）
            //   · **体积穿模 28 对**：例如 corridor_link 的北墙（4m）整段落在 corridor_ward 的南墙（15m）
            //     同一块 0.22m 厚的板里 → 两段墙叠着建，顶/底面共面、端头露内部面 = 用户看到的"重复建模"
            // 只靠"同位置同尺寸"去重**吃不掉**后者（尺寸/中心都不同）。所以这里不再直接建盒：
            // 先把墙段**收集**起来，`Build()` 末尾由 `FlushWalls()` 按 `WallRunMerger`（同板内
            // 矩形并集分解）合并后统一建 —— 于是"两段墙叠在同一块板里"在生成期就**不可能发生**。
            // 探针实测：57 → 30 段；完全同位 10→0、体积穿模 28→12（剩 12 是正交拐角搭接）。
            Vector3 world = parent.transform.position + local;
            _pendingWalls.Add(horizontal
                ? new WallRunMerger.Run
                {
                    AlongX = true,
                    From = world.x - len / 2f, To = world.x + len / 2f,
                    Plane = world.z, BaseY = 0f, Height = h, Thickness = thickness,
                }
                : new WallRunMerger.Run
                {
                    AlongX = false,
                    From = world.z - len / 2f, To = world.z + len / 2f,
                    Plane = world.x, BaseY = 0f, Height = h, Thickness = thickness,
                });
            _pendingWallColors.Add(ToColor(LevelPalette.Wall(zone)));
        }

        /// <summary>合并并建墙（`Build()` 末尾调用一次）。合并后每段墙只建一次，见 AddWallSegment 注释。</summary>
        void FlushWalls()
        {
            if (_pendingWalls.Count == 0) return;
            var merged = WallRunMerger.Merge(_pendingWalls, out var mr);
            WallMergeResult = mr;
            if (_wallRoot != null) DestroyImmediate(_wallRoot);
            _wallRoot = new GameObject("Walls");
            _wallRoot.transform.SetParent(transform, false);
            _wallRoot.transform.localPosition = Vector3.zero;   // 下面按**世界坐标**当局部坐标用
            WallObjects.Clear();
            foreach (var r in merged)
            {
                float len = r.To - r.From;
                if (len <= 0.001f) continue;
                Color color = ColorOfMerged(r);
                Vector3 local = r.AlongX
                    ? new Vector3((r.From + r.To) * 0.5f - transform.position.x, r.BaseY + r.Height * 0.5f - transform.position.y, r.Plane - transform.position.z)
                    : new Vector3(r.Plane - transform.position.x, r.BaseY + r.Height * 0.5f - transform.position.y, (r.From + r.To) * 0.5f - transform.position.z);
                Vector3 size = r.AlongX
                    ? new Vector3(len, r.Height, r.Thickness)
                    : new Vector3(r.Thickness, r.Height, len);
                WallObjects.Add(AddBox(_wallRoot, $"Wall_{WallObjects.Count}", local, size, color));
            }
            Debug.Log($"[几何] {mr} · 墙对象 {WallObjects.Count}");
        }

        /// <summary>合并段的颜色取"覆盖它中点的原墙段"的颜色（同一块板上不同分区相撞时，取先出现的那段）。</summary>
        Color ColorOfMerged(in WallRunMerger.Run r)
        {
            float mid = (r.From + r.To) * 0.5f;
            for (int i = 0; i < _pendingWalls.Count; i++)
            {
                var p = _pendingWalls[i];
                if (p.AlongX != r.AlongX || Math.Abs(p.Plane - r.Plane) > 0.002f) continue;
                if (mid < p.From - 0.002f || mid > p.To + 0.002f) continue;
                return _pendingWallColors[i];
            }
            return _pendingWallColors[0];
        }


        /// <summary>量化到 1mm，用作"同一面墙"的键（浮点相等不可靠，量化才稳）。</summary>
        static long Q(float v) => (long)System.Math.Round(v * 1000.0);

        /// <summary>已建墙的键集合（跨房间共享，用于消除同位重复墙）。</summary>
        readonly System.Collections.Generic.HashSet<string> _wallKeys = new System.Collections.Generic.HashSet<string>();

        /// <summary>
        /// 每层一张格网（索引 = 楼层号）。`Geometry` 是 `Floors[0]` 的别名（玩家所在层，兼容既有调用）。
        /// **不要**用 `LevelGeometry.Compile(level)` 合成一张 —— 会串层并清掉门格（见 `Build` 里的实测）。
        /// </summary>
        public readonly System.Collections.Generic.List<LevelGeometry> Floors = new System.Collections.Generic.List<LevelGeometry>();

        /// <summary>多层世界（竖井连通 + 跨层可达判据）；单层关卡时 `FloorCount == 1`。</summary>
        public LevelWorld World;

        /// <summary>待合并的墙段与它们的颜色（`FlushWalls()` 统一处理，见 AddWallSegment 注释）。</summary>
        readonly System.Collections.Generic.List<WallRunMerger.Run> _pendingWalls = new System.Collections.Generic.List<WallRunMerger.Run>();
        readonly System.Collections.Generic.List<Color> _pendingWallColors = new System.Collections.Generic.List<Color>();
        GameObject _wallRoot;
        /// <summary>已建的墙对象（诊断/取证用：不含套件与道具）。</summary>
        public readonly System.Collections.Generic.List<GameObject> WallObjects = new System.Collections.Generic.List<GameObject>();
        /// <summary>墙段合并的统计（`InCount → OutCount`；日志与取证都读它）。</summary>
        public WallRunMerger.Result WallMergeResult;


        void BuildProps(Room r)
        {
            foreach (var p in r.Props)
            {
                var go = new GameObject($"Prop_{r.Id}_{p.Kit}");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(r.MinX + p.X, r.Floor * 3.5f + p.Y, r.MinZ + p.Z);
                go.transform.rotation = Quaternion.Euler(0f, p.Rot, 0f);
                // 尺寸取装配计划的占地盒（footprint+rot），而不是固定 0.8³ ——
                // 否则"看得见的道具"与"撞得到的道具"又不一致（本项目反复踩过的双口径问题）。
                var part = Plan != null
                    ? Plan.Props.Find(x => x.RoomId == r.Id && x.Kit == p.Kit && Math.Abs(x.CenterX - (r.MinX + p.X)) < 1e-3f)
                    : default;
                float sx = part.Kit != null ? part.SizeX : 0.8f;
                float sz = part.Kit != null ? part.SizeZ : 0.8f;
                // 道具套件（cabinet_a / bed_b）真身：优先用 GLB 部件，缺则退回程序化方块。
                // 为什么按"部件 y 偏移"整体下移到贴地：套件的部件各自带 nodeTranslation
                // （机柜 ±0.6、病床床脚 -0.28 等），直接摆会把部件埋进地板或悬空。
                var propParts = KitMeshLibrary.GetParts(p.Kit);
                if (propParts != null && propParts.Length > 0)
                {
                    // 整体包围盒的最低点 → 抬到地面（与 LevelGeometry 的 PropBoxes 同口径：
                    // 碰撞盒只管水平占地，竖直方向靠这一步对齐，避免"看得见的悬空/嵌地"）
                    float minY = float.MaxValue;
                    foreach (var m in propParts) if (m != null && m.bounds.min.y < minY) minY = m.bounds.min.y;
                    if (minY == float.MaxValue) minY = 0f;
                    for (int i = 0; i < propParts.Length; i++)
                    {
                        if (propParts[i] == null) continue;
                        var mgo = new GameObject($"Kit_{p.Kit}_{i}");
                        mgo.transform.SetParent(go.transform, false);
                        mgo.transform.localPosition = new Vector3(0f, -minY, 0f);
                        var mf2 = mgo.AddComponent<MeshFilter>();
                        mf2.sharedMesh = propParts[i];
                        var mr2 = mgo.AddComponent<MeshRenderer>();
                        mr2.sharedMaterial = FlatMaterial(ToColor(LevelPalette.Prop(r.LightZone)));
                    }
                }
                else
                {
                    AddBox(go, "Body", new Vector3(0f, 0.4f, 0f), new Vector3(sx, 0.8f, sz),
                        ToColor(LevelPalette.Prop(r.LightZone)));
                }
                PropObjects.Add(go);
            }
        }

        static GameObject AddBox(GameObject parent, string name, Vector3 localPos, Vector3 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = CubeMesh;
            mr.sharedMaterial = FlatMaterial(color);
            return go;
        }

        // ── 材质与网格全部代码构建（V9 §19.1 C2：编辑器零参与）──

        /// <summary>
        /// 关卡几何专用着色器（随包发布，见 Assets/Resources/Shaders/WhisperUnlitColor.shader）。
        ///
        /// 真机事故（2026-10-03）：此前这里写 `Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")`，
        /// 两个都返回 null —— 本工程没有任何 `.mat` 资产引用它们（几何全部运行时生成），
        /// Unity 打包时把这两个内置着色器**剥离**掉了。结果 `new Material(null)` 抛
        /// ArgumentNullException("shader")，整个 Boot 在"关卡装载"这一步失败，真机全黑。
        ///
        /// 修法：把着色器作为**资产**放进 Resources/（Resources 内容无条件进包，不依赖
        /// Graphics Settings 的 Always Included Shaders）。`Shader.Find` 仅作保险，
        /// 因为同一个着色器一旦作为资产进包，按名字也一定找得到。
        /// </summary>
        public const string UnlitShaderName = "Whisper/UnlitColor";
        const string UnlitShaderResourcePath = "Shaders/WhisperUnlitColor";

        /// <summary>
        /// 关卡几何的**首选**着色器：自研 PBR。
        ///
        /// ## 为什么改用 PBR（2026-10-06）
        /// 用户反复反馈「建模上色都不行」。查下来根因很具体：
        /// **本文件此前把【全部关卡几何】都挂 `Whisper/UnlitColor`** ——
        /// 那是不吃光的平涂着色器，盒体因此永远是均匀一块色，没有明暗、没有体积、没有材质层次。
        /// 而**同一目录下早就躺着一个完整的 PBR 着色器**（`WhisperLitPbr.shader`，392 行）：
        /// 模型（`ModelLibrary`）和大厅（`HallScene` → `SceneMaterials.Lit`）**都已迁到它**，
        /// 只剩关卡这一条没迁 —— 而关卡正是玩家待得最久的地方。
        ///
        /// ## 为什么可以直接换（逐条核对过，不是想当然）
        /// · **要 NORMAL**：几何是 `CreatePrimitive(PrimitiveType.Cube)`，内置网格自带法线 ✓
        /// · **不要 TANGENT**：着色器用屏幕空间导数重建切线（Mikkelsen 做法），
        ///   其 162~163 行原话是"不需要网格带切线，任何从 Blender 导出的 glb 都能用"✓
        /// · **认 `Material.color`**：顶点着色器写 `o.color = v.color * _Color`，
        ///   而 `_Color` 正是 `Material.color` 与 `MaterialPropertyBlock` 写的名字（其 26 行注释）✓
        /// · **顶点色默认值安全**：内置 Cube 无顶点色数组 ⇒ `v.color` 取 (1,1,1,1)
        ///   ⇒ B 通道脏化项 = `1-_DirtAmount`、G 通道粗糙度项 = 0，都是设计内的中性值 ✓
        ///
        /// ## 回退链（与 `ModelLibrary` 既有做法逐字一致）
        /// `Whisper/LitPbr` → `Whisper/UnlitColor` → 报错。
        /// **保留回退不是偷懒**：万一 PBR 在某台设备上编译失败（GLES3 变体问题），
        /// 退到 Unlit 至少还看得见画面，而不是整片洋红。
        /// </summary>
        public const string PbrShaderName = "Whisper/LitPbr";
        const string PbrShaderResourcePath = "Shaders/WhisperLitPbr";

        static Shader _shader;
        public static Shader GeometryShader
        {
            get
            {
                if (_shader != null) return _shader;
                // ① 首选：PBR（Resources 资产 = 确定性进包）
                _shader = Resources.Load<Shader>(PbrShaderResourcePath);
                if (_shader == null) _shader = Shader.Find(PbrShaderName);
                // ② 回退：Unlit（老路径，保证"至少看得见"）
                if (_shader == null) _shader = Resources.Load<Shader>(UnlitShaderResourcePath);
                if (_shader == null) _shader = Shader.Find(UnlitShaderName);
                return _shader;   // 仍为 null 时由调用方明确报错，不做沉默降级
            }
        }

        static Mesh _cube;
        static Mesh CubeMesh
        {
            get
            {
                if (_cube == null)
                {
                    var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    _cube = temp.GetComponent<MeshFilter>().sharedMesh;
                    DestroyImmediate(temp);
                }
                return _cube;
            }
        }

        static readonly Dictionary<Color, Material> _matCache = new Dictionary<Color, Material>();
        static Material FlatMaterial(Color c)
        {
            if (_matCache.TryGetValue(c, out var m) && m != null) return m;
            var shader = GeometryShader;
            // 不再允许"悄悄用一个 null 着色器"：宁可抛出可读的错误，
            // 也不产出"构建成功但满屏粉红/全黑"的包（本项目反复强调的门禁精神）。
            if (shader == null)
                throw new InvalidOperationException(
                    $"关卡着色器缺失：Resources/{PbrShaderResourcePath}.shader、Resources/{UnlitShaderResourcePath}.shader、" +
                    $"Shader.Find(\"{PbrShaderName}\") 与 Shader.Find(\"{UnlitShaderName}\") 全都没找到。" +
                    "几何无法上色——请确认这两个 .shader 资产已进包（Resources 目录无条件进包）。");
            // ⚠ 【切 PBR 时新增的防线】强制 alpha=1。
            // 为什么：Unlit 与 PBR 的片元都写 `return fixed4(..., i.color.a)` ——
            // **顶点色的 A 通道直接当输出透明度**。若色板里某个色碰巧带着 a=0，
            // 那块几何就会**完全隐形**（不是变暗，是看不见），而且不报任何错。
            // 关卡几何一律不透明，所以这里统一钉死 1，把这类"静默消失"堵在源头。
            m = new Material(shader) { color = new Color(c.r, c.g, c.b, 1f) };
            _matCache[c] = m;
            return m;
        }

    }
}
