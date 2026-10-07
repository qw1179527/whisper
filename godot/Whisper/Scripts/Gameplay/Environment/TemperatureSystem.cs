using System;
using System.Collections.Generic;
using Whisper.Gameplay.Config;

namespace Whisper.Gameplay.Environment
{
    /// <summary>
    /// 天气（用户 2026-10-04 晚原件第 8 条）：8 种，各带一个正常室温基线。
    /// 天气决定"这一局的基础室温"，而鬼的影响是在此之上再降温 —— 两者叠加才是玩家读到的数。
    /// </summary>
    public static class Weather
    {
        /// <summary>全部天气 id（与 config.temperature.weather 的键一致）。</summary>
        public static readonly string[] All =
        {
            "clear", "cloudy", "rain", "heavyRain", "snow", "sleet", "hail", "blizzard",
        };

        public static string Label(string id) => GameConfig.Get($"temperature.weather.{id}.label") as string ?? id;

        /// <summary>该天气的正常室温基线（°C）。缺配置抛异常（不许兜默认值）。</summary>
        public static float BaselineC(string id)
        {
            var raw = GameConfig.Get($"temperature.weather.{id}.baselineC");
            if (raw is long l) return l;
            if (raw is double d) return (float)d;
            if (raw is int i) return i;
            throw new InvalidOperationException($"配置缺 temperature.weather.{id}.baselineC（天气 id 必须来自 Weather.All）");
        }

        /// <summary>刮风等环境事件对室温的额外降温量（用户第 9 条）。</summary>
        public static float WindCoolsBy() => F("temperature.events.wind.coolsBy");

        // ── 由局种子派生（跨端一致）──────────────────────────────────────────
        //
        // 【为什么要这样】天气决定基线室温，而这是 60 Tick 同步的联机游戏：
        // 各端必须选到同一种天气，否则寒温证据、吐寒气全都对不上。
        // 用引擎自带的随机数生成器做不到跨端一致（全局种子 / 系统时间），
        // 所以天气与刮风一律从**局种子**纯函数派生 —— 同种子同结果，且不需要额外的同步字段。
        // **种子的真源是 `MatchDirector.matchSeed`**（它已经用同样的思路做事件调度）；
        // 这里不自己造种子，只提供"由种子决定天气"的纯函数。
        // `gate-physics` 曾因我在启动时用墙钟取种子而判红（"墙钟时间不可复现"），这条正是修法。

        /// <summary>由局种子取天气下标（纯函数 → 各端一致）。</summary>
        public static int PickIndexForMatch(int matchSeed)
        {
            uint x = (uint)matchSeed;
            if (x == 0u) x = 0x9E3779B9u;
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;     // xorshift32 洗一次，避免低位规律
            return (int)(x % (uint)All.Length);
        }

        /// <summary>由局种子决定本局是否刮风（纯函数 → 各端一致）。</summary>
        public static bool WindForMatch(int matchSeed)
        {
            uint x = (uint)matchSeed ^ 0x85EBCA6Bu;
            if (x == 0u) x = 0x27D4EB2Fu;
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            return (x % 10u) < 3u;                        // 30%
        }

        /// <summary>
        /// 由局种子 + 一条"派生流标签"得到 [0,1) 的确定值。
        /// 用途：所有"必须各端一致"的随机选择（天气、刮风、鬼房）都走这里。
        /// `streamTag` 让不同选择**互不相关** —— 否则会出现"晴天必然配某间鬼房"这种可被玩家反推的相关性。
        /// </summary>
        public static float Pick01ForMatch(int matchSeed, uint streamTag)
        {
            uint x = (uint)matchSeed ^ streamTag;
            if (x == 0u) x = 0x9E3779B9u;
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;      // 洗两轮，去掉 streamTag 的线性痕迹
            return (x >> 8) / 16777216f;                  // 取高 24 位 → [0,1)
        }

