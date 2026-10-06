using System;
using System.Collections.Generic;
using UnityEngine;

namespace Whisper.Gameplay.Level
{
    /// <summary>
    /// 室内灯光装置（**环境渲染**）：按关卡的 `lightZone` 给每个房间摆点光，并支持**闪烁**与分区开关。
    ///
    /// ## 为什么需要它（用户点名"需要加入灯光渲染"）
    /// 此前场景里只有 `GameBootstrap` 的一盏方向光：房间没有光源，玩家看到的是"环境项 + 主光"的
    /// 平光。本着色器补了 **ForwardAdd** pass 之后，**每盏额外像素光都会被逐盏加进来**，
    /// 于是"房间里有没有灯"才第一次真的影响画面。本类就是那些灯的**唯一来源**。
    ///
    /// ## 设计取舍
    ///   · 灯位**从关卡数据推**（房间中心 + 沿长轴每 6m 一盏），不写死坐标 —— 改布局不用改这里。
    ///   · 灯色取 `LevelPalette.ZoneBase(zone)`：与墙/地的配色同源，"风险越高越暗"的语义在灯光上也成立。
    ///   · 基准强度按分区给（safe 1.15 / pressure 0.85 / high-risk 0.55）：安全区亮、深处暗。
    ///   · **闪烁是确定性的**：`sin` 组合 + 每盏灯固定相位，不用 `Random`、不用墙钟 ——
    ///     联机各端、回放、门禁复算都能得到同一结果（本项目对"不可复现随机"是判红的）。
    ///   · `Update()` 只做两件小事：把开关态平滑推向目标 + 叠加闪烁；灯数很少，开销可忽略。
    ///
    /// ## 用法（Runtime 侧只需三行）
    ///   `var rig = LightRig.Build(levelGo.transform, level);`   // 布景后建灯
    ///   `rig.SetZoneLights("corridor_main", false);`            // 停电事件：关某个/某些分区的灯
    ///   `rig.SetAllLights(true);`                               // 来电
    /// </summary>
    public sealed class LightRig : MonoBehaviour
    {
        /// <summary>一盏室内灯（含它所属房间/分区与闪烁相位）。</summary>
        public sealed class RoomLight
        {
            public string RoomId;
            public string Zone;
            public Light Light;
            public float BaseIntensity;
            /// <summary>闪烁相位（由房间 id 的稳定哈希给出，确定性）。</summary>
            public float Phase;
            /// <summary>这盏灯是否参与闪烁（安全区不闪：教学区要稳）。</summary>
            public bool Flicker;
            /// <summary>开关平滑进度 0=灭 1=亮（避免停电时"啪"地全黑）。</summary>
            public float On;
            public float Target;
        }

        /// <summary>全部门的灯（诊断与事件用；顺序稳定 = 按房间顺序、再按房间内序号）。</summary>
        public readonly List<RoomLight> Lights = new List<RoomLight>();

        /// <summary>已建的灯对象（重建时按这份清单销毁 —— 不用 Transform.childCount/GetChild：
        /// 它们不在 `native/unity-stubs` 里，而 native/** 不在构建 A 写权内）。</summary>
        readonly List<GameObject> _lampObjects = new List<GameObject>();

        /// <summary>开关过渡速度（每秒）。1.6 → 约 0.6 秒亮/灭，停电时看得见"灯在暗下去"。</summary>
        public const float ToggleSpeed = 1.6f;
        /// <summary>闪烁深度（0.28 → 亮度在 72%~100% 之间晃，恐怖游戏够用且不刺眼）。</summary>
        public const float FlickerDepth = 0.28f;
        /// <summary>沿房间长轴每隔多远放一盏灯（米）。</summary>
        public const float SpacingM = 6f;
        /// <summary>灯离地高度（米）—— 吊灯/吸顶灯的位置。</summary>
        public const float LampHeightM = 2.6f;

        float _t;

        /// <summary>
        /// 按关卡摆灯。返回挂好的组件（调用方持有它即可控制开关）。
        /// 幂等：重复调用会先清掉上一次建的灯。
        /// </summary>
        public static LightRig Build(Transform parent, LevelData level)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (level == null) throw new ArgumentNullException(nameof(level));

            var go = new GameObject("LightRig");
            go.transform.SetParent(parent, false);
            var rig = go.AddComponent<LightRig>();
            rig.Rebuild(level);
            return rig;
        }

        /// <summary>清掉已有灯并重新按关卡摆一遍。</summary>
        public void Rebuild(LevelData level)
        {
            Lights.Clear();
            foreach (var go in _lampObjects) if (go != null) DestroyImmediate(go);
            _lampObjects.Clear();

            foreach (var room in level.Rooms)
            {
                if (room == null) continue;
                float longSide = Math.Max(room.SizeX, room.SizeZ);
                bool alongX = room.SizeX >= room.SizeZ;
                int count = Math.Max(1, (int)Math.Floor(longSide / SpacingM));
                var baseColor = LevelPalette.ZoneBase(room.LightZone);
                float baseIntensity = BaseIntensityOf(room.LightZone);
                for (int i = 0; i < count; i++)
                {
                    float f = count == 1 ? 0.5f : (i + 0.5f) / count;
                    float x = alongX
                        ? room.MinX + room.SizeX * f
                        : room.CenterX;
                    float z = alongX
                        ? room.CenterZ
                        : room.MinZ + room.SizeZ * f;
                    var lampGo = new GameObject($"Lamp_{room.Id}_{i}");
                    lampGo.transform.SetParent(transform, false);
                    lampGo.transform.position = new Vector3(x, room.Floor * LevelGeometry.FloorHeightM + LampHeightM, z);
                    _lampObjects.Add(lampGo);
                    var l = lampGo.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.color = new Color(baseColor.R, baseColor.G, baseColor.B, 1f);
                    l.intensity = baseIntensity;
                    Lights.Add(new RoomLight
                    {
                        RoomId = room.Id,
                        Zone = room.LightZone,
                        Light = l,
                        BaseIntensity = baseIntensity,
                        Phase = PhaseOf(room.Id, i),
                        // 安全区不闪（教学区要稳），压力区轻微闪，高风险区闪得最明显
                        Flicker = room.LightZone != "safe",
                        On = 1f,
                        Target = 1f,
                    });
                }
            }
            Debug.Log($"[LightRig] 建灯 {Lights.Count} 盏 · 房间 {level.Rooms.Count} 个（ForwardAdd 才会让它们真的照亮画面）");
        }

