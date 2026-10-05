using System.Collections.Generic;
using UnityEngine;
using Whisper.Gameplay.Render;

namespace Whisper.Runtime
{
    /// <summary>
    /// 主界面 3D 场景：**工业风两层仓库**（用户永久约束 §4，完全参考恐鬼症官方大厅）。
    ///
    /// ## 为什么另起一个文件而不是改 MenuScene
    /// 用户 2026-10-05 明确指示：「主界面的 3D 场景需要更换，因为这是为之前的玩法准备的」
    /// 以及「不要再在旧界面上花时间了」。旧的 `MenuScene` 是"一条走廊 + 5 个按钮"，
    /// 它的几何是照着"恐怖游戏走廊"设计的；官方大厅是**一个可探索的仓库**，两者没有可复用的部分。
    /// 所以：旧文件保留（UI 与交互逻辑仍在那里可用），**3D 场景整体换成这个类**。
    ///
    /// ## 官方大厅的结构（自查证过的清单，逐条落地）
    ///   · **两层**：一层 = 核心交互区，二层 = 娱乐/展示区
    ///   · 风格：工业风仓库，**灰暗水泥色 + 金属质感**
    ///   · **主菜单板**在**最亮的那面墙**上（视觉焦点）
    ///   · **地图选项板**在菜单板**左侧**（投票选图）
    ///   · **装备商店**在菜单板**右侧**的一台**电脑**上
    ///   · **ID 卡**在菜单板**右上角**（资金/等级/进度）
    ///   · 窗外天空**异样颜色**（血红）；白板附近**灵球粒子**漂浮
    ///   · 完整碰撞体、可自由行走；道具（篮球/喷漆罐）有独立物理
    ///   · 二楼：篮球 / 玉米洞 / 保龄球 / 叠叠乐
    ///
    /// ## 碰撞体策略（**必须做**，官方明确"玩家可在其中自由行走"）
    /// 所有地板/墙/楼板都挂 `BoxCollider`；楼梯用**分段盒体**模拟（每级一个薄盒），
    /// 这样任何胶囊体角色控制器都能走上去，不需要专门的斜坡代码。
    ///
    /// ## 纪律（与项目其他部分一致）
    ///   · 无 `UnityEngine.Random`、无 `DateTime` —— 粒子相位等全部用整数哈希派生
    ///   · 全部材质走 `SceneMaterials.Lit`（自研 PBR，见 WhisperLitPbr.shader）
    ///   · 只建几何与灯光；**UI 与按钮在 MenuScene 里**（本类不碰 uGUI）
    /// </summary>
    public sealed class HallScene
    {
        // ── 尺寸（米）。工业风仓库：高大、开阔 ──
        public float WidthM = 26f;      // X：跨度
        public float LengthM = 18f;     // Z：进深
        public float FloorHeightM = 5.6f;
        public float WallThicknessM = 0.35f;
        /// <summary>二层楼板高度（一层净高）。</summary>
        public float MezzanineYM = 3.1f;
        /// <summary>二层楼板占用的进深比例（靠后墙那侧）。</summary>
        public float MezzanineDepthRatio = 0.42f;

        Transform _root;
        Camera _cam;
        /// <summary>主菜单板的中心（供 MenuScene 把 UI 画板对准它 / 相机对准它）。</summary>
        public Vector3 MenuBoardPos { get; private set; }
        /// <summary>6 张"选项纸片"的世界中心（顺序：左上→中上→右上→左下→中下→右下）。</summary>
        Vector3[] _noteCenter;
        /// <summary>菜单板的**拾取面**（不可见 BoxCollider；射线打它 = 玩家想操作菜单）。</summary>
        GameObject _pickPlate;

        /// <summary>
        /// 货车（移动基地/安全指挥中心，用户《补充说明》§8）。
        /// 大厅与局内**共用同一辆**：大厅里熄屏待命（除 "00:00" 计时器），局内是命令区+装备区+安全区。
        /// 共用同一实例，避免"两处各建一份慢慢漂移"（本项目在 UI 上已踩过这个坑）。
        /// </summary>
        public TruckScene Truck { get; private set; }

        /// <summary>
        /// 货车安全区（世界 AABB）。局内每帧把它喂给玩法层做理智与怪物判定 ——
        /// 玩法层（Gameplay）**不反向依赖** Runtime 的几何，只收纯数据。
        /// 货车未建时返回一个"空且在地底"的矩形，保证任何点都不在内（fail-safe：宁可不保护也不误保护）。
        /// </summary>
        public Bounds TruckSafeZone => Truck != null
            ? Truck.SafeZoneBounds
            : new Bounds(new Vector3(0f, -1000f, 0f), Vector3.zero);

