using UnityEngine;

namespace Whisper.Runtime
{
    /// <summary>
    /// 货车（移动基地与安全指挥中心）——用户《补充说明》§8 的落地实现。
    ///
    /// ## 规格来源（不猜，逐条对齐 §8 原文）
    /// · 外观参考 **Iveco Eurocargo 75E18**（2015 款，4×2 底盘，驾驶室在整车最前，厢体在后）；
    /// · 车牌 **7GHD666**（GHD = 虚构公司 Ghost Huntin' Distribution，666 彩蛋）；
    /// · 内部**前部指挥区 + 后部装备区**；
    /// · 监控电脑（驾驶座一侧桌面）：看摄像机 / 头戴 / 地图 CCTV 实时画面，可切摄像头与夜视；
    /// · 信息与任务面板（乘客侧前部）：本合约目标与鬼魂信息；旁边是**地点地图**（可切楼层）；
    /// · 装备墙（后部）：自带装备挂在墙架上，可取用/放回；
    /// · **车尾键盘**控制坡道；主门钥匙固定生成在键盘左侧；
    /// · **货车及其周围是安全区**：鬼无法进入，车内不掉理智。
    ///
    /// ## 尺寸依据（Eurocargo 75E18 真实规格 + 移动端可玩性取舍）
    /// 真实 75E18：总重 7.5 t、轴距 3105 mm、驾驶室宽约 2.1 m、厢体长约 4.5–5.2 m、整车高约 3.0 m。
    /// 本项目取：**驾驶室 2.2(宽)×2.1(长)×2.2(高)**、**厢体 2.4(宽)×5.4(长)×2.3(高)**、
    /// 整车长 ≈ 8.0 m、地板高 0.95 m、坡道 2.2×1.6 m。
    /// ⚠ 这些是**按真实规格取的工程近似**（移动端需要更宽的车内通道），标 design；
    ///   若后续查到 75E18 官方图纸再收紧，不改玩法只改数字。
    ///
    /// ## 为什么单独一个文件
    /// 货车既是"大厅里的一个物件"，也是"局内的安全区与装备区"——两处都要用同一份几何，
    /// 放独立文件避免大厅与局内各建一份、慢慢漂移（本项目在 UI 上已经踩过这个坑）。
    /// </summary>
    public class TruckScene
    {
        // ── 真实规格取的工程近似（米）────────────────────────────────────
        /// <summary>厢体内净尺寸（宽×长×高）—— 内部可走区。</summary>
        public const float BoxWidth = 2.4f;
        public const float BoxLength = 5.4f;
        public const float BoxHeight = 2.3f;
        /// <summary>驾驶室（宽×长×高）。</summary>
        public const float CabWidth = 2.2f;
        public const float CabLength = 2.1f;
        public const float CabHeight = 2.2f;
        /// <summary>地板离地高（厢体底板）。</summary>
        public const float FloorHeight = 0.95f;
        /// <summary>厢体板厚。</summary>
        public const float WallThickness = 0.08f;

        // ── 套件真实包围盒（**实测值**，来自 truck_eurocargo.glb 的导出尺寸）──────────
        // Blender（Z-up）导出为 x=2.70 · y=9.15 · z=3.52 → Unity Y-up 后：
        //   X ±1.35（含后视镜与挡泥板）· Y 0..3.52（含车顶导流罩）· Z −1.53..7.62（含坡道与驾驶室）
        // 为什么单列而不是从 BoxWidth/BoxHeight 推导：套件是**外部建模产物**，实际尺寸会随重做变化
        // （样板 v2→v3 就调整过）。判定范围跟着**实际几何**走才稳；设计常量继续表达"逻辑意图"（厢体净空）。
        /// <summary>套件半宽（含后视镜/挡泥板）。</summary>
        public const float KitHalfWidth = 1.35f;
        /// <summary>套件顶高（含车顶导流罩）。</summary>
        public const float KitTopY = 3.60f;
        /// <summary>套件在 Z 向的范围（含坡道 −1.53 与驾驶室 7.62）。</summary>
        public const float KitMinZ = -1.60f;
        public const float KitMaxZ = 7.70f;

        /// <summary>车牌（用户指定）。</summary>
        public const string PlateNumber = "7GHD666";

        /// <summary>整车的世界包围（用于安全区判定与大厅摆放）。</summary>
        public Bounds WorldBounds { get; private set; }

        /// <summary>指挥区（前部）中心；装备区（后部）中心。</summary>
        public Vector3 CommandCenter { get; private set; }
        public Vector3 GearCenter { get; private set; }

