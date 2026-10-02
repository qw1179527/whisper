# 仓库约定与接口契约（喂入包 · AI 常驻上下文）

> 铁律（V9 §27.1）：AI 书写的任何第三方 API 调用**必须能在喂入包里找到出处**，找不到就提问而不是编造。

## 1. 三接口（定义在 `Assets/Scripts/Core/Contracts/`，状态：**契约已定 · 实现待接**）

```csharp
namespace Whisper.Core.Contracts
{
    /// V9 §13.2：玩法代码只依赖接口；SDK 实现类是唯一允许 import 第三方命名空间的位置。
    public interface INetService
    {
        bool IsHost { get; }
        bool IsConnected { get; }
        int  TickRate { get; }                       // 固定 60（V9 §13.4）
        void Connect(string roomCode, string authToken);
        void Disconnect();
        void SendVoiceStimulus(in StimulusEvent stimulus);   // 声纹事件：瞬时 RPC，不走状态同步
        event System.Action<string> OnRoomClosed;
        /// Host 迁移开始/结束（V9 §13.4：RTT>200ms 持续 10 秒或 Host 退出时迁移；迁移期播「信号干扰」遮罩 2~3 秒）
        event System.Action<bool> OnHostMigration;

        // ── 只读状态面（V9 §13.4 四类同步对象的 ①③④）──
        // 为什么必须在接口上：Gameplay 按 §13.1 禁止引用 Net，若状态不经接口暴露，
        // 玩法代码就永远拿不到「玩家位置 / 门与道具状态 / 对局阶段」。
        MatchPhase Phase { get; }                       // ④ 对局阶段
        NetworkSnapshot Snapshot { get; }               // ①③④ 只读快照（玩家/道具/阶段/证据/世界哈希）
        event System.Action<MatchPhase> OnPhaseChanged;      // ④ 变更通知
        event System.Action<PropState> OnPropChanged;        // ③ 变更通知
        event System.Action<PlayerSnapshot> OnPlayerUpdated; // ① 位姿更新（10Hz 插值结果）
    }

    // 配套值类型（Core/Contracts/MatchState.cs）
    public enum MatchPhase { Lobby, Loading, Playing, Extraction, Ended }
    public readonly struct PlayerSnapshot { /* PlayerId, X/Y/Z, Yaw, IsLocal, Sanity01 */ }
    public readonly struct PropState { /* PropId, Open, Locked, Charge01 */ }
    public sealed class NetworkSnapshot { /* Phase, Players, Props, EvidenceCount, WorldHash */ }

    public interface IVoiceService
    {
        bool IsMuted { get; }
        float LocalEnergy01 { get; }                 // 本地参与者音频能量（V9 §13.5 采集源优先级 ①）
        void JoinChannel(string channelName);         // 频道名 = 房间码 + 局序号
        void LeaveChannel();
        void SetLocalMute(string participantId, bool muted);   // 屏蔽用 LocalMute
        event System.Action<string, float> OnParticipantEnergy;
    }

    public interface IBackendService
    {
        bool IsAvailable { get; }                    // Firebase 不可达 → 无后端模式，联机与语音不受影响
        void IssueTokens(string appVersion, System.Action<TokenBundle> onOk, System.Action<string> onFail);
        void VerifyPurchase(string purchaseToken, string productId, System.Action<bool> onDone);
        void SubmitReport(string category, string matchId, System.Action<bool> onDone);
    }
}
```

**判定规则**：`Assets/Scripts/Gameplay/`、`Assets/Scripts/UI/` 下出现 `using Photon` / `using Unity.Services.Vivox` / `using Firebase` 一律 fail（`tools/arch-guard.mjs`）。

## 2. EventBus<T>（Core，跨模块通信唯一通道）

V9 §13.1：跨模块通信统一走类型安全事件总线 `EventBus<T>`，**快照遍历防迭代增删**（发布中订阅/退订不得影响本轮派发）。

用法约定：
- 发布：`EventBus<PlayerDiedEvent>.Publish(in evt)`
- 订阅：`EventBus<PlayerDiedEvent>.Subscribe(handler)`，**必须在 `Dispose`/`OnDisable` 里退订**
- 禁止：模块间直接互相持有引用（除 Core 提供的接口）

## 3. Level DSL schema（V9 §19.2，关卡即数据）