        /// <summary>
        /// 造一个**由局种子派生的随机源委托**（连续调用得到不同值，但同种子同序列）。
        /// 给 `GhostRoom` 这类需要"多次掷"的机制用 —— 联机各端同种子 → 换房序列完全一致。
        /// </summary>
        public static Func<float> MakeSeededRoll(int matchSeed, uint streamTag)
        {
            uint state = (uint)matchSeed ^ streamTag;
            if (state == 0u) state = 0x9E3779B9u;
            return () =>
            {
                state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                return (state >> 8) / 16777216f;
            };
        }

        static float F(string path)
        {
            var raw = GameConfig.Get(path);
            if (raw is long l) return l;
            if (raw is double d) return (float)d;
            if (raw is int i) return i;
            throw new InvalidOperationException($"配置缺 {path}");
        }
    }

    /// <summary>
    /// 温度系统（权威出处：用户 2026-10-04 晚原件第二节）。
    ///
    /// ## 语义（与我上一版自拟值的关键区别）
    /// · **两档降温**：鬼房（gradual）与**鬼周围**（更陡，3m 内）—— 玩家体感是"走到鬼边上突然一冷"；
    /// · **鬼在鬼房内时降温更快**（×1.6）；
    /// · **降温到区间而非单值**：普通鬼 [−2, 5]°C；**带「刺骨寒温」证据的鬼 [−8, −5]°C**；
    /// · 玩家周围 **低于 −1°C → 口吐寒气**（其他玩家可见）；
    /// · **鬼离开后逐渐回升**，直至与周围正常区域持平（回暖比降温慢）；
    /// · 基线室温由**天气**决定（8 种，−1~23°C），局与局不同。
    ///
    /// ## 纯逻辑
    /// 时间由调用方喂 `dt`，随机不参与 —— 可在没有 Unity 的机器上按固定 dt 验完所有性质。
    /// </summary>
    public sealed class TemperatureSystem
    {
        readonly float _ambientMin, _ambientMax;
        readonly float _roomTargetNormal, _roomFloorNormal;
        readonly float _roomTargetFreezing, _roomFloorFreezing;
        readonly float _roomCoolPerSec, _inRoomMultiplier;
        readonly float _aroundRadiusM, _aroundCoolPerSec;
        readonly float _rewarmPerSec, _breathC;
        readonly float _freezingThreshold, _holdSeconds;

        readonly Dictionary<string, float> _temp = new Dictionary<string, float>(StringComparer.Ordinal);
        readonly Dictionary<string, float> _belowFor = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>本局基线室温（由天气决定）。</summary>
        public float BaselineC { get; private set; }
        /// <summary>本局天气 id。</summary>
        public string WeatherId { get; private set; }
        /// <summary>鬼当前所在房间 id（null = 不在任何房间）。</summary>
        public string GhostRoomId { get; private set; }
        /// <summary>该鬼是否带「刺骨寒温」证据（决定降温区间）。</summary>
        public bool GhostHasFreezingEvidence { get; private set; }
        /// <summary>鬼是否**正在鬼房内**（在房内降温 ×1.6）。判据是"鬼房"这个设定，不是"鬼在哪"。</summary>
        public bool GhostInsideItsRoom { get; private set; }
        /// <summary>鬼的实时坐标（只影响"鬼周围 3m 更陡"这一条）。</summary>
        public float GhostX { get; private set; }
        public float GhostZ { get; private set; }
        /// <summary>鬼当前实际所在房间（由调用方每帧反查后喂入，仅用于"回鬼房时降温加速"）。</summary>
        string _ghostCurrentRoom;
        /// <summary>设置鬼当前所在房间（只用于判定"鬼是否回到了鬼房"）。</summary>
        public void SetGhostCurrentRoom(string roomId) => _ghostCurrentRoom = roomId;
        /// <summary>本帧是否刚刚确认寒温证据（一次性提示用）。</summary>
        public bool FreezingJustConfirmed { get; private set; }
        /// <summary>玩家是否在吐寒气（玩家所在处温度低于 −1°C）。</summary>
        public bool PlayerBreathing { get; private set; }

        public TemperatureSystem(GameConfigReader cfg, string weatherId, bool wind = false)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            WeatherId = string.IsNullOrEmpty(weatherId) ? "cloudy" : weatherId;

