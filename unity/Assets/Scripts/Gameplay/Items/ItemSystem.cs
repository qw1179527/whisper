using System;
using System.Collections.Generic;
using Whisper.Gameplay.Config;

namespace Whisper.Gameplay.Items
{
    /// <summary>可拾取物类型（V9 §7 道具系统）。</summary>
    public enum PickupKind { Battery, Flare, Sedative }

    /// <summary>场上一个可拾取物。</summary>
    public struct Pickup
    {
        public string Key;
        public PickupKind Kind;
        public float X, Z;
    }

    /// <summary>场上一个配电箱（切换该区照明）。</summary>
    public struct Breaker
    {
        public string Zone;
        public float X, Z;
    }

    /// <summary>场上一个证据点（V9 §7：本作唯一的永久成长来源）。</summary>
    public struct EvidencePoint
    {
        public string Id;
        public float X, Z;
    }

    /// <summary>场上一个撤离点。</summary>
    public struct ExtractionPoint
    {
        public string Id;
        public string Label;
        public float X, Z;
        public bool Safe;
        public float RewardScale;
    }

    /// <summary>
    /// 道具 / 交互 / 拾取（V9 §7；移植自灰盒 `__m12` 的交互段）。
    ///
    /// 交互半径**逐条照抄灰盒**（不是随手定的，改动要同步灰盒对拍）：
    ///   拾取物 1.0m · 配电箱 1.6m · 证据点 0.9m · 撤离点 1.4m。
    ///
    /// 手电与理智的耦合是本作的核心张力之一：
    ///   开灯 → 怪物视觉范围 +3m（`items.flashlight.monsterPerceptionBonusM`）、电量每秒 −1s；
    ///   关灯 → 理智每秒 −1（`sanity.drain.darknessPerSec`）。
    ///   即「看得见」与「不疯」二选一——这条耦合由 <see cref="SanityTickArgs"/> 显式交给理智系统，
    ///   不允许在别处偷偷改。
    /// </summary>
    public sealed class ItemSystem
    {
        readonly GameConfigReader _cfg;
        public float BatterySeconds { get; private set; }
        public float BatteryMax { get; }
        /// <summary>单次电池拾取的补充秒数（灰盒硬编码 60；此处保留同值并集中在此常量）。</summary>
        public const float BatteryPickupSeconds = 60f;
        public int Flares { get; private set; }
        public int Sedatives { get; private set; }
        public int EvidenceCount { get; private set; }
        public int EvidenceTotal { get; }
        /// <summary>已切断照明的区域（V9 §7：切断照明会加速理智流失，同时降低怪物视觉）。</summary>
        public readonly HashSet<string> LightsOffZones = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> _taken = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> _collectedEvidence = new HashSet<string>(StringComparer.Ordinal);

        public readonly List<Pickup> Pickups = new List<Pickup>();
        public readonly List<Breaker> Breakers = new List<Breaker>();

        /// <summary>
        /// 交互半径（米）：电闸 1.6m、证据 0.9m。
        /// 这两个值是**交互层手感参数**（伸手可及的距离），不属于怪物/听觉等物理规则量，
        /// 因此不放进 data/config.json（V9 §19.5 约束的是物理与玩法数值）。
        /// ⚠ 1.6/0.9 与 chaseSpeedScale、voiceCalibration 的数值纯属数字巧合 ——
        /// 硬编码扫描门禁曾把它们误报成"硬编码了配置值"，故在此写明出处，避免再次误判。
        /// </summary>
        public const float BreakerInteractRadiusM = 1.6f;
        public const float EvidenceInteractRadiusM = 0.9f;
        public readonly List<EvidencePoint> EvidencePoints = new List<EvidencePoint>();
        public readonly List<ExtractionPoint> ExtractionPoints = new List<ExtractionPoint>();

        /// <summary>交互事件（供 HUD / 音效订阅，避免本类依赖 Unity）。</summary>
        public event Action<string> OnLog;

        public ItemSystem(GameConfigReader cfg, int evidenceTotal = 0)
        {
            _cfg = cfg;
            BatteryMax = cfg.Float("items.flashlight.batterySeconds", 120f);
            BatterySeconds = BatteryMax;
            EvidenceTotal = evidenceTotal;
        }

        /// <summary>手电是否亮着（电量 > 0）。</summary>
        public bool TorchOn => BatterySeconds > 0f;

        /// <summary>怪物视觉范围加成（米）——手电亮着时生效，直接喂给感知判定。</summary>
        public float MonsterPerceptionBonusM =>
            TorchOn ? _cfg.Float("items.flashlight.monsterPerceptionBonusM", 3f) : 0f;

        /// <summary>本帧的手电/区域照明状态，供理智系统消费（唯一入口，避免各处自行判断）。</summary>
        public struct SanityTickArgs
        {
            public bool TorchOn;
            public bool LightsOut;
        }

