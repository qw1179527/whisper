using System.Collections.Generic;

namespace Whisper.Gameplay.Level
{
    /// <summary>
    /// Level DSL 数据模型（V9 §19.2：关卡即数据，运行时由 LevelBuilder 拼装）。
    ///
    /// 三方一致纪律：字段名与
    ///   · unity/docs/ai-context/repo-conventions-and-contracts.md 的 schema
    ///   · tools/validate-levels.mjs 的校验规则
    ///   · native/contract-mirror（Java 镜像）
    /// 必须保持一致；改一处必须同步其余三处。
    ///
    /// D1 修复（独立验证轨指出）：旧模型没有 pos/rotY/floor、门没有 id、走廊没有 doorA/doorB，
    /// 导致 §19.2 要求的「实例化房间几何 → 连接走廊」**不可实现**（房间摆不到平面上，
    /// 走廊连不出来），且门与走廊数量对不上却无人校验。以下字段即为补齐。
    /// </summary>
    public sealed class LevelData
    {
        public string LevelId;
        public readonly List<Room> Rooms = new List<Room>();
        public readonly List<Corridor> Corridors = new List<Corridor>();
        public readonly List<EventDef> Events = new List<EventDef>();
        /// <summary>撤离双点制（V9 §7：标准点安全 / 深处点 +30%）。</summary>
        public ExtractionPoints Extraction;
        /// <summary>
        /// 竖井（楼梯间 / 电梯 / 管道井）：**跨层的可走区域**。
        ///
        /// 为什么必须是独立概念、而不是"每层各一个房间"：`LevelGeometry` 的格网是 2D 的，
        /// 若把竖井按层各建一个房间，层与层之间**没有任何可走连接** —— 寻路会在层间断开
        /// （"从入口可达"只在一层内成立），而玩家站在竖井里上下楼时碰撞体也会互相打架。
        /// 竖井在此显式声明"同一块 (x,z) 在 [FromFloor, ToFloor] 各层都可走且互通"，
        /// 由 `LevelWorld` 在跨层寻路时把它当边用。
        /// </summary>
        public readonly List<Shaft> Shafts = new List<Shaft>();
    }

    /// <summary>
    /// 竖井：一块**贯穿若干楼层**的 (x,z) 矩形。单位米，与房间同一坐标系。
    /// `FromFloor`/`ToFloor` 含两端（例如 0→2 表示一层到三层都通）。
    /// </summary>
    public sealed class Shaft
    {
        public string Id;
        public string Kind;          // stair / lift / duct（蓝图第七节：楼梯间 / 电梯 / 管道井）
        public float MinX, MinZ, MaxX, MaxZ;
        public int FromFloor, ToFloor;
        public float CenterX => (MinX + MaxX) * 0.5f;
        public float CenterZ => (MinZ + MaxZ) * 0.5f;
        public bool Covers(int floor) => floor >= FromFloor && floor <= ToFloor;
        public bool Contains(float x, float z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
    }

    /// <summary>
    /// 撤离双点引用（V9 §7）：标准点安全、深处点 +30% 奖励但更危险。
    /// 只存房间 id —— 具体奖励系数与安全标记由配置表提供，避免数值散落在关卡数据里。
    /// </summary>
    public sealed class ExtractionPoints
    {
        public string Standard;
        public string Deep;
    }

