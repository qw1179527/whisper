using System;
using System.Collections.Generic;

namespace Whisper.Gameplay.Level
{
    /// <summary>
    /// <see cref="LevelGeometry"/> 的**门**部分（partial）：门洞凿穿与登记、门的开关态、
    /// 门的交互查询与门型判定。
    ///
    /// 【为什么拆成两个文件】门系统收口后主文件涨到 683 行，触发规模纪律（gate-code C5，单文件 >600 行判红）。
    /// 拆法是"按职责切一刀"而不是"随便截断"：**门**是一块边界干净的功能
    /// （它的入口只有 `Compile` 里的 `BuildDoors` 与对外的 `SetDoorOpen`/`TryFindDoorNear`），
    /// 而格子/墙盒/道具/移动解析与它无关。行为零变化（原来是同一个类，现在只是分成两个 partial 文件）。
    ///
    /// 不变式（两个文件共享，改动时必须一起看）：
    ///   · `_blocked` = **结构**（墙 / 空间），门洞在结构上是空间 → 建墙盒不会在门口造假墙；
    ///   · `_closedDoorCells` = **动态**阻挡（只由门的开关态决定）→ `PassableCell` 查它；
    ///   · 门的开关态**唯一权威是格**，世界矩形只作交互/建型元数据。
    /// </summary>
    public sealed partial class LevelGeometry
    {
        /// <summary>
        /// **门格**（门洞占的格）与动态阻挡。设计要点（2026-10-04 收口，三轮返工换来的）：
        ///
        /// · 门在**结构**上是一个"洞"：`BuildDoors` 把墙带凿穿，所以 `_blocked` 里它是可走的
        ///   —— 这样墙体碰撞盒、洪水填充、`PassableCount` 都把门洞当空间，而不是墙。
        /// · 门的**开关态**是**动态**的一层：关着 = 把它的格放进 `_closedDoorCells`，
        ///   `PassableCell` 查这一层。开门只需增删这个集合，**不必重编译整个网格**
        ///   （编译是 O(房间×尺寸)，开门是高频操作）。
        /// · **唯一权威是格**：世界坐标矩形只作为门洞的元数据（位置/尺寸，供交互提示用），
        ///   不再参与碰撞判定。历史教训：让"矩形"和"格"同时判阻挡，两边对不齐时症状是
        ///   "开着门也走不过去 / 关着门却能走过去"，反复修不掉（见 git 历史里那几版注释）。
        /// </summary>
        /// <summary>每扇门（键 = 房间id/门id）占的格；开关门时按这份清单重算动态阻挡。</summary>
        readonly Dictionary<string, List<long>> _doorCellsByKey = new Dictionary<string, List<long>>(StringComparer.Ordinal);
        /// <summary>DSL 门键 → **物理洞口**的规范键（同一洞口在走廊侧与房间侧各登记一次）。</summary>
        readonly Dictionary<string, string> _doorAlias = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>当前**关着**的门所占的格（`PassableCell` 查它）。</summary>
        readonly HashSet<long> _closedDoorCells = new HashSet<long>();
        /// <summary>已打开的门 id（不在集合里 = 关着 = 挡路）。</summary>
        readonly HashSet<string> _openDoors = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>登记在册的所有门 id。</summary>
        readonly HashSet<string> _doorIds = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// 门洞：**凿穿墙带 + 登记门格**（门格在门关着时不可走）。
        ///
        /// ## 为什么"沿法线走到两侧都可走为止"，而不是按固定公式算格范围
        /// 墙在本模型里是**格带**：每个房间的内部按 INSET 内缩，边界附近留下的无人认领格就是墙。
        /// 由于房间尺寸与 0.5m 格的对齐关系不同，**墙带厚 1 格还是 2 格并不固定**
        /// （实测：corridor_ward↔ward_02 是 1 格；corridor_main↔morgue_deep 是 2 格）。
        /// 曾按 `boundary ± (墙厚/2 + 格/4)` 算固定范围：对 1 格的带刚好，对 2 格的带**够不到外侧那一格**，
        /// 症状就是"开着门也走不过去"。现在从边界格出发沿法线两侧各走到第一个可走格为止，
        /// 中间一次凿开 —— **连通性由构造保证**，与带厚、与对齐都无关。
        ///
        /// ## 为什么沿墙方向**一格都不外扩**
        /// 外扩会吃掉门洞旁边的侧墙：「相邻房间共享边上只允许门洞处可穿」断言立刻红
        /// （实测：外扩一格后 corridor_ward|ward_01 的共享边 6 个采样点里 5 个可穿，允许上限 3）。
        /// 门洞沿墙的范围**只取与门宽相交的格**。
        /// </summary>
        /// ## 一个物理洞口在 DSL 里登记了**两次**（走廊侧 `d_n2` + 房间侧 `d_south`）
        /// `LevelData` 的走廊用 `doorA`/`doorB` 分别指向共享墙两侧的门，所以同一个门洞会有两个
        /// `房间id/门id`。**它们必须被当成同一扇门**，否则：
        ///   · 第二个条目再去收集墙带格时，那些格已被第一个条目凿开 → 收集到 **0 个格**
        ///     （实测 `登记格数=0`）→ 关它等于没关（`[关门] 开=557 → 关=557`）；
        ///   · 只开一侧 → 另一侧仍算关着 → 门洞依旧挡住（实测 `开着可穿行=False`，
        ///     穿行采样 `±0.2/±0.4 全 X`）。
        /// 现在按**几何重合**合并：洞口矩形相交的门归入同一个"物理洞口"，共享同一份格与开关态；
        /// 每个 DSL 键都作为**别名**指向它（`SetDoorOpen`/`IsDoorOpen` 两个键都认）。
        /// </summary>
        void BuildDoors(LevelData level)
        {
            if (level?.Rooms == null) return;
            foreach (var room in level.Rooms)
            {
                if (room.Doors == null) continue;
                foreach (var d in room.Doors)
                {
                    if (string.IsNullOrEmpty(d.Id)) continue;
                    string key = DoorKey(room.Id, d.Id);
                    d.ToWorld(room, out float x, out float z);
                    float w = d.WidthM > 0f ? d.WidthM : 1.2f;
                    bool alongX = d.Wall == "north" || d.Wall == "south";
                    // 门洞的世界半宽：沿墙 = 门宽的一半；垂直墙方向 = **四分之一格**（薄）。
                    // 垂直方向故意取很小：它只用来把"边界格"定位出来（`CellOf`），
                    // 真正的凿穿范围由下面的"走到可走格为止"决定，不靠这个厚度。
                    float hx = alongX ? w * 0.5f : CellSize * 0.25f;
                    float hz = alongX ? CellSize * 0.25f : w * 0.5f;
                    var rect = new DoorRect
                    {
                        Key = key,
                        X0 = x - hx, X1 = x + hx, Z0 = z - hz, Z1 = z + hz,
                        Open = false,
                        AlongX = alongX,
                        Width = w,
                        CenterX = x, CenterZ = z,
                        BaseY = room.Floor * FloorHeightM,
                        Height = room.SizeY,
                        Type = InferDoorType(room, d, w),
                        Locked = d.Locked,
                        RequiredKey = d.Key,
                    };

                    // 已经是同一个物理洞口？（洞口矩形相交即视为同一个）
                    int existing = FindOpening(rect);
                    if (existing >= 0)
                    {
                        _doorAlias[key] = Doors[existing].Key;
                        _doorIds.Add(key);
                        continue;                       // 格与开关态都复用先登记的那一份
                    }

                    var cells = new List<(int gx, int gz)>();
                    int a0 = CellOf(alongX ? x - hx : z - hz);
                    int a1 = CellOf((alongX ? x + hx : z + hz) - 1e-4f);
                    int b0 = CellOf(alongX ? z : x);
                    for (int a = a0; a <= a1; a++) CollectApertureCells(alongX, a, b0, cells);

                    var list = new List<long>();
                    foreach (var (gx, gz) in cells)
                    {
                        Unmark(gx, gz);                       // 结构上变成空间
                        long ck = Key(gx, gz);
                        if (!list.Contains(ck)) list.Add(ck);   // 两个方向都会命中边界格，去重
                    }
                    _doorCellsByKey[key] = list;
                    _doorAlias[key] = key;
                    _doorIds.Add(key);
                    // 世界矩形只作元数据（门的位置与尺寸，供交互提示/落位用），不参与碰撞判定
                    Doors.Add(rect);
                }
            }
        }

