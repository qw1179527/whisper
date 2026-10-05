using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Whisper.Core;                 // Services 定位器在 Whispers.Core（写成同文件内引用会 CS0103）
using Whisper.Core.Contracts;       // MatchPhase
using Whisper.Net.Direct;
using Whisper.Gameplay.Level;        // MiniJson（地图注册表读取用）           // LanSession / RoomReachJudge（零信令直连：可达范围与会话编排）

namespace Whisper.Runtime
{
    /// <summary>
    /// 主界面（用户点名要的那一版）。
    ///
    /// ## 用户原话要求（逐条对应到实现）
    /// · "主界面以一处 **3D 建模空间** 为主体" → `BuildRoom()`：代码搭出一间黑暗的走廊式房间（地板/天花/两面墙 + 灯）
    /// · "**灯光不停闪烁**" → `Tick()` 里的 `_flicker` 随机相位 + 偶发骤暗（不是正弦呼吸，那不像坏灯管）
    /// · "空间 **右下角** 为一个手电筒" → `BuildFlashlight()`：右下角屏幕锚定，用**真模型**（`props` 的手电筒几何）
    /// · "灯光闪烁时会有一定概率在 **远处黑暗中随机刷新一个鬼怪**" → TrySpawnGhost()：闪烁骤暗时按概率刷
    /// · "并且加重眼部亮处理，**分红眼与白眼**" → 鬼用 `ModelLibrary` 的鬼部件 + 眼用 `_WhisperEmission` 自发光
    /// · "**右边区域做玩法选项**" → `BuildOptions()`：右侧竖排按钮
    /// · "**适配多人联机**" → `BuildOptions()` 里的联机入口接 `Services.Net`（本地回环可用）
    ///
    /// ## 为什么全部用代码建（而不是 .unity 场景）
    /// `arch-guard` 的 C1 只允许 `Assets/Scenes/Boot.unity` 一个场景文件 —— 所以新增界面必须**代码构建**。
    /// 这与本项目既有做法一致（HUD 也是 `GameBootstrap` 里代码建的）。
    /// </summary>
    public sealed partial class MenuScene : MonoBehaviour
    {
        // ── 可调参数（都在一处，便于手感调整）──
        /// <summary>房间尺寸（米）。走廊式：长 16 · 宽 5 · 高 3.2。</summary>
        public Vector3 RoomSizeM = new Vector3(26f, 3.4f, 6f);
        /// <summary>
        /// 灯基准强度。
        /// </summary>
        /// <remarks>
        /// 调参史（每次都写清原因，避免来回摆）：
        ///   1.15 → 2.2：Unlit 改成吃光后必须整体提亮，否则主界面黑得看不出是空间。
        ///   2.2 → 0.95：0.1.55 真机截图**过曝**（墙面/地面冲到近白，恐怖氛围全丢）。
        ///     原因不只是灯：那版同时开了 `allowHDR` + 4x MSAA + 像素光 4 盏，
        ///     整体亮了一大截。**恐怖游戏的主界面必须暗**（用户明确要"画面暗调"），
        ///     所以把基准拉回来，靠点光与体积感做层次，而不是靠整体亮度。
        /// </remarks>
        public float LightBase = 0.95f;
        /// <summary>每秒"骤暗"的概率（灯光闪烁时刷鬼的触发点）。</summary>
        public float DipChancePerSec = 0.55f;
        /// <summary>每次骤暗刷鬼的概率。</summary>
        public float GhostChancePerDip = 0.22f;
        /// <summary>同时最多几只鬼（用户要"一个鬼怪"，但偶尔多一只有惊喜感，故留可调）。</summary>
        public int MaxGhosts = 1;
        /// <summary>鬼刷新距离范围（米）——"远处黑暗中"。4~7.5m：26m 走廊里既有纵深又能看清形体。</summary>
        public float GhostMinDistM = 4.0f;
        public float GhostMaxDistM = 7.5f;

        Transform _root;
        Light _ceilingLight;
        readonly List<GameObject> _ghosts = new List<GameObject>();
        /// <summary>确定性伪随机：本项目禁 `确定性伪随机（禁用的那个 API）`/`System.Random`（gate-physics 会判红）。</summary>
        uint _rng = 0x9E3779B9u;
        float _dipTimer;
        float _dipLeft;
        bool _paused;

        /// <summary>当前在场的鬼数量（HUD/自检可读）。</summary>
        public int GhostCount => _ghosts.Count;
        /// <summary>本轮累计刷鬼次数（自检可读：证明"概率刷鬼"真的发生过）。</summary>
        public int GhostSpawned { get; private set; }

        // ── 确定性伪随机（xorshift32；与 HuntScheduler 同一套，不引入 System.Random）──
        uint NextU32()
        {
            _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
            return _rng;
        }
        /// <summary>[0,1) 的确定性随机数。</summary>
        float Next01() => (NextU32() & 0xFFFFFF) / 16777216f;

