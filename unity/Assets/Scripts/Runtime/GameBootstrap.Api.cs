using UnityEngine;
using Whisper.Gameplay.Progression;   // Progression / Shop / TaskSystem（等级-商店-任务，恐鬼症对齐）
using Whisper.Gameplay.Objectives;    // ObjectiveSystem（**局内任务**：合同日志里的可选目标，与每日任务是两套）
using Whisper.Net.Direct;   // LanSession / LanAddress / RoomCode（零信令直连层）
using UnityEngine.UI;
using Whisper.Core;
using Whisper.Core.Contracts;
using Whisper.Gameplay.Config;
using Whisper.Gameplay.Level;
using Whisper.Net;
using Whisper.Audio;
using Whisper.Backend;

namespace Whisper.Runtime
{
    // 2026-10-06 从 GameBootstrap.cs 拆出（gate-code C5：单文件 ≤600 行）。
    // 拆的是**内聚的一块**（公共 API 与描述），不是按行数硬切：SelectMap/DescribeMaps/菜单与惊吓回调/Describe* —— 都是"对外说话"的一组。
    // 用项目既有的 partial 惯例（同 GameBootstrap.Spawn.cs / .Temperature.cs）。
    public sealed partial class GameBootstrap
    {
        public bool SelectMap(string mapId)
        {
            if (string.IsNullOrEmpty(mapId)) return false;
            var list = GameConfig.Get("maps.list") as System.Collections.Generic.List<object>;
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                var m = MiniJson.AsMap(list[i]);
                if (m == null) continue;
                var id = MiniJson.AsString(MiniJson.Get(m, "id"));
                if (id != mapId) continue;
                var res = MiniJson.AsString(MiniJson.Get(m, "resource"));
                if (string.IsNullOrEmpty(res)) return false;
                LevelResourcePath = res;
                MapId = id;
                return true;
            }
            return false;
        }

