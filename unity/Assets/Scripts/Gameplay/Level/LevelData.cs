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
    }

    public sealed class ExtractionPoints
    {
        public string Standard;
        public string Deep;
    }

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

    public sealed class Prop
    {
        public string Kit;
        public float X, Y, Z;
        public float Rot;
    }

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