        /// <summary>找一个与该矩形**相交**的已登记洞口（同一物理洞口在 DSL 里出现两次）；没有返回 -1。</summary>
        int FindOpening(in DoorRect r)
        {
            for (int i = 0; i < Doors.Count; i++)
            {
                var o = Doors[i];
                if (Math.Min(o.X1, r.X1) - Math.Max(o.X0, r.X0) > 0.05f &&
                    Math.Min(o.Z1, r.Z1) - Math.Max(o.Z0, r.Z0) > 0.05f) return i;
            }
            return -1;
        }

        /// <summary>楼层高度（米）。与 `LevelBuilder` 的房间 Y 放置口径一致（floor * 3.5m）。</summary>
        public const float FloorHeightM = 3.5f;

        /// <summary>
        /// 门型判定：**优先读 DSL 的 `type`**（`LevelData.Door.Type`，lead 已落字段与解析），
        /// 只有没写 `type` 时才按上下文推断 —— 既有 DSL 行为不变，新 DSL 数据驱动。
        ///
        /// 推断规则（确定性、可复核）：
        ///   · `locked`                      → card（禁区磁卡门）
        ///   · 宽 ≥ 1.9 **且**东西向         → double（走廊端头双开门）
        ///   · 所属房间 id 含 `morgue`        → sliding（太平间推拉门）
        ///   · id 含 `stair`/`fire`           → fire（楼梯间防火门）
        ///   · id 含 `lift`/`elevator`        → elevator（电梯门）
        ///   · 其余                          → swing（病房平开门）
        /// </summary>
        static string InferDoorType(Room room, Door d, float w)
        {
            if (!string.IsNullOrEmpty(d.Type)) return d.Type;      // ← DSL 说了算
            string id = d.Id ?? "";
            string roomId = room?.Id ?? "";
            if (d.Locked) return "card";
            bool alongX = d.Wall == "north" || d.Wall == "south";
            if (w >= 1.9f && !alongX) return "double";
            if (roomId.IndexOf("morgue", StringComparison.OrdinalIgnoreCase) >= 0) return "sliding";
            if (id.IndexOf("stair", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("fire", StringComparison.OrdinalIgnoreCase) >= 0) return "fire";
            if (id.IndexOf("lift", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("elevator", StringComparison.OrdinalIgnoreCase) >= 0) return "elevator";
            return "swing";
        }

        /// <summary>门型 → HUD 可显示的中文名（HUD 只管显示，不在玩法层再维护一份映射）。</summary>
        public static string DoorTypeDisplayName(string type)
        {
            switch (type)
            {
                case "double": return "双开门";
                case "sliding": return "推拉门";
                case "card": return "禁区门（需磁卡）";
                case "fire": return "防火门";
                case "elevator": return "电梯门";
                default: return "平开门";
            }
        }

        /// <summary>点到门洞矩形（XZ）的最近距离 —— 站在门口**任何位置**都算得近，不会"贴着门却没提示"。</summary>
        static float DistanceToRect(in DoorRect d, float x, float z)
        {
            float dx = x < d.X0 ? d.X0 - x : (x > d.X1 ? x - d.X1 : 0f);
            float dz = z < d.Z0 ? d.Z0 - z : (z > d.Z1 ? z - d.Z1 : 0f);
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// 找离 (x,z) **最近且在 radius 内**的门（供 HUD 提示与交互）。
        /// 距离口径 = **到门洞矩形的最近距离**（不是到门洞中心的距离）：
        /// 门宽 1.6~2.0m，用中心距离会出现"站在门框边上却超出半径"的假阴性。
        /// </summary>
        public bool TryFindDoorNear(float x, float z, float radius, out DoorRect door)
        {
            door = default;
            float best = radius;
            bool found = false;
            for (int i = 0; i < Doors.Count; i++)
            {
                var d = Doors[i];
                float dist = DistanceToRect(d, x, z);
                if (dist > best) continue;
                best = dist; door = d; found = true;
            }
            return found;
        }

        /// <summary>
        /// 切换一扇门（`房间id/门id`，走廊侧/房间侧两个键都认）。
        /// 锁着的门**拒绝**并把原因写进 <paramref name="reason"/>（不静默失败，本项目纪律）。
        /// </summary>
        public bool TryToggleDoor(string doorId, out bool nowOpen, out string reason)
        {
            nowOpen = false;
            reason = null;
            string canon = ResolveDoorKey(doorId);
            for (int i = 0; i < Doors.Count; i++)
            {
                if (!string.Equals(Doors[i].Key, canon, StringComparison.Ordinal)) continue;
                if (Doors[i].Locked)
                {
                    reason = string.IsNullOrEmpty(Doors[i].RequiredKey)
                        ? $"门 {canon} 锁着（{DoorTypeDisplayName(Doors[i].Type)}）—— 需要对应的钥匙/磁卡"
                        : $"门 {canon} 锁着（{DoorTypeDisplayName(Doors[i].Type)}）—— 需要 {Doors[i].RequiredKey}";
                    return false;
                }
                bool open = !Doors[i].Open;
                SetDoorOpen(canon, open);
                nowOpen = open;
                reason = open ? $"门 {canon} 已打开" : $"门 {canon} 已关闭";
                return true;
            }
            reason = $"没有登记过的门：{doorId}";
            return false;
        }

        /// <summary>把任意 DSL 门键解析成它所属**物理洞口**的规范键（未登记的键原样返回）。</summary>
        public string ResolveDoorKey(string doorId)
            => doorId != null && _doorAlias.TryGetValue(doorId, out var canon) ? canon : doorId;

        /// <summary>墙带最多凿这么多格 —— 防"开在实心外圈上的门"一路凿穿到关卡外面。</summary>
        const int MaxBandCells = 6;
        const int NoOpenCell = int.MinValue;

        /// <summary>把某个"沿墙格"处的墙带整条凿开：两侧各走到第一个可走格，中间全收。</summary>
        void CollectApertureCells(bool alongX, int along, int b0, List<(int gx, int gz)> cells)
        {
            int negOpen = FirstOpenAlong(alongX, along, b0, -1);
            int posOpen = FirstOpenAlong(alongX, along, b0, +1);
            // 一侧没有可走格（门开在实心外墙上，或墙带超限）：该侧退化为"连续墙段"，不越界乱凿
            int lo = negOpen == NoOpenCell ? WallRunEnd(alongX, along, b0, -1) : negOpen + 1;
            int hi = posOpen == NoOpenCell ? WallRunEnd(alongX, along, b0, +1) : posOpen - 1;
            for (int b = lo; b <= hi; b++)
            {
                int gx = alongX ? along : b, gz = alongX ? b : along;
                if (IsWallCell(gx, gz)) cells.Add((gx, gz));
            }
        }

        /// <summary>沿法线找第一个**不是结构墙**的格；找不到（超限/出界）返回 <see cref="NoOpenCell"/>。</summary>
        int FirstOpenAlong(bool alongX, int along, int b0, int dir)
        {
            for (int k = 1; k <= MaxBandCells; k++)
            {
                int b = b0 + dir * k;
                int gx = alongX ? along : b, gz = alongX ? b : along;
                if (OutOfGrid(gx, gz)) return NoOpenCell;
                if (!IsWallCell(gx, gz)) return b;
            }
            return NoOpenCell;
        }

        /// <summary>从 b0 沿法线取**连续墙段**的最后一个格号（上限 <see cref="MaxBandCells"/>）。</summary>
        int WallRunEnd(bool alongX, int along, int b0, int dir)
        {
            int last = b0;
            for (int k = 1; k <= MaxBandCells; k++)
            {
                int b = b0 + dir * k;
                int gx = alongX ? along : b, gz = alongX ? b : along;
                if (!IsWallCell(gx, gz)) break;
                last = b;
            }
            return last;
        }

        /// <summary>把所有门按当前开关态写进动态阻挡集合（编译收尾调一次：门出厂是关的）。</summary>
        void SyncClosedDoorCells()
        {
            _closedDoorCells.Clear();
            foreach (var kv in _doorCellsByKey)
            {
                if (_openDoors.Contains(kv.Key)) continue;
                foreach (var c in kv.Value) _closedDoorCells.Add(c);
            }
        }

        /// <summary>
        /// 门（门洞的世界矩形 + 开关态 + 供交互/建型用的元数据）。矩形只是**元数据**
        /// （位置/尺寸/门型，供交互提示与门扇建型用），碰撞判定一律走**格**
        /// （见 `PassableCell` 的注释：两套判据并存时，对不齐的症状就是
        /// "开着门走不过去 / 关着门能穿过去"，本项目在这上面返工了三轮）。
        /// </summary>
        public struct DoorRect
        {
            public string Key;                // 物理洞口的规范键（房间id/门id）
            public float X0, Z0, X1, Z1;      // 门洞的世界矩形（沿墙 = 门宽；垂直墙 = 薄）
            public bool Open;
            // ── 以下字段**不参与碰撞**，只服务交互与门扇建型 ──
            public bool AlongX;               // true = 洞口在南北墙上（沿 X 展开）
            public float Width;               // 门洞宽（米）
            public float CenterX, CenterZ;    // 洞口中心（世界坐标）
            public float BaseY;               // 洞口底（所属楼层的世界 Y）
            public float Height;              // 洞口高（所属房间层高）
            /// <summary>门型：swing / double / sliding / card / fire / elevator（优先读 DSL `type`，缺省推断）。</summary>
            public string Type;
            /// <summary>需要磁卡/钥匙（DSL 的 locked）。玩法层决定"有没有卡"，几何层只如实标注。</summary>
            public bool Locked;
            /// <summary>解锁所需的物品 id（DSL 的 `key`；可为空 = 只标了 locked 没指定物）。</summary>
            public string RequiredKey;
        }

        /// <summary>所有门（门洞矩形 + 开关态）。</summary>
        public readonly List<DoorRect> Doors = new List<DoorRect>();

        /// <summary>
        /// 门的**全局唯一键** = `房间id/门id`。
        /// 【2026-10-04 修 · 被自检抓出来的真实缺陷】门的 `Id` 在房间之间是**重复**的：
        /// `ward_01..05` 的南门全叫 `d_south`，`morgue_deep/ante` 也都叫 `d_south`。
        /// 而本类按 id 索引门格 → 五间病房的门被当成**同一扇**：开一扇等于开五扇，
        /// 关一扇等于关五扇（实测 `[连通] 门 10/20 扇已开（登记数 11）`、
        /// `[关门] 门 d_south：开=478 格 → 关=478 格` 即关门毫无效果）。
        /// 现在一律用 `房间/门` 作为键，重复 id 不再互相串。
        /// </summary>
        public static string DoorKey(string roomId, string doorId) => roomId + "/" + doorId;

        static long Key(int gx, int gz) => ((long)gx << 32) ^ (uint)gz;

        /// <summary>
        /// 开/关一扇门（按 `房间id/门id`，**走廊侧与房间侧两个键都认** —— 同一物理洞口）。
        /// 只增删**动态阻挡集合**里的那几个格 —— 代价 O(该门格数)，可以随按随调；
        /// 不动 `_blocked`，所以开关门不会破坏结构（开门也挖不穿墙）。
        /// 返回 false 表示这个 id 不是门（拼错 id 会被抓到，而不是静默无效）。
        /// </summary>
        public bool SetDoorOpen(string doorId, bool open)
        {
            if (string.IsNullOrEmpty(doorId)) return false;
            string canon = ResolveDoorKey(doorId);
            for (int i = 0; i < Doors.Count; i++)
            {
                if (!string.Equals(Doors[i].Key, canon, StringComparison.Ordinal)) continue;
                var d = Doors[i];
                d.Open = open;
                Doors[i] = d;                 // struct：改完要写回
                if (open) _openDoors.Add(canon); else _openDoors.Remove(canon);
                // 同步动态阻挡：**整表重算**（门格总数只有几十，代价可忽略），
                // 换来"开关态 == 阻挡态"恒定成立，不必为每个键维护增量。
                SyncClosedDoorCells();
                return true;
            }
            return false;                     // 拼错 id 会被抓到，而不是静默无效
        }

        /// <summary>这扇门开着吗（走廊侧/房间侧任一键都认；没登记过的 id 返回 false）。</summary>
        public bool IsDoorOpen(string doorId) => !string.IsNullOrEmpty(doorId) && _openDoors.Contains(ResolveDoorKey(doorId));

        /// <summary>登记在册的**DSL 门键**数量（= 走廊侧 + 房间侧条目数；为 0 说明门全没登记上）。</summary>
        public int DoorCount => _doorIds.Count;

        /// <summary>**物理洞口**数量（同一洞口在 DSL 里出现两次，这里按几何重合合并后计数）。</summary>
        public int DoorOpeningCount => Doors.Count;

        /// <summary>某扇门占了多少格（诊断/自检用：为 0 说明门洞没凿出来）。</summary>
        public int DoorCellCount(string doorId)
            => !string.IsNullOrEmpty(doorId) && _doorCellsByKey.TryGetValue(ResolveDoorKey(doorId), out var c) ? c.Count : 0;

        /// <summary>
        /// 最近一次门交互的人类可读结果（成功与失败**都**写；HUD 直接显示）。
        /// 为什么做成字段：交互是玩家最容易"按了没反应"的地方，本项目纪律是失败必须可见 ——
        /// `ToggleDoor` 返回 bool 只够做分支，文案得有个确定的出处。
        /// </summary>
        public string LastDoorMessage { get; private set; }

        /// <summary>
        /// 交互查询（**纯 C# 版**：给"手上只有几何层"的调用方，例如 `PlayerController`）。
        /// 返回最近的门及其世界位姿/开关态/门型/是否上锁。Unity 侧要 `Vector3` 就自己 `new` 一个
        /// —— 几何层刻意不引 UnityEngine，它必须能在本机被断言（V9 §19 代码优先）。
        /// </summary>
        public bool TryFindInteractableDoor(float x, float z, float radius, out DoorInfo info)
        {
            info = default;
            if (!TryFindDoorNear(x, z, radius, out var d)) return false;
            info = new DoorInfo
            {
                Key = d.Key,
                X = d.CenterX, Y = d.BaseY, Z = d.CenterZ,
                Open = d.Open,
                Locked = d.Locked,
                Type = d.Type,
                DisplayName = $"{DoorTypeDisplayName(d.Type)}（{RoomIdOf(d.Key)}）",
                RequiredKey = d.RequiredKey,
                Distance = DistanceToRect(d, x, z),
            };
            return true;
        }

        /// <summary>门键 `房间id/门id` → 房间 id（HUD 文案用）。</summary>
        static string RoomIdOf(string doorKey)
        {
            int slash = doorKey == null ? -1 : doorKey.IndexOf('/');
            return slash > 0 ? doorKey.Substring(0, slash) : (doorKey ?? "");
        }

        /// <summary>
        /// 交互切换：开↔关。返回**是否真的切了**（拼错键、锁着的门都返回 false），
        /// 并且无论成败都把原因写进 <see cref="LastDoorMessage"/>（HUD 直接显示，不静默）。
        /// </summary>
        public bool ToggleDoor(string doorKey)
        {
            bool ok = TryToggleDoor(doorKey, out _, out string reason);
            LastDoorMessage = reason;
            return ok;
        }

        /// <summary>交互查询结果：规范门键 / 世界位姿 / 开关态 / 门型 / 是否上锁 / 距离（米）。</summary>
        public struct DoorInfo
        {
            public string Key;
            public float X, Y, Z;
            public bool Open, Locked;
            /// <summary>机器可读门型：swing / double / sliding / card / fire / elevator。</summary>
            public string Type;
            /// <summary>HUD 可直接显示的中文名（含房间 id），如「推拉门（morgue_deep）」。</summary>
            public string DisplayName;
            /// <summary>解锁所需物品 id（DSL `key`；空 = 未指定）。</summary>
            public string RequiredKey;
            /// <summary>到**门洞矩形**的最近距离（米）—— 站在门口任何位置都算得近。</summary>
            public float Distance;
        }
    }
}