    /// <summary>
    /// 房间（V9 §19.2）：pos 是**最小角点**（与灰盒 rect 同义：x1 = x0 + 宽），size = [宽, 高, 深]。
    /// 布局字段是本项目早期最大的缺口 —— 没有它 LevelBuilder 无法实例化几何、走廊也连不出来。
    /// </summary>
    public sealed class Room
    {
        public string Id;
        /// <summary>
        /// 房间**最小角点** [x, z]，单位米（XZ 平面）。
        ///
        /// ⚠ 与灰盒 `__m4.rect()` 严格一致：x0 = pos[0]，z0 = pos[1]，x1 = x0 + 宽，z1 = z0 + 深。
        /// 早期我把这里当"中心点"实现（MinX = PosX - 宽/2），与灰盒不兼容——
        /// 后果是同一份关卡数据在两条线上整体错位半间房，门也对不上。
        /// 灰盒是本项目唯一"已验证行为"的参照物，故此处以它为准。
        /// </summary>
        public float PosX, PosZ;
        /// <summary>[宽, 高, 深]，单位米。</summary>
        public float SizeX, SizeY, SizeZ;
        /// <summary>绕 Y 轴旋转（度）。当前几何校验只支持 0（轴对齐）。</summary>
        public float RotY;
        /// <summary>楼层（0 = 地面，-1 = 地下）。跨层走廊当前不支持。</summary>
        public int Floor;
        /// <summary>套件库 ID，必须存在于 Assets/Data/asset-manifest.json。</summary>
        public string Kit;
        public readonly List<Door> Doors = new List<Door>();
        public readonly List<Prop> Props = new List<Prop>();
        public bool EvidencePoint;
        /// <summary>safe | pressure | high-risk（V9 §11 动态光分区）。</summary>
        public string LightZone;
        /// <summary>
        /// **分翼**（官方 Sunny Meadows 是分翼机构：限制病房 / 礼拜堂 / 庭院 / 太平间 / 锅炉房）。
        ///
        /// 官方机制原文（`docs/reference-official/04-…§7.2`）：
        /// &gt; 猎杀时**所在分翼封锁**，极难躲藏；房间高度相似，**极易迷路**
        ///
        /// ⚠ 与 <see cref="LightZone"/> 是**两个正交维度**，不要合并：
        ///   · `Wing` 管"哪一片是一个整体 / 猎杀时封哪一片"（**玩法连通性**）；
        ///   · `LightZone` 管光照强度与闪烁（**观感**）。
        /// 礼拜堂可以 `wing=chapel` 且 `lightZone=safe`；太平间可以 `wing=morgue` 且
        /// `lightZone=high-risk` —— 两者互不决定。
        /// </summary>
        public string Wing;

        /// <summary>点 (x,z) 是否落在本房间内（**不含边界**，避免相邻房间同时命中）。</summary>
        public bool ContainsPoint(float x, float z)
            => x > MinX && x < MaxX && z > MinZ && z < MaxZ;

        /// <summary>房间中心到 (x,z) 的距离平方（用于"玩家在哪间房"的就近判定）。</summary>
        public float DistSqToCenter(float x, float z)
        {
            float dx = CenterX - x, dz = CenterZ - z;
            return dx * dx + dz * dz;
        }

        public float MinX => PosX;
        public float MinZ => PosZ;
        public float MaxX => PosX + SizeX;
        public float MaxZ => PosZ + SizeZ;
        public float CenterX => PosX + SizeX / 2f;
        public float CenterZ => PosZ + SizeZ / 2f;

        public Door FindDoor(string id)
        {
            for (int i = 0; i < Doors.Count; i++) if (Doors[i].Id == id) return Doors[i];
            return null;
        }
    }

    /// <summary>
    /// 门洞（V9 §19.2）：以「贴哪面墙 + 沿墙起点（米）+ 洞口宽（米）」表达，与灰盒
    /// `compileWalls` 的 `offsetM/widthM` 同义。用米而不是归一化比例，是为了让"门宽"
    /// 与"墙长"能直接比较，从而在生成期就能判定门洞是否越界。
    /// </summary>
    public sealed class Door
    {
        /// <summary>门 id（房间内唯一）。走廊以 `房间id/门id` 引用它。</summary>
        public string Id;
        /// <summary>north | south | east | west。</summary>
        public string Wall;
        /// <summary>沿墙位置（**米**，从该墙起点算起）——与灰盒 `compileWalls` 的 `offsetM` 同名同义。</summary>
        public float OffsetM;
        /// <summary>门洞宽度（米）——灰盒 `widthM`。</summary>
        public float WidthM;
        public bool Locked;

