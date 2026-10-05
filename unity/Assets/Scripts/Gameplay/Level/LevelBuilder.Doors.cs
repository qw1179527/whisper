using System;
using System.Collections.Generic;
using UnityEngine;

namespace Whisper.Gameplay.Level
{
    /// <summary>
    /// <see cref="LevelBuilder"/> 的**门扇**部分（partial）：门套/门楣/门樘、门叶与枢轴、开合动画、
    /// 以及给玩法层的门交互入口。
    ///
    /// 【为什么拆成两个文件】门扇与开合动画加进来后主文件涨到 617 行，触发规模纪律（gate-code C5，>600 判红）。
    /// 切法按职责：**门**这块自成一体（入口是 `Build` 里的 `BuildDoorLeaves()` 与对外的
    /// `ToggleDoor`/`TryFindDoorNear`），房间/墙/道具/材质与它无关。
    ///
    /// 本文件同时**删掉了旧的 `BuildDoors(Room)`**：它按每个 DSL 条目建一块填满门洞的实心板，
    /// 而 DSL 里同一门洞登记两次 → 10 对完全同位共面（z-fighting），且开门时门板不动。
    /// 现在按几何层的**物理洞口**建门（`Geometry.Doors` 已合并 twin）。
    /// </summary>
    public sealed partial class LevelBuilder
    {

        /// <summary>门叶高度（米）。门洞是**通高**的（墙段从地到顶切开），所以门叶上方用门楣补齐。</summary>
        public const float LeafHeightM = 2.4f;
        /// <summary>开门动画速度（进度/秒）：0.9 → 约 1.1 秒开到底（恐怖游戏的门不该"啪"地弹开）。</summary>
        public const float DoorOpenSpeed = 0.9f;
        /// <summary>门叶厚度（米）。</summary>
        const float LeafThickness = 0.05f;
        /// <summary>门樘（两侧立柱）宽度（米）。</summary>
        const float JambWidth = 0.08f;

        /// <summary>
        /// 一扇门叶：枢轴（平开=合页 / 推拉=滑块）+ 关/开两个极限位姿 + 平滑进度。
        /// 为什么不在建门时直接摆成"开"或"关"：用户点门是**高频交互**，视觉必须连续过渡；
        /// 而且门的状态由几何层（碰撞）决定，视觉只是它的投影 —— 两边不能各存一份状态。
        /// </summary>
        public sealed class DoorLeaf
        {
            public string Key;                 // 规范门键（物理洞口）
            public string Type;                // swing/double/sliding/card/fire/elevator
            public Transform Pivot;            // 平开：绕它转；推拉：沿墙平移
            public Vector3 ClosedPos, OpenPos; // 枢轴的关/开位置（推拉用；平开两者相同）
            public float ClosedYaw, OpenYaw;   // 枢轴的关/开偏航（平开用；推拉两者相同）
            public float T;                    // 当前进度 0=关 1=开
            public float Target;               // 目标进度（由几何层的门状态给出）
        }

        /// <summary>全部门叶（一个**物理洞口**一条，DSL 里走廊侧/房间侧重复登记已在几何层合并）。</summary>
        public readonly List<DoorLeaf> DoorLeaves = new List<DoorLeaf>();