        /// <summary>坡道（车尾）中心与其宽度——撤离时所有存活玩家要站上去或进车。</summary>
        public Vector3 RampCenter { get; private set; }
        public float RampWidth => 2.2f;

        readonly Transform _root;

        /// <summary>
        /// 在 <paramref name="origin"/> 处建一辆货车。车头朝 <paramref name="yawDeg"/>（0 = 朝 +Z）。
        /// </summary>
        public TruckScene(Transform parent, Vector3 origin, float yawDeg, Material body, Material metal, Material glass)
        {
            var go = new GameObject("Truck");
            go.transform.SetParent(parent, false);
            go.transform.position = origin;
            go.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
            _root = go.transform;

            // 局部坐标：+Z = 车头方向，原点 = 厢体后缘地面
            float totalLen = CabLength + BoxLength;
            // 厢体从 z=0 到 z=BoxLength；驾驶室在 z=BoxLength..totalLen
            // ── 优先用**已登记的套件**装配（几何 + 真实 PBR 材质）────────────────
            // 为什么优先套件：套件是在 Blender 里参数化建的（带倒角、32 边轮胎、侧板竖筋、
            // 挡泥板、后视镜、导流罩），并经过 gate-model M9/M10 校验；而下面的程序化路径
            // 只有 Cube + 3 种材质。套件缺失时**回退**程序化（不静默失败、不空白）。
            if (TryBuildFromKit()) { KitBuilt = true; }
            else
            {
            BuildBox(body, metal, glass);
            BuildCab(body, metal, glass);
            BuildWheels(metal);
            BuildRamp(metal);
            BuildInterior(metal, glass);
            }

            // 世界包围：按**套件真实尺寸**算（原先用设计常量，而且方向还错了）
            // 【旧实现的错】`forward * (totalLen*0.5 - BoxLength*0.5)` 得到 +3.75，
            // 但车头朝 **−Z**（套件局部 Z ∈ [KitMinZ, KitMaxZ]，+Z 端是车头 → 世界朝 −Z），
            // 于是包围盒中心被推到车身之外，安全区跟着错位。
            float midLocalZ = (KitMinZ + KitMaxZ) * 0.5f;
            float lenWorld = KitMaxZ - KitMinZ;
            var center = origin - go.transform.forward * midLocalZ;   // 车头朝 −Z，故取负
            WorldBounds = new Bounds(
                center + Vector3.up * (KitTopY * 0.5f),
                new Vector3(KitHalfWidth * 2f, KitTopY, lenWorld));

            CommandCenter = L(new Vector3(0f, FloorHeight + 1.1f, BoxLength - 0.9f));
            GearCenter = L(new Vector3(0f, FloorHeight + 1.2f, 1.0f));
            RampCenter = L(new Vector3(0f, FloorHeight * 0.5f, -0.8f));
        }

        /// <summary>本车几何是否来自已登记的套件（false = 走了程序化回退）。</summary>
        public bool KitBuilt { get; private set; }

        /// <summary>套件装配诊断一行（HUD 关闭时落盘取证用）。</summary>
        public string SelfTest()
            => KitBuilt
               ? $"货车几何：套件 {KitId}（部件 {_kitParts} · 材质 {_kitMats}）"
               : $"货车几何：**程序化回退**（套件 {KitId} 不可用）";

        const string KitId = "truck_eurocargo";
        int _kitParts, _kitMats;

        /// <summary>
        /// 用套件装配货车（几何来自 GLB，材质按**材质名**映射到本产品的 MaterialFamily）。
        /// 返回 false 表示套件不可用 —— 调用方应回退程序化几何。
        /// </summary>
        bool TryBuildFromKit()
        {
            var parts = Whisper.Gameplay.Level.KitMeshLibrary.GetParts(KitId);
            if (parts == null || parts.Length == 0) return false;
            var matIdx = Whisper.Gameplay.Level.KitMeshLibrary.GetPartMaterials(KitId);
            var kitMats = Whisper.Gameplay.Level.KitMeshLibrary.GetMaterials(KitId);
            _kitParts = parts.Length;
            _kitMats = kitMats != null ? kitMats.Length : 0;

            // 套件顶点已在"世界坐标"（构建时应用了 node 变换），故直接置于本车原点下。
            // 高度对齐：套件脚底在 y=0，与本车 FloorHeight 语义一致（样板即按此建的）。
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == null) continue;
                var go = new GameObject($"TruckKit_{i}");
                go.transform.SetParent(_root, false);
                go.transform.localPosition = Vector3.zero;
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = parts[i];
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = MaterialForPart(matIdx, kitMats, i);