```jsonc
{
  "levelId": "asylum_v1",
  "rooms": [{
    "id": "ward_03",
    "pos": [9, 8],                             // 房间中心 [x, z]（米，XZ 平面）—— D1 修复后为必需
    "size": [2, 3.5, 4],                       // [宽, 高, 深] 米
    "rotY": 0,                                 // 绕 Y 旋转（度）；当前几何校验只支持 0（轴对齐）
    "floor": 0,                                // 楼层；跨层走廊暂不支持
    "kit": "hospital_ward",                    // 套件库 ID（asset-manifest.json 索引，kind 必须为 room）
    "doors": [{ "id": "d_south", "wall": "south", "offset": 0.25, "locked": false }],
    "props": [{ "kit": "bed_b", "pos": [1.2, 0, -2.0], "rot": 90 }],
    "evidencePoint": true,
    "lightZone": "pressure"                    // safe | pressure | high-risk（V9 §11 动态光）
  }],
  "corridors": [{
    "from": "ward_03", "to": "corridor_ward",
    "doorA": "ward_03/d_south", "doorB": "corridor_ward/d_n3",   // 必须引用两端的具体门（D1）
    "width": 1.6
  }],
  "events": [{
    "type": "blackout", "minute": 6, "durationSec": 10,
    "params": { "scope": "ward_zone" }, "sanityEffect": -3,
    "counterplay": "手电筒照走廊地面确认出口；黑暗持续掉理智"      // V9 §30.2 硬要求
  }]
}
```

校验：`tools/validate-levels.mjs`（schema + 引用完整性：kit ID 必须存在于 asset-manifest）。

## 4. 目录与命名（摘要，全文见 `unity/docs/repo-conventions.md`）

- 模块 = asmdef 名 = 目录名（`Whisper.Core` / `Assets/Scripts/Core`）；
- 一个文件一个类型（除嵌套私有类型）；文件名 = 类型名；
- 配置表：`Assets/Data/config.json` 是**运行时唯一数值真源**，代码内不得硬编码数值；
- 设计 Token：`Assets/Data/design-tokens.json` → 生成 `DesignTokens` 静态类（C2 要求"设计与代码一一对应"）。

## 4.1 文档↔代码一致性（独立验证轨 S2 指出后补的纪律）

契约文档曾漏写 `INetService.OnHostMigration`（V9 §13.4 明确要求 Host 迁移）。
**两者不一致以 .cs 为准、文档必须跟着改**；每次改动接口后同步本文件。
（当前尚无机器校验覆盖"文档 ↔ .cs 成员集合"，属已知缺口，记入机制清单。）

## 4.2 DSL 语义裁决记录（避免两侧校验器强度漂移）

| 字段 | 裁决 | 理由 |
|---|---|---|
| `doors[].offset` | **可选，缺省 0**（0..1，越界报错） | 独立验证轨 D4 指出 Node 曾要求"必须 number"而 C# 缺省 0f，属"CI 红 / Unity 正常"的反向漂移。取更宽松的一侧并写进 schema。 |
| `extraction` | **可选**；给了就必须双点存在且不同 | 两侧实现都支持，但 V9 §19.2 示例与本文原 schema 都漏写 → 已补进 §3。 |
| 动态事件数量 2~3 | 保留（>0 时） | **出处更正**：来自 V9 §5/§11，不是 §19.2（旧注释误标，已改）。 |
| 事件类型 | **当前闭集 6 型**（blackout/doorlock/static/mirror/overload/laugh） | ⚠ 与 V9 §30.2「AI 生成 5 个新类型」冲突，**待人类裁决**：改为「6 内建 + 命名空间前缀扩展」白名单，并给 EventDef 补 `params/sanityEffect/counterplay`。 |
| `rooms[].pos` / `rotY` / `floor` | **已补**（D1 修复） | 房间有中心坐标与楼层，LevelBuilder 可实例化几何 |
| `doors[].id` + `corridors[].doorA/doorB` | **已补**（D1 修复） | 走廊引用两端具体门；校验器强制「同轴 / 共墙 / 对开法向 / 开口对齐」 |
| 房间重叠 | **已加校验** | 同层房间在 XZ 上不得重叠 |
| 跨层走廊（stairs） | **仍缺**（已知缺口） | V9 的 morgue 是「地下」，但当前不支持跨层墙；本版以 floor:0 + 位置/命名表达，待 stair 能力实现 |

## 5. 未填充项（**故意留白，不得编造**）

| 文件 | 需要填充什么 | 从哪里取 |
|---|---|---|
| `fusion2-api.md` | Fusion 2 当前锁定版的 `NetworkBehaviour` / `[Networked]` / `Rpc` / Host 迁移 API 摘要 | Photon 官方文档（锁定版本对应的那一版） |
| `vivox16-api.md` | Vivox 16.x 登录 / 频道 / 能量回调 / `LocalMute` / external audio input | Unity Vivox 官方文档 |
| `firebase-fn.md` | Firebase Functions(Node 20) + Play Developer API + RTDN 摘要 | Firebase / Google Play 官方文档 |

**维护时机**：每月偿债日固定动作 = 检查四个 SDK 的 changelog 并更新喂入包（4 小时，V9 §27.1）。