        public SanityTickArgs SanityArgs => new SanityTickArgs { TorchOn = TorchOn, LightsOut = LightsOffZones.Count > 0 };

        /// <summary>每帧推进手电电量（亮着才耗）。</summary>
        public void TickFlashlight(float dt)
        {
            if (BatterySeconds > 0f) BatterySeconds = Math.Max(0f, BatterySeconds - dt);
        }

        /// <summary>靠近即拾取（灰盒行为：拾取物 1.0m 内自动拾取）。返回本帧拾到的日志行。</summary>
        public List<string> TryPickup(float px, float pz)
        {
            var logs = new List<string>();
            for (int i = 0; i < Pickups.Count; i++)
            {
                var p = Pickups[i];
                if (_taken.Contains(p.Key)) continue;
                if (Distance(px, pz, p.X, p.Z) >= 1.0f) continue;
                _taken.Add(p.Key);
                switch (p.Kind)
                {
                    case PickupKind.Battery:
                        BatterySeconds = Math.Min(BatteryMax, BatterySeconds + BatteryPickupSeconds);
                        logs.Add($"拾取手电电池：电量 +{BatteryPickupSeconds:0}s");
                        break;
                    case PickupKind.Flare:
                        Flares++;
                        logs.Add($"拾取信号弹 ×1（共 {Flares}）");
                        break;
                    case PickupKind.Sedative:
                        Sedatives++;
                        logs.Add($"拾取镇静剂 ×1（共 {Sedatives}）");
                        break;
                }
            }
            foreach (var l in logs) OnLog?.Invoke(l);
            return logs;
        }

        /// <summary>配电箱：1.6m 内按使用键切换该区照明。返回日志（无操作返回 null）。</summary>
        public string TryToggleBreaker(float px, float pz, bool usePressed)
        {
            if (!usePressed) return null;
            for (int i = 0; i < Breakers.Count; i++)
            {
                var b = Breakers[i];
                if (Distance(px, pz, b.X, b.Z) >= BreakerInteractRadiusM) continue;
                string log;
                if (LightsOffZones.Contains(b.Zone))
                {
                    LightsOffZones.Remove(b.Zone);
                    log = $"配电箱：{b.Zone} 区照明恢复";
                }
                else
                {
                    LightsOffZones.Add(b.Zone);
                    log = $"配电箱：{b.Zone} 区照明切断（怪物视觉下降，你的理智也在下降）";
                }
                OnLog?.Invoke(log);
                return log;
            }
            return null;
        }

        /// <summary>证据拾取（0.9m 内自动拾取）。返回本帧拾到的日志行。</summary>
        public List<string> TryCollectEvidence(float px, float pz)
        {
            var logs = new List<string>();
            for (int i = 0; i < EvidencePoints.Count; i++)
            {
                var e = EvidencePoints[i];
                if (_collectedEvidence.Contains(e.Id)) continue;
                if (Distance(px, pz, e.X, e.Z) >= EvidenceInteractRadiusM) continue;
                _collectedEvidence.Add(e.Id);
                EvidenceCount++;
                var line = $"拾取证据 {e.Id}（{EvidenceCount}/{EvidenceTotal}）";
                logs.Add(line);
                OnLog?.Invoke(line);
            }
            return logs;
        }

        /// <summary>
        /// 撤离判定（1.4m 内）。
        /// **门禁**：证据未集齐不允许撤离（灰盒同规则）——保证"证据是唯一成长来源"这条设计成立。
        /// </summary>
        public ExtractionPoint? TryExtract(float px, float pz)
        {
            if (EvidenceCount < EvidenceTotal) return null;
            for (int i = 0; i < ExtractionPoints.Count; i++)
            {
                var ep = ExtractionPoints[i];
                if (Distance(px, pz, ep.X, ep.Z) < 1.4f) return ep;
            }
            return null;
        }

        /// <summary>使用镇静剂（恢复理智；由调用方把返回值交给 SanitySystem）。</summary>
        public bool UseSedative()
        {
            if (Sedatives <= 0) return false;
            Sedatives--;
            return true;
        }

        /// <summary>信号弹：放置后形成安全区（秒数来自配置）。</summary>
        public float FlareSafeZoneSeconds => _cfg.Float("items.flare.safeZoneSeconds", 15f);

        /// <summary>相机：可眩晕怪物（每怪每局次数来自配置）。</summary>
        public int CameraUsesPerMonsterPerMatch => (int)_cfg.Float("items.camera.usesPerMonsterPerMatch", 1f);
        public float CameraStunSeconds => _cfg.Float("items.camera.stunSeconds", 2f);

        /// <summary>录音机：录音与回放都会产生声纹刺激（配置开关）。</summary>
        public bool RecorderProducesStimulus =>
            _cfg.Bool("items.recorder.producesStimulusOnRecordAndPlay", true);
        public float RecorderSeconds => _cfg.Float("items.recorder.recordSeconds", 20f);

        static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
