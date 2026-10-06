using System.Collections.Generic;
using UnityEngine;
using Whisper.Gameplay.Level;

namespace Whisper.Editor
{
    /// <summary>
    /// **套件可用净高** —— 把"房间声明高度 ↔ 套件实际几何高度"的落差变成可查数据。
    ///
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// 为什么需要它（2026-10-06 我为 morgue 纯黑改了**四次**相机才找到真因）
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// 套件几何是**上下对称、原点在房间中心**摆放的：
    /// `LevelBuilder.BuildRoom` 把 `Room_<id>` 放在 `(CenterX, Floor*3.5, CenterZ)`，
    /// 套件部件挂在它下面且 `localPosition = 0` ⇒ 套件在房间里的实际竖向范围 = **中心 ± 套件高/2**。
    ///
    /// 实测各套件 GLB 顶点（`tmp/all-kit-bounds.mjs` 量的）：
    ///   `hall_main` 2.91m · `hall_main_lobby` 3.41m · **`morgue` 仅 1.69m**
    /// 而房间 DSL 里 `morgue_deep` 声明高 **3.2m** ⇒ 套件顶只到 **0.845m**。
    ///
    /// ⇒ 后果：任何按"人眼 1.6m"放的相机，在 `morgue` 房里都**在顶棚之上**，
    ///   拍到的是越过墙顶的雾 ⇒ 渲染纯黑（颜色数=1），而**没有任何报错**。
    ///
    /// 这正是本项目反复出现的失效形态：**两套尺寸各自都对，合起来对不上**。
    /// 所以这里把它做成**可查询的数据 + 可判红的落差**，而不是继续靠"看哪张图黑了"去发现。
    ///
    /// 数据来源：`unity/Assets/Resources/Kits/<id>.glb.bytes` 的 POSITION accessor min/max
    /// （**直接量几何，不信任何声明值**）。写到 `Assets/Data/kit-heights.json` 由本类读回。
    /// </summary>
    public static class KitUsableHeight
    {
        /// <summary>套件的竖向范围（GLB 局部坐标，单位米）。</summary>
        public struct Range
        {
            public float YMin, YMax;
            public float Height => YMax - YMin;
            public float Center => (YMin + YMax) * 0.5f;
        }

        static Dictionary<string, Range> _cache;

        const string JsonAssetPath = "Assets/Data/kit-heights.json";

        static void Load()
        {
            if (_cache != null) return;
            _cache = new Dictionary<string, Range>();
            try
            {
                var ta = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(JsonAssetPath);
                if (ta == null || string.IsNullOrEmpty(ta.text)) return;
                var root = MiniJson.AsMap(MiniJson.Parse(ta.text));
                foreach (var kv in root)
                {
                    var m = MiniJson.AsMap(kv.Value);
                    if (m == null) continue;
                    _cache[kv.Key] = new Range
                    {
                        YMin = m.ContainsKey("yMin") ? MiniJson.AsFloat(m["yMin"]) : 0f,
                        YMax = m.ContainsKey("yMax") ? MiniJson.AsFloat(m["yMax"]) : 0f,
                    };
                }
            }
            catch { /* 读不到就退回保守缺省（调用方有兜底） */ }
        }

        /// <summary>取某套件的竖向范围；没有登记返回 false。</summary>
        public static bool TryGet(string kitId, out Range range)
        {
            Load();
            range = default;
            return !string.IsNullOrEmpty(kitId) && _cache.TryGetValue(kitId, out range);
        }

        /// <summary>
        /// 该房间里"能站人/能取景"的高度。
        ///
        /// 口径：套件竖向范围的**中部偏上**（看得到地面与家具，又不至于顶到天花板）。
        /// 没有套件数据时退回房间声明高度的一半（保持可用，不静默失败为零）。
        /// </summary>
        public static float EyeHeightFor(string roomId, float declaredRoomHeightM)
        {
            var level = LevelCache.Current;
            string kit = null;
            if (level?.Rooms != null)
            {
                var r = level.Rooms.Find(x => x.Id == roomId);
                if (r != null) kit = r.Kit;
            }
            if (kit != null && TryGet(kit, out var range))
            {
                // ══════════════════════════════════════════════════════════════════
                // 【2026-10-06 更正：我在这上面栽了一整条错误链路，代价 5 轮 CI】
                // 我原先假定两件事，**两件都错**：
                //   ① "glTF 是 Y-up，所以 accessor 的 Y 是高度" —— 错。
                //      实测（Blender 世界 Z + 部件命名交叉验证）：`skirt_*`（踢脚线）Z∈[-0.01,0.10]、
                //      `cornice_*`/`ceiling` 在 Z≈3.1 ⇒ **垂直轴是 Z**。
                //      我把 `morgue` 高度读成 **1.69m** —— 那是它的**水平进深**。
                //   ② "套件原点在几何中心、上下对称" —— 也错。实测 yMin≈0 ⇒ **原点在底面**。
                //
                // 由此连锁出的假结论（**全部作废**）：
                //   · `kit-heights.json` 33 个套件高度全错（量的是水平尺寸）
                //   · "房间↔套件高度不匹配"审计报的 3 个缺陷是我自己造的
                //   · 为绕开假高度把相机放到 y=0.30（荒谬高度）
                //   · 而 morgue 纯黑的真因**始终没被碰到**
                //
                // 现在数据由 `tools/measure_kit_heights.py` 用 Blender 量**世界 Z** 生成。
                // 取景口径回到最朴素的那个：**人眼 1.6m**，再夹进套件竖向范围内。
                // ══════════════════════════════════════════════════════════════════
                const float HumanEyeM = 1.60f;
                float lo = range.YMin, hi = range.YMax;
                if (hi - lo < 0.8f) return (lo + hi) * 0.5f;   // 极矮构件：取中部
                return Mathf.Clamp(HumanEyeM, lo + 0.3f, hi - 0.3f);
            }
            return Mathf.Max(0.6f, declaredRoomHeightM * 0.5f);
        }

        /// <summary>
        /// 落差审计：房间声明高度 vs 套件实际高度。
        /// 落差超过 <paramref name="toleranceM"/> 就是**真缺陷**（相机/玩家会在几何之外）。
        /// </summary>
        public static List<string> AuditHeightMismatch(float toleranceM = 0.5f)
        {
            var problems = new List<string>();
            var level = LevelCache.Current;
            if (level?.Rooms == null) return problems;
            foreach (var r in level.Rooms)
            {
                if (!TryGet(r.Kit, out var range)) continue;
                // 套件原点在**底面**（实测 yMin≈0）⇒ 可用高度就是套件自身高度，与房间声明高度直接比。
                float gap = r.SizeY - range.Height;
                if (gap > toleranceM)
                    problems.Add($"房间 {r.Id}（kit={r.Kit}）声明高 {r.SizeY:0.00}m，"
                        + $"但套件实际只高 {range.Height:0.00}m ⇒ 顶部空 {gap:0.00}m。"
                        + "玩家/相机若按声明高度摆放就会落在几何之外（实测表现为**渲染纯黑**）。");
            }
            return problems;
        }
    }

    /// <summary>
    /// 关卡缓存：`KitUsableHeight` 需要知道"房间用的是哪个套件"。
    /// 放在这里而不是每次自己加载：取证脚本已经加载过一遍关卡。
    /// </summary>
    public static class LevelCache
    {
        /// <summary>当前关卡（由取证/构建流程设置）。</summary>
        public static LevelData Current { get; set; }
    }
}