        /// <summary>
        /// 建门：**按几何层的洞口清单**（`Geometry.Doors`，已按几何重合合并 twin），
        /// 一个洞口 = 一副门套（两门樘 + 门楣）+ 1~2 扇门叶。
        ///
        /// 【为什么不再按 DSL 条目建】旧实现按每个 `room.Doors` 条目建一块填满门洞的实心板，
        /// 而 DSL 里同一个门洞登记了两次（走廊侧 + 房间侧）→ **10 对完全同位共面**的门板
        /// （与刚修掉的"共享墙重复"是同一类 z-fighting 源），而且门永远是"关着的样子"。
        /// </summary>
        void BuildDoorLeaves()
        {
            if (Geometry == null) return;
            float leafH = LeafHeightM;
            foreach (var d in Geometry.Doors)
            {
                var zone = ZoneOf(d.Key);
                float half = d.Width * 0.5f;
                float h = Mathf.Min(leafH, d.Height);
                bool alongX = d.AlongX;

                var root = new GameObject($"Door_{d.Key.Replace('/', '_')}_{d.Type}");
                root.transform.SetParent(transform, false);
                root.transform.position = new Vector3(d.CenterX, d.BaseY, d.CenterZ);
                DoorObjects.Add(root);

                // 门楣：门叶上方的开口（墙段是通高切开的）——没有它，门上方会透空
                AddBox(root, "Lintel", new Vector3(0f, (h + d.Height) * 0.5f, 0f),
                    alongX ? new Vector3(d.Width, Mathf.Max(d.Height - h, 0.02f), 0.08f)
                           : new Vector3(0.08f, Mathf.Max(d.Height - h, 0.02f), d.Width),
                    ToColor(LevelPalette.DoorFrame(zone)));
                // 两侧门樘
                for (int s = -1; s <= 1; s += 2)
                {
                    float off = (half + JambWidth * 0.5f) * s;
                    AddBox(root, s < 0 ? "JambA" : "JambB",
                        new Vector3(alongX ? off : 0f, Mathf.Min(h, d.Height) * 0.5f, alongX ? 0f : off),
                        alongX ? new Vector3(JambWidth, Mathf.Min(h, d.Height), 0.08f)
                               : new Vector3(0.08f, Mathf.Min(h, d.Height), JambWidth),
                        ToColor(LevelPalette.DoorFrame(zone)));
                }

                bool doubleLeaf = d.Type == "double";
                bool sliding = d.Type == "sliding" || d.Type == "elevator";
                int leaves = doubleLeaf ? 2 : 1;
                for (int i = 0; i < leaves; i++)
                {
                    float sign = (i == 0) ? -1f : 1f;                  // 双开：两扇反向
                    float leafW = doubleLeaf ? half : d.Width;
                    // 枢轴位置：平开在**合页侧**（洞口边）；推拉在洞口中心（整扇沿墙平移）
                    float hinge = sliding ? 0f : sign * half;
                    var pivotGo = new GameObject(doubleLeaf ? (i == 0 ? "LeafA" : "LeafB") : "Leaf");
                    pivotGo.transform.SetParent(root.transform, false);
                    var pivot = pivotGo.transform;
                    Vector3 closed = alongX ? new Vector3(hinge, 0f, 0f) : new Vector3(0f, 0f, hinge);
                    pivot.localPosition = closed;

                    // 门叶挂在枢轴上，从合页向另一侧铺开（所以面板相对枢轴偏半个门宽）
                    var leafGo = new GameObject("Panel");
                    leafGo.transform.SetParent(pivot, false);
                    float panelOffset = sliding ? 0f : -sign * leafW * 0.5f;
                    leafGo.transform.localPosition = alongX
                        ? new Vector3(panelOffset, h * 0.5f, 0f)
                        : new Vector3(0f, h * 0.5f, panelOffset);
                    AddBox(leafGo, "Body", Vector3.zero,
                        alongX ? new Vector3(leafW, h, LeafThickness) : new Vector3(LeafThickness, h, leafW),
                        ToColor(LevelPalette.Prop(zone)));

                    // 关/开两个极限位姿：平开绕 Y 转 90°；推拉沿墙平移一个门宽（门"缩进墙里"）
                    float openYaw = sliding ? 0f : sign * 90f;
                    Vector3 open = sliding
                        ? (alongX ? new Vector3(-sign * d.Width, 0f, 0f) : new Vector3(0f, 0f, -sign * d.Width))
                        : closed;
                    DoorLeaves.Add(new DoorLeaf
                    {
                        Key = d.Key, Type = d.Type, Pivot = pivot,
                        ClosedPos = closed, OpenPos = open,
                        ClosedYaw = 0f, OpenYaw = openYaw,
                        T = 0f, Target = 0f,
                    });
                }
            }
            SyncDoorVisuals(instant: true);   // 出厂：与几何层一致（默认全关）
        }

