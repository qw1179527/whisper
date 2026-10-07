using System;
using System.Collections.Generic;

namespace Whisper.Gameplay.Level
{
    /// <summary>
    /// Level DSL 加载与校验（V9 §19.2 / §19.4）。
    ///
    /// 设计要点：
    ///  · 纯 C#，**不引用 UnityEngine** —— 因此能在 Edit Mode / 纯 dotnet 环境里被独立测试；
    ///  · 只做「解析 + 结构校验」，不做实例化（拼装是 LevelBuilder 的职责，需要引擎）；
    ///  · 校验失败抛 LevelValidationException，附带**全部**问题清单（不是只报第一个）。
    /// </summary>
    public static class LevelLoader
    {
        /// <summary>
        /// 关卡校验失败（携带**全部**问题而不是第一条）：关卡数据出错时最常见的诉求是
        /// "一次看到所有错"，只抛第一条会让人来回改十几轮。
        /// </summary>
        public sealed class LevelValidationException : Exception
        {
            public readonly IReadOnlyList<string> Problems;
            public LevelValidationException(IReadOnlyList<string> problems)
                : base($"Level DSL 校验失败（{problems.Count} 个问题）：{string.Join(" | ", problems)}")
            {
                Problems = problems;
            }
        }

        static readonly HashSet<string> ValidWalls = new HashSet<string>(StringComparer.Ordinal) { "north", "south", "east", "west" };
        static readonly HashSet<string> ValidLightZones = new HashSet<string>(StringComparer.Ordinal) { "safe", "pressure", "high-risk" };
        /// <summary>
        /// 内建事件类型 —— **必须与 data/config.json 的 level.eventPool 逐字一致**。
        ///
        /// 踩过的坑：这里曾写成 blackout/doorlock/static/mirror/overload/laugh（想当然的简名），
        /// 而配置真源是 blackout/door_lock_shift/radio_static/mirror_flicker/power_surge/child_laughter。
        /// 后果是**合法的关卡事件会被判非法**（校验器说谎）。现由 tools/config-lint.mjs
        /// 与 tools/validate-levels.mjs 双向守住这条一致性。
        /// </summary>
        static readonly HashSet<string> BuiltinEventTypes = new HashSet<string>(StringComparer.Ordinal)
        { "blackout", "door_lock_shift", "radio_static", "mirror_flicker", "power_surge", "child_laughter" };

        /// <summary>事件类型是否允许：内建 6 型，或以 x- / ext- / ns: 前缀的扩展类型（V9 §30.2 要求可扩展）。</summary>
        static bool IsEventTypeAllowed(string t)
        {
            if (string.IsNullOrEmpty(t)) return false;
            if (t.StartsWith("x-", StringComparison.Ordinal) || t.StartsWith("ext-", StringComparison.Ordinal) || t.StartsWith("ns:", StringComparison.Ordinal))
            {
                var bare = t.Substring(t.StartsWith("ns:", StringComparison.Ordinal) ? 3 : t.IndexOf('-') + 1);
                return BuiltinEventTypes.Contains(bare) || bare.Length > 0;
            }
            return BuiltinEventTypes.Contains(t);
        }

        /// <summary>
        /// 解析 + 校验。knownKits 为 null 时跳过套件引用检查（离线单测用）。
        ///
        /// 错误口径（独立验证轨 S3 指出后修正）：**所有**结构/类型错误都以
        /// LevelValidationException 抛出，绝不把 FormatException 泄漏给调用方 ——
        /// 否则 GameBootstrap 只 catch LevelValidationException，畸形关卡会在 Awake 里硬崩
        /// （未捕获异常 → 启动状态永不更新）。
        /// </summary>
        public static LevelData Load(string json, ISet<string> knownKits = null, IDictionary<string, string> knownKinds = null)
        {
            try
            {
                return LoadCore(json, knownKits, knownKinds);
            }
            catch (LevelValidationException)
            {
                throw;
            }
            catch (FormatException ex)
            {
                throw new LevelValidationException(new[] { "关卡数据结构/类型错误：" + ex.Message });
            }
        }