        /// <summary>
        /// 门的类型（6 类，蓝图 `hospital-plan-3floors.md` 第七节）：
        /// `swing`（病房平开，默认）· `double`（走廊双开）· `sliding`（太平间推拉）·
        /// `card`（禁区磁卡，需 <see cref="Key"/>）· `fire`（楼梯间防火门，常闭自闭）· `elevator`（电梯门，需供电）。
        ///
        /// 【为什么要有这个字段】此前门型是**推断**出来的（按宽度/朝向/房间 id 猜），
        /// 那意味着"门长什么样"这件事没有真源 —— 布局一改，同一扇门可能从平开变成双开。
        /// 现在 DSL 可以直接声明；缺省 `swing`，推断只作为兜底。
        /// </summary>
        public string Type;

        /// <summary>磁卡/钥匙 id（仅 `card` 类用）。缺省 null = 不需要钥匙，锁着就真的打不开。</summary>
        public string Key;

        /// <summary>门洞中心在 XZ 平面上的绝对位置。</summary>
        public void ToWorld(Room room, out float x, out float z)
        {
            float w = WidthM > 0f ? WidthM : 1.2f;
            float mid = OffsetM + w / 2f;
            switch (Wall)
            {
                case "north": x = room.MinX + mid; z = room.MaxZ; break;
                case "south": x = room.MinX + mid; z = room.MinZ; break;
                case "west": x = room.MinX; z = room.MinZ + mid; break;
                default: x = room.MaxX; z = room.MinZ + mid; break; // east
            }
        }

        /// <summary>门洞沿墙的起止（用于墙段切洞）。</summary>
        public void SpanOnWall(Room room, out float a, out float b)
        {
            float w = WidthM > 0f ? WidthM : 1.2f;
            a = OffsetM; b = OffsetM + w;
        }
    }

    /// <summary>
    /// 房间内道具（家具/陈设）。pos 是**房间局部坐标**（相对房间最小角点），
    /// 因此同一套件放到不同房间只需改房间原点，不必重算世界坐标。
    /// 占地尺寸不在关卡里写，而由 asset-manifest 的 footprint 提供（唯一真源）。
    /// </summary>
    public sealed class Prop
    {
        public string Kit;
        public float X, Y, Z;
        public float Rot;
    }

    /// <summary>
    /// 走廊连接（V9 §19.1 C1）：用**两端的具体门**（`房间id/门id`）表达，而不是只写房间对。
    /// 这样校验器与 LevelBuilder 才能验证"两端门贴在同一条共享墙上、开口对齐"——
    /// 只写房间对时，门摆错墙也无人发现（本项目实际踩过：门位置全是 NaN，房间图 0 条边）。
    /// </summary>
    public sealed class Corridor
    {
        public string From;
        public string To;
        /// <summary>`房间id/门id`：走廊起点端的门。</summary>
        public string DoorA;
        /// <summary>`房间id/门id`：走廊终点端的门。</summary>
        public string DoorB;
        public float Width;
    }

    /// <summary>
    /// 动态事件定义（V9 §19.2 / §30.2）：类型取自配置的 `level.eventPool`（逐字一致，由
    /// tools/config-lint.mjs 双向守），且**必须有 counterplay** —— 没有反制手段的事件
    /// 只会变成不可对抗的惩罚，V9 §30.2 把它列为硬要求。
    /// </summary>
    public sealed class EventDef
    {
        /// <summary>内建 6 型（blackout/doorlock/static/mirror/overload/laugh）或以 x-/ext-/ns: 前缀的扩展类型（V9 §30.2）。</summary>
        public string Type;
        public float Minute;
        public float DurationSec;
        /// <summary>V9 §30.2 schema：事件参数（键值对，序列化保留原文）。</summary>
        public string ParamsJson;
        /// <summary>V9 §30.2：理智影响（负值为损耗）。</summary>
        public float SanityEffect;
        /// <summary>V9 §30.2：明确的反制手段（硬要求）。</summary>
        public string Counterplay;
    }
}