        /// <summary>地图注册表摘要（地图板/HUD 用；明确标出"哪些已实现"）。</summary>
        public string DescribeMaps()
        {
            var list = GameConfig.Get("maps.list") as System.Collections.Generic.List<object>;
            if (list == null) return "地图注册表缺失";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                var m = MiniJson.AsMap(list[i]);
                if (m == null) continue;
                var id = MiniJson.AsString(MiniJson.Get(m, "id"));
                var name = MiniJson.AsString(MiniJson.Get(m, "name"));
                bool impl = MiniJson.AsBool(MiniJson.Get(m, "implemented"));
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(name).Append("(").Append(id).Append(")");
                if (id == MapId) sb.Append("★");
                if (!impl) sb.Append("（未实现）");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 联机意图：**默认单人**（行为与接入前完全一致 —— 不开 socket，走本机回环桩）。
        /// 菜单里点「多人联机」= Host（建房），点「加入房间」= Join（用 <see cref="NetJoinCode"/>）。
        /// 之所以做成显式意图而不是"自动联机"：真实现会 bind 端口，不该在大厅里偷偷开。
        /// </summary>
        public NetIntentKind NetIntent = NetIntentKind.Solo;

        /// <summary>加入时要输入的房间码（仅 <see cref="NetIntentKind.Join"/> 用）。</summary>
        public string NetJoinCode;

        /// <summary>建房成功后要发给朋友的房间码（UI 显示在菜单板右上角）；未建房为 null。</summary>
        public string NetRoomCode => LanSession.NetRoomCode;

        /// <summary>
        /// 货车安全区（世界 AABB）。**唯一数据源**是 `_hall.TruckSafeZone`。
        /// 用户《补充说明》§8：「货车及其周围是安全区：鬼无法进入，在车内不掉理智」。
        /// 暴露在组合根是为了**单一口径** —— 撤离判定、理智保护、怪物寻路都读它，
        /// 不再各自算一遍（本项目在 UI 与关卡生成器上已多次吃过"两套口径"的亏）。
        /// </summary>
        /// <summary>货车安全区数据（由 MenuScene 推送，见 SetTruckSafeZone）。默认在地底且零尺寸。</summary>
        Bounds _truckSafeZone = new Bounds(new Vector3(0f, -1000f, 0f), Vector3.zero);

        public Bounds TruckSafeZone => _truckSafeZone;

        /// <summary>
        /// 由**持有 HallScene 的一方**（MenuScene）把货车安全区推给组合根。
        /// 为什么用推送而不是让 GameBootstrap 去抓：`GameBootstrap` 没有 HallScene 引用
        /// （全仓只有 `MenuScene.cs:82 HallScene _hall;`），让它去抓等于反向依赖 UI 层、破坏分层。
        /// 推送的是**纯数据**（世界 AABB），玩法层因此不依赖任何几何实现。
        /// </summary>
        public void SetTruckSafeZone(Bounds zone)
        {
            _truckSafeZone = zone;
            // ⚠ 这里**无法**转交给玩法层：`GameBootstrap` 没有 `GameSession` 字段
            // （实测 grep 全仓 `GameSession` 仅 3 处命中，全在 GameSession.cs 自己文件里
            //  —— 即 **GameSession 全仓零实例化**，与"玩法层从未接进对局"是同一件事）。
            // 故此处只保存 Bounds；转交裸浮点给玩法层的逻辑，留到"玩法层接线"那一轮统一做
            // （见 docs/truck-and-session-wiring.md 的 P0 条目）。
            // 玩法层收到的是**裸浮点**而非 Bounds/Vector3：`Whisper.Gameplay` 不引用 UnityEngine。
        }

        /// <summary>玩家是否在货车安全区内（§8：安全区不掉理智、鬼进不来）。</summary>
        public bool PlayerInTruckSafeZone
        {
            get
            {
                if (_player == null) return false;
                var b = TruckSafeZone;
                if (b.size.x <= 0f || b.size.z <= 0f) return false;   // 货车未建 → 不保护（fail-safe）
                float dx = Mathf.Abs(_player.X - b.center.x);
                float dz = Mathf.Abs(_player.Z - b.center.z);
                return dx <= b.size.x * 0.5f && dz <= b.size.z * 0.5f;
            }
        }

        /// <summary>联机装配结果的一句话说明（失败原因也在这里，便于真机 HUD 取证）。</summary>
        public string NetStatus { get; private set; } = "单人模式";

        [Tooltip("配置表资源路径（数值唯一真源）")]
        public string ConfigResourcePath = "Data/config";

        /// <summary>主界面请求开始对局（由 MenuScene 调用）。</summary>
        /// <remarks>
        /// 为什么做成公开方法而不是让 MenuScene 直接操作本对象的字段：
        /// 组合根是**唯一**知道"对局怎么开始"的地方（要关主界面、要保证玩家/怪物/HUD 都就绪）。
        /// 主界面只负责"用户点了什么"，不负责"怎么开局" —— 否则两处都要改。
        /// </remarks>
        public void OnMenuStartRequested()
        {
            // 大厅里选的联机意图在这里才生效（Boot 时装的还是单人桩）——见 ApplyNetIntentAtMatchStart 注释。
            ApplyNetIntentAtMatchStart();
            if (_menu != null) _menu.gameObject.SetActive(false);
            // 猎杀/死亡状态复位：重开一局不能带着上一局的红闪与文字
            if (_jumpscare != null) _jumpscare.Reset();
            // 真正的对局从这里才建（玩家/怪物/跳脸）—— Boot 阶段只到"关卡已加载 + 主界面已出"。
            StartMatch();
        }

        /// <summary>猎杀致死入口（供怪物/网络层调用）：播跳脸。</summary>
        public void OnPlayerKilled(bool redEyes)
        {
            if (_jumpscare == null) return;
            _jumpscare.RedEyes = redEyes;
            _jumpscare.Play();
        }

        /// <summary>
        /// 被鬼**抓到致死**时的表现层入口（订阅 <c>GameSession.PlayerKilled</c>）。
        ///
        /// ## 与 <see cref="OnPlayerKilled"/> 的分工
        /// 那个是"直接命令播跳脸"（联机/脚本/测试用）；本方法是**玩法层判定之后的落点**：
        /// 它只做表现层的事 —— 定红眼/白眼、按配置校准跳脸的时长与距离。
        ///
        /// ## 为什么红眼要按怪查配置
        /// 首版 <c>JumpscareView.RedEyes</c> 注释写的是"随机取"。但"哪只鬼红眼"是可调的
        /// 美术取舍 → 进 `death.caught.redEyeWhenKilledBy`，而不是在代码里写 if。
        /// 这样换配色不用改逻辑，也符合本仓"数值不落代码"的纪律。
        /// </summary>
        void OnPlayerCaught(string monsterId)
        {
            if (_jumpscare == null)
            {
                // 不静默：被判死却没有跳脸，必须留下可查的痕迹（本项目"静默失效"教训）
                Debug.LogWarning("[Whisper] 玩家被抓到，但 JumpscareView 未构建 —— 跳脸不会播放");
                return;
            }
            bool red = false;
            if (_cfg != null)
            {
                var arr = _cfg.Get("death.caught.redEyeWhenKilledBy", null) as System.Collections.IList;
                if (arr != null)
                {
                    for (int i = 0; i < arr.Count; i++)
                    {
                        if (string.Equals(arr[i] as string, monsterId, System.StringComparison.Ordinal)) { red = true; break; }
                    }
                }
                _jumpscare.DurationSec = _cfg.Float("death.caught.jumpscareDurationSec", _jumpscare.DurationSec);
                _jumpscare.StartDistM = _cfg.Float("death.caught.jumpscareStartDistM", _jumpscare.StartDistM);
                _jumpscare.EndDistM = _cfg.Float("death.caught.jumpscareEndDistM", _jumpscare.EndDistM);
            }
            OnPlayerKilled(red);
            Debug.Log($"[Whisper] 被 {monsterId} 抓到（距离 {Session?.LastCaughtDistanceM:0.00}m）→ 跳脸 · 红眼={red}");
        }

        /// <summary>HUD/自检用：主界面与跳脸状态。</summary>
        public string DescribeMenuAndScare()
            => (_menu != null ? _menu.Describe() : "主界面：未构建") + " · "
             + (_jumpscare != null ? _jumpscare.Describe() : "跳脸：未构建");

        /// <summary>等级档案（主界面与结算读写）。</summary>
        public Progression Progression => _progression;
        /// <summary>商店（主界面买/装备）。</summary>
        public Shop Shop => _shop;
        /// <summary>任务（主界面任务板）。</summary>
        public TaskSystem Tasks => _tasks;
        /// <summary>渲染质量档位（主界面可切）。</summary>
        public Whisper.Gameplay.Render.RenderQuality Quality => _quality;
        /// <summary>后处理（HUD 取证用）。</summary>
        public PostFx PostFx => _postFx;
        /// <summary>局内任务（合同日志）。
        public ObjectiveSystem Objectives => _objectives;
        /// <summary>电力。</summary>
        public Whisper.Gameplay.Power.PowerSystem Power => _power;
        /// <summary>互动。</summary>
        public Whisper.Gameplay.Interaction.InteractionSystem Interaction => _interaction;

        public LevelData Level { get; private set; }

        /// <summary>
        /// **玩法层**（理智→猎杀→取证→撤离）。第 17 轮发现它此前**全仓零实例化**，
        /// 本类第一次把它接进对局。接线策略见 wire-game-session.mjs 头注释：
        /// 先并行驱动 + 可观测，再逐项替换本类的简化逻辑（避免"接线了但没验证"）。
        /// </summary>
        public Whisper.Gameplay.Session.GameSession Session { get; private set; }

        /// <summary>
        /// 玩法层状态一行文本（**单一口径**：HUD / 诊断面板 / 将来的结算页都读它）。
        /// 为什么必须有它：接线之后如果没人能看见它在跑，就等于没接线 —— 本项目已有
        /// SendLocalPlayer 零调用者、UdpV6NetService 从未构造、MenuScene 键盘检查挂在条件链里
        /// 从未生效这三条同类前科。判据：**理智数字随时间变化**，才证明 Tick 真被调用。
        /// </summary>
        public string SessionStatus
        {
            get
            {
                if (Session == null) return "玩法层：**未接线**（理智/猎杀/撤离不会推进）";
                var so = Session.Outcome;
                return "玩法层 已推进 " + so.ElapsedSeconds.ToString("F1") + "s"
                     + " · 理智 " + (Session.Sanity.Value * 100f).ToString("F1") + "%"
                     + "（" + Session.Sanity.Band.Label + "）"
                     + " · 证据 " + so.EvidenceCollected + "/" + so.EvidenceTotal
                     + " · 阶段 " + Session.Director.Stage
                     + (so.Ended ? " · 已结束" : "");
            }
        }
        /// <summary>配置读取器（与 Progression/Shop/TaskSystem 共用同一份，避免两套配置口径）。</summary>
        Whisper.Gameplay.Config.GameConfigReader _cfg;
        public bool Booted { get; private set; }
        public string LastError { get; private set; }
        /// <summary>Play 循环帧计数（用于证明"真的在跑"，而不是只挂了个组件）。</summary>
        public long Ticks { get; private set; }
        /// <summary>Boot 全流程耗时（毫秒）——真机验收用，证明"启动了"而不是"挂着"。</summary>
        public double BootMs { get; private set; }
        /// <summary>Boot 各阶段的人类可读记录（真机日志取证用：logcat -s Unity）。</summary>
        public string BootLog { get; private set; } = "";

        LevelBuilder _levelBuilder;
        PlayerController _player;
        MonsterViews _monsters;
        /// <summary>温度系统（刺骨寒温证据）。每帧推进；鬼所在房间持续降温。</summary>
        Whisper.Gameplay.Environment.TemperatureSystem _temperature;
        /// <summary>本局天气 id（决定基线室温）。</summary>
        string _weatherId;
        /// <summary>
        /// 本局种子：**天气与刮风都由它派生**，联机时各端必须用同一个值（由 host 下发）。
        /// 默认 0 → `PickIndexForMatch` 走确定性兜底（不依赖任何随机源或时钟，见 `gate-physics` 的两条判红）。
        /// 接入对局流程后，由 `MatchDirector` 在开局时写入这里。
        /// </summary>
        public int MatchSeed { get; set; }
        /// <summary>本局鬼是否带「刺骨寒温」证据（决定它把鬼房降到 [-8,-5] 还是 [-2,5]）。</summary>
        bool _ghostHasFreezingEvidence;
        /// <summary>HUD 用的温度行缓存（0.5s 刷新时算一次，别每帧拼字符串）。</summary>
        string _temperatureLine = "温度：—";
        /// <summary>玩家身体（模型）。</summary>
        PlayerBody _playerBody;
        /// <summary>跳脸视图（猎杀致死时播放）。</summary>
        JumpscareView _jumpscare;
        /// <summary>主界面（代码构建；arch-guard 只允许 Boot.unity 一个场景文件）。</summary>
        MenuScene _menu;
        /// <summary>等级/经验/声望/钱/碎片（恐鬼症对齐 · docs/spec/phasmophobia-alignment.md §3.1）。</summary>
        Progression _progression;
        /// <summary>商店与已装备（§3.2）。</summary>
        Shop _shop;
        /// <summary>每日/每周任务（§3.3）。</summary>
        TaskSystem _tasks;
        /// <summary>电力：单总闸 + 各房间灯（§3.4）。</summary>
        /// <summary>局内任务（每局按本局种子抽 N 条，结算时叠加奖励）。</summary>
        ObjectiveSystem _objectives;
        /// <summary>渲染质量档位（低/高/顶级 + 60/90/120 帧）。</summary>
        Whisper.Gameplay.Render.RenderQuality _quality;
        /// <summary>后处理执行器（挂在主相机上）。</summary>
        PostFx _postFx;
        Whisper.Gameplay.Power.PowerSystem _power;
        /// <summary>互动：鬼开关灯/扔物/敲击/关总闸（§3.5）。</summary>
        Whisper.Gameplay.Interaction.InteractionSystem _interaction;
        /// <summary>关卡灯光（由电力系统驱动；它已有平滑开关过渡，不重复实现）。</summary>
        Whisper.Gameplay.Level.LightRig _lightRig;
        /// <summary>互动节拍用的确定性随机源（禁 UnityEngine.Random；见 gate-physics）。</summary>
        uint _interactRng = 0x1BADB002u;
        float _ghostRoomDwellAcc;
        /// <summary>对局是否已开始（幂等保护：主界面按钮可能被连点）。</summary>
        bool _matchStarted;
        PlayerController _playerControllerRef;
        Text _status;

        /// <summary>
        /// HUD 诊断文字总开关。**默认 false = 不留任何字体**（用户 2026-10-05：「去除所有小字，不留字体」）。
        ///
        /// 为什么必须放在**这里**而不是 MenuScene：左上角那一大块（Tick/接口/关卡/几何着色器/
        /// 玩家/怪物/本局任务/温度/玩法层…）是**本类 Update() 每 0.5s 写 `_status.text`** 渲染的；
        /// 第 10 轮我把开关加在 MenuScene 上，管不到这条路径 —— 0.1.80 真机截图里整块小字照样在。
        ///
        /// 为什么用 SetActive(false) 而不只是清空文本：用户要的是"**不留字体**"（屏幕上不该有调试文字，
        /// 含空壳控件）；且空 Text 仍占位、仍可能留描边残影。
        ///
        /// 为什么保留开关而不是删代码：本项目真机取证**只能走 HUD**（release IL2CPP 下 Debug.Log 时有时无，
        /// 交接 §0.6 已记）。排查时把此字段置 true 即可恢复全部诊断行。
        /// </summary>
    }
}