        /// <summary>菜单板拾取面（供 MenuScene 做射线比对）。</summary>
        public GameObject MenuBoardPicker => _pickPlate;
        /// <summary>选项纸片数（= 官方主菜单板上的条目数）。</summary>
        public int NoteCount => _noteCenter != null ? _noteCenter.Length : 0;
        /// <summary>取第 i 张纸片的世界中心（UI 用来把文字贴到纸片上）。</summary>
        public Vector3 NoteCenter(int i)
            => (_noteCenter != null && i >= 0 && i < _noteCenter.Length) ? _noteCenter[i] : MenuBoardPos;
        /// <summary>菜单板尺寸（UI 定位用）。</summary>
        public float MenuBoardWidthM { get; private set; } = 7.2f;
        public float MenuBoardHeightM { get; private set; } = 3.4f;
        /// <summary>建议的相机站位与朝向（主界面视角）。</summary>
        public Vector3 ViewPos { get; private set; }
        public Vector3 ViewLookAt { get; private set; }

        /// <summary>已建出的物件数（自检 / HUD）。</summary>
        public int BuiltCount { get; private set; }
        /// <summary>建场失败原因（null = 正常）。</summary>
        public string Problem { get; private set; }

        // ── 材质缓存（同一颜色只建一次）──
        readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();

        public HallScene(Transform parent, Camera cam)
        {
            _root = new GameObject("HallRoot").transform;
            _root.SetParent(parent, false);
            _cam = cam;
        }

        /// <summary>取（或建）一个 PBR 材质。family 决定细节贴图与金属度。</summary>
        Material Mat(Color c, float roughness, MaterialFamily family)
        {
            string key = ((int)(c.r * 255)) + "_" + ((int)(c.g * 255)) + "_" + ((int)(c.b * 255))
                       + "_" + (int)(roughness * 100) + "_" + (int)family;
            if (_mats.TryGetValue(key, out var hit) && hit != null) return hit;
            var m = SceneMaterials.Lit(c, roughness, family);
            _mats[key] = m;
            return m;
        }

