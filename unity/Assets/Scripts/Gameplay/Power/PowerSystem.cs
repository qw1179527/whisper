using System;
using System.Collections.Generic;
using Whisper.Gameplay.Config;

namespace Whisper.Gameplay.Power
{
    /// <summary>
    /// 电力系统：**一个总闸（Fuse Box）+ 各房间灯开关**（恐鬼症对齐 · spec §3.4）。
    ///
    /// ## 为什么推翻本工程原有的"分区域配电箱"
    /// 原实现是"每条走廊一个配电箱，各自管一片灯"。官方是**一个总闸开全屋电**，
    /// 各房间灯再单独开关。两者在玩法上差别很大：
    /// · 分区：玩家就近开一片灯，压力分散；
    /// · **总闸**：玩家必须**深入未知区域**去开总闸（这是恐怖感的来源），而鬼可以**关掉总闸**
    ///   逼玩家再跑一趟 —— 官方核心循环之一。
    /// 用户明确要求对齐，故改为单总闸；分区照明作为"总闸 ON 且该房间灯 ON"的**派生态**保留。
    ///
    /// ## 确定性
    /// 无随机：开关状态是显式状态机。鬼关总闸由 <see cref="Interaction"/> 触发。
    /// </summary>
    public sealed class PowerSystem
    {
        /// <summary>总闸是否开启。</summary>
        public bool BreakerOn { get; private set; }
        /// <summary>总闸位置（世界 XZ）。</summary>
        public float BreakerX { get; private set; }
        public float BreakerZ { get; private set; }
        /// <summary>总闸所在房间 id（HUD 提示"总闸在 X"用）。</summary>
        public string BreakerRoom { get; private set; }

        readonly HashSet<string> _lightsOn = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>房间 id → 是否有灯（没有灯的房间不参与）。</summary>
        readonly Dictionary<string, bool> _hasLight = new Dictionary<string, bool>(StringComparer.Ordinal);

        /// <summary>总闸被切换事件（on）—— 灯光与音频系统订阅它。</summary>
        public event Action<bool> OnBreakerChanged;
        /// <summary>某房间灯被切换事件（roomId, on）。</summary>
        public event Action<string, bool> OnRoomLightChanged;

        readonly float _interactRadiusM;
        readonly float _ghostOffChancePerInteract;

        public PowerSystem(GameConfigReader cfg)
        {
            BreakerOn = false;   // 开局必为关：玩家要去找总闸，这是官方的开场压力
            _interactRadiusM = cfg.Float("power.breaker.interactRadiusM", 1.6f);
            _ghostOffChancePerInteract = cfg.Float("power.breaker.ghostOffChance", 0.15f);
        }

        /// <summary>总闸交互半径（米）。</summary>
        public float InteractRadiusM => _interactRadiusM;

        /// <summary>登记一个房间的灯（<paramref name="hasLight"/>=false 表示该房间没有灯）。</summary>
        public void RegisterRoomLight(string roomId, bool hasLight)
        {
            if (string.IsNullOrEmpty(roomId)) return;
            _hasLight[roomId] = hasLight;
            if (!hasLight) _lightsOn.Remove(roomId);
        }

        /// <summary>放置总闸。</summary>
        public void PlaceBreaker(string roomId, float x, float z)
        {
            BreakerRoom = roomId; BreakerX = x; BreakerZ = z;
        }

        /// <summary>某房间的灯是否亮着（= 总闸 ON 且该房间开关 ON）。</summary>
        public bool IsRoomLit(string roomId)
        {
            if (!BreakerOn || string.IsNullOrEmpty(roomId)) return false;
            return _lightsOn.Contains(roomId);
        }

        /// <summary>某房间的灯开关是否被按过（与"是否亮着"区分：总闸关时开关状态仍在）。</summary>
        public bool IsSwitchOn(string roomId) => !string.IsNullOrEmpty(roomId) && _lightsOn.Contains(roomId);

        /// <summary>点灯开关（玩家或鬼都可调；鬼调用时由 <see cref="Interaction"/> 负责）。</summary>
        public bool ToggleRoomLight(string roomId, bool? force = null)
        {
            if (string.IsNullOrEmpty(roomId) || !_hasLight.TryGetValue(roomId, out var has) || !has) return false;
            bool now = _lightsOn.Contains(roomId);
            bool next = force ?? !now;
            if (next == now) return false;
            if (next) _lightsOn.Add(roomId); else _lightsOn.Remove(roomId);
            OnRoomLightChanged?.Invoke(roomId, next);
            return true;
        }

        /// <summary>切总闸（玩家或鬼）。返回是否真的改变了状态。</summary>
        public bool ToggleBreaker(bool? force = null)
        {
            bool next = force ?? !BreakerOn;
            if (next == BreakerOn) return false;
            BreakerOn = next;
            OnBreakerChanged?.Invoke(BreakerOn);
            return true;
        }

        /// <summary>鬼关总闸的概率（Interaction 读它做判定）。</summary>
        public float GhostOffChance => _ghostOffChancePerInteract;

        /// <summary>玩家是否站在总闸交互半径内。</summary>
        public bool PlayerNearBreaker(float x, float z)
        {
            float dx = x - BreakerX, dz = z - BreakerZ;
            return dx * dx + dz * dz <= _interactRadiusM * _interactRadiusM;
        }

        /// <summary>亮着灯的房间数（HUD 与理智系统用）。</summary>
        public int LitRoomCount
        {
            get
            {
                if (!BreakerOn) return 0;
                int n = 0;
                foreach (var id in _lightsOn) if (_hasLight.TryGetValue(id, out var h) && h) n++;
                return n;
            }
        }

        /// <summary>有灯的房间总数。</summary>
        public int RoomWithLightCount
        {
            get { int n = 0; foreach (var kv in _hasLight) if (kv.Value) n++; return n; }
        }

        /// <summary>HUD 一行摘要。</summary>
        public string Describe()
            => $"电闸：{(BreakerOn ? "已合闸" : "断开")}"
             + $" · 灯 {LitRoomCount}/{RoomWithLightCount}"
             + (string.IsNullOrEmpty(BreakerRoom) ? "" : $" · 总闸在 {BreakerRoom}")
             + $" · 玩家{(PlayerNearBreaker(0f, 0f) ? "" : "")}";
    }
}