        static LevelData LoadCore(string json, ISet<string> knownKits, IDictionary<string, string> knownKinds)
        {
            var map = MiniJson.AsMap(MiniJson.Parse(json));
            var level = new LevelData { LevelId = MiniJson.AsString(MiniJson.Get(map, "levelId")) };
            var problems = new List<string>();

            // rooms
            var rooms = MiniJson.Get(map, "rooms");
            if (rooms == null) problems.Add("缺少 rooms");
            else
            {
                foreach (var rv in MiniJson.AsList(rooms))
                {
                    var rm = MiniJson.AsMap(rv);
                    var room = new Room
                    {
                        Id = MiniJson.AsString(MiniJson.Get(rm, "id")),
                        Kit = MiniJson.AsString(MiniJson.Get(rm, "kit")),
                        EvidencePoint = MiniJson.Get(rm, "evidencePoint") is bool ep && ep,
                        LightZone = MiniJson.Get(rm, "lightZone") is string lz ? lz : null,
                        // 分翼（官方 Sunny Meadows 机制）：猎杀时封锁"玩家所在翼"。
                        // 缺失时置 null，由下面的校验判红 —— 不静默当作"无翼"，
                        // 否则"某间房没登记翼"会让封锁逻辑悄悄漏掉它。
                        Wing = MiniJson.Get(rm, "wing") is string wg ? wg : null,
                    };
                    var size = MiniJson.AsList(MiniJson.Get(rm, "size"));
                    if (size.Count != 3) problems.Add($"房间 {room.Id} 的 size 必须是 [宽,高,深] 三个数");
                    else
                    {
                        room.SizeX = MiniJson.AsFloat(size[0]);
                        room.SizeY = MiniJson.AsFloat(size[1]);
                        room.SizeZ = MiniJson.AsFloat(size[2]);
                    }
                    if (MiniJson.Get(rm, "pos") is { } posv)
                    {
                        var pv2 = MiniJson.AsList(posv);
                        if (pv2.Count != 2) problems.Add($"房间 {room.Id} 的 pos 必须是 [x,z]（最小角点）");
                        else { room.PosX = MiniJson.AsFloat(pv2[0]); room.PosZ = MiniJson.AsFloat(pv2[1]); }
                    }
                    else problems.Add($"房间 {room.Id} 缺 pos:[x,z]（布局必需）");
                    if (MiniJson.Get(rm, "rotY") is { } ry) room.RotY = MiniJson.AsFloat(ry);
                    if (MiniJson.Get(rm, "floor") is { } fl) room.Floor = MiniJson.AsInt(fl);
                    if (MiniJson.Get(rm, "doors") is { } dv)
                        foreach (var d in MiniJson.AsList(dv))
                        {
                            var dm = MiniJson.AsMap(d);
                            room.Doors.Add(new Door
                            {
                                Id = MiniJson.Get(dm, "id") is string did ? did : null,
                                Wall = MiniJson.AsString(MiniJson.Get(dm, "wall")),
                                // 灰盒约定：offsetM（米）+ widthM（米）
                                OffsetM = MiniJson.Get(dm, "offsetM") is { } om ? MiniJson.AsFloat(om) : 0f,
                                WidthM = MiniJson.Get(dm, "widthM") is { } wm ? MiniJson.AsFloat(wm) : 1.2f,
                                Locked = MiniJson.Get(dm, "locked") is bool lk && lk,
                                // 门型与钥匙（6 类门的数据驱动来源，见蓝图第七节）。
                                // 缺省 null → 由消费方按 `swing` 处理 / 回退到推断。
                                Type = MiniJson.Get(dm, "type") is string dt ? dt : null,
                                Key = MiniJson.Get(dm, "key") is string dk ? dk : null,
                            });
                        }
                    if (MiniJson.Get(rm, "props") is { } pv)
                        foreach (var p in MiniJson.AsList(pv))
                        {
                            var pm = MiniJson.AsMap(p);
                            var pos = MiniJson.AsList(MiniJson.Get(pm, "pos"));
                            room.Props.Add(new Prop
                            {
                                Kit = MiniJson.AsString(MiniJson.Get(pm, "kit")),
                                X = pos.Count > 0 ? MiniJson.AsFloat(pos[0]) : 0f,
                                Y = pos.Count > 1 ? MiniJson.AsFloat(pos[1]) : 0f,
                                Z = pos.Count > 2 ? MiniJson.AsFloat(pos[2]) : 0f,
                                Rot = MiniJson.Get(pm, "rot") is { } rr ? MiniJson.AsFloat(rr) : 0f,
                            });
                        }
                    level.Rooms.Add(room);
                }
            }

            // corridors
            if (MiniJson.Get(map, "corridors") is { } cv)
                foreach (var c in MiniJson.AsList(cv))
                {
                    var cm = MiniJson.AsMap(c);
                    level.Corridors.Add(new Corridor
                    {
                        From = MiniJson.AsString(MiniJson.Get(cm, "from")),
                        To = MiniJson.AsString(MiniJson.Get(cm, "to")),
                        DoorA = MiniJson.Get(cm, "doorA") is string da ? da : null,
                        DoorB = MiniJson.Get(cm, "doorB") is string db ? db : null,
                        Width = MiniJson.AsFloat(MiniJson.Get(cm, "width")),
                    });
                }

            // events
            if (MiniJson.Get(map, "events") is { } ev)
                foreach (var e in MiniJson.AsList(ev))
                {
                    var em = MiniJson.AsMap(e);
                    level.Events.Add(new EventDef
                    {
                        Type = MiniJson.AsString(MiniJson.Get(em, "type")),
                        Minute = MiniJson.AsFloat(MiniJson.Get(em, "minute")),
                        DurationSec = MiniJson.AsFloat(MiniJson.Get(em, "durationSec")),
                        SanityEffect = MiniJson.Get(em, "sanityEffect") is { } se ? MiniJson.AsFloat(se) : 0f,
                        Counterplay = MiniJson.Get(em, "counterplay") is string cp ? cp : null,
                        ParamsJson = MiniJson.Get(em, "params") != null ? MiniJson.Get(em, "params").ToString() : null,
                    });
                }

            // shafts（可选，缺省=单层；判据见 ParseShafts/ValidateShaft）
            ParseShafts(map, level, problems);

            // extraction（可选，但给了就必须合法）
            ParseExtraction(map, level);

            problems.AddRange(Validate(level, knownKits, knownKinds));
            if (problems.Count > 0) throw new LevelValidationException(problems);
            return level;
        }

