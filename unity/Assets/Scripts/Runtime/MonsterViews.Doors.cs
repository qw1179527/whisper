using System.Collections.Generic;
using UnityEngine;
using Whisper.Gameplay.Config;
using Whisper.Gameplay.Level;
using Whisper.Gameplay.Monsters;

namespace Whisper.Runtime
{
    /// <summary>
    /// 鬼开关门（`MonsterViews` 的 partial 拆分）。
    ///
    /// ## 用户要求
    /// 「并且鬼也会开关门」—— 这让"关门躲鬼"不再是绝对安全：会开门的鬼能追进房间。
    /// 这也是 27 鬼表里 `canOpenDoors` 字段的用途（`config.monsters.*.canOpenDoors`）。
    ///
    /// ## 设计：为什么是"接近就开"而不是"寻路时开门"
    /// 寻路是格子级的（`MonsterDirector` + `LevelGeometry`），门在几何层是**动态阻挡层**
    /// （`_closedDoorCells`）：关着的门让格子不可走，于是怪**根本不会往门那边走** ——
    /// 它会在门口停下或绕路，永远走不到门前，"到门前才开门"这条永远不触发。
    /// 所以正确做法是**在怪接近时先把门打开**：下一 tick 格子变可走，它的寻路自然就穿过去了。
    ///
    /// ## 规则（按鬼种区分，不做成一种行为）
    /// - `canOpenDoors == false`（如**缝匠 stitcher**：教学怪的应对手段就是"关门可迟滞"）
    ///   → **永不开门**。若让它也开门，教学怪就失去了唯一的反制手段。
    /// - `canOpenDoors == true` → 进入 `OpenRadiusM` 内把**关着**的门打开。
    ///
    /// ## 会关门吗
    /// **不主动关**。关门的唯一效果是把玩家关在房里 —— 那是纯粹的恶意，会造成
    /// "被堵死在房间、看着自己被追"的挫败体验（本作接触已是"扣理智 + 无敌期"而非即死，
    /// 堵门没有配套的公平性）。门由**玩家**关，鬼只负责推开。
    /// 若后续要做"鬼关门埋伏"，应做成某鬼种的**专属机制**，而不是默认行为。
    /// </summary>
    public sealed partial class MonsterViews
    {
        /// <summary>鬼进入这个半径就把它面前关着的门推开（比玩家的 2.2m 稍大：鬼不必贴到门上）。</summary>
        const float GhostOpenDoorRadiusM = 2.6f;
        /// <summary>节流：每 0.25s 检查一次（门不会瞬移，没必要每帧）。</summary>
        const float DoorCheckIntervalSec = 0.25f;

        LevelBuilder _levelBuilder;
        readonly Dictionary<string, bool> _canOpenDoors = new Dictionary<string, bool>();
        float _nextDoorCheck;

        /// <summary>
        /// 注入 `LevelBuilder`（门扇与开关态的 Runtime 入口）并读取每只鬼的 `canOpenDoors`。
        /// 由 `GameBootstrap` 在装配怪物后调用。
        /// </summary>
        public void BindDoors(LevelBuilder builder, GameConfigReader cfg)
        {
            _levelBuilder = builder;
            _canOpenDoors.Clear();
            if (cfg == null) return;
            foreach (var id in MonsterDirector.DefaultMonsterIds)
                // 缺字段 = 不开门（保守：宁可少一种行为，也不要凭空给鬼加能力）
                _canOpenDoors[id] = cfg.Bool($"monsters.{id}.canOpenDoors", false);
        }

        /// <summary>本帧的开关门检查（内部节流）。由 `MonsterViews.Update` 调用。</summary>
        void TickDoors()
        {
            if (_levelBuilder == null || _levelBuilder.Geometry == null) return;
            if (Time.unscaledTime < _nextDoorCheck) return;
            _nextDoorCheck = Time.unscaledTime + DoorCheckIntervalSec;

            var views = _lastViews;
            if (views == null) return;

            for (int i = 0; i < views.Length; i++)
            {
                string id = views[i].Id;
                // 缺字段的鬼按"不开门"处理（保守）
                if (!_canOpenDoors.TryGetValue(id, out bool canOpen) || !canOpen) continue;

                LevelGeometry.DoorInfo info;   // DoorInfo 是 LevelGeometry 的嵌套类型，必须限定
                if (!_levelBuilder.Geometry.TryFindInteractableDoor(views[i].X, views[i].Z, GhostOpenDoorRadiusM, out info))
                    continue;
                if (info.Open) continue;                       // 已经开着，无需动作

                // 只开门、不关门（理由见类注释）。走 LevelBuilder 以便门扇视觉一起动。
                if (_levelBuilder.ToggleDoor(info.Key))
                    Debug.Log($"[Whisper] 鬼({id}) 推开了门 {info.DisplayName}");
            }
        }

        /// <summary>HUD 用：哪些鬼会开门（便于真机核验"按鬼种区分"确实生效）。</summary>
        public string DescribeDoorPolicy()
        {
            if (_canOpenDoors.Count == 0) return "门：未绑定";
            var sb = new System.Text.StringBuilder("门：");
            bool first = true;
            foreach (var kv in _canOpenDoors)
            {
                if (!first) sb.Append(" · ");
                sb.Append(kv.Key).Append(kv.Value ? "会开门" : "不开门");
                first = false;
            }
            return sb.ToString();
        }
    }
}