        /// <summary>组合根注入的相机。**不要用 Camera.main** —— 本工程的相机刻意不设 MainCamera tag。</summary>
        Camera _cam;
        /// <summary>组合根（读等级/商店/任务；主界面不自己持有档案，避免两份真相源）。</summary>
        GameBootstrap _boot;
        /// <summary>主界面 3D 场景：工业风两层仓库（替代旧的走廊）。</summary>
        HallScene _hall;

        /// <summary>大厅第一人称自由行走 + 自由环视（用户要求；官方原文见 LobbyCamera 类注释）。</summary>
        LobbyCamera _lobbyCam;

        // ── 主菜单板操作视角（官方形态：空格/点击进入，再按一次退出）──
        /// <summary>是否处于"操作菜单板"视角。</summary>
        bool _boardMode;
        /// <summary>相机是否已在目标位（到位后不再每帧写 transform —— 否则会抖屏）。</summary>
        bool _boardSettled;
        /// <summary>相机过渡进度 0..1（0 = 走廊视角，1 = 菜单板正视）。</summary>
        float _boardBlend;
        /// <summary>菜单板上的 6 个选项文字（官方条目）。</summary>
        readonly System.Collections.Generic.List<Text> _boardItems = new System.Collections.Generic.List<Text>();

        // `_taskBoardText` / `_navHintText` 已删除：它们是我编的、官方形态里没有的元素（见 remove-invented-ui.mjs）。

        /// <summary>房间码显示（仅建房成功后可见；官方在菜单板右上角）。</summary>
        Text _roomCodeText;
        /// <summary>选项标题（与官方一致，顺序即纸片顺序）。</summary>
        static readonly string[] BoardItemTitles = { "单人游戏", "多人联机", "训练", "选项", "制作人员", "退出游戏" };
        /// <summary>地图选项板文字。</summary>
        Text _mapBoardText;
        /// <summary>商店电脑屏幕文字。</summary>
        Text _shopScreenText;
        /// <summary>右上角 ID 卡文字。</summary>
        Text _idCardText;
        /// <summary>操作视角下的提示行。</summary>
        Text _boardHint;
        /// <summary>菜单板 UI 的诊断串（走 HUD —— release 下 Debug.Log 不可靠）。</summary>
        string _boardUiDiag = "板UI:未建";
        /// <summary>
        /// 旧 UI 的容器（右侧竖排按钮 / 标题 / 副标题 / 左侧面板 / 底部提示）。
        /// </summary>
        /// <remarks>
        /// 【为什么需要 · 用户 2026-10-05 报"点一次退出后就无法再点"】
        /// 旧的 8 个竖排按钮与左侧面板在板模式里**没有被隐藏**，于是退出操作视角后旧按钮又盖在场景上；
        /// 而板模式下的点击先被 BoardInput 消费（点在板上就 return）→ 表现就是"点了没反应 / 无法再点"。
        /// 用户已明确旧 UI 要废弃 → 正确行为是：**板模式 = 唯一 UI**。
        /// 用一个容器统一显隐，比逐个 SetActive 更不容易漏。
        /// </remarks>
        GameObject _legacyUi;
        /// <summary>大厅左侧信息面板（等级/钱/碎片/任务）。</summary>
        Text _lobbyPanel;
        /// <summary>大号居中提示（任何按钮都要有"大到不可能看漏"的反馈）。</summary>
        Text _modal;
        GameObject _modalBg;
        /// <summary>相机/后处理诊断（真机黑屏排查用；release 下 Debug.Log 不可靠，只能走 HUD）。</summary>
        /// <summary>
        /// 主界面诊断文字总开关。**默认关**：交付给玩家看的主界面必须干净（用户 2026-10-05：
        /// "主界面成分复杂，字体到处都是"、"其他的小字也去掉"）。
        /// 但**不能删代码**：release IL2CPP 下 `Debug.Log` 时有时无，HUD 是本项目唯一可靠的真机取证通道
        /// （交接 §0.6）。排查时把它置 true 即可恢复全部诊断行。
        /// </summary>
        public bool ShowDiagnostics = false;

        /// <summary>
        /// 旧 UI（右侧竖排 8 按钮 + 右下诊断行 + 左上信息面板）是否还建。**默认 false = 不建**。
        ///
        /// 用户永久约束：「不要再在旧界面上花时间」「主界面成分复杂，字体到处都是」——
        /// 旧界面已由新版官方形态（3D 菜单板 + 地图板 + 商店电脑 + ID 卡）取代，故整体停建。
        /// 之所以保留开关而不是删代码：旧 UI 里带了按钮屏幕矩形诊断，偶发排查时仍可能要；
        /// 且 `BuildOptions()` 还负责登记 `_hitButtons`（自绘点击判定），不能凭直觉删。
        /// </summary>
        public bool ShowLegacyUi = false;