        /// <summary>解析撤离双点（从 LoadCore 拆出：加上竖井解析后 LoadCore 触到 C5 的 120 行上限）。</summary>
        static void ParseExtraction(Dictionary<string, object> map, LevelData level)
        {
            if (MiniJson.Get(map, "extraction") is not { } xv) return;
            var xm = MiniJson.AsMap(xv);
            level.Extraction = new ExtractionPoints
            {
                Standard = MiniJson.AsString(MiniJson.Get(xm, "standard")),
                Deep = MiniJson.AsString(MiniJson.Get(xm, "deep")),
            };
        }

        /// <summary>由房间盒与门的 wall/offset 推导门贴墙几何：轴、墙面固定坐标、沿墙位置、外法向。</summary>
        static void WallGeometry(Room r, Door d, out string axis, out float fixedCoord, out float along, out float nx, out float nz)
        {
            switch (d.Wall)
            {
                // 沿墙取**门洞中心**（offsetM + widthM/2）以便与对端对齐比较；口径与灰盒 compileDoors 一致
                case "north": axis = "z"; fixedCoord = r.MaxZ; along = r.MinX + d.OffsetM + d.WidthM / 2f; nx = 0f; nz = 1f; break;
                case "south": axis = "z"; fixedCoord = r.MinZ; along = r.MinX + d.OffsetM + d.WidthM / 2f; nx = 0f; nz = -1f; break;
                case "west": axis = "x"; fixedCoord = r.MinX; along = r.MinZ + d.OffsetM + d.WidthM / 2f; nx = -1f; nz = 0f; break;
                default: axis = "x"; fixedCoord = r.MaxX; along = r.MinZ + d.OffsetM + d.WidthM / 2f; nx = 1f; nz = 0f; break; // east
            }
        }