                // 【P0-2 补 · 大厅碰撞体】此前这里**只有 MeshFilter + MeshRenderer**，
                // 于是玩家自由行走时会**直接穿过货车** —— 与官方要求
                // 「大厅的3D模型包含完整的碰撞体，玩家可以在其中自由行走」不符。
                //
                // 为什么用**非凸 MeshCollider** 而不是省事的 BoxCollider：
                // 货车在本作里是"移动基地/安全指挥中心"（《补充说明》§8），**玩家要能走进车厢**。
                // 一个包住整车的实心 Box 会把车厢内部填实 —— 看着能进、走进去被弹开，
                // 比没有碰撞体更糟。非凸网格才能表达"有外壳、内有空腔"。
                // 静态物体允许非凸（本车在 HallScene 里是停放状态、无 Rigidbody），故这条路可行。
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = parts[i];
                mc.convex = false;
            }
            return true;
        }

        /// <summary>
        /// 第 i 个部件的材质：优先用套件自带 PBR 参数，缺则用中性灰兜底。
        /// 映射**按材质名**（下标会随导出顺序变，名字稳定）。
        /// </summary>
        static Material MaterialForPart(int[] matIdx, Whisper.Gameplay.Level.GlbReader.KitMaterial[] mats, int part)
        {
            if (matIdx == null || mats == null || part >= matIdx.Length) return Fallback();
            int mi = matIdx[part];
            if (mi < 0 || mi >= mats.Length) return Fallback();
            var km = mats[mi];
            var col = new Color(km.R, km.G, km.B, 1f);
            var fam = FamilyOf(km.Name);
            return Whisper.Runtime.SceneMaterials.Lit(col, Mathf.Clamp(km.Roughness, 0.05f, 1f), fam);
        }

        static Material Fallback()
            => Whisper.Runtime.SceneMaterials.Lit(new Color(0.5f, 0.5f, 0.52f), 0.6f,
                   Whisper.Gameplay.Render.MaterialFamily.Metal);

        /// <summary>
        /// 套件材质名 → 本产品材质族。产品族没有玻璃/橡胶（实测：Plaster/Concrete/Wood/Metal/
        /// RustMetal/Tile/Fabric），按下表就近映射；未知名字落 Metal（中性、不会出现怪色）。
        /// </summary>
        static Whisper.Gameplay.Render.MaterialFamily FamilyOf(string name)
        {
            switch (name)
            {
                case "chassis_metal": return Whisper.Gameplay.Render.MaterialFamily.Metal;
                case "body_paint":    return Whisper.Gameplay.Render.MaterialFamily.Metal;
                case "plate":         return Whisper.Gameplay.Render.MaterialFamily.Metal;
                case "glass":         return Whisper.Gameplay.Render.MaterialFamily.Tile;    // 光滑平整面
                case "trim_plastic":  return Whisper.Gameplay.Render.MaterialFamily.Fabric;  // 哑光非金属
                case "tyre_rubber":   return Whisper.Gameplay.Render.MaterialFamily.Fabric;  // 极粗糙非金属
                default:              return Whisper.Gameplay.Render.MaterialFamily.Metal;
            }
        }

        /// <summary>局部 → 世界。</summary>
        public Vector3 L(Vector3 local) => _root.TransformPoint(local);

        /// <summary>
        /// 安全区外扩（米）。§8 原文是"**货车及其周围**是安全区"，故不是只有车厢内部。
        /// 取 2.5 m：够覆盖坡道落地区与绕车一圈，又不会把"刚下车就被保护"扩散太远
        /// （那会让玩家在车边站着刷理智，破坏"必须回车上"的张力）。**design 值**。
        /// </summary>
        public const float SafeZoneMargin = 2.5f;

        /// <summary>安全区世界 AABB（车体 + 外扩）。局内每帧读它做理智与怪物判定。</summary>
        public Bounds SafeZoneBounds => new Bounds(
            WorldBounds.center + Vector3.up * 0.5f,
            new Vector3(WorldBounds.size.x + SafeZoneMargin * 2f,
                        WorldBounds.size.y,
                        WorldBounds.size.z + SafeZoneMargin * 2f));

        /// <summary>世界点是否在安全区内（§8：鬼无法进入，在车内不掉理智）。</summary>
        public bool IsInSafeZone(Vector3 world) => SafeZoneBounds.Contains(world);

        /// <summary>世界点是否在货车内部（含车厢地板以上、顶棚以下）。</summary>
        public bool Contains(Vector3 world)
        {
            var p = _root.InverseTransformPoint(world);
            // 【换套件后同步判定范围】原先用设计常量（上限 0.95+2.30=3.25、半宽 1.20），
            // 而套件实际是 Y 0..3.52、|X| ≤1.35 → 车顶与后视镜处会被判成"不在车内"，
            // 于是 §8 的"车内不掉理智/鬼无法进入"在那两处失效。改为按**套件真实包围盒**判定。
            if (p.y < FloorHeight - 0.2f || p.y > KitTopY) return false;   // 地板下留 0.2m 容差（踩坡道时不抖）
            if (Mathf.Abs(p.x) > KitHalfWidth) return false;
            return p.z > KitMinZ && p.z < KitMaxZ;
        }

        // ── 车体 ─────────────────────────────────────────────────────────
        /// <summary>厢体：底板 + 四壁 + 顶棚 + 后门框（后门留给坡道）。</summary>
        void BuildBox(Material body, Material metal, Material glass)
        {
            float h = BoxHeight, t = WallThickness, w = BoxWidth, l = BoxLength;
            float y0 = FloorHeight;
            // 底板
            Part("Box_Floor", new Vector3(0f, y0 - t * 0.5f, l * 0.5f), new Vector3(w, t, l), metal);
            // 两侧壁
            Part("Box_WallL", new Vector3(-w * 0.5f + t * 0.5f, y0 + h * 0.5f, l * 0.5f), new Vector3(t, h, l), body);
            Part("Box_WallR", new Vector3(w * 0.5f - t * 0.5f, y0 + h * 0.5f, l * 0.5f), new Vector3(t, h, l), body);
            // 前壁（朝驾驶室）
            Part("Box_WallFront", new Vector3(0f, y0 + h * 0.5f, l - t * 0.5f), new Vector3(w, h, t), body);
            // 顶棚
            Part("Box_Roof", new Vector3(0f, y0 + h + t * 0.5f, l * 0.5f), new Vector3(w, t, l), body);
            // 后门框（上梁）：下方即坡道开口
            Part("Box_RearHeader", new Vector3(0f, y0 + h - 0.25f, t * 0.5f), new Vector3(w, 0.5f, t), body);
            // 车牌（车尾）
            Part("Plate", new Vector3(0f, y0 - 0.30f, -0.02f), new Vector3(0.52f, 0.13f, 0.02f), metal);
        }

        /// <summary>驾驶室：Eurocargo 是平头（cab-over），驾驶室在最前、与厢体同宽略窄。</summary>
        void BuildCab(Material body, Material metal, Material glass)
        {
            float w = CabWidth, l = CabLength, h = CabHeight;
            float z0 = BoxLength;              // 驾驶室起点
            float cz = z0 + l * 0.5f;
            float y0 = FloorHeight - 0.35f;    // 驾驶室地板比厢体略低
            Part("Cab_Body", new Vector3(0f, y0 + h * 0.5f, cz), new Vector3(w, h, l), body);
            // 挡风玻璃（前倾）
            var ws = Part("Cab_Windshield", new Vector3(0f, y0 + h * 0.72f, cz + l * 0.5f - 0.05f),
                new Vector3(w * 0.86f, h * 0.34f, 0.04f), glass);
            ws.transform.localRotation = Quaternion.Euler(-12f, 0f, 0f);
            // 侧窗
            Part("Cab_WindowL", new Vector3(-w * 0.5f + 0.02f, y0 + h * 0.70f, cz - 0.15f), new Vector3(0.04f, h * 0.30f, l * 0.45f), glass);
            Part("Cab_WindowR", new Vector3(w * 0.5f - 0.02f, y0 + h * 0.70f, cz - 0.15f), new Vector3(0.04f, h * 0.30f, l * 0.45f), glass);
            // 保险杠 + 前照灯
            Part("Cab_Bumper", new Vector3(0f, y0 + 0.28f, cz + l * 0.5f + 0.05f), new Vector3(w * 0.98f, 0.34f, 0.14f), metal);
            Part("Cab_LampL", new Vector3(-w * 0.34f, y0 + 0.52f, cz + l * 0.5f + 0.06f), new Vector3(0.30f, 0.18f, 0.06f), metal);
            Part("Cab_LampR", new Vector3(w * 0.34f, y0 + 0.52f, cz + l * 0.5f + 0.06f), new Vector3(0.30f, 0.18f, 0.06f), metal);
        }

        /// <summary>车轮：4×2 底盘 = 前 1 轴 + 后 1 轴，共 4 轮。</summary>
        void BuildWheels(Material metal)
        {
            float r = 0.48f, tw = 0.30f;
            float x = CabWidth * 0.5f - 0.06f;
            float zFront = BoxLength + CabLength * 0.55f;
            float zRear = BoxLength * 0.42f;
            foreach (var z in new[] { zFront, zRear })
            {
                Part("WheelL", new Vector3(-x, r, z), new Vector3(tw, r * 2f, r * 2f), metal);
                Part("WheelR", new Vector3(x, r, z), new Vector3(tw, r * 2f, r * 2f), metal);
            }
        }

        /// <summary>坡道（车尾）：键盘控制升降；本类只建几何，"升/降"由局内逻辑驱动。</summary>
        void BuildRamp(Material metal)
        {
            float w = RampWidth, len = 1.6f;
            var ramp = Part("Ramp", new Vector3(0f, FloorHeight * 0.5f, -len * 0.5f), new Vector3(w, 0.07f, len), metal);
            ramp.name = "Truck_Ramp";     // 局内逻辑按名字找它做升降动画
            // 键盘（车尾左侧，§8：主门钥匙固定生成在键盘左侧）
            Part("RampKeypad", new Vector3(-w * 0.5f - 0.12f, FloorHeight + 0.55f, 0.05f), new Vector3(0.16f, 0.22f, 0.05f), metal);
        }

        /// <summary>车内三大功能区：指挥区（前）+ 装备区（后）+ 监控/信息面板。</summary>
        void BuildInterior(Material metal, Material glass)
        {
            float y0 = FloorHeight, h = BoxHeight;
            // ── 指挥区（前部）：监控电脑在驾驶座一侧（左），信息/任务面板在乘客侧（右）──
            float zCmd = BoxLength - 0.9f;
            // 桌面
            Part("Cmd_Desk", new Vector3(-0.55f, y0 + 0.75f, zCmd), new Vector3(1.0f, 0.06f, 0.55f), metal);
            // 监控电脑屏（左击切换摄像头；左击键盘切夜视 —— 交互由局内逻辑接）
            var mon = Part("Cmd_MonitorScreen", new Vector3(-0.55f, y0 + 1.12f, zCmd + 0.10f), new Vector3(0.72f, 0.42f, 0.03f), glass);
            mon.name = "Truck_MonitorScreen";
            // 信息与任务面板（乘客侧）
            var task = Part("Cmd_TaskPanel", new Vector3(0.62f, y0 + 1.15f, zCmd + 0.06f), new Vector3(0.62f, 0.44f, 0.03f), glass);
            task.name = "Truck_TaskPanel";
            // 地点地图（面板旁；可切楼层 —— 交互由局内逻辑接）
            var map = Part("Cmd_MapPanel", new Vector3(0.62f, y0 + 0.62f, zCmd + 0.06f), new Vector3(0.62f, 0.36f, 0.03f), glass);
            map.name = "Truck_MapPanel";
            // 座椅（两张，指挥位）
            Part("Cmd_SeatL", new Vector3(-0.62f, y0 + 0.30f, zCmd - 0.62f), new Vector3(0.42f, 0.60f, 0.42f), metal);
            Part("Cmd_SeatR", new Vector3(0.62f, y0 + 0.30f, zCmd - 0.62f), new Vector3(0.42f, 0.60f, 0.42f), metal);

            // ── 装备区（后部）：装备墙（左右两排挂架）──
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -(BoxWidth * 0.5f - 0.16f) : (BoxWidth * 0.5f - 0.16f);
                Part("Gear_Rack" + i, new Vector3(x, y0 + 1.15f, 1.35f), new Vector3(0.10f, 1.30f, 2.20f), metal);
                // 墙架横档（挂自带装备的位置，三段）
                for (int k = 0; k < 3; k++)
                {
                    Part("Gear_Shelf" + i + "_" + k,
                        new Vector3(x + (i == 0 ? 0.16f : -0.16f), y0 + 0.75f + k * 0.42f, 1.35f),
                        new Vector3(0.30f, 0.04f, 2.10f), metal);
                }
            }
            // 后部中央通道留空（可走到车尾坡道）
        }

        /// <summary>建一个长方体零件（挂在本车根节点下）。</summary>
        GameObject Part(string name, Vector3 localCenter, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(_root, false);
            go.transform.localPosition = localCenter;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;
            return go;
        }
    }
}