        /// <summary>
        /// 分区基准强度：安全区最亮、高风险区最暗（与 LevelPalette 的明暗梯度同语义）。
        ///
        /// 【2026-10-06 按 URP 重标定】原值（0.55~1.15）是 **Built-in 时代的感性值**；
        /// 迁到 URP 后点光的照度口径变了，实测后果有判据原文为证：
        /// &gt; `entrance_safe/eye` 判定视角开灯帧平均亮度 **10.8** 低于亮度目标下限 **20**（暗到看不清）
        /// 而更早的 Unlit 路径同类视角能到 **40.4** ⇒ 这是**迁 PBR/URP 后变暗**，不是设计意图。
        ///
        /// 标定依据（不猜）：目标是"开灯帧 ≥ 20"。PBR 漫反射对光强**近似线性**（色调映射再压一次），
        /// 故先按 **×2** 抬一档实测（#44 结果：corridor 18.2 仍未达 20，故再 ×1.5） —— 上限是 110，留足余量，不一次抬到可能洗白的程度。
        /// **分区梯度比例保持不变**（安全区仍最亮、高风险区仍最暗）。
        /// </summary>
        public static float BaseIntensityOf(string zone)
        {
            switch (zone)
            {
                case "safe": return 3.45f;
                case "high-risk": return 1.65f;
                default: return 2.55f;
            }
        }

        /// <summary>由房间 id + 序号推出稳定的闪烁相位（FNV-1a，跨端一致，不用 Random）。</summary>
        public static float PhaseOf(string roomId, int index)
        {
            uint h = 2166136261u;
            string s = roomId ?? "";
            for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619u; }
            h ^= (uint)index; h *= 16777619u;
            return (h % 1000u) / 1000f * 6.2831853f;   // 0..2π
        }

        /// <summary>关/开某个**房间**的灯（停电事件按房间点名的情形）。</summary>
        public int SetRoomLights(string roomId, bool on)
        {
            int n = 0;
            foreach (var rl in Lights)
            {
                if (!string.Equals(rl.RoomId, roomId, StringComparison.Ordinal)) continue;
                rl.Target = on ? 1f : 0f; n++;
            }
            return n;
        }

        /// <summary>关/开某个**光分区**的灯（`safe` / `pressure` / `high-risk`）。返回命中的灯数。</summary>
        public int SetZoneLights(string zone, bool on)
        {
            int n = 0;
            foreach (var rl in Lights)
            {
                if (!string.Equals(rl.Zone, zone, StringComparison.Ordinal)) continue;
                rl.Target = on ? 1f : 0f; n++;
            }
            return n;
        }

        /// <summary>全开/全关（停电事件的一键版）。返回命中的灯数。</summary>
        public int SetAllLights(bool on)
        {
            foreach (var rl in Lights) rl.Target = on ? 1f : 0f;
            return Lights.Count;
        }

        /// <summary>当前亮着的灯数（诊断/HUD 用）。</summary>
        public int LightsOn()
        {
            int n = 0;
            foreach (var rl in Lights) if (rl.On > 0.5f) n++;
            return n;
        }

        /// <summary>自检摘要（日志/HUD：证明灯真的建出来了）。</summary>
        public string Describe()
        {
            return $"灯光：{Lights.Count} 盏 · 亮 {LightsOn()} · 闪烁 {CountFlicker()}（ForwardAdd 逐盏叠加）";
        }

        int CountFlicker()
        {
            int n = 0;
            foreach (var rl in Lights) if (rl.Flicker) n++;
            return n;
        }

        /// <summary>开关平滑 + 确定性闪烁（只动正在变的灯与闪烁灯）。</summary>
        void Update()
        {
            if (Lights.Count == 0) return;
            float dt = Time.deltaTime;
            _t += dt;
            for (int i = 0; i < Lights.Count; i++)
            {
                var rl = Lights[i];
                if (rl.Light == null) continue;
                if (Mathf.Abs(rl.On - rl.Target) > 1e-4f)
                {
                    rl.On = Mathf.MoveTowards(rl.On, rl.Target, ToggleSpeed * dt);
                }
                // 闪烁：两个不同频率的 sin 相乘 → 不规则但完全确定（不用 Random，跨端一致）。
                // 用 System.Math.Sin 而不是 Mathf.Sin：后者不在 native/unity-stubs 里。
                float k = 1f;
                if (rl.Flicker && rl.On > 0.01f)
                {
                    float s = (float)(Math.Sin(_t * 13.7f + rl.Phase) * Math.Sin(_t * 7.3f + rl.Phase * 1.7f));
                    k = 1f - FlickerDepth * (0.5f - 0.5f * s);   // s=1 → 1.0；s=-1 → 0.72
                }
                rl.Light.intensity = rl.BaseIntensity * rl.On * k;
            }
        }
    }
}