        /// <summary>
        /// 解析并校验竖井（从 LoadCore 拆出来：加上这段后 LoadCore 超了 C5 的 120 行上限）。
        /// 判据见 <see cref="ValidateShaft"/> —— 缺省（无 `shafts` 字段）就是单层关卡，行为不变。
        /// </summary>
        static void ParseShafts(Dictionary<string, object> map, LevelData level, List<string> problems)
        {
            if (MiniJson.Get(map, "shafts") is not { } sv) return;
            foreach (var s in MiniJson.AsList(sv))
            {
                var sm = MiniJson.AsMap(s);
                var sh = new Shaft
                {
                    Id = MiniJson.AsString(MiniJson.Get(sm, "id")),
                    Kind = MiniJson.Get(sm, "kind") is string kd ? kd : "stair",
                    MinX = MiniJson.AsFloat(MiniJson.Get(sm, "minX")),
                    MinZ = MiniJson.AsFloat(MiniJson.Get(sm, "minZ")),
                    MaxX = MiniJson.AsFloat(MiniJson.Get(sm, "maxX")),
                    MaxZ = MiniJson.AsFloat(MiniJson.Get(sm, "maxZ")),
                    FromFloor = (int)MiniJson.AsFloat(MiniJson.Get(sm, "fromFloor")),
                    ToFloor = (int)MiniJson.AsFloat(MiniJson.Get(sm, "toFloor")),
                };
                problems.AddRange(ValidateShaft(sh, level));
                level.Shafts.Add(sh);
            }
        }

        /// <summary>
        /// 竖井合法性（跨层地图最容易出错的地方，所以判据写全）：
        /// ① 矩形非退化（宽/深 &gt; 0.5m —— 比玩家直径略大，否则"能通"是纸面上的）；
        /// ② id 非空且不重复；
        /// ③ 层区间升序且**至少跨 1 层**（等于同层就没意义）。
        /// 注：与 tools/validate-levels.mjs 同一套规则，任一方新增规则须同步。
        /// </summary>
        static List<string> ValidateShaft(Shaft sh, LevelData level)
        {
            var bad = new List<string>();
            if (string.IsNullOrEmpty(sh.Id)) bad.Add("[shafts] 竖井 id 为空");
            for (int i = 0; i < level.Shafts.Count; i++)
            {
                // ⚠ 必须跳过**自己**：`Validate()` 会对已在清单里的竖井再校验一遍，
                // 不排除自身的话每条竖井都会报"id 重复"（第一版就是这么炸的）。
                if (ReferenceEquals(level.Shafts[i], sh)) continue;
                if (level.Shafts[i].Id == sh.Id) bad.Add($"[shafts] 竖井 id 重复：{sh.Id}");
            }
            float w = sh.MaxX - sh.MinX, d = sh.MaxZ - sh.MinZ;
            if (w <= 0.5f || d <= 0.5f) bad.Add($"[shafts] 竖井 {sh.Id} 太小（{w:0.00}×{d:0.00}m，需 >0.5m）");
            if (sh.FromFloor > sh.ToFloor) bad.Add($"[shafts] 竖井 {sh.Id} 层区间反了（from {sh.FromFloor} > to {sh.ToFloor}）");
            if (sh.FromFloor == sh.ToFloor) bad.Add($"[shafts] 竖井 {sh.Id} 只覆盖 {sh.FromFloor} 一层 —— 竖井必须跨层");
            return bad;
        }