            _ambientMin = Req("temperature.ambient.minC");
            _ambientMax = Req("temperature.ambient.maxC");
            BaselineC = Weather.BaselineC(WeatherId);
            if (wind) BaselineC -= Weather.WindCoolsBy();      // 刮风进一步压低
            if (BaselineC < _ambientMin) BaselineC = _ambientMin;
            if (BaselineC > _ambientMax) BaselineC = _ambientMax;

            _roomTargetNormal = Req("temperature.ghostRoom.targetC");
            _roomFloorNormal = Req("temperature.ghostRoom.floorC");
            _roomTargetFreezing = Req("temperature.ghostRoom.targetWithFreezingEvidenceC");
            _roomFloorFreezing = Req("temperature.ghostRoom.floorWithFreezingEvidenceC");
            _roomCoolPerSec = Req("temperature.ghostRoom.coolPerSecC");
            _inRoomMultiplier = Req("temperature.ghostRoom._inRoomCoolMultiplier");
            _aroundRadiusM = Req("temperature.aroundGhost.radiusM");
            _aroundCoolPerSec = Req("temperature.aroundGhost.coolPerSecC");
            _rewarmPerSec = Req("temperature.rewarmPerSecC");
            _breathC = Req("temperature.playerBreathC");
            _freezingThreshold = Req("temperature.freezingEvidence.thresholdC");
            _holdSeconds = Req("temperature.freezingEvidence.holdSecondsToConfirm");

            if (_roomFloorFreezing >= _roomTargetFreezing)
                throw new InvalidOperationException("温度配置不自洽：带证据鬼的下限必须低于其目标（[-8,-5] 的语义）");
            if (_roomTargetFreezing >= _roomFloorNormal)
                throw new InvalidOperationException("温度配置不自洽：带证据鬼最暖的一端必须比普通鬼最冷的一端还冷");
            if (_aroundCoolPerSec <= _roomCoolPerSec)
                throw new InvalidOperationException("温度配置不自洽：鬼周围（3m 内）必须比鬼房降得更陡，否则「突然一冷」的体感不成立");
        }

