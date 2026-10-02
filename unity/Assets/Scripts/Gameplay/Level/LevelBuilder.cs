using System;
using System.Collections.Generic;
using UnityEngine;
using Whisper.Core;   // DesignTokens（V9 §11 光分区配色）——漏了这行时 DesignTokens 解析不到，几何配色会全部落到兜底色

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
    public sealed class LevelBuilder : MonoBehaviour
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

            Geometry = LevelGeometry.Compile(level);
            // 装配计划由 LevelAssembly（纯 C#）计算 —— 本类只负责"把计划变成 GameObject"。
            // 为什么这样拆：装配计算必须能**在本机无引擎环境断言**（V9 §19 代码优先），
            // 而 GameObject 创建只能靠引擎。独立复核 F2b 指出几何层此前根本没接进产品，
            // 拆分后计算部分有 5 条本机断言覆盖（墙段数/贴边界/占地/越界/确定性）。
            Plan = LevelAssembly.Build(level);
            foreach (var room in level.Rooms) BuildRoom(room);
            foreach (var room in level.Rooms) BuildDoors(room);
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

            var tint = LightZoneColor(r.LightZone);
            // 地板
            AddBox(roomGo, "Floor", new Vector3(0f, -0.05f, 0f),
                new Vector3(r.SizeX, 0.1f, r.SizeZ), tint * 0.6f);
            // 天花板
            AddBox(roomGo, "Ceiling", new Vector3(0f, r.SizeY, 0f),
                new Vector3(r.SizeX, 0.1f, r.SizeZ), tint * 0.35f);
            // 四面墙：按门洞切段（门洞处留缺口，几何上由 LevelGeometry 保证可走）
            BuildWallsWithDoorGaps(roomGo, r, tint);
        }

        void BuildWallsWithDoorGaps(GameObject parent, Room r, Color tint)
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

            AddWallRun(parent, "N", north, r.SizeX, h, thickness, tint, true, true, r.SizeX, r.SizeZ);
            AddWallRun(parent, "S", south, r.SizeX, h, thickness, tint, true, false, r.SizeX, r.SizeZ);
            AddWallRun(parent, "W", west, r.SizeZ, h, thickness, tint, false, false, r.SizeX, r.SizeZ);
            AddWallRun(parent, "E", east, r.SizeZ, h, thickness, tint, false, true, r.SizeX, r.SizeZ);
        }

        /// <summary>把一面墙按门洞切成若干段（门洞位置来自 DSL 的 offsetM/widthM）。</summary>
        void AddWallRun(GameObject parent, string tag, List<(float a, float b)> gaps,
            float wallLen, float h, float thickness, Color tint, bool horizontal, bool atPositive,
            float roomSizeX, float roomSizeZ)
        {
            gaps.Sort((x, y) => x.a.CompareTo(y.a));
            float cursor = 0f;
            int seg = 0;
            foreach (var (a, b) in gaps)
            {
                if (a > cursor) AddWallSegment(parent, tag, seg++, cursor, a, wallLen, h, thickness, tint, horizontal, atPositive, roomSizeX, roomSizeZ);
                cursor = Mathf.Max(cursor, b);
            }
            if (cursor < wallLen) AddWallSegment(parent, tag, seg, cursor, wallLen, wallLen, h, thickness, tint, horizontal, atPositive, roomSizeX, roomSizeZ);
        }

        void AddWallSegment(GameObject parent, string tag, int idx, float from, float to,
            float wallLen, float h, float thickness, Color tint, bool horizontal, bool atPositive,
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
            AddBox(parent, $"Wall_{tag}{idx}", local, size, tint);
        }

        void BuildDoors(Room r)
        {
            foreach (var d in r.Doors)
            {
                d.ToWorld(r, out float x, out float z);
                var go = new GameObject($"Door_{r.Id}_{d.Id}");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(x, r.Floor * 3.5f, z);
                // 门框（门槛）：视觉标记 + 玩法层的开合锚点
                AddBox(go, "Frame", new Vector3(0f, r.SizeY / 2f, 0f),
                    d.Wall == "north" || d.Wall == "south"
                        ? new Vector3(d.WidthM, r.SizeY, 0.08f)
                        : new Vector3(0.08f, r.SizeY, d.WidthM),
                    LightZoneColor(r.LightZone) * 1.3f);
                DoorObjects.Add(go);
            }
        }

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
                AddBox(go, "Body", new Vector3(0f, 0.4f, 0f), new Vector3(sx, 0.8f, sz),
                    LightZoneColor(r.LightZone) * 0.8f);
                PropObjects.Add(go);
            }
        }

        static void AddBox(GameObject parent, string name, Vector3 localPos, Vector3 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = CubeMesh;
            mr.sharedMaterial = FlatMaterial(color);
        }

        // ── 材质与网格全部代码构建（V9 §19.1 C2：编辑器零参与）──
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
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(shader) { color = c };
            _matCache[c] = m;
            return m;
        }

        /// <summary>按 V9 §11 动态光分区给几何上色（数值来自 DesignTokens）。</summary>
        static Color LightZoneColor(string zone)
        {
            switch (zone)
            {
                case "safe": return Hex(DesignTokens.ColorBone);        // 安全区：暖白
                case "high-risk": return Hex(DesignTokens.ColorRust);    // 高风险：锈褐
                default: return Hex(DesignTokens.ColorMold);             // 压力区：霉绿灰
            }
        }

        static Color Hex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.gray;
            if (hex[0] == '#') hex = hex.Substring(1);
            if (hex.Length < 6) return Color.gray;
            return new Color32(
                System.Convert.ToByte(hex.Substring(0, 2), 16),
                System.Convert.ToByte(hex.Substring(2, 2), 16),
                System.Convert.ToByte(hex.Substring(4, 2), 16), 255);
        }
    }
}