        /// <summary>结构性校验（与 tools/validate-levels.mjs 同一套规则；任一方新增规则须同步）。</summary>
        public static List<string> Validate(LevelData level, ISet<string> knownKits = null, IDictionary<string, string> knownKinds = null)
        {
            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(level.LevelId)) problems.Add("levelId 为空");
            if (level.Rooms.Count == 0) problems.Add("rooms 为空");
            foreach (var sh in level.Shafts) problems.AddRange(ValidateShaft(sh, level));

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in level.Rooms)
            {
                if (string.IsNullOrWhiteSpace(r.Id)) problems.Add("存在无 id 的房间");
                else if (!ids.Add(r.Id)) problems.Add($"房间 id 重复：{r.Id}");
                if (r.SizeX <= 0 || r.SizeY <= 0 || r.SizeZ <= 0) problems.Add($"房间 {r.Id} 的 size 必须为正数");
                if (string.IsNullOrWhiteSpace(r.Kit)) problems.Add($"房间 {r.Id} 缺 kit");
                else if (knownKits != null && !knownKits.Contains(r.Kit)) problems.Add($"房间 {r.Id} 的 kit `{r.Kit}` 不在 asset-manifest 中");
                // D2：kind 必须与用途匹配（房间用 room 类套件）
                else if (knownKinds != null && knownKinds.TryGetValue(r.Kit, out var rk) && rk != "room")
                    problems.Add($"房间 {r.Id} 的 kit `{r.Kit}` 的 kind={rk}，房间必须用 kind=room 的套件");
                if (r.LightZone == null || !ValidLightZones.Contains(r.LightZone))
                    problems.Add($"房间 {r.Id} 的 lightZone 非法：{r.LightZone}（合法：safe/pressure/high-risk）");
                // 分翼：**必须登记**。缺失会让"猎杀封锁所在翼"的逻辑悄悄漏掉这间房，
                // 而漏掉的后果是玩家能从一个"本该被封的翼"跑出去 —— 那是玩法缺陷，不是数据瑕疵。
                if (string.IsNullOrWhiteSpace(r.Wing))
                    problems.Add($"房间 {r.Id} 缺 wing（分翼）：官方机制要求猎杀时封锁玩家所在翼，"
                        + "未登记的房间无法被封锁 —— 请在关卡 DSL 里补 wing");
                var doorIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var d in r.Doors)
                {
                    if (!ValidWalls.Contains(d.Wall)) problems.Add($"房间 {r.Id} 的门 wall 非法：{d.Wall}");
                    float wallLen = (d.Wall == "north" || d.Wall == "south") ? r.SizeX : r.SizeZ;
                    if (d.OffsetM < 0f) problems.Add($"房间 {r.Id} 的门 {d.Id} 的 offsetM 不能为负：{d.OffsetM}");
                    if (d.WidthM <= 0f) problems.Add($"房间 {r.Id} 的门 {d.Id} 的 widthM 必须为正：{d.WidthM}");
                    if (d.OffsetM + d.WidthM > wallLen + 1e-4f)
                        problems.Add($"房间 {r.Id} 的门 {d.Id} 门洞越界：offsetM {d.OffsetM} + widthM {d.WidthM} > 墙长 {wallLen}");
                    // D1：门必须有 id（走廊要引用它）
                    if (string.IsNullOrWhiteSpace(d.Id)) problems.Add($"房间 {r.Id} 的门缺 id（走廊需要引用它）");
                    else if (!doorIds.Add(d.Id)) problems.Add($"房间 {r.Id} 门 id 重复：{d.Id}");
                }
                foreach (var p in r.Props)
                {
                    if (string.IsNullOrWhiteSpace(p.Kit)) problems.Add($"房间 {r.Id} 有道具缺 kit");
                    else if (knownKits != null && !knownKits.Contains(p.Kit)) problems.Add($"房间 {r.Id} 的道具 kit `{p.Kit}` 不在 asset-manifest 中");
                    else if (knownKinds != null && knownKinds.TryGetValue(p.Kit, out var pk) && pk != "prop")
                        problems.Add($"房间 {r.Id} 的道具 kit `{p.Kit}` 的 kind={pk}，道具必须用 kind=prop 的套件");
                }
            }
            // 门索引：`房间id/门id` → (房间, 门)
            var doorIndex = new Dictionary<string, KeyValuePair<Room, Door>>(StringComparer.Ordinal);
            var roomIndex = new Dictionary<string, Room>(StringComparer.Ordinal);
            foreach (var r in level.Rooms)
            {
                roomIndex[r.Id] = r;
                foreach (var d in r.Doors)
                    if (!string.IsNullOrWhiteSpace(d.Id)) doorIndex[r.Id + "/" + d.Id] = new KeyValuePair<Room, Door>(r, d);
            }
            foreach (var c in level.Corridors)
            {
                var label = c.From + "→" + c.To;
                if (!ids.Contains(c.From)) problems.Add($"走廊起点不存在：{c.From}");
                if (!ids.Contains(c.To)) problems.Add($"走廊终点不存在：{c.To}");
                if (c.Width <= 0) problems.Add($"走廊 {label} 的 width 必须为正");
                if (string.IsNullOrWhiteSpace(c.DoorA) || string.IsNullOrWhiteSpace(c.DoorB))
                {
                    problems.Add($"走廊 {label} 必须给 doorA/doorB（引用 房间id/门id）");
                    continue;
                }
                if (!doorIndex.TryGetValue(c.DoorA, out var A)) { problems.Add($"走廊 {label} 的 doorA 不存在：{c.DoorA}"); continue; }
                if (!doorIndex.TryGetValue(c.DoorB, out var B)) { problems.Add($"走廊 {label} 的 doorB 不存在：{c.DoorB}"); continue; }
                if (A.Key.Id != c.From) problems.Add($"走廊 {label} 的 doorA 属于 {A.Key.Id}，与 from 不一致");
                if (B.Key.Id != c.To) problems.Add($"走廊 {label} 的 doorB 属于 {B.Key.Id}，与 to 不一致");
                if (A.Key.RotY != 0f || B.Key.RotY != 0f) { problems.Add($"走廊 {label} 端点房间带 rotY（几何校验只支持轴对齐）"); continue; }
                if (A.Key.Floor != B.Key.Floor) problems.Add($"走廊 {label} 两端楼层不同（{A.Key.Floor} vs {B.Key.Floor}）");
                // 几何：同轴 / 共墙 / 对开 / 开口对齐
                string axisA, axisB; float fixedA, fixedB, atA, atB; float nxA, nzA, nxB, nzB;
                WallGeometry(A.Key, A.Value, out axisA, out fixedA, out atA, out nxA, out nzA);
                WallGeometry(B.Key, B.Value, out axisB, out fixedB, out atB, out nxB, out nzB);
                if (axisA != axisB) problems.Add($"走廊 {label} 的两门朝向轴不同（{axisA} vs {axisB}）");
                else if (Math.Abs(fixedA - fixedB) > 0.05f) problems.Add($"走廊 {label} 的两门不在同一墙面（{fixedA} vs {fixedB}）");
                else if (Math.Abs(nxA - nxB) < 1e-6f && Math.Abs(nzA - nzB) < 1e-6f) problems.Add($"走廊 {label} 的两门法向相同，不是对开门");
                else if (Math.Abs(atA - atB) > 0.05f) problems.Add($"走廊 {label} 的两门开口未对齐（{atA} vs {atB}）");
            }

            // 房间在 XZ 上不得重叠（同层）
            for (int i = 0; i < level.Rooms.Count; i++)
            for (int j = i + 1; j < level.Rooms.Count; j++)
            {
                var a = level.Rooms[i]; var b = level.Rooms[j];
                if (a.Floor != b.Floor) continue;
                var ox = Math.Min(a.MaxX, b.MaxX) - Math.Max(a.MinX, b.MinX);
                var oz = Math.Min(a.MaxZ, b.MaxZ) - Math.Max(a.MinZ, b.MinZ);
                if (ox > 0.01f && oz > 0.01f) problems.Add($"房间 {a.Id} 与 {b.Id} 在 XZ 上重叠（{ox:F2}×{oz:F2} 米）");
            }
            foreach (var e in level.Events)
            {
                if (!IsEventTypeAllowed(e.Type)) problems.Add($"事件类型非法：{e.Type}（内建 6 型或 x-/ext-/ns: 前缀扩展，V9 §30.2）");
                if (e.DurationSec <= 0) problems.Add($"事件 {e.Type} 的 durationSec 必须为正");
                // V9 §30.2：每个事件必须有明确反制手段
                if (string.IsNullOrWhiteSpace(e.Counterplay)) problems.Add($"事件 {e.Type} 缺 counterplay（V9 §30.2 要求每个事件都有明确反制手段）");
            }
            // V9 §19.2 动态事件每局注入 2~3 个
            if (level.Events.Count > 0 && (level.Events.Count < 2 || level.Events.Count > 3))
                problems.Add($"动态事件数量应为 2~3 个（V9 §19.2），当前 {level.Events.Count}");
            // V9 §7 撤离双点制：标准点与深处点都必须指向真实房间，且不能同一点
            if (level.Extraction != null)
            {
                if (!ids.Contains(level.Extraction.Standard)) problems.Add($"撤离标准点不存在：{level.Extraction.Standard}");
                if (!ids.Contains(level.Extraction.Deep)) problems.Add($"撤离深处点不存在：{level.Extraction.Deep}");
                if (level.Extraction.Standard == level.Extraction.Deep) problems.Add("撤离标准点与深处点不能是同一房间");
            }
            return problems;
        }
    }
}