        string _camInfo = "相机：—";
        /// <summary>画质按钮被点的累计次数（偶数切画质、奇数切帧率）。</summary>
        int _qualityToggleCount;
        /// <summary>上一次命中的按钮序号 + 时刻（动作层去重，防连环触发；见 HandleSelfDrawClick）。</summary>
        int _lastHitIndex = -1;
        float _lastHitTime = -999f;
        /// <summary>商店条目缓存（按 tier、id 排序，保证"先 Tier I 后 II"的展示顺序稳定）。</summary>
        readonly System.Collections.Generic.List<Whisper.Gameplay.Progression.ShopItem> _shopAll =
            new System.Collections.Generic.List<Whisper.Gameplay.Progression.ShopItem>();
        /// <summary>大厅面板下次刷新时刻（0.5s 一次，避免每帧拼字符串产生 GC）。</summary>
        float _nextLobbyRefresh;

        /// <summary>构建整个主界面。`parent` 通常是组合根对象；`cam` 由组合根显式给出。</summary>
        public void Build(Transform parent, Camera cam, GameBootstrap boot = null)
        {
            if (_root != null) return;
            _cam = cam;
            _boot = boot;
            _root = new GameObject("MenuRoot").transform;
            _root.SetParent(parent, false);
            // ── 3D 场景：**工业风两层仓库**（用户 2026-10-05 §4：主界面 3D 场景整体更换）──
            // 旧实现是 BuildRoom()（一条 26m 走廊）+ BuildFlashlight()（右下角手电筒）——
            // 那是为"恐怖走廊"玩法准备的，与官方大厅（可探索的仓库）没有可复用部分。
            // 用户原话：「不要再在旧界面上花时间了」→ 整体替换，旧方法保留但不再调用。
            _hall = new HallScene(_root, _cam);

            // ── 大厅自由视角（P0）────────────────────────────────────────────
            // 官方：玩家可在带完整碰撞体的大厅里**自由行走**；"进操作界面"是按键/点击触发的 UI 状态。
            // 这里给 LobbyCamera 一个**墙内收 0.4m 的可行走矩形**（大厅是规则矩形仓库；真实道具碰撞
            // 留给后续"道具物理"那一轮）。起点放在大厅靠前、面向菜单板（玩家一进来就面对虚拟焦点）。
            {
                float m = 0.4f;
                float hx = _hall.WidthM * 0.5f - m;
                float hz = _hall.LengthM * 0.5f - m;
                _lobbyCam = gameObject.AddComponent<LobbyCamera>();
                // 起点：靠前墙（-Z）一侧、略偏左，朝向 +Z（面向大厅纵深与菜单板）
                _lobbyCam.Configure(-hx, hx, -hz, hz,
                    new Vector3(0f, LobbyCamera.EyeHeightM, -hz + 1.2f), 0f);
                _lobbyCam.Apply();
            }

            // 【§8 安全区】HallScene 由本类持有，故由本类把货车安全区**推送**给组合根
            // （GameBootstrap 没有 HallScene 引用 —— 让它去抓会反向依赖 UI 层）。
            if (GetComponent<GameBootstrap>() is { } bootForZone && _hall.Truck != null)
                bootForZone.SetTruckSafeZone(_hall.TruckSafeZone);
            _hall.Build();
            BuildBoardUi();      // 菜单板上的选项 / 地图板 / 商店屏 / ID 卡（默认隐藏，进入操作视角才显示）
            BuildOptions();
            ShowBoardUi(false);
            BuildShopCache();
            // 【不再刷鬼】官方大厅是**等待区/安全区**（无鬼）。旧的"远处黑暗中刷红眼鬼"
            // 属于旧玩法的氛围设计，本次随 3D 场景一起下线。
            // 但**鬼的模型池仍保留**（对局里要用），见 GhostModelPool / MonsterViews。
        }

        /// <summary>
        /// 建"菜单板 UI 层"：把 6 个选项贴到纸片上，并在地图板 / 商店屏 / ID 卡上写字。
        /// </summary>
        /// <remarks>
        /// 为什么用**世界坐标 → 屏幕坐标**而不是把 UI 直接挂在 3D 物体上：
        /// 本工程的 UI 全是代码建在 Canvas 上的（arch-guard 只允许 Boot.unity 一个场景，V9 §19.1 C2），
        /// 而 Canvas 是 Screen Space - Overlay → 没有世界空间画布可用。
        /// 所以每帧把 3D 点投影到屏幕，再摆 UI 文字 —— 效果等价，且不引入新的资产依赖。
        /// </remarks>
    }
}