        public void RegisterRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return;
            if (!_temp.ContainsKey(roomId)) { _temp[roomId] = BaselineC; _belowFor[roomId] = 0f; }
        }

        public float TemperatureOf(string roomId)
            => (!string.IsNullOrEmpty(roomId) && _temp.TryGetValue(roomId, out float t)) ? t : BaselineC;

        public bool IsFreezingConfirmed(string roomId)
            => !string.IsNullOrEmpty(roomId) && _belowFor.TryGetValue(roomId, out float s) && s >= _holdSeconds;

        /// <summary>该鬼房的降温下限（带刺骨寒温证据的鬼更冷）。</summary>
        public float FloorFor(bool hasFreezingEvidence) => hasFreezingEvidence ? _roomFloorFreezing : _roomFloorNormal;
        /// <summary>该鬼房的降温目标。</summary>
        public float TargetFor(bool hasFreezingEvidence) => hasFreezingEvidence ? _roomTargetFreezing : _roomTargetNormal;
        /// <summary>鬼周围的降温半径（米）。</summary>
        public float AroundGhostRadiusM => _aroundRadiusM;
        /// <summary>玩家吐寒气阈值。</summary>
        public float BreathThresholdC => _breathC;

        /// <summary>
        /// 设置**鬼房**（一个设定，不随鬼移动而变）+ 该鬼是否带刺骨寒温证据。
        ///
        /// 【2026-10-04 语义修正 · 用户澄清】我此前把"鬼当前所在房间"当成鬼房，是错的：
        /// > 「不是鬼所处的房间就是鬼房，**鬼房是一个设定**」
        /// 现在鬼房与"鬼的位置"是**两件独立的事**：
        /// · 鬼房 = 恒冷的那个房间（`GhostRoomId`，由 `GhostRoom` 系统选定/更换）；
        /// · 鬼的位置 = 只产生**周围 3m 的瞬时降温**（`SetGhostPosition`）。
        /// </summary>
        public void SetGhostRoom(string ghostRoomId, bool hasFreezingEvidence)
        {
            GhostRoomId = ghostRoomId;
            GhostHasFreezingEvidence = hasFreezingEvidence;
        }

        /// <summary>
        /// 更新鬼的**实时位置**（只用于"鬼周围 3m 更陡的降温"）。
        /// 与鬼房无关 —— 鬼走到别的房间时，鬼房仍然恒冷。
        /// </summary>
        public void SetGhostPosition(float x, float z, bool insideItsRoom)
        {
            GhostX = x; GhostZ = z;
            GhostInsideItsRoom = insideItsRoom;
        }

        /// <summary>
        /// 推进 dt 秒。
        /// <paramref name="playerRoomId"/>：玩家所在房间（用于吐气判定）。
        /// <paramref name="playerNearGhost"/>：玩家是否在鬼周围半径内（则该处额外降温）。
        /// </summary>
        public void Tick(float dtSec, string playerRoomId = null, bool playerNearGhost = false)
        {
            FreezingJustConfirmed = false;
            if (dtSec <= 0f) return;

            float target = TargetFor(GhostHasFreezingEvidence);
            // 鬼**正在鬼房内**时降温更快（用户第 5 条）。注意判据是"鬼房"而不是"鬼在哪"：
            // 鬼房是设定，鬼回去了才会加速。
            bool ghostInItsRoom = string.Equals(GhostRoomId, _ghostCurrentRoom, StringComparison.Ordinal);
            float roomRate = _roomCoolPerSec * (ghostInItsRoom ? _inRoomMultiplier : 1f);

            var ids = new List<string>(_temp.Keys);
            foreach (var id in ids)
            {
                float cur = _temp[id];
                bool isGhostRoom = string.Equals(id, GhostRoomId, StringComparison.Ordinal);
                // 鬼周围的降温作用在"玩家所在房间 + 玩家靠近鬼"时叠加（体感：走到鬼边上突然一冷）
                bool extraAround = playerNearGhost && string.Equals(id, playerRoomId, StringComparison.Ordinal);

                float next;
                if (isGhostRoom)
                {
                    // 鬼房**恒冷**：一直朝目标降（鬼走了也不回暖 —— 它是"被设定过的房间"）
                    float floor = FloorFor(GhostHasFreezingEvidence);
                    next = MoveTowards(cur, target, roomRate * dtSec);
                    if (extraAround) next -= _aroundCoolPerSec * dtSec;
                    if (next < floor) next = floor;
                }
                else if (extraAround)
                {
                    // 非鬼房但玩家贴着鬼：也降（但下限取普通鬼下限，避免"到处都-8"）
                    next = MoveTowards(cur, _roomTargetNormal, _aroundCoolPerSec * dtSec);
                    if (next < _roomFloorNormal) next = _roomFloorNormal;
                }
                else
                {
                    // 其余房间：向基线**回升**（比降温慢）
                    next = MoveTowards(cur, BaselineC, _rewarmPerSec * dtSec);
                }
                _temp[id] = next;

                // 寒温证据防抖：连续低于阈值才累积；回到阈值以上即清零（"鬼路过"不算）
                float below = _belowFor[id];
                if (next < _freezingThreshold)
                {
                    bool was = below >= _holdSeconds;
                    below += dtSec;
                    if (!was && below >= _holdSeconds) FreezingJustConfirmed = true;
                }
                else below = 0f;
                _belowFor[id] = below;
            }

            // 玩家吐寒气：玩家所在处温度 < 阈值（用户第 4 条）
            float here = TemperatureOf(playerRoomId);
            if (playerNearGhost && here > _roomFloorNormal) here = _roomFloorNormal;   // 贴着鬼时按更冷的读数
            PlayerBreathing = here < _breathC;
        }

        static float MoveTowards(float cur, float target, float maxDelta)
        {
            float d = target - cur;
            if (Math.Abs(d) <= maxDelta) return target;
            return cur + (d > 0f ? maxDelta : -maxDelta);
        }

        float Req(string path)
        {
            var raw = GameConfig.Get(path);
            if (raw is long l) return l;
            if (raw is double d) return (float)d;
            if (raw is int i) return i;
            throw new InvalidOperationException($"配置缺 {path} —— 温度机制不许硬编码数值");
        }
    }
}