        /// <summary>门键（`房间id/门id`）→ 该房间的光区（决定门框/门叶配色）。</summary>
        string ZoneOf(string doorKey)
        {
            int slash = doorKey == null ? -1 : doorKey.IndexOf('/');
            string roomId = slash > 0 ? doorKey.Substring(0, slash) : null;
            if (roomId != null && Level != null)
                foreach (var r in Level.Rooms)
                    if (r.Id == roomId) return r.LightZone;
            return "pressure";
        }

        /// <summary>
        /// 把几何层的门开关态投影到门叶（`instant=true` 直接到位：取证/恢复用；
        /// 否则只设目标，由 <see cref="Update"/> 平滑推进）。
        /// </summary>
        public void SyncDoorVisuals(bool instant = false)
        {
            if (Geometry == null) return;
            foreach (var leaf in DoorLeaves)
            {
                leaf.Target = Geometry.IsDoorOpen(leaf.Key) ? 1f : 0f;
                if (instant)
                {
                    leaf.T = leaf.Target;
                    ApplyLeafPose(leaf);
                }
            }
        }

        /// <summary>Runtime 便捷入口：改**碰撞**（几何层）并同步门扇。门锁着时返回 false。</summary>
        public bool SetDoorOpen(string key, bool open, bool instant = false)
        {
            if (Geometry == null) return false;
            if (!Geometry.SetDoorOpen(key, open)) return false;
            SyncDoorVisuals(instant);
            return true;
        }

        /// <summary>
        /// 玩家交互入口：找 `radius` 内最近的门并切换它（碰撞 + 门扇一起动）。
        /// 返回值与 `reason` 供 HUD 显示（"附近没有门" / "需要磁卡" / "已打开"…），
        /// **不静默失败** —— 本项目反复强调"失败要可见"。
        /// </summary>
        public bool TryToggleNearestDoor(float x, float z, float radius, out string key, out bool nowOpen, out string reason)
        {
            key = null; nowOpen = false; reason = null;
            if (Geometry == null) { reason = "关卡几何尚未编译"; return false; }
            if (!Geometry.TryFindDoorNear(x, z, radius, out var d)) { reason = "附近没有门"; return false; }
            key = d.Key;
            if (!Geometry.TryToggleDoor(d.Key, out nowOpen, out reason)) return false;
            SyncDoorVisuals();
            return true;
        }

        /// <summary>把一扇门叶摆到它的进度位姿（平开=绕合页转，推拉=沿墙平移）。</summary>
        static void ApplyLeafPose(DoorLeaf leaf)
        {
            if (leaf == null || leaf.Pivot == null) return;
            float yaw = leaf.ClosedYaw + (leaf.OpenYaw - leaf.ClosedYaw) * leaf.T;
            leaf.Pivot.localRotation = Quaternion.Euler(0f, yaw, 0f);
            leaf.Pivot.localPosition = new Vector3(
                leaf.ClosedPos.x + (leaf.OpenPos.x - leaf.ClosedPos.x) * leaf.T,
                leaf.ClosedPos.y + (leaf.OpenPos.y - leaf.ClosedPos.y) * leaf.T,
                leaf.ClosedPos.z + (leaf.OpenPos.z - leaf.ClosedPos.z) * leaf.T);
        }