        /// <summary>建一个盒体（可选带碰撞）。几何 + 碰撞 + 材质一步到位，避免三处各写一遍。</summary>
        GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material mat, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && mat != null) mr.sharedMaterial = mat;
            if (!collider)
            {
                var col = go.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
            }
            BuiltCount++;
            return go;
        }

        GameObject Light(Transform parent, string name, Vector3 pos, LightType type, Color color,
                   float intensity, float range, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var l = go.AddComponent<Light>();
            l.type = type;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;   // Auto 在灯多时会被降级成顶点光
            BuiltCount++;
            return go;
        }

        /// <summary>搭整个大厅。</summary>
        public void Build()
        {
            try
            {
                BuildShell();
                BuildMezzanine();
                BuildStairs();
                BuildMenuBoard();
                BuildMapBoard();
                BuildShopComputer();
                BuildIdCard();
                BuildWindows();
                BuildLights();
                BuildProps();
                PlaceView();
                Problem = null;
            }
            catch (System.Exception e)
            {
                Problem = e.GetType().Name + "：" + e.Message;
                Debug.LogError("[Whisper] 大厅建造失败：" + Problem);
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        // 一层：外壳（地板 / 四面墙 / 天花板桁架）
        // ════════════════════════════════════════════════════════════════════════
        void BuildShell()
        {
            float w = WidthM, d = LengthM, h = FloorHeightM, t = WallThicknessM;

            // 材质：官方是"灰暗水泥色 + 金属质感"
            var concrete = Mat(new Color(0.26f, 0.26f, 0.27f), 0.85f, MaterialFamily.Concrete);
            var concreteDark = Mat(new Color(0.17f, 0.17f, 0.18f), 0.90f, MaterialFamily.Concrete);
            var metal = Mat(new Color(0.30f, 0.31f, 0.34f), 0.55f, MaterialFamily.Metal);
            var rust = Mat(new Color(0.32f, 0.22f, 0.16f), 0.80f, MaterialFamily.RustMetal);

            // 地板（分块：让细节贴图有变化，一整块会显得平）
            Box(_root, "Floor", new Vector3(0f, -t * 0.5f, 0f), new Vector3(w, t, d), concrete);
            for (int i = 0; i < 5; i++)
            {
                float x = -w * 0.4f + i * (w * 0.2f);
                Box(_root, "FloorSlab", new Vector3(x, 0.012f, 0f),
                    new Vector3(w * 0.19f, 0.02f, d * 0.98f), i % 2 == 0 ? concrete : concreteDark, false);
            }

            // 四面墙
            Box(_root, "WallBack", new Vector3(0f, h * 0.5f, d * 0.5f - t * 0.5f), new Vector3(w, h, t), concrete);
            Box(_root, "WallFront", new Vector3(0f, h * 0.5f, -d * 0.5f + t * 0.5f), new Vector3(w, h, t), concreteDark);
            Box(_root, "WallLeft", new Vector3(-w * 0.5f + t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, d), concrete);
            Box(_root, "WallRight", new Vector3(w * 0.5f - t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, d), concrete);

            // 天花板
            Box(_root, "Ceiling", new Vector3(0f, h + t * 0.5f, 0f), new Vector3(w, t, d), concreteDark);

            // 钢结构：立柱 + 顶桁架（工业风的骨架，也是最省成本的空间层次来源）
            // 【摆位修正 · 用户 2026-10-05 反馈"菜单中间有个柱子你不觉得奇怪吗"】
            // 原先是 5 个等分开间（-13/-6.5/0/+6.5/+13），而**菜单板中心也在 x=0**
            // → 画面正中立起一根结构柱，正好挡住主视觉。这是**建模摆位错误**，不是审美问题：
            // 墙面主视觉元素之前不该有结构柱。
            // 改成 4 个开间、**跳过中间**（x = ±w*0.375 与 ±w*0.125），柱子落在纸片与纸片之间。
            for (int i = 0; i < 4; i++)
            {
                float x = (i < 2 ? -1f : 1f) * w * (i % 2 == 0 ? 0.375f : 0.125f);
                Box(_root, "Column", new Vector3(x, h * 0.5f, d * 0.5f - 0.45f), new Vector3(0.32f, h, 0.32f), metal);
                Box(_root, "Column", new Vector3(x, h * 0.5f, -d * 0.5f + 0.45f), new Vector3(0.32f, h, 0.32f), metal);
                // 顶桁架（X 向）
                Box(_root, "Truss", new Vector3(x, h - 0.30f, 0f), new Vector3(0.20f, 0.20f, d * 0.96f), metal, false);
            }
            // 纵向系梁
            for (int i = 0; i <= 3; i++)
            {
                float z = -d * 0.5f + 0.45f + i * ((d - 0.9f) / 3f);
                // 顶桁架原先是**一根横贯全宽**的梁，正好压在菜单板上方（取景里就是一条横杠）。
            // 拆成两段、让开中间 3.6m —— 与立柱同样的道理：别在主视觉正上方压东西。
            Box(_root, "TrussZ", new Vector3(-w * 0.30f, h - 0.30f, z), new Vector3(w * 0.34f, 0.16f, 0.16f), metal, false);
            Box(_root, "TrussZ", new Vector3(w * 0.30f, h - 0.30f, z), new Vector3(w * 0.34f, 0.16f, 0.16f), metal, false);
            }

            // 管道（沿后墙上方，工业感 + 遮挡视线制造纵深）
            Box(_root, "Pipe", new Vector3(0f, h - 0.75f, d * 0.5f - 0.55f), new Vector3(w * 0.94f, 0.22f, 0.22f), rust, false);
            Box(_root, "Pipe", new Vector3(0f, h - 1.05f, d * 0.5f - 0.75f), new Vector3(w * 0.94f, 0.14f, 0.14f), rust, false);
        }

        // ════════════════════════════════════════════════════════════════════════
        // 二层楼板（娱乐/展示区）
        // ════════════════════════════════════════════════════════════════════════
        void BuildMezzanine()
        {
            float w = WidthM, d = LengthM, t = WallThicknessM;
            float slabDepth = d * MezzanineDepthRatio;
            float slabZ = d * 0.5f - slabDepth * 0.5f;      // 靠后墙那侧
            var deck = Mat(new Color(0.20f, 0.20f, 0.21f), 0.80f, MaterialFamily.Concrete);
            var rail = Mat(new Color(0.30f, 0.31f, 0.34f), 0.50f, MaterialFamily.Metal);

            Box(_root, "Mezzanine", new Vector3(0f, MezzanineYM - t * 0.5f, slabZ),
                new Vector3(w - WallThicknessM * 2f, t, slabDepth), deck);

            // 栏杆（沿二层临空边）—— 官方大厅二层是开放式的
            float railZ = slabZ - slabDepth * 0.5f + 0.08f;
            Box(_root, "RailTop", new Vector3(0f, MezzanineYM + 1.05f, railZ),
                new Vector3(w - WallThicknessM * 2f, 0.09f, 0.09f), rail, false);
            Box(_root, "RailMid", new Vector3(0f, MezzanineYM + 0.55f, railZ),
                new Vector3(w - WallThicknessM * 2f, 0.06f, 0.06f), rail, false);
            int posts = 13;
            for (int i = 0; i < posts; i++)
            {
                float x = -w * 0.5f + WallThicknessM + i * ((w - WallThicknessM * 2f) / (posts - 1));
                Box(_root, "RailPost", new Vector3(x, MezzanineYM + 0.55f, railZ),
                    new Vector3(0.07f, 1.10f, 0.07f), rail, false);
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        // 楼梯（分段盒体 = 任何角色控制器都能走，不需要斜坡代码）
        // ════════════════════════════════════════════════════════════════════════
        void BuildStairs()
        {
            var metal = Mat(new Color(0.28f, 0.29f, 0.32f), 0.60f, MaterialFamily.Metal);
            const int Steps = 16;
            float stepH = MezzanineYM / Steps;
            float stepD = 0.30f;
            float stairW = 2.2f;
            // 靠右墙、沿 +Z 往上走
            float x = WidthM * 0.5f - WallThicknessM - stairW * 0.5f - 0.1f;
            float z0 = LengthM * 0.5f - LengthM * MezzanineDepthRatio - 0.4f;

            for (int i = 0; i < Steps; i++)
            {
                float y = stepH * (i + 0.5f);
                float z = z0 - i * stepD;
                Box(_root, "Step", new Vector3(x, y, z), new Vector3(stairW, stepH, stepD), metal);
            }
            // 楼梯扶手（斜向，用分段）
            for (int i = 0; i < Steps; i += 2)
            {
                float y = stepH * (i + 0.5f) + 0.95f;
                float z = z0 - i * stepD;
                Box(_root, "StairRail", new Vector3(x - stairW * 0.5f, y, z),
                    new Vector3(0.07f, 0.07f, stepD * 2.2f), metal, false);
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        // 主菜单板（最亮墙上 = 视觉焦点）
        // ════════════════════════════════════════════════════════════════════════
        /// <summary>
        /// 建货车（§8）。材质在本方法内现取 —— HallScene 的材质都是方法内局部变量，
        /// 没有可供外部引用的材质字段（第一版我凭猜写了 `_matBody`，语法门禁抓不到、EditMode 才红）。
        /// 位置：左侧靠前空地、车头朝 +X（横停），车尾朝出生点方向便于上车。
        /// </summary>
        void BuildTruck()
        {
            // 【材质对齐样板 v3】依据 docs/truck-sample-v3-material-contrast.md 的**材质对比自检**
            // （相对亮度 L = 0.2126R+0.7152G+0.0722B，要求"必须一眼分得开"的组合 ΔL ≥ 0.15）：
            //   车漆 L≈0.71 · 底盘 L≈0.21 · 玻璃 L≈0.08 → 车身/玻璃 ΔL≈0.63
            var bodyMat = Mat(new Color(0.70f, 0.71f, 0.73f), 0.32f, MaterialFamily.Metal);   // 车漆：亮、微金属
            var metalMat = Mat(new Color(0.20f, 0.21f, 0.23f), 0.45f, MaterialFamily.Metal);  // 底盘/轮毂/键盘：暗、强金属
            // 【玻璃被"洗白"的根因】原先用 `MaterialFamily.Metal`（**高金属度族**）→ 玻璃反射环境光被洗白，
            // 视觉上与车身同色（样板正面图实测：两者几乎分不开）。
            // 而产品的 `MaterialFamily` **没有玻璃族**（实测只有 Plaster/Concrete/Wood/Metal/RustMetal/Tile/Fabric），
            // 故改用 **Tile**（光滑、接缝少 —— 最接近玻璃的平整洁净面）+ 压暗基色 → 读作"暗色平整面"。
            var glassMat = Mat(new Color(0.06f, 0.08f, 0.11f), 0.15f, MaterialFamily.Tile);   // 车窗与屏幕：暗色平整面
            // 【用户 2026-10-05：「谁家好人把车放在这里」】
            // 原先放在 x=-0.30W、z=-0.08L —— 大厅中偏左、紧邻菜单板那面前墙，正好挡住主视野。
            // 官方大厅（《补充说明》§4 工业风两层仓库 + §8 货车是移动基地）里，货车停在
            // **仓库一侧的卷帘门位**、车尾朝玩家活动区。故改为：沿右后侧停放、车头朝后墙（-Z），
            // 车尾朝场内 —— 玩家出生在货车后方、面向菜单板，货车成为"身后的基地"而不是"眼前的路障"。
            float tx = WidthM * 0.30f;              // 右侧（商店电脑一侧），避开菜单板主视野
            float tz = LengthM * 0.22f;             // 靠后墙（+Z 侧），前墙（-Z）留给菜单板
            Truck = new TruckScene(_root, new Vector3(tx, 0f, tz), 180f, bodyMat, metalMat, glassMat);
        }
        void BuildMenuBoard()
        {
            var boardMat = Mat(new Color(0.52f, 0.44f, 0.34f), 0.78f, MaterialFamily.Wood);   // 软木板色
            var frameMat = Mat(new Color(0.26f, 0.26f, 0.29f), 0.55f, MaterialFamily.Metal);

            float w = WidthM, h = FloorHeightM;
            // 前墙（-Z）是最容易被相机看到的面 → 菜单板放这里，并把灯打上去
            float z = -LengthM * 0.5f + WallThicknessM + 0.08f;
            float by = 2.55f;
            float bw = 7.2f, bh = 3.4f;
            MenuBoardWidthM = bw; MenuBoardHeightM = bh;

            Box(_root, "MenuBoardFrame", new Vector3(0f, by, z), new Vector3(bw + 0.18f, bh + 0.18f, 0.10f), frameMat, false);
            Box(_root, "MenuBoard", new Vector3(0f, by, z - 0.02f), new Vector3(bw, bh, 0.06f), boardMat, false);
            MenuBoardPos = new Vector3(0f, by, z - 0.05f);

            // 货车（§8）：大厅里停着待命，局内复用同一实例。放在建板之后，确保灯光/相机已就绪。
            BuildTruck();

            // ── 交互用的实体板 ──
            // 【为什么用 3D 拾取而不是屏幕 UI】用户 §4 要求"**进入游戏后你会面对一块主菜单板，
            // 按空格键或鼠标左键点击即可进入操作界面**" —— 菜单板是场景里的**实体**，
            // 点击要打在它身上。所以板子带 BoxCollider，由 MenuScene 用射线拾取。
            // 拾取对象用一个不可见的"拾取面"（比板略大），避免手指点在板边缘外就失效。
            _pickPlate = Box(_root, "MenuBoardPick", new Vector3(0f, by, z - 0.10f),
                             new Vector3(bw + 0.5f, bh + 0.5f, 0.30f), null, true);
            if (_pickPlate != null)
            {
                // 拾取面自己不该被看见：关掉渲染器，只留碰撞体。
                var mr = _pickPlate.GetComponent<MeshRenderer>();
                if (mr != null) mr.enabled = false;
            }

            // 板上的"纸片"：**每个选项一张纸**（官方软木板风格），并记录它们的中心供 UI 定位。
            _noteCenter = new Vector3[6];
            for (int i = 0; i < 6; i++)
            {
                float px = -bw * 0.32f + (i % 3) * (bw * 0.32f);
                float py = by + (i < 3 ? 0.72f : -0.72f);
                Box(_root, "MenuNote", new Vector3(px, py, z - 0.07f),
                    new Vector3(1.62f, 1.02f, 0.02f), Mat(new Color(0.86f, 0.84f, 0.76f), 0.85f, MaterialFamily.Fabric), false);
                _noteCenter[i] = new Vector3(px, py, z - 0.08f);
            }

            // 打光：这是"最亮的墙"，所以给一盏专用射灯（官方明确"主菜单板是最亮点"）
            // 官方明确"**主菜单板是最亮点**"。第一版单盏 3.4 强度在真机截图里仍偏暗
            // （仓库整体很暗 + 点光衰减快）→ 改**双灯**：射灯打板面 + 近距点光补亮纸片。
            // 为什么不干脆整体提亮：那会毁掉仓库的暗调（用户要"画面暗调"）。
            // 射灯：从**板子上方**往下打（原先放在板子高度附近，光轴与板面几乎平行 → 照不亮）。
            var spotGo = Light(_root, "MenuSpot", new Vector3(0f, h - 0.8f, z + 2.4f), LightType.Spot,
                  new Color(1.0f, 0.96f, 0.88f), 9f, 13f, true);
            if (spotGo != null)
            {
                // 光轴朝下偏前，正中板面中心
                var l = spotGo.GetComponent<Light>();
                if (l != null)
                {
                    l.spotAngle = 82f;      // 广角：覆盖整块 7.2x3.4 的板
                    l.transform.localRotation = Quaternion.Euler(58f, 0f, 0f);
                }
            }
            // 展板照明：三盏近距点光沿板面均匀布置（左/中/右）。
            // 依据：0.1.62 真机截图里单盏射灯只在板面中部留下**一条光带**，
            // 而纸片分上下两行、没被照到 → 读不出"这是菜单板"。
            // 射灯打平面本质是**一个亮斑**，要铺满 7.2x3.4m 就得过曝；
            // 三盏近距点光各覆盖约 2.6m，叠加后均匀 —— 这就是真实展板照明的做法。
            for (int k = -1; k <= 1; k++)
            {
                Light(_root, "MenuFill",
                      new Vector3(k * bw * 0.30f, by + 0.20f, z + 2.30f), LightType.Point,
                      new Color(0.97f, 0.96f, 0.92f), 2.6f, 8.5f, false);
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        // 地图选项板（菜单板左侧）
        // ════════════════════════════════════════════════════════════════════════
        void BuildMapBoard()
        {
            var frame = Mat(new Color(0.22f, 0.23f, 0.25f), 0.55f, MaterialFamily.Metal);
            var face = Mat(new Color(0.14f, 0.16f, 0.19f), 0.70f, MaterialFamily.Concrete);
            float z = -LengthM * 0.5f + WallThicknessM + 0.08f;
            float x = -WidthM * 0.30f;

            Box(_root, "MapBoardFrame", new Vector3(x, 1.85f, z), new Vector3(4.2f, 2.5f, 0.10f), frame, false);
            Box(_root, "MapBoard", new Vector3(x, 1.85f, z - 0.02f), new Vector3(4.0f, 2.3f, 0.06f), face, false);
            // 地图缩略图（小方块阵列模拟"可选地图"）
            for (int i = 0; i < 8; i++)
            {
                float px = x - 1.45f + (i % 4) * 0.95f;
                float py = 1.85f + (i < 4 ? 0.52f : -0.52f);
                Box(_root, "MapThumb", new Vector3(px, py, z - 0.06f),
                    new Vector3(0.78f, 0.62f, 0.02f), Mat(new Color(0.30f, 0.34f, 0.30f), 0.85f, MaterialFamily.Concrete), false);
            }
            Light(_root, "MapSpot", new Vector3(x, FloorHeightM - 0.9f, z + 1.4f), LightType.Spot,
                  new Color(0.92f, 0.94f, 1.0f), 1.9f, 7f, true);
        }

        // ════════════════════════════════════════════════════════════════════════
        // 装备商店（菜单板右侧的**一台电脑**）
        // ════════════════════════════════════════════════════════════════════════
        void BuildShopComputer()
        {
            var desk = Mat(new Color(0.26f, 0.24f, 0.22f), 0.70f, MaterialFamily.Wood);
            var metal = Mat(new Color(0.28f, 0.29f, 0.32f), 0.50f, MaterialFamily.Metal);
            var screen = Mat(new Color(0.30f, 0.62f, 0.95f), 0.25f, MaterialFamily.Tile);

            float x = WidthM * 0.30f;
            float z = -LengthM * 0.5f + 1.5f;

            // 桌子
            Box(_root, "ShopDeskTop", new Vector3(x, 0.78f, z), new Vector3(2.6f, 0.10f, 1.2f), desk);
            Box(_root, "ShopDeskLeg", new Vector3(x - 1.15f, 0.38f, z), new Vector3(0.12f, 0.76f, 1.0f), metal);
            Box(_root, "ShopDeskLeg", new Vector3(x + 1.15f, 0.38f, z), new Vector3(0.12f, 0.76f, 1.0f), metal);

            // 显示器（屏幕自发光，暗场里一眼能认出"这是商店"）
            var stand = Box(_root, "ShopStand", new Vector3(x, 0.90f, z), new Vector3(0.22f, 0.18f, 0.18f), metal, false);
            if (stand != null) { }
            var body = Box(_root, "ShopScreenBody", new Vector3(x, 1.28f, z - 0.05f), new Vector3(1.5f, 0.92f, 0.07f), metal, false);
            if (body != null) { }
            var panel = Box(_root, "ShopScreenPanel", new Vector3(x, 1.28f, z - 0.10f), new Vector3(1.36f, 0.78f, 0.02f), screen, false);
            if (panel != null)
            {
                // 屏幕自发光：黑场里的"这里可以买东西"信号
                var mr = panel.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    var emissive = SceneMaterials.Emissive(new Color(0.30f, 0.62f, 0.95f), 1.8f);
                    if (emissive != null) mr.sharedMaterial = emissive;
                }
            }
            // 键盘
            Box(_root, "ShopKeyboard", new Vector3(x, 0.845f, z + 0.45f), new Vector3(1.10f, 0.03f, 0.38f), metal, false);
            Light(_root, "ShopSpot", new Vector3(x, FloorHeightM - 0.9f, z + 1.2f), LightType.Spot,
                  new Color(0.75f, 0.85f, 1.0f), 2.2f, 6.5f, true);
        }

        // ════════════════════════════════════════════════════════════════════════
        // ID 卡（菜单板右上角）
        // ════════════════════════════════════════════════════════════════════════
        void BuildIdCard()
        {
            var cardMat = Mat(new Color(0.80f, 0.78f, 0.70f), 0.80f, MaterialFamily.Fabric);
            var clipMat = Mat(new Color(0.30f, 0.31f, 0.34f), 0.45f, MaterialFamily.Metal);
            float z = -LengthM * 0.5f + WallThicknessM + 0.08f;
            float x = WidthM * 0.5f - 2.4f;
            float y = 3.75f;

            Box(_root, "IdCardClip", new Vector3(x, y + 0.55f, z), new Vector3(0.14f, 0.30f, 0.06f), clipMat, false);
            Box(_root, "IdCard", new Vector3(x, y, z - 0.03f), new Vector3(1.5f, 0.95f, 0.03f), cardMat, false);
            // 卡上的"照片"与"条形码"（用深色小方块表示，远处一眼能认出是证件）
            Box(_root, "IdPhoto", new Vector3(x - 0.48f, y + 0.10f, z - 0.06f), new Vector3(0.42f, 0.52f, 0.01f),
                Mat(new Color(0.35f, 0.38f, 0.42f), 0.85f, MaterialFamily.Fabric), false);
            for (int i = 0; i < 5; i++)
                Box(_root, "IdLine", new Vector3(x + 0.14f + i * 0.02f, y - 0.10f, z - 0.06f),
                    new Vector3(0.10f, 0.42f, 0.01f), Mat(new Color(0.12f, 0.12f, 0.14f), 0.9f, MaterialFamily.Fabric), false);
        }

        // ════════════════════════════════════════════════════════════════════════
        // 窗户 + 窗外异样天空（血红）
        // ════════════════════════════════════════════════════════════════════════
        void BuildWindows()
        {
            var frame = Mat(new Color(0.20f, 0.21f, 0.23f), 0.50f, MaterialFamily.Metal);
            // 窗外天空：一块**自发光**的大面（血红），从窗户透进来 = 官方那抹异样的天色
            var sky = SceneMaterials.Emissive(new Color(0.42f, 0.06f, 0.05f), 0.85f);
            float w = WidthM, d = LengthM, h = FloorHeightM;
            float wallX = w * 0.5f - WallThicknessM - 0.02f;

            // 天空底板（贴在右墙外侧位置，用一个面片，不开碰撞）
            var skyGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            skyGo.name = "SkyPanel";
            skyGo.transform.SetParent(_root, false);
            skyGo.transform.localPosition = new Vector3(w * 0.5f + 3.5f, h * 0.62f, 0f);
            skyGo.transform.localScale = new Vector3(0.1f, h * 0.85f, d * 0.95f);
            var skyCol = skyGo.GetComponent<Collider>(); if (skyCol != null) Object.Destroy(skyCol);
            var skyMr = skyGo.GetComponent<MeshRenderer>(); if (skyMr != null && sky != null) skyMr.sharedMaterial = sky;
            BuiltCount++;

            // 窗框（右墙两扇大窗）
            for (int i = 0; i < 2; i++)
            {
                float z = -d * 0.22f + i * (d * 0.44f);
                float cy = h * 0.62f;
                Box(_root, "WinFrame", new Vector3(wallX, cy + 1.25f, z), new Vector3(0.10f, 0.14f, 3.2f), frame, false);
                Box(_root, "WinFrame", new Vector3(wallX, cy - 1.25f, z), new Vector3(0.10f, 0.14f, 3.2f), frame, false);
                Box(_root, "WinFrame", new Vector3(wallX, cy, z - 1.6f), new Vector3(0.10f, 2.6f, 0.14f), frame, false);
                Box(_root, "WinFrame", new Vector3(wallX, cy, z + 1.6f), new Vector3(0.10f, 2.6f, 0.14f), frame, false);
                Box(_root, "WinMullion", new Vector3(wallX, cy, z), new Vector3(0.10f, 2.6f, 0.10f), frame, false);
                // 窗玻璃（半透感：用很暗的偏色，靠天空底板透光）
                var glass = Box(_root, "WinGlass", new Vector3(wallX, cy, z), new Vector3(0.02f, 2.5f, 3.1f),
                    Mat(new Color(0.08f, 0.05f, 0.05f), 0.20f, MaterialFamily.Tile), false);
                if (glass != null) { }
            }
        }

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
        void BuildProps()
        {
            var canMat = Mat(new Color(0.55f, 0.18f, 0.16f), 0.40f, MaterialFamily.Metal);
            var canMat2 = Mat(new Color(0.20f, 0.35f, 0.55f), 0.40f, MaterialFamily.Metal);
            var ballMat = Mat(new Color(0.62f, 0.30f, 0.10f), 0.65f, MaterialFamily.Fabric);
            var crateMat = Mat(new Color(0.34f, 0.26f, 0.17f), 0.80f, MaterialFamily.Wood);

            // 货架（沿左墙）
            for (int s = 0; s < 3; s++)
            {
                float z = -LengthM * 0.25f + s * (LengthM * 0.22f);
                float x = -WidthM * 0.5f + 0.9f;
                Box(_root, "Shelf", new Vector3(x, 0.9f, z), new Vector3(1.2f, 0.08f, 3.4f), crateMat);
                Box(_root, "Shelf", new Vector3(x, 1.7f, z), new Vector3(1.2f, 0.08f, 3.4f), crateMat);
                Box(_root, "ShelfSide", new Vector3(x - 0.56f, 1.0f, z), new Vector3(0.08f, 2.0f, 3.4f), crateMat);
                Box(_root, "ShelfSide", new Vector3(x + 0.56f, 1.0f, z), new Vector3(0.08f, 2.0f, 3.4f), crateMat);
                // 架上的箱子
                for (int c = 0; c < 3; c++)
                    Box(_root, "Crate", new Vector3(x + (c % 2 == 0 ? -0.2f : 0.25f), 1.06f + (c == 2 ? 0.8f : 0f), z - 1.1f + c * 1.1f),
                        new Vector3(0.7f, 0.55f, 0.7f), crateMat);
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

        List<Transform> _orbs;
        float _orbPhase;

        /// <summary>每帧驱动的环境动效（灵球漂浮）。由 MenuScene 在 Update 里调。</summary>
        public void Tick(float dt)
        {
            if (_orbs == null || _orbs.Count == 0) return;
            _orbPhase += dt;
            for (int i = 0; i < _orbs.Count; i++)
            {
                var t = _orbs[i];
                if (t == null) continue;
                var p = t.localPosition;
                // 每个球不同相位（由序号派生），看起来像随机漂浮而不是整排上下
                float ph = _orbPhase * (0.35f + 0.12f * (i % 5)) + i * 1.7f;
                t.localPosition = new Vector3(p.x + Mathf.Sin(ph * 0.7f) * 0.0016f,
                                              p.y + Mathf.Sin(ph) * 0.0022f,
                                              p.z + Mathf.Cos(ph * 0.9f) * 0.0016f);
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        // 相机站位
        // ════════════════════════════════════════════════════════════════════════
        void PlaceView()
        {
            // 站在一层中央偏前，面向菜单板（-Z），能同时看到左地图板 / 右商店电脑 / 上方 ID 卡
            // 【取景依据】0.1.60 真机截图：相机在 1.68m 平视 → 菜单板（中心 2.55m）落在画面上沿之外，
            // 玩家看不到"主菜单板"这个焦点。所以站位后移 + 微仰视，把板子放到画面中心偏上。
            ViewPos = new Vector3(0f, 1.72f, LengthM * 0.30f);
            ViewLookAt = new Vector3(0f, 2.55f, -LengthM * 0.5f);
            if (_cam == null) return;
            _cam.transform.position = ViewPos;
            _cam.transform.rotation = Quaternion.LookRotation((ViewLookAt - ViewPos).normalized);
            _cam.fieldOfView = 62f;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 120f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.012f, 0.014f, 0.020f);
        }

        /// <summary>确定性哈希 → [0,1)（替代 Random，跨端一致）。</summary>
        static float H01(int i)
        {
            unchecked
            {
                uint h = (uint)(i * 374761393 + 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        /// <summary>HUD 一行摘要。</summary>
        public string Describe()
            => Problem != null ? "大厅：✗ " + Problem
             : $"大厅：工业风两层仓库 {WidthM:0}x{LengthM:0}x{FloorHeightM:0.#}m · 物件 {BuiltCount}"
             + $" · 菜单板在 (0,{MenuBoardPos.y:0.#}) · 灵球 {( _orbs != null ? _orbs.Count : 0)}";
    }
}
