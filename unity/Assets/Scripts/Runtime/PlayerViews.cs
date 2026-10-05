using System.Collections.Generic;
using UnityEngine;
using Whisper.Core;
using Whisper.Core.Contracts;

namespace Whisper.Runtime
{
    /// <summary>
    /// 联机玩家视图：**上行发送本机位姿** + **把远端玩家画出来**。
    ///
    /// ## 为什么这一个组件是"可开黑"的咽喉
    /// 实测（2026-10-05）发现联机两端各断一半，而且**两半都没有任何门禁能发现**：
    ///   ① **上行没人调用** —— `INetService.SendLocalPlayer` 早就定义好、两个实现也实现了
    ///      （`LocalNetService` / `UdpV6NetService`），但**没有任何玩法代码调用它**（全仓 grep 只有契约与实现）。
    ///      后果：我动得再欢，对面也收不到我的位姿。
    ///   ② **下行没人渲染** —— 快照里的远端玩家只会被写进实现类的 `_players` 列表，
    ///      `Runtime/` 下只有 `MonsterViews`（怪），没有任何代码把玩家画到世界里。
    ///      后果：即使收到位姿，屏幕上也看不见人。
    /// 两半合起来就是"连上了却像单机"。本组件同时补上：<see cref="Pump"/> 负责 ①，远端身体负责 ②。
    ///
    /// ## 复用既有件，不另起炉灶
    /// 远端身体直接用 <see cref="PlayerBody"/> + `ModelLibrary.InstantiateWhole("player", …)`，
    /// 与玩家自己的第一人称身体同源；远端一律**第三人称**（`SetFirstPerson(false)` 显示头/躯干，
    /// 否则别人的头是隐形的）。
    ///
    /// ## 坐标约定（与 LevelGeometry / 关卡 DSL 一致）
    /// XZ 是水平面、Y 是高度。本机相机眼高 `PlayerController.EyeHeightM`，而身体模型自带贴地标定
    /// （见 `PlayerBody.ApplyEyeAlignment`），故**根节点放在地面 y=0**，不要再加眼高，
    /// 否则远端会浮在半空。
    /// </summary>
    public sealed class PlayerViews : MonoBehaviour
    {
        /// <summary>本机玩家 id。联机实现里主机占槽 0 用的就是 "local"（见 UdpV6NetService.TryStart）。</summary>
        public const string LocalPlayerId = "local";

        PlayerController _player;
        /// <summary>远端玩家身体（按 PlayerId 索引）。</summary>
        readonly Dictionary<string, GameObject> _bodies = new Dictionary<string, GameObject>();
        float _sanity01 = 1f;

        /// <summary>已建立的远端身体数（HUD/自检可核）。</summary>
        public int RemoteCount => _bodies.Count;
        /// <summary>已发送的本机快照数（用于判定上行是否真的在跑）。</summary>
        public long SentFrames { get; private set; }
        /// <summary>最近一次发送的位姿（HUD 取证用）。</summary>
        public string LastSent { get; private set; } = "-";

        public void Initialize(PlayerController player)
        {
            _player = player;
        }

        /// <summary>理智值由理智系统写入（0..1）；仅用于快照上报，不参与本地判定。</summary>
        public void SetSanity01(float sanity01)
        {
            _sanity01 = sanity01 < 0f ? 0f : (sanity01 > 1f ? 1f : sanity01);
        }

        void Update()
        {
            Pump();
            SyncRemoteBodies();
        }

        /// <summary>
        /// ① 上行：把本机位姿交给联机服务（发送频率由实现内部按 `network.transformSendHz` 节流，
        /// 故这里每帧调用是正确用法，不要自己再限流 —— 那会和实现内部的节流叠成两套口径）。
        /// </summary>
        public void Pump()
        {
            if (_player == null) return;
            if (!Services.HasNet) return;
            var m = _player.Motion;
            float yaw = m != null ? m.YawDeg : 0f;
            var snap = new PlayerSnapshot(LocalPlayerId, _player.X, 0f, _player.Z, yaw, true, _sanity01);
            Services.Net.SendLocalPlayer(snap);
            SentFrames++;
            LastSent = $"({snap.X:0.00}, {snap.Z:0.00}) yaw {snap.Yaw:0}";
        }

        /// <summary>② 下行：按快照增删改远端身体。</summary>
        public void SyncRemoteBodies()
        {
            if (!Services.HasNet) return;
            var snap = Services.Net.Snapshot;
            var players = snap?.Players;
            if (players == null) return;

            // 收集本帧见到的远端 id，用于回收已离开的玩家
            var seen = new List<string>(players.Count);
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p.IsLocal) continue;                    // 自己的身体由 PlayerController/PlayerBody 负责
                if (p.PlayerId == LocalPlayerId) continue;   // 兜底：实现里"local"就是本机
                seen.Add(p.PlayerId);
                var go = EnsureBody(p.PlayerId);
                if (go == null) continue;
                // 远端是**第三人称**：必须看得见头与躯干，否则"看不见彼此"
                go.transform.position = new Vector3(p.X, 0f, p.Z);
                go.transform.rotation = Quaternion.Euler(0f, p.Yaw, 0f);
            }
            if (seen.Count != _bodies.Count) RemoveMissing(seen);
        }

        GameObject EnsureBody(string playerId)
        {
            if (_bodies.TryGetValue(playerId, out var existing) && existing != null) return existing;

            var root = new GameObject("RemotePlayer_" + playerId);
            root.transform.SetParent(transform, false);
            var body = root.AddComponent<PlayerBody>();
            // 远端：第一人称的"藏头藏躯干"在这里必须关掉，否则别人是隐形的
            body.HideHeadInFirstPerson = true;
            body.HideTorsoInFirstPerson = true;
            body.HideNeckInFirstPerson = true;
            body.Build(root.transform);
            body.SetFirstPerson(false);
            _bodies[playerId] = root;
            return root;
        }

        void RemoveMissing(List<string> seen)
        {
            var dead = new List<string>();
            foreach (var kv in _bodies)
            {
                if (seen.Contains(kv.Key)) continue;
                dead.Add(kv.Key);
            }
            for (int i = 0; i < dead.Count; i++)
            {
                var go = _bodies[dead[i]];
                if (go != null) Destroy(go);
                _bodies.Remove(dead[i]);
            }
        }

        /// <summary>HUD 摘要（真机取证用：能看到"我发了多少帧 / 看到几个远端"）。</summary>
        public string Describe()
            => $"联机视图：已发 {SentFrames} 帧 · 最近 {LastSent} · 远端可见 {RemoteCount} 人";
    }
}