        /// <summary>门扇开合的平滑过渡（每帧把 T 推向 Target；只动正在动的门，静止时零开销）。</summary>
        void Update()
        {
            if (DoorLeaves.Count == 0 || Geometry == null) return;
            float step = DoorOpenSpeed * Time.deltaTime;
            for (int i = 0; i < DoorLeaves.Count; i++)
            {
                var leaf = DoorLeaves[i];
                // 【为什么每帧从几何层取目标】这样**任何**开门路径都会带动门扇：
                // 几何层直调（PlayerController 手上只有 Geometry）、开局敞开主干道（GameBootstrap）、
                // 本类的 ToggleDoor。视觉与碰撞不会各说各话 —— 门的状态只有几何层一个真源。
                // 代价：10 个门叶各一次集合查表，可忽略。
                leaf.Target = Geometry.IsDoorOpen(leaf.Key) ? 1f : 0f;
                if (Mathf.Abs(leaf.T - leaf.Target) < 1e-4f) continue;
                if (step <= 0f) { leaf.T = leaf.Target; ApplyLeafPose(leaf); continue; }   // 暂停/编辑：直接到位
                leaf.T = Mathf.MoveTowards(leaf.T, leaf.Target, step);
                ApplyLeafPose(leaf);
            }
        }

        /// <summary>
        /// Runtime 查询 API（lead 约定的签名）：`radius` 内最近的门 → **规范门键**（可直接喂给
        /// `ToggleDoor`/`SetDoorOpen`）+ 世界坐标（门洞中心、地面高度）。
        /// </summary>
        public bool TryFindDoorNear(float x, float z, float radius, out string doorKey, out Vector3 worldPos)
        {
            doorKey = null;
            worldPos = default;
            if (Geometry == null) return false;
            if (!Geometry.TryFindInteractableDoor(x, z, radius, out var info)) return false;
            doorKey = info.Key;
            worldPos = new Vector3(info.X, info.Y, info.Z);
            return true;
        }

        /// <summary>
        /// Runtime 交互 API（lead 约定的签名）：开↔关，碰撞与门扇一起动。
        /// 返回**是否真的切了**（拼错键、锁着的门 → false，原因见 <see cref="LastDoorMessage"/>）。
        /// </summary>
        public bool ToggleDoor(string doorKey)
        {
            if (Geometry == null) return false;
            if (!Geometry.ToggleDoor(doorKey)) return false;   // 上锁/未知键：几何层已写明原因
            SyncDoorVisuals();
            return true;
        }

        /// <summary>最近一次门交互的人类可读结果（HUD 提示直接显示；几何层未编译时为 null）。</summary>
        public string LastDoorMessage => Geometry != null ? Geometry.LastDoorMessage : null;

        /// <summary>
        /// 直接把某扇门叶摆到指定开合进度（0=关 1=开），**不改碰撞**。
        /// 用途：取证（编辑模式没有 Update，动画不会自己跑）、以及运行时把网络下发的门位姿贴上来。
        /// 平时请走 <see cref="ToggleDoor"/>，让碰撞与视觉一起动。
        /// </summary>
        public bool SetDoorVisualProgress(string doorKey, float t)
        {
            if (Geometry == null) return false;
            string canon = Geometry.ResolveDoorKey(doorKey);
            bool hit = false;
            foreach (var leaf in DoorLeaves)
            {
                if (!string.Equals(leaf.Key, canon, StringComparison.Ordinal)) continue;
                leaf.T = t < 0f ? 0f : (t > 1f ? 1f : t);
                leaf.Target = leaf.T;
                ApplyLeafPose(leaf);
                hit = true;
            }
            return hit;
        }

        /// <summary>门扇自检摘要（HUD/日志用：证明"门真的建出来了、且开关态一致"）。</summary>
        public string DescribeDoors()
        {
            if (Geometry == null) return "门：几何未编译";
            int open = 0;
            foreach (var leaf in DoorLeaves) if (leaf.Target > 0.5f) open++;
            return $"门：物理洞口 {Geometry.DoorOpeningCount}（DSL 键 {Geometry.DoorCount}）· 门叶 {DoorLeaves.Count} · 开着 {open}";
        }
    }
}
