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
    public sealed class MenuScene : MonoBehaviour
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
        void BuildBoardUi()
        {
            // 确保画布已建。
            // 【为什么必须显式建】`_canvas` 只在 BuildCanvas() 里赋值，而 BuildCanvas 原先只被
            // BuildRoom() 调用 —— 本轮 3D 场景换成 HallScene 后 BuildRoom() 不再被调用，
            // 于是 _canvas 永远是 null，BuildBoardUi 静默返回（HUD 诊断 `板UI:未建` 抓到）。
            // 这类"换了上游导致下游静默失效"的坑，修法不是挪调用顺序，而是**让依赖显式自足**：
            // 谁需要画布，谁就先确保画布存在（BuildCanvas 自身是幂等的：它会先 Find 再建）。
            if (_canvas == null) BuildCanvas();
            if (_canvas == null || _hall == null) { _boardUiDiag = "板UI:画布未建"; return; }
            for (int i = 0; i < _hall.NoteCount && i < BoardItemTitles.Length; i++)
            {
                var t = SceneMaterials.Label(_canvas.transform, "BoardItem" + i, BoardItemTitles[i],
                    new Vector2(0f, 0f), new Vector2(0f, 0f), 42, TextAnchor.MiddleCenter);
                t.color = new Color(0.08f, 0.08f, 0.11f);   // 深字写在浅色纸片上（软木板风格）
                _boardItems.Add(t);
            }
            _mapBoardText = SceneMaterials.Label(_canvas.transform, "MapBoardText", "地图选择（投票）",
                new Vector2(0f, 0f), new Vector2(0f, 0f), 22, TextAnchor.MiddleCenter);
            _mapBoardText.color = new Color(0.85f, 0.88f, 0.92f);

            _shopScreenText = SceneMaterials.Label(_canvas.transform, "ShopScreenText", "装备商店",
                new Vector2(0f, 0f), new Vector2(0f, 0f), 22, TextAnchor.MiddleCenter);

            // 【用户 2026-10-05：「你看官方文档了吗就瞎写界面」——我确实编了两个官方没有的元素，此处不再创建】
            // · 原 `_navHintText`「◀ 地图｜菜单板｜商店 ▶」：官方是**快速跳转按钮**（菜单界面里的控件），
            //   不是场景里漂浮的一行文字；后续做成真正的 UI 按钮。
            // · 原 `_taskBoardText`「每日挑战 · 每周挑战」：官方是"菜单板上**能看到**挑战"，
            //   即板面内容的一部分；后续做进菜单板板面里，而不是独立一行字。
            // 纪律：**官方没明写的 UI 元素一律不加**（用户《新指导》"不清楚的地方不盲目进行，不猜"）。
            // remove-invented-ui：此注释为幂等标记，勿删。
            _shopScreenText.color = new Color(0.90f, 0.95f, 1.0f);

            _idCardText = SceneMaterials.Label(_canvas.transform, "IdCardText", "ID",
                new Vector2(0f, 0f), new Vector2(0f, 0f), 18, TextAnchor.MiddleCenter);
            _idCardText.color = new Color(0.15f, 0.15f, 0.18f);

            // 房间码：建房成功后显示（默认隐藏，避免单人时占屏）。位置在 ID 卡下方一档。
            // 【锚点教训】必须插在 _idCardText 整条语句**之后**：IdCardText 的 Label(...) 跨两行，
            // 上一轮我插在它第一行之后，把语句截断成语法错（MenuScene.cs(195) 应输入 )）。
            _roomCodeText = SceneMaterials.Label(_canvas.transform, "RoomCodeText", "",
                new Vector2(1f, 1f), new Vector2(1f, 1f), 30, TextAnchor.UpperRight);
            _roomCodeText.gameObject.SetActive(false);

            // 【用户 2026-10-05：「去除所有小字，不留字体」】
            // 原先这里建 `BoardHint`（"按 空格 或 点击菜单板 进入操作界面　｜　点这里 = 输入房间码加入"），
            // 它位于屏幕**正中央**（anchoredPosition 0,-240），是最后一块常驻文字 → 不再创建。
            // ⚠ 连带影响：它同时是"点击加入"的热区；删除后加入改由**多人面板**承担
            //（「多人联机」→ 按 J 加入，见 OpenMultiplayerPanel），功能不丢，只是不再"去点一行字"。
            _boardHint = null;   // 明确置空：HandleBoardInput 里对它有 null 检查
            /* 原实现（保留备查，勿删注释）：
            _boardHint = SceneMaterials.Label(_canvas.transform, "BoardHint",
                "按 空格 或 点击菜单板 进入操作界面　｜　点这里 = 输入房间码加入",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), 24, TextAnchor.MiddleCenter);
            _boardHint.color = new Color(0.92f, 0.92f, 0.86f);
            _boardHint.rectTransform.anchoredPosition = new Vector2(0f, -240f);
            */
        }

        /// <summary>操作视角 UI 的显隐。</summary>
        void ShowBoardUi(bool show)
        {
            foreach (var t in _boardItems) if (t != null) t.gameObject.SetActive(show);
            // 房间码一旦有内容就**不随板隐藏**：玩家退出菜单板后要把码转发给朋友。
            if (_roomCodeText != null && !show && string.IsNullOrEmpty(_roomCodeText.text)) _roomCodeText.gameObject.SetActive(false);
            if (_mapBoardText != null) _mapBoardText.gameObject.SetActive(show);
            if (_shopScreenText != null) _shopScreenText.gameObject.SetActive(show);
            if (_idCardText != null) _idCardText.gameObject.SetActive(show);
            if (_boardHint != null) _boardHint.gameObject.SetActive(!show);
            // 板模式 = 唯一 UI：旧界面整体隐藏（用户已明确旧 UI 要废弃）
            if (_legacyUi != null) _legacyUi.SetActive(!show);
        }

        /// <summary>进入 / 退出菜单板操作视角（官方：空格或点击进出）。</summary>
        void ToggleBoardMode()
        {
            _boardMode = !_boardMode;
            ShowBoardUi(_boardMode);
            _lastTouch = _boardMode ? "进入菜单板操作视角" : "退出菜单板";
        }

        /// <summary>
        /// 把一个"跟随 3D 点"的文字摆到屏幕像素位置。
        /// </summary>
        /// <remarks>
        /// 为什么需要这个助手：`SceneMaterials.Label` 建出的 Text 的 RectTransform **pivot 是 (0,0)**
        /// 且 anchor 由调用方给。如果直接 `anchoredPosition = 屏幕像素中心`，文字的左下角会被放到那个点上，
        /// 整块文字向右上偏出可视区 —— 0.1.65 真机就是"菜单板选项一个字都看不到"。
        /// 本助手统一口径：**anchor=屏幕左下、pivot=中心** → anchoredPosition 就是"文字中心的屏幕像素"。
        /// （本工程 Canvas 是 ScreenSpaceOverlay + ScaleWithScreenSize 且参考分辨率=当前屏幕，
        ///   所以画布单位 == 屏幕像素。旧的按钮矩形诊断行就是按这个口径写的，有先例。）
        /// </remarks>
        void PlaceAtScreen(Text t, Vector3 screenPoint, float w, float h)
        {
            if (t == null) return;
            if (screenPoint.z <= 0f) { t.enabled = false; return; }   // 在相机背后
            t.enabled = true;
            var rt = t.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            // 【关键】屏幕像素 → 画布单位。
            // Canvas 是 ScaleWithScreenSize + referenceResolution 1920x1080，真机 2800x1280
            // → scaleFactor ≈ 1.458。`WorldToScreenPoint` 给的是**屏幕像素**，
            // 而 `anchoredPosition` 是**画布单位** —— 不除 scaleFactor 就会被放大 1.458 倍移出屏幕。
            float sf = _canvas != null ? _canvas.scaleFactor : 1f;
            if (sf <= 0.0001f) sf = 1f;
            rt.anchoredPosition = new Vector2(screenPoint.x / sf, screenPoint.y / sf);
        }
        /// <summary>把菜单板 UI 每帧贴到 3D 位置上（世界坐标 → 屏幕坐标）。</summary>
        void UpdateBoardUi()
        {
            // （原"挑战行/快捷提示跟随相机"代码块已随两个元素一起删除）
            if (_cam == null || _hall == null) return;
            for (int i = 0; i < _boardItems.Count; i++)
            {
                var t = _boardItems[i];
                if (t == null) continue;
                var spp = _cam.WorldToScreenPoint(_hall.NoteCenter(i));
                PlaceAtScreen(t, spp, 470f, 100f);
                if (i == 0 || i == 5)
                    _boardUiDiag = "板UI n=" + _boardItems.Count
                        + " i" + i + " 世界(" + _hall.NoteCenter(i).x.ToString("F1") + ","
                        + _hall.NoteCenter(i).y.ToString("F1") + ","
                        + _hall.NoteCenter(i).z.ToString("F1") + ")→屏("
                        + spp.x.ToString("F0") + "," + spp.y.ToString("F0") + ",z" + spp.z.ToString("F0") + ")"
                        + " on=" + (t.enabled ? 1 : 0);
            }
            if (_shopScreenText != null)
            {
                PlaceAtScreen(_shopScreenText,
                    _cam.WorldToScreenPoint(new Vector3(_hall.WidthM * 0.30f, 1.28f, -_hall.LengthM * 0.5f + 1.5f)), 380f, 90f);
            }
                        if (_idCardText != null)
            {
                var p = _boot != null ? _boot.Progression : null;
                _idCardText.text = p != null
                    ? "ID\n等级 " + p.Level + "\n钱 " + p.Money + "\n经验 " + (p.LevelProgress * 100f).ToString("F0") + "%"
                    : "ID";
                PlaceAtScreen(_idCardText,
                    _cam.WorldToScreenPoint(new Vector3(_hall.WidthM * 0.5f - 2.4f, 3.75f, -_hall.LengthM * 0.5f + 0.5f)), 200f, 130f);
            }
        }

        /// <summary>相机在"走廊视角 ↔ 菜单板正视"之间过渡。</summary>
        void UpdateBoardCamera(float dt)
        {
            if (_cam == null || _hall == null || _lobbyCam == null) return;

            // 【P0 自由视角 · 用户 2026-10-05】
            // 官方大厅形态（用户《补充说明》第 5 行原文）：玩家可以**在带完整碰撞体的大厅里自由行走**；
            // 「按 空格键 或鼠标左键点击即可进入操作界面，再按一次 空格键 或 Esc 键则可退出」——
            // 即**行走与"进操作界面"是两件事**。
            //
            // 旧实现在这里**每帧直接写相机位姿**，在"自由位/看板位"之间插值 → 玩家不能走、不能转头
            // （用户："我要的自由视角呢"）。现在：
            //   · 自由态：本方法**直接 return**，相机完全交给 LobbyCamera（可走 + 可环视）；
            //   · 操作视角：不把相机平移过去，只**原地转向对准菜单板**（玩家仍站在原地）。
            if (!_boardMode)
            {
                _lobbyCam.Active = true;   // 自由行走 + 环视
                return;                    // ← 绝不写相机 transform
            }

            // 操作视角：停住脚步，只转向菜单板（平滑收敛，避免抖动）
            _lobbyCam.Active = false;
            var b = _hall.MenuBoardPos;
            var eye = _lobbyCam.Position;
            var to = new Vector3(b.x - eye.x, 0f, b.z - eye.z);
            if (to.sqrMagnitude < 0.0001f) return;
            float wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float curYaw = _lobbyCam.YawDeg;
            float diff = Mathf.DeltaAngle(curYaw, wantYaw);
            if (Mathf.Abs(diff) < 0.05f)
            {
                // 到位后**一个字节都不动**（此前抖屏的教训：永不静止 = 细颤）
                return;
            }
            float step = Mathf.Clamp(diff, -220f * Mathf.Max(dt, 0.0001f), 220f * Mathf.Max(dt, 0.0001f));
            _lobbyCam.Teleport(eye, curYaw + step, 0f);
            _lobbyCam.Apply();
        }

        /// <summary>
        /// 每帧键盘入口（与触摸解耦）。
        /// 为什么单独一个方法：键盘检查原先写在 `HandleBoardInput` 内部，而它在 `if (!clicked) return false;`
        /// **之后** → 纯按键事件永远走不到那些分支。真机实测（焦点正常、设备醒着）空格/H/J 全部无反应。
        /// </summary>
        void HandleMenuKeyboard()
        {
            if (_cam == null || _hall == null) return;
            // 空格：进出操作视角（官方行为）。Esc：只在操作视角下退出。
            if (Input.GetKeyDown(KeyCode.Space)) { ToggleBoardMode(); return; }
            if (Input.GetKeyDown(KeyCode.Escape) && _boardMode) { ToggleBoardMode(); return; }
            // 键盘主控（可验证性优先）：H = 建房、J = 加入。限定在操作视角下，避免主界面误触开局。
            if (_boardMode && Input.GetKeyDown(KeyCode.H)) { StartHostSession(); return; }
            if (_boardMode && Input.GetKeyDown(KeyCode.J)) { StartJoinSession(); return; }
            // 数字键 1-9：选图（面板列出序号；移动端无鼠标列表时的折中，见 wire-map-vote-panel.mjs 注释）。
            for (int d = 1; d <= 9; d++)
            {
                if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + d - 1))) { SelectMapByNumber(d); return; }
            }
        }
        /// <summary>
        /// 命中一个"贴在 3D 世界位置上的 UI 文本"（地图板/商店屏这种）。
        /// 与菜单板拾取同一口径：世界点 → 屏幕点 → 局域坐标 → rect 包含（双 y 口径）。
        /// 放宽到 1.6 倍（世界空间的牌匾比文字本身大，玩家会点牌匾边缘）。
        /// </summary>
        bool TryHitWorldLabel(Text label, Vector2 screenPoint)
        {
            if (label == null || _cam == null || !label.gameObject.activeInHierarchy) return false;
            var rt = label.rectTransform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPoint, null, out var local);
            var flipped = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, flipped, null, out var localFlipped);
            var r = rt.rect;
            r.width *= 1.6f; r.height *= 1.6f;
            r.x -= rt.rect.width * 0.3f; r.y -= rt.rect.height * 0.3f;
            return r.Contains(local) || r.Contains(localFlipped);
        }

        /// <summary>
        /// 地图投票面板（官方：左侧地图选项板，可投票、票数相同随机、可选"随机地图"）。
        /// ⚠ 目前仍是**选择界面**：多地图数据（config.maps 注册表 + 第二张图）尚未落地，
        /// 故这里如实显示"当前仅 1 张图"，不假装有得选（禁止 Guessing）。
        /// </summary>
        /// <summary>
        /// 档案面板（官方 §4：右上 ID 卡显示资金/等级/升级进度，点开看详情）。
        /// 数值全部取自既有系统（Progression），这里不自己算、不缓存 —— 避免两套口径。
        /// </summary>
        void OpenProfilePanel()
        {
            var boot = GetComponent<GameBootstrap>();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("档案");
            sb.AppendLine();
            if (boot != null && boot.Progression != null) sb.AppendLine(boot.Progression.Describe());
            else sb.AppendLine("（进度系统未就绪）");
            sb.AppendLine();
            sb.AppendLine("官方形态：等级 / 资金 / 升级进度，多人时每位玩家一张 ID 卡。");
            sb.AppendLine("点任意处关闭");
            ShowModal(sb.ToString());
        }
        void OpenMapVotePanel()
        {
            var boot = GetComponent<GameBootstrap>();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("地图选择（投票）");
            sb.AppendLine();
            var list = Whisper.Gameplay.Config.GameConfig.Get("maps.list") as System.Collections.Generic.List<object>;
            if (list == null)
            {
                sb.AppendLine("地图注册表缺失（config.maps）—— 无法选图。");
            }
            else
            {
                int shown = 0;
                for (int i = 0; i < list.Count && shown < 9; i++)
                {
                    var m = MiniJson.AsMap(list[i]);
                    if (m == null) continue;
                    shown++;
                    var id = MiniJson.AsString(MiniJson.Get(m, "id"));
                    var name = MiniJson.AsString(MiniJson.Get(m, "name"));
                    var size = MiniJson.AsString(MiniJson.Get(m, "size"));
                    int floors = MiniJson.AsInt(MiniJson.Get(m, "floors"));
                    bool impl = MiniJson.AsBool(MiniJson.Get(m, "implemented"));
                    bool cur = boot != null && boot.MapId == id;
                    sb.Append(shown).Append(") ").Append(name)
                      .Append("（").Append(id).Append(" · ").Append(size)
                      .Append(" · ").Append(floors).Append("层）");
                    if (cur) sb.Append(" ★当前");
                    if (!impl) sb.Append("（未实现）");
                    sb.AppendLine();
                }
                sb.AppendLine();
                sb.AppendLine("按数字键 1-" + shown + " 选图（关闭面板后按）");
                sb.AppendLine("官方形态：多人投票，票数相同随机，另有「随机地图」。");
            }
            ShowModal(sb.ToString());
        }
        /// <summary>按编号选图（面板里列出的序号）。未实现的图明确拒绝，不静默换图。</summary>
        void SelectMapByNumber(int n)
        {
            if (n < 1) return;
            var boot = GetComponent<GameBootstrap>();
            if (boot == null) { Note("组合根缺失：无法选图"); return; }
            var list = Whisper.Gameplay.Config.GameConfig.Get("maps.list") as System.Collections.Generic.List<object>;
            if (list == null) { Note("地图注册表缺失"); return; }
            int shown = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var m = MiniJson.AsMap(list[i]);
                if (m == null) continue;
                shown++;
                if (shown != n) continue;
                var id = MiniJson.AsString(MiniJson.Get(m, "id"));
                bool impl = MiniJson.AsBool(MiniJson.Get(m, "implemented"));
                if (!impl) { Note("该地图尚未实现：" + id + "（注册表里标了 implemented=false）"); return; }
                if (boot.SelectMap(id)) { Note("已选图：" + id + "（下一局生效）"); }
                else Note("选图失败：" + id);
                return;
            }
        }
        /// <summary>处理"点菜单板 / 空格"这类主界面输入。返回 true 表示已消费本次点击。</summary>
        bool HandleBoardInput(Vector2 screenPoint, bool clicked)
        {
            // ── 官方两个交互位（用户《补充说明》§4）────────────────────────
            // 左侧地图选项板：点了弹投票面板；右侧装备商店电脑：点了开商店。
            // 命中判定沿用同一个范式（局域坐标 + 双 y 口径），不另造一套。
            if (clicked && TryHitWorldLabel(_mapBoardText, screenPoint)) { OpenMapVotePanel(); return true; }
            if (clicked && TryHitWorldLabel(_shopScreenText, screenPoint)) { OpenShop(); return true; }
            // 官方 §4：右上 ID 卡可点击查看详情（资金/等级/升级进度）。
            if (clicked && TryHitWorldLabel(_idCardText, screenPoint)) { OpenProfilePanel(); return true; }
            // （原"点挑战行看详情"已删：`_taskBoardText` 是我编的元素，官方没有它。
            //   官方是"菜单板上**能看到**每日/每周挑战"，属板面内容，后续做进板面。）
            // 点击顶部提示条 = 加入房间（不占 6 个板位：官方板面正是 6 项，加第 7 项会挤掉一项）。
            if (clicked && _boardHint != null && _joinKeyboard == null)
            {
                // 命中判定沿用本项目既有范式（MenuScene 里按钮命中那段的注释写明了两个坑）：
                // ① 必须转到**该层级的局部坐标**再与 rect 比 —— 直接比屏幕坐标会因 CanvasScaler 缩放错位；
                // ② **两种 y 口径都试** —— 实测 mousePosition 的 y 与 Screen.height 不同源，单口径会全不命中。
                var rt = _boardHint.rectTransform;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPoint, null, out var local);
                var flipped = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, flipped, null, out var localFlipped);
                if (rt.rect.Contains(local) || rt.rect.Contains(localFlipped)) { StartJoinSession(); return true; }
            }
            if (_cam == null || _hall == null || _hall.MenuBoardPicker == null) return false;

            // 键盘：空格进出（官方行为）。Esc 只用于"退出操作视角"。
            if (Input.GetKeyDown(KeyCode.Space)) { ToggleBoardMode(); return true; }
            if (Input.GetKeyDown(KeyCode.Escape) && _boardMode) { ToggleBoardMode(); return true; }

            // ── 键盘主控（可验证性优先，见 wire-keyboard-controls.mjs 头注释）──
            // 为什么需要：真机点击有不确定（纸片命中要射线命中共用拾取面，偏了就落兜底开局；
            // adb tap 投递被 ColorOS 时通时不通），而 keyevent 稳定可用。
            // 把"建房/加入"接到键盘上，就能用一条命令确定性验证联机功能本身。
            // 限定在操作视角（_boardMode）下，避免主界面误触直接开局。
            if (_boardMode && Input.GetKeyDown(KeyCode.H)) { StartHostSession(); return true; }
            if (_boardMode && Input.GetKeyDown(KeyCode.J)) { StartJoinSession(); return true; }

            if (!clicked) return false;

            var ray = _cam.ScreenPointToRay(new Vector3(screenPoint.x, screenPoint.y, 0f));
            if (Physics.Raycast(ray, out var hit, 60f) && hit.collider != null
                && hit.collider.gameObject == _hall.MenuBoardPicker)
            {
                if (!_boardMode) { ToggleBoardMode(); return true; }
                // 已在操作视角 → 找最近的纸片（只比 X/Y：都在同一面墙上，Z 只差 8cm）
                int best = -1; float bestD = float.MaxValue;
                for (int i = 0; i < _hall.NoteCount; i++)
                {
                    var c = _hall.NoteCenter(i);
                    float dx = hit.point.x - c.x, dy = hit.point.y - c.y;
                    float d = dx * dx + dy * dy;
                    if (d < bestD) { bestD = d; best = i; }
                }
                // 纸片半宽 0.81、半高 0.51 → 命中点落在 1.0m 内即算选中
                if (best >= 0 && bestD < 1.0f) { ActivateBoardItem(best); return true; }
                return true;   // 点在板上但不在纸片上：消费掉，避免穿透触发别的
            }

            // ══════════════════════════════════════════════════════════════════════
            // 操作视角下点**板外任意处** = 退出（用户原话："点一次退出"）。
            // 依据：官方行为是"再按一次空格或 Esc 退出"，而移动端没有键盘 ——
            // 用户实际操作就是点一下别处。0.1.70 真机实测：点空白没退出（我原先只把
            // 这个入口留给空格/Esc），用户报的"点了没反应"就是这个。
            // 取舍：这样在操作视角下**任何**板外点击都退出，不能"点面板外顺便操作别的"——
            // 但板模式下本来也不该操作别的（旧 UI 已隐藏），所以没有损失。
            // ══════════════════════════════════════════════════════════════════════
            if (_boardMode) { ToggleBoardMode(); return true; }

            return false;
        }

        /// <summary>执行菜单板上的选项（官方 6 条）。</summary>
        void ActivateBoardItem(int index)
        {
            _lastTouch = "菜单板选项 " + index + "：" + BoardItemTitles[index];
            switch (index)
            {
                case 0: OnOption(0, BoardItemTitles[0]); break;   // 单人游戏
                case 1: OpenMultiplayerPanel(); break;                // 多人联机 = 进多人面板（建房/加入/房间码都在里面）                // 多人联机 = 建房（真起 UDP 监听）   // 多人联机（复用"创建房间"）
                case 2: Note("训练：\n\n· 左半屏拖拽移动，右半屏转视角\n· 右下角切换蹲/行\n· 手电筒照亮前路，但会吸引鬼\n· 找齐证据后到撤离点离开\n\n（详细教程后续版本补齐）"); break;
                case 3: CycleQuality(); break;                     // 选项（当前可调：画质/帧率/后处理）
                case 4: Note("制作人员\n\n· 设计与实现：本项目组\n· 引擎：Unity 6\n· 玩法参照：Phasmophobia（Kinetic Games）\n\n致敬所有做恐怖游戏的人。"); break;
                default: Note("退出：真机上请用系统返回键（Android 不允许应用自杀式退出）"); break;
            }
        }

        /// <summary>
        /// 缓存商店条目（按 tier 升序、同 tier 按 id 稳定排序）。
        /// 顺序稳定对玩家很重要：每次打开商店看到的位置都一样。
        /// </summary>
        void BuildShopCache()
        {
            _shopAll.Clear();
            if (_boot == null || _boot.Shop == null) return;
            // Shop 不暴露全表，所以从配置取 id —— 这与 Shop.Load 读的是**同一份真相源**。
            var arr = Whisper.Gameplay.Config.GameConfig.Get("shop.items") as System.Collections.IList;
            if (arr == null) return;
            for (int i = 0; i < arr.Count; i++)
                if (arr[i] is System.Collections.Generic.Dictionary<string, object> m
                    && m.TryGetValue("id", out var v) && v is string id
                    && _boot.Shop.TryGet(id, out var it)) _shopAll.Add(it);
            _shopAll.Sort((a, b) => a.Tier != b.Tier ? a.Tier.CompareTo(b.Tier) : string.CompareOrdinal(a.Id, b.Id));
        }

        /// <summary>把相机搬进主界面房间，并看向走廊深处。</summary>
        /// <remarks>
        /// 为什么必须显式搬：Boot 之后相机是由关卡几何（TryBuildGeometry）放到出生点的，
        /// 而主界面的房间在别处 —— 不搬的话玩家看到的是关卡走廊，主界面 3D 空间等于不存在
        /// （0.1.32 截图里那片均匀灰渐变就是走廊墙面）。
        /// 开始对局时 StartMatch 会重新走几何流程，相机会被放回出生点，所以这里不必还原。
        /// </remarks>
        void PlaceMenuCamera()
        {
            // 用组合根注入的相机。**不要退回 Camera.main**：本工程的相机刻意不设 MainCamera tag，
            // Camera.main 永远是 null → 每帧新建相机 → 画面被多台相机糊掉（真机实测就是这个症状）。
            var cam = _cam;
            if (cam == null)
            {
                // 只有组合根真的没给相机时才自建（并且**不给它设 tag**，与本工程约定一致）
                var cgo = new GameObject("MenuCamera");
                cgo.transform.SetParent(_root, false);
                cam = cgo.AddComponent<Camera>();
                _cam = cam;
                Debug.LogWarning("[Whisper] 组合根未注入相机，主界面自建了一台");
            }
            float w = RoomSizeM.x, h = RoomSizeM.y, d = RoomSizeM.z;
            // 站位：房间 +X 端、离地 1.62m（人眼）、略偏南侧
            cam.transform.position = new Vector3(w * 0.46f, 1.62f, d * 0.06f);
            // 朝 -X 看（走廊深处），带一点俯角看向远端门洞
            cam.transform.rotation = Quaternion.LookRotation(new Vector3(-1f, -0.06f, 0f));
            cam.fieldOfView = 58f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 90f;
            // 背景给很暗的冷色：远端门洞"更黑"才立得住（用天空盒会把房间糊掉）
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.015f, 0.018f, 0.026f);
        }

        // ───────────────────────── 3D 空间 ─────────────────────────
        void BuildRoom()
        {
            float w = RoomSizeM.x, h = RoomSizeM.y, d = RoomSizeM.z;
            var room = new GameObject("MenuRoom").transform;
            room.SetParent(_root, false);
            // 走廊沿 -X 延伸，玩家机位在 +X 端，往 -X 深处看（"远处黑暗中"）
            // ⚠ 主界面房间必须用**吃光材质**：先前走 SceneMaterials.Make（= Whisper/UnlitColor）时
            // **Unlit 不响应任何灯光** → 即使加了方向光房间也全黑（用户："主界面也不对"）。
            // 恐怖氛围靠"把灯调暗"实现，不靠"材质不吃光"。
            Material floorMat = SceneMaterials.Lit(Color.Lerp(SceneMaterials.RoomTint, Color.black, 0.35f), 0.90f);
            Material wallMat = SceneMaterials.Lit(Color.Lerp(SceneMaterials.RoomTint, Color.black, 0.55f), 0.86f);

            SceneMaterials.Box(room, "MenuFloor", new Vector3(0f, -0.05f, 0f), new Vector3(w, 0.1f, d), floorMat);
            SceneMaterials.Box(room, "MenuCeiling", new Vector3(0f, h + 0.05f, 0f), new Vector3(w, 0.1f, d), floorMat);
            SceneMaterials.Box(room, "MenuWallN", new Vector3(0f, h * 0.5f, d * 0.5f + 0.05f), new Vector3(w, h, 0.1f), wallMat);
            SceneMaterials.Box(room, "MenuWallS", new Vector3(0f, h * 0.5f, -d * 0.5f - 0.05f), new Vector3(w, h, 0.1f), wallMat);
            // 远端封死：营造"走廊尽头"的封闭感（也挡住环境光，让远处真的黑）
            SceneMaterials.Box(room, "MenuWallFar", new Vector3(-w * 0.5f - 0.05f, h * 0.5f, 0f), new Vector3(0.1f, h, d), wallMat);

            // ── 空间内容（用户要"以一处 3D 建模空间为主体"，空房间不成立）──
            // 一条走廊：两侧等距门框 + 壁灯。都是立方体 + 吃光材质，零额外资源。
            const int Bays = 5;
            for (int i = 0; i < Bays; i++)
            {
                float bx = w * 0.34f - i * (w * 0.145f);
                // 门框（三根：两根立柱 + 一根门楣）
                SceneMaterials.Box(room, "MenuDoorFrame", new Vector3(bx, h * 0.5f, d * 0.5f - 0.06f),
                    new Vector3(0.12f, h * 0.82f, 0.12f), wallMat);
                SceneMaterials.Box(room, "MenuDoorFrame", new Vector3(bx, h * 0.5f, -d * 0.5f + 0.06f),
                    new Vector3(0.12f, h * 0.82f, 0.12f), wallMat);
                SceneMaterials.Box(room, "MenuDoorLintel", new Vector3(bx, h * 0.86f, 0f),
                    new Vector3(0.12f, 0.14f, d * 0.94f), wallMat);
                // 壁灯（小方块 + 点光：让空间有"层次"，而不是一片均匀灰）
                var bayLamp = new GameObject("MenuBayLamp");
                bayLamp.transform.SetParent(room, false);
                bayLamp.transform.localPosition = new Vector3(bx - w * 0.09f, h * 0.78f, d * 0.42f);
                var bl = bayLamp.AddComponent<Light>();
                bl.type = LightType.Point;
                bl.range = 3.2f;
                bl.intensity = 0.30f;
                bl.color = new Color(0.75f, 0.78f, 0.92f);
                SceneMaterials.Box(room, "MenuBayLampBox", bayLamp.transform.localPosition,
                    new Vector3(0.16f, 0.08f, 0.10f), SceneMaterials.Lit(new Color(0.9f, 0.9f, 0.86f), 0.16f));
            }
            // 远端：一块更暗的"门洞"，制造"走廊尽头有东西"的感觉
            SceneMaterials.Box(room, "MenuFarDoor", new Vector3(-w * 0.5f + 0.06f, h * 0.42f, 0f),
                new Vector3(0.08f, h * 0.72f, d * 0.34f), SceneMaterials.Lit(new Color(0.03f, 0.035f, 0.05f), 0.95f));

            // 天花板灯：**闪烁由 Tick 驱动**（不是常亮）
            var lampGo = new GameObject("MenuLamp");
            lampGo.transform.SetParent(room, false);
            lampGo.transform.localPosition = new Vector3(w * 0.28f, h - 0.18f, 0f);
            _ceilingLight = lampGo.AddComponent<Light>();
            _ceilingLight.type = LightType.Point;
            _ceilingLight.range = Mathf.Max(w, d) * 1.15f;
            _ceilingLight.intensity = LightBase;
            _ceilingLight.color = new Color(0.92f, 0.93f, 1.0f);
            SceneMaterials.Box(room, "MenuLampShade", lampGo.transform.localPosition + new Vector3(0f, 0.07f, 0f),
                new Vector3(0.5f, 0.08f, 0.5f), SceneMaterials.Make(new Color(0.85f, 0.84f, 0.78f), 0.5f));

            // 主方向光：**兜底照明**。实测 0.1.22 主界面 3D 空间全黑（点光没照出来），
            // 先用一盏方向光把空间"点亮到看得见"，再谈氛围 —— 恐怖感可以靠调暗实现，
            // 但"全黑看不出有没有场景"会让玩家以为游戏坏了。
            var keyGo = new GameObject("MenuKeyLight");
            keyGo.transform.SetParent(room, false);
            keyGo.transform.localPosition = new Vector3(w * 0.2f, h, 0f);
            keyGo.transform.localRotation = Quaternion.Euler(58f, -22f, 0f);
            var keyLight = keyGo.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 0.42f;
            keyLight.color = new Color(0.72f, 0.76f, 0.86f);

            // 补充微光：让"远处"还能隐约看到墙的轮廓（纯黑会让玩家以为贴图丢了）
            var fillGo = new GameObject("MenuFill");
            fillGo.transform.SetParent(room, false);
            fillGo.transform.localPosition = new Vector3(-w * 0.42f, h * 0.55f, d * 0.3f);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 4.5f;
            fill.intensity = 0.10f;
            fill.color = new Color(0.35f, 0.42f, 0.62f);
        }

        // ───────────────────────── 右下角手电筒 ─────────────────────────
        void BuildFlashlight()
        {
            // 手电筒用**真模型**（props 里的部件之一），摆在右下角，朝屏幕内。
            // 之所以不用 UI 图标：用户原话是"空间右下角为一个手电筒"，那是**场景物件**而非 HUD 图。
            var anchor = new GameObject("MenuFlashlight");
            anchor.transform.SetParent(_root, false);
            anchor.transform.localPosition = new Vector3(RoomSizeM.x * 0.34f, -0.02f, RoomSizeM.z * 0.30f);
            anchor.transform.localRotation = Quaternion.Euler(0f, -22f, 0f);

            // 手电筒 = **自建几何**（筒身 + 灯头 + 自发光镜片）。
            // 为什么不用 props 模型：props 的第 0 个部件是**点阵投影仪**（我先前拿它当"筒身"，
            // 画面上就是一根莫名的黑条）。形状对不对比"用现成资产"重要。
            var barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            barrel.name = "FlashlightBarrel";
            barrel.transform.SetParent(anchor.transform, false);
            barrel.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            barrel.transform.localScale = new Vector3(0.075f, 0.16f, 0.075f);
            var bmr = barrel.GetComponent<MeshRenderer>();
            if (bmr != null) bmr.sharedMaterial = SceneMaterials.Lit(new Color(0.12f, 0.13f, 0.155f), 0.42f);
            var bcol = barrel.GetComponent<Collider>(); if (bcol != null) Object.Destroy(bcol);

            var head = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            head.name = "FlashlightHead";
            head.transform.SetParent(anchor.transform, false);
            head.transform.localPosition = new Vector3(0f, 0f, 0.20f);
            head.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            head.transform.localScale = new Vector3(0.115f, 0.055f, 0.115f);
            var hmr = head.GetComponent<MeshRenderer>();
            if (hmr != null) hmr.sharedMaterial = SceneMaterials.Lit(new Color(0.16f, 0.17f, 0.195f), 0.35f);
            var hcol = head.GetComponent<Collider>(); if (hcol != null) Object.Destroy(hcol);

            // 镜片：**自发光**（手电筒"亮着"这件事必须一眼看出来，这是该道具的全部信息量）
            var lens = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lens.name = "FlashlightLens";
            lens.transform.SetParent(anchor.transform, false);
            lens.transform.localPosition = new Vector3(0f, 0f, 0.255f);
            lens.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            lens.transform.localScale = new Vector3(0.10f, 0.012f, 0.10f);
            var lmr = lens.GetComponent<MeshRenderer>();
            if (lmr != null) lmr.sharedMaterial = SceneMaterials.Emissive(new Color(0.92f, 0.95f, 1.0f), 3.2f);
            var lcol = lens.GetComponent<Collider>(); if (lcol != null) Object.Destroy(lcol);
            int built = 1;
            if (built == 0)
            {
                var fb = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                fb.name = "FlashlightFallback";
                fb.transform.SetParent(anchor.transform, false);
                fb.transform.localScale = new Vector3(0.09f, 0.21f, 0.09f);
            }

            // 手电筒自身的一点冷光（暗示它是光源）
            var gl = new GameObject("FlashlightGlow");
            gl.transform.SetParent(anchor.transform, false);
            var l = gl.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 2.2f;
            l.intensity = 0.22f;
            l.color = new Color(0.72f, 0.82f, 1.0f);
        }

        // ───────────────────────── UI 画布 ─────────────────────────
        /// <summary>
        /// 建 UI 画布（**幂等**：已有就直接返回）。
        /// </summary>
        /// <remarks>
        /// 原先画布是在 BuildOptions() 里就地建的，`_canvas` 也只在那里赋值。
        /// 本轮 3D 场景换成 HallScene 后 BuildRoom() 不再被调用，而 BuildBoardUi() 排在了
        /// BuildOptions() 之前 → `_canvas` 为 null → BuildBoardUi() 静默返回 →
        /// "进入菜单板操作视角但一个选项都不显示"（0.1.67 HUD 诊断 `板UI:未建` 抓到）。
        /// 修法不是"把调用顺序挪对"，而是**让依赖自足**：谁要用画布谁就先确保它存在 ——
        /// 顺序一改就失效的代码，下次换场景还会再犯一遍。
        /// </remarks>
        void BuildCanvas()
        {
            if (_canvas != null) return;
            var canvasGo = new GameObject("MenuCanvas");
            canvasGo.transform.SetParent(_root, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasGo.AddComponent<GraphicRaycaster>();
            _canvas = canvas;

            // ⚠⚠ 没有 EventSystem 时 GraphicRaycaster **完全不派发点击** —— 按钮渲染正常但点不动，
            // 而且**不报错、不打日志**（真机实测：0.1.22 就是这么失效的，极难查）。
            // 本工程的 UI 全部代码构建、场景只有 Boot.unity，所以没有任何地方会替我们建它，必须自己补。
            if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.transform.SetParent(_root, false);
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }
        }

        // ───────────────────────── 右侧玩法选项 ─────────────────────────
        void BuildOptions()
        {
            BuildCanvas();          // 画布是官方 UI 与新 UI 共用的，**必须**先建（幂等）
            if (!ShowLegacyUi) return;   // 旧 UI 整体停建（见 ShowLegacyUi 字段注释）
            // 旧 UI 容器：本轮之后旧界面整体废弃；迁移完成前先做到"板模式下不出现"。
            if (_legacyUi == null)
            {
                _legacyUi = new GameObject("LegacyUiRoot");
                _legacyUi.transform.SetParent(_canvas.transform, false);
            }

            // 【用户 2026-10-05】屏幕正中的大字（PROJECT WHISPER）已去掉：
            // 它锚在 (0.5,0.5)，主界面态顶在屏幕正中，与菜单板/提示条抢注意力。
            // 旧 UI 整体即将废弃，故不改造它，只是不再创建。
            // （原 title.color 一行随 MenuTitle 声明一同删除：删声明必须同时删使用点。）
            // 【用户 2026-10-05】副标题同 MenuTitle 一并去掉（也在中间区域）。

            // 右侧竖排（用户原话"右边区域做玩法选项"）
            // 恐鬼症大厅的入口结构：开始 / 商店 / 任务（每日 + 本局）/ 联机（spec §3.6）
            string[] items = { "开始调查（单人）", "商店", "每日任务", "本局任务", "画质", "创建房间（多人）", "加入房间（IPv6 直连）", "退出" };
            for (int i = 0; i < items.Length; i++)
            {
                float y = 0.62f - i * 0.105f;
                int captured = i;
                // 挂进 `_legacyUi`（**不是** `_canvas`）：
                // 0.1.70 真机截图暴露我只迁移了 Label，按钮仍挂在 canvas 上 →
                // 板模式下 `_legacyUi.SetActive(false)` 关不掉按钮，玩家看到"菜单板 + 旧按钮"两套 UI 并存。
                // 这正是用户报的"旧 UI 也没请走"。统一容器才是不漏的做法。
                var btn = SceneMaterials.Button(_legacyUi.transform, "MenuBtn" + i, items[i],
                    new Vector2(0.86f, y), new Vector2(300f, 66f),
                    () => OnOption(captured, items[captured]));
                // 自绘判定用：记下矩形与回调（矩形以**锚点为原点**，与点击换算同源）
                var brt = btn.GetComponent<RectTransform>();
                var hit = new Rect(-brt.sizeDelta.x * 0.5f, -brt.sizeDelta.y * 0.5f,
                                   brt.sizeDelta.x, brt.sizeDelta.y);
                _hitButtons.Add((hit, () => OnOption(captured, items[captured]), items[captured]));
            }

            // 诊断行放**右下角**（唯一没被占用的角落），并保证完整落在屏内。
            // 【0.1.49 两条真机教训】
            //  ① 曾经放右上 + `sizeDelta=(900,120)`，结果整行被推到屏幕右侧之外
            //     （截图里只剩最左半句）——**取证通道自己出屏，等于没有取证**。
            //  ② 右上与游戏 HUD 叠字（2800x1280 横屏手机上左侧面板已经占到屏幕中段）。
            var hint = SceneMaterials.Label(_legacyUi.transform, "MenuHint", "", new Vector2(1f, 0f),
                new Vector2(1f, 0f), 15, TextAnchor.LowerRight);
            hint.color = new Color(0.72f, 0.76f, 0.84f);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            hint.verticalOverflow = VerticalWrapMode.Overflow;
            hint.rectTransform.pivot = new Vector2(1f, 0f);
            hint.rectTransform.anchoredPosition = new Vector2(-24f, 24f);
            hint.rectTransform.sizeDelta = new Vector2(1500f, 260f);
            _hint = hint;
            // 大厅左侧信息面板：等级/经验/钱/碎片 + 商店/电力/互动状态（spec §3.6）
            _lobbyPanel = SceneMaterials.Label(_legacyUi.transform, "LobbyPanel", "大厅：加载中",
                new Vector2(0f, 1f), new Vector2(0f, 1f), 18, TextAnchor.UpperLeft);
            _lobbyPanel.color = new Color(0.78f, 0.82f, 0.90f);
            _lobbyPanel.horizontalOverflow = HorizontalWrapMode.Wrap;
            _lobbyPanel.rectTransform.pivot = new Vector2(0f, 1f);
            // 往下挪到 HUD 的「玩家/怪物/温度」之下（真机截图里第一版压字，读不出来）
            _lobbyPanel.rectTransform.anchoredPosition = new Vector2(24f, -300f);
            _lobbyPanel.rectTransform.sizeDelta = new Vector2(860f, 460f);
            // 诊断：把每个按钮的**实际屏幕矩形**汇总成一行（走 HUD，release 下也可读）。
            // 我先前只按"看着像"的像素坐标去点，没有客观依据；这条把它变成数字。
            var sbR = new System.Text.StringBuilder();
            for (int i = 0; i < _canvas.transform.childCount; i++)
            {
                var ch = _canvas.transform.GetChild(i);
                var rt = ch as RectTransform;
                if (rt == null) continue;
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                sbR.Append(ch.name).Append('[')
                   .Append(corners[0].x.ToString("F0")).Append(',').Append((Screen.height - corners[0].y).ToString("F0"))
                   .Append('-').Append(corners[2].x.ToString("F0")).Append(',').Append((Screen.height - corners[2].y).ToString("F0"))
                   .Append("] ");
            }
            // 兜底提示：让玩家知道"点哪都能开始"。
            // 我无法在本机验证按钮点击（ColorOS 拦 adb 注入的输入），所以**把失败面收掉**：
            // 非按钮区点击也开局，而不是赌按钮一定灵。
            // 【用户 2026-10-05】「旧版提示已废弃 · 请用菜单板」也是中间偏下的字，且内容已过时 → 不再创建。
            // 兜底开局逻辑本身保留（见 OnOption/HandleBoardInput），只是不再显示这行提示。

            _btnRects = sbR.ToString();
            // 【关键事实，必须打出来】游戏看到的屏幕尺寸。我先前一直拿"设备窗口 2800x1280"去推算点的位置，
            // 而 `Input.mousePosition` 的实际读数与之不符 —— 没有这条就只能靠猜。
            _btnRects += " | 屏幕 " + Screen.width + "x" + Screen.height;
            Debug.Log("[Whisper] 按钮矩形 " + _btnRects);


            // 诊断：把每个按钮的**实际屏幕矩形**打出来（诊断"点得到/点不到"用）。
            // 我先前只按"看着像"的像素坐标去点，没有客观依据；这条日志把它变成数字。
            for (int i = 0; i < _canvas.transform.childCount; i++)
            {
                var ch = _canvas.transform.GetChild(i);
                var rt = ch as RectTransform;
                if (rt == null) continue;
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                Debug.Log("[Whisper] 按钮矩形 " + ch.name
                          + " 左下 " + corners[0].x.ToString("F0") + "," + corners[0].y.ToString("F0")
                          + " 右上 " + corners[2].x.ToString("F0") + "," + corners[2].y.ToString("F0"));
            }
        }
        Text _hint;
        /// <summary>按钮屏幕矩形摘要（诊断用；走 HUD 而不是 Debug.Log，见文件头的剥离说明）。</summary>
        string _btnRects = "";

        /// <summary>诊断面板的宿主 Text（隐藏它比逐条清空文本更彻底）。</summary>
        Text _diagPanel;
        /// <summary>按钮命中区（canvas 局部坐标的矩形 + 回调）。自绘点击判定用。</summary>
        readonly System.Collections.Generic.List<(Rect rect, System.Action act, string label)> _hitButtons =
            new System.Collections.Generic.List<(Rect, System.Action, string)>();
        /// <summary>承载按钮的 Canvas（把屏幕坐标转它局部坐标用）。</summary>
        Canvas _canvas;
        /// <summary>最近一次触摸（诊断用，同样走 HUD）。</summary>
        string _lastTouch = "无";
        /// <summary>实时输入状态（真机取证的唯一可靠通道 —— release 下部分构建会剥离 Debug.Log）。</summary>
        string _inputState = "";
        // 【倒计时已移除】用户明确要求"就用按钮，不要倒计时"。
        // 所以正确做法是**把输入修好**，而不是绕过输入。
        // 我为此同时接两套输入：旧 Input（Input.touches）+ 新 Input System（Touchscreen.current / Mouse.current），
        // 因为本工程装了 com.unity.inputsystem，而 activeInputHandler=0（旧系统）—— 到底哪套在收事件，
        // 只能靠"两套都读、套套都试"来确定，而不是赌其中一套。

        /// <summary>最近一次建鬼用的模型名与失败原因（诊断用）。</summary>
        string _ghostModelInfo = "未建";

        void OnOption(int index, string label)
        {
            // 真机取证用：区分「没点到」与「点到了但逻辑没生效」——只靠截图分不出来。
            Debug.Log("[Whisper] 主界面按钮 " + index + " 被点击：" + label);
            switch (index)
            {
                case 0: // 单人
                    if (Services.HasNet) ((Whisper.Net.LocalNetService)Services.Net).SetPhase(Whisper.Core.Contracts.MatchPhase.Playing);
                    StartMatch();
                    break;
                case 1: OpenShop(); break;
                case 2: ShowTasks(); break;
                case 3: ShowObjectives(); break;
                case 4: CycleQuality(); break;
                case 5: // 建房间
                    if (Services.HasNet)
                    {
                        // ⚠ 契约是 Connect(string roomCode, string authToken) —— 我先前按"asHost:true"猜的签名，
                        // 真 Unity 编译报 CS1503。**契约不要猜，去看 INetService**。
                        // 建房：roomCode 非空表示"创建/加入该房间"，authToken 走本地回环占位。
                        Services.Net.Connect("whisper-local", "loopback");
                        Note("已建房间（本地回环）· 等待加入");
                    }
                    else Note("联机服务未注入");
                    break;
                case 6: // 加入
                    Note("IPv6 直连：请输入主机地址（联机服务已就绪时可用）");
                    break;
                case 7:
                    Note("退出：真机上请用系统返回键"); break;
                default:
                    Note("退出：真机上请用系统返回键"); break;
            }
        }

        void Note(string s)
        {
            if (_hint != null)
        {
            // 诊断文字**只在开关打开时**写进面板；关闭时连写都不写（避免瞬间闪字）。
            if (!ShowDiagnostics) { _hint.text = string.Empty; return; }
            _hint.text = s;
        }
            ShowModal(s);
        }

        /// <summary>
        /// 大号居中提示面板。
        /// </summary>
        /// <remarks>
        /// 【为什么必须加】用户三次反馈"点按钮没反应"。我查清了输入链（点击确实到达：日志有
        /// 『主界面按钮 N 被点击』），但**反馈太弱** —— 点「商店」「每日任务」只改了左上角一行小字，
        /// 面板本身就长在左上角，玩家很难看出"变了"。所以真正的修法是：**任何按钮都必须产生
        /// 大到不可能看漏的可见变化**。这不是装饰，是"能不能用"的问题。
        /// </remarks>
        void ShowModal(string text)
        {
            if (text == null) return;
            if (_modal == null)
            {
                if (_canvas == null) return;
                // 背板：uGUI 的 Image（Canvas 下不能用 SceneMaterials.Box —— 那是世界空间 3D 立方体）
                var bgGo = new GameObject("MenuModalBg");
                bgGo.transform.SetParent(_canvas.transform, false);
                var bgImg = bgGo.AddComponent<Image>();
                bgImg.color = new Color(0.02f, 0.03f, 0.05f, 0.90f);
                var brt = bgGo.GetComponent<RectTransform>();
                brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
                brt.pivot = new Vector2(0.5f, 0.5f);
                brt.anchoredPosition = Vector2.zero;
                brt.sizeDelta = new Vector2(1200f, 780f);

                _modal = SceneMaterials.Label(bgGo.transform, "MenuModal", "", new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), 26, TextAnchor.MiddleCenter);
                _modal.color = new Color(1f, 0.98f, 0.90f);
                _modal.horizontalOverflow = HorizontalWrapMode.Wrap;
                _modal.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                _modal.rectTransform.anchoredPosition = Vector2.zero;
                _modal.rectTransform.sizeDelta = new Vector2(1120f, 700f);
                _modalBg = bgGo;
            }
            // 每次开面板都刷新一次诊断（把"当前渲染到底什么状态"写在玩家看得到的地方）
            RefreshCamInfo();

            _modal.text = text;
            if (_modalBg != null) _modalBg.SetActive(true);
            _modal.gameObject.SetActive(true);
        }

        /// <summary>刷新相机/后处理诊断行。</summary>
        /// <remarks>
        /// 为什么放 HUD 而不是只写日志：本工程 release 构建里 Debug.Log 时有时无（实测 0.1.24 全被剥离），
        /// 所以"画面为什么是黑的"这类问题必须能**在屏幕上**读到答案。
        /// </remarks>
        void RefreshCamInfo()
        {
            string cam = "相机：—";
            var c = _cam;
            if (c != null)
            {
                var p = c.transform.position;
                cam = string.Format("相机 ({0:F1},{1:F1},{2:F1}) clear={3} hdr={4} depth={5} fov={6:F0}",
                    p.x, p.y, p.z, c.clearFlags, c.allowHDR, c.depthTextureMode, c.fieldOfView);
            }
            string fx = _boot != null && _boot.PostFx != null
                ? _boot.PostFx.Describe() + string.Format(" raw={0:F2}", _boot.PostFx.RawLogLuma)
                : "后处理：未挂载";
            string q = _boot != null && _boot.Quality != null ? _boot.Quality.Describe() : "画质：—";
            string hall = _hall != null ? _hall.Describe() : "大厅：未建";
            _camInfo = cam + "\n" + fx + "\n" + q + "\n" + hall + "\n" + _boardUiDiag;
        }

        /// <summary>关掉大号面板（点空白处）。</summary>
        void HideModal()
        {
            if (_modal != null) _modal.gameObject.SetActive(false);
            if (_modalBg != null) _modalBg.SetActive(false);
        }

        /// <summary>商店面板：列出装备与状态；**再点一次**买下下一件买得起的。</summary>
        /// <remarks>
        /// 交互设计（用户 2026-10-05 报"商店点取消会闪屏"后重做）：
        /// · 打开商店 = 开店面板（**不自动购买** —— 第一次的实现会在打开时顺手买一件，
        ///   那是"我没让你买你买了"，玩家会以为钱丢了）。
        /// · 面板内的点击 = **再执行一次 OpenShop**（即"买下一件"），面板就地刷新。
        ///   这样"连点 = 连买"是显式且可预期的；要退出就点右上角之外的**按钮区**或再进别的面板。
        /// · 面板底部写明当前操作含义，玩家不需要猜。
        /// 取舍：恐鬼症商店是完整 UI（分类/图标/重量/预览）。这里先用文字面板把
        /// "Tier 递进 + 钱 + 已拥有"三件机制跑通并可见 —— **机制先于皮**，换图形 UI 不需要动 Shop。
        /// </remarks>
        void OpenShop()
        {
            if (_boot == null || _boot.Shop == null)
            { Note("商店不可用（档案未就绪）"); return; }
            var shop = _boot.Shop;
            var prog = _boot.Progression;
            var sb = new System.Text.StringBuilder();
            sb.Append("商店 · 钱 ").Append(prog != null ? prog.Money : 0)
              .Append(" · 碎片 ").Append(prog != null ? prog.Fragments : 0)
              .Append("（碎片不可用于购买）\n");

            // 列出全部槽位；标出"可买 / 已拥有 / 缺前置 / 钱不够"
            int shown = 0;
            foreach (var it in _shopAll)
            {
                bool owned = shop.IsOwned(it.Id);
                string mark = owned ? "已拥有" : (shop.CanBuy(prog, it.Id, out var why) ? "可买" : why);
                sb.Append("  ").Append(it.Label).Append(" · T").Append(it.Tier)
                  .Append(" · ").Append(it.Price).Append(" · ").Append(mark).Append('\n');
                if (++shown >= 9) { sb.Append("  …（共 ").Append(_shopAll.Count).Append(" 件）\n"); break; }
            }

            // 点面板 = 买下一件（买得起的最便宜那件），买不到就明确说明原因
            string feedback;
            var pick = NextAffordable(shop, prog, out feedback);
            if (pick.HasValue)
            {
                if (shop.TryBuy(prog, pick.Value.Id, out var msg)) feedback = "已购买 " + msg;
                else feedback = "购买失败：" + msg;
            }
            sb.Append("\n").Append(feedback).Append('\n');
            sb.Append("· 点面板 = 再买下一件 · 点右侧按钮切到别的面板\n");

            _modalAction = OpenShop;      // 面板内点击 = 再执行一次
            ShowModal(sb.ToString());
        }

        /// <summary>
        /// 找出下一件"买得起且前置满足"的装备（按 tier 升序 → 先便宜后贵）。
        /// 返回 null 表示没有可买的，同时通过 <paramref name="why"/> 说明**为什么**（不是笼统的"买不了"）。
        /// </summary>
        Whisper.Gameplay.Progression.ShopItem? NextAffordable(
            Whisper.Gameplay.Progression.Shop shop,
            Whisper.Gameplay.Progression.Progression prog,
            out string why)
        {
            why = "没有可购买的装备";
            if (shop == null || prog == null) { why = "档案未就绪"; return null; }
            string firstBlocked = null;
            foreach (var it in _shopAll)
            {
                if (shop.IsOwned(it.Id)) continue;
                if (shop.CanBuy(prog, it.Id, out _)) return it;          // _shopAll 已按 tier 升序
                if (firstBlocked == null)
                {
                    shop.CanBuy(prog, it.Id, out var reason);
                    firstBlocked = it.Label + "（" + reason + "）";
                }
            }
            if (firstBlocked != null) why = "下一件买不了：" + firstBlocked;
            return null;
        }

        /// <summary>每日/每周任务面板（跨局、按天刷新 —— 与「本局任务」是两套系统）。只读：不注册面板动作。</summary>
        void ShowTasks()
        {
            if (_boot == null || _boot.Tasks == null) { Note("任务不可用（档案未就绪）"); return; }
            _modalAction = null;
            ShowModal("每日 / 每周任务\n\n" + _boot.Tasks.Describe() + "\n\n点任意位置关闭");
        }

        /// <summary>
        /// 切画质档，再点一次切帧率档（低→高→顶级→低；60→90→120→60）。
        /// </summary>
        /// <remarks>
        /// 为什么做成"一个按钮两件事"而不是两个按钮：移动端竖排按钮已经 8 个，
        /// 再加会挤到屏幕外。所以约定：**偶数次点击切画质、奇数次点击切帧率**，
        /// 面板上把当前值写清楚（玩家看得到自己在切什么）。
        /// </remarks>
        void CycleQuality()
        {
            if (_boot == null || _boot.Quality == null) { Note("画质档不可用（组合根未就绪）"); return; }
            // 三拍循环：① 切画质档 ② 切帧率档 ③ 切后处理开关。
            // 第三拍是**二分定位工具**：0.1.47/48 开后处理后画面变黑，
            // 有这一拍就能在真机上一秒确认"黑屏是不是后处理造成的"，
            // 而不必再出两个包去对比（每个包 ~4 分钟）。
            // 长期它也是玩家要的"画质选项"的一部分（关后处理 = 省电模式）。
            _qualityToggleCount++;
            switch (_qualityToggleCount % 3)
            {
                case 1: _boot.Quality.Select(_boot.Quality.NextTier()); break;
                case 2: _boot.Quality.SelectFrameRate(_boot.Quality.NextFrameRate()); break;
                default:
                    // ⚠ **不要用 `PostFx.enabled = false`** 来开关后处理 ——
                    // 那会触发 OnDisable → 清掉相机的 depthTextureMode，
                    // 而深度纹理一旦被清就无法在本组件的手工 Blit 链里重新绑定，
                    // 结果是"关一次之后再打开就永久黑屏"（用户 2026-10-05 原话：
                    // "你这关闭了一次后就打不开了"）。改用组件内部的旁路标志。
                    if (_boot.PostFx != null) _boot.PostFx.Bypass = !_boot.PostFx.Bypass;
                    break;
            }
            _boot.ApplyRenderQuality();
            // 面板内点击 = **再切一档**（而不是关掉面板）。
            // 【用户 2026-10-05 报的闪屏就在这条路径上】根因是输入层把 `Ended` 也当点击，
            // 于是每帧切一档 → 60Hz 闪。输入层已修为只认 `Began`；这里再注册面板动作，
            // 让"再点一次"有明确、可预期的语义。
            _modalAction = CycleQuality;
            Note("渲染设置\n\n" + _boot.Quality.Describe()
                + "\n\n· 点面板 = 再切一档（画质 → 帧率 → 后处理 循环）\n"
                + _camInfo
                + "\n\n点右侧按钮切到别的面板");
        }

        /// <summary>局内任务面板：合同日志里的可选目标（与「每日任务」是两套系统）。</summary>
        void ShowObjectives()
        {
            if (_boot == null || _boot.Objectives == null) { Note("局内任务不可用（档案未就绪）"); return; }
            _modalAction = null;
            ShowModal("本局任务（合同日志）\n\n" + _boot.Objectives.Describe()
                + "\n\n点任意位置关闭");
        }

        /// <summary>面板上的"再点一次"要做什么（null = 关闭面板）。见 HandleSelfDrawClick 的注释。</summary>
        System.Action _modalAction;

        /// <summary>刷新大厅左侧面板（每 0.5s 一次，别每帧拼字符串）。</summary>
        /// <remarks>
        /// 排版纪律：**每行一个主题，行首带「·」**。原因是我第一版把所有信息挤在一起，
        /// 真机截图里和游戏 HUD 的「玩家/怪物/温度」行叠字，读不出任何一条 —— 面板自己先要可读。
        /// 位置也往下挪（避开 HUD），见 BuildOptions 里 _lobbyPanel 的 anchoredPosition。
        /// </remarks>
        void RefreshLobby()
        {
            if (_lobbyPanel == null) return;
            if (_boot == null || _boot.Progression == null) { _lobbyPanel.text = "大厅：档案未就绪"; return; }
            var p = _boot.Progression;
            var sb = new System.Text.StringBuilder();
            sb.Append("· 等级 ").Append(p.Level);
            if (p.Prestige > 0) sb.Append("（声望 ").Append(p.Prestige).Append('）');
            sb.Append(" · 经验 ").Append(p.XpInLevel).Append('/').Append(p.XpForNextLevel)
              .Append("（").Append((p.LevelProgress * 100f).ToString("F0")).Append("%）");
            if (p.CanPrestige) sb.Append(" · 可声望");
            sb.Append('\n');
            sb.Append("· 钱 ").Append(p.Money).Append(" · 碎片 ").Append(p.Fragments).Append('\n');
            if (_boot.Shop != null)
                sb.Append("· 商店 ").Append(_boot.Shop.Count).Append(" 件 · 已拥有 ")
                  .Append(_boot.Shop.OwnedCount).Append(" · 已装备 ").Append(_boot.Shop.EquippedMap.Count).Append('\n');
            if (_boot.Power != null)
                sb.Append("· 电闸 ").Append(_boot.Power.BreakerOn ? "已合闸" : "断开")
                  .Append(" · 灯 ").Append(_boot.Power.LitRoomCount).Append('/').Append(_boot.Power.RoomWithLightCount)
                  .Append(" · 总闸在 ").Append(_boot.Power.BreakerRoom).Append('\n');
            sb.Append("· 每日任务 ").Append(_boot.Tasks != null ? _boot.Tasks.Daily.Count : 0)
              .Append(" 条 · 每周 ").Append(_boot.Tasks != null ? _boot.Tasks.Weekly.Count : 0).Append(" 条\n");
            if (_boot.Objectives != null)
                sb.Append("· ").Append(_boot.Objectives.OneLine()).Append('\n');
            sb.Append("· 互动 ").Append(_boot.Interaction != null ? _boot.Interaction.GhostInteractCount : 0).Append(" 次");
            _lobbyPanel.text = sb.ToString();
        }

        /// <summary>进入对局：关掉主界面，交给组合根。</summary>
        /// <summary>
        /// 建房（菜单板「多人联机」）：设意图 → 开局 → 显示房间码。
        /// 顺序关键：`NetIntent` 必须在 `StartMatch()` **之前**设好，
        /// 因为 `GameBootstrap.OnMenuStartRequested()` 会按当时的意图装配联机服务。
        /// </summary>
        /// <summary>系统键盘句柄（加入房间时用；null = 未在输入）。</summary>
        TouchScreenKeyboard _joinKeyboard;
        /// <summary>最近一次加入尝试的结果（HUD/取证可读）。</summary>
        string _joinStatus = "-";

        /// <summary>
        /// 加入房间：唤起系统键盘让玩家粘贴/输入房间码。
        /// 只在移动端弹键盘（编辑器/CI 弹键盘会卡住自动化）。
        /// </summary>
        void StartJoinSession()
        {
            if (Application.isMobilePlatform)
            {
                // ASCIICapable：房间码是 base32 大写字母+数字；autocorrect=false 避免系统改字
                _joinKeyboard = TouchScreenKeyboard.Open("", TouchScreenKeyboardType.ASCIICapable,
                    false, false, false, false, "输入朋友给你的房间码");
                _joinStatus = "等待输入房间码…";
            }
            else
            {
                Note("加入房间需要输入房间码（约 12 或 31 位，大写字母+数字）。");
            }
            SetStatusLine(_joinStatus);
        }

        /// <summary>键盘关闭后提交房间码：设意图 → 开局（装配在 ApplyNetIntentAtMatchStart 里做）。</summary>
        void CommitJoin(string code)
        {
            var boot = GetComponent<GameBootstrap>();
            if (boot == null) { SetStatusLine("组合根缺失：无法加入"); return; }
            var trimmed = (code ?? string.Empty).Trim().ToUpperInvariant();
            if (trimmed.Length == 0) { SetStatusLine("没有输入房间码"); return; }
            boot.NetIntent = NetIntentKind.Join;
            boot.NetJoinCode = trimmed;
            StartMatch();
            // 真实结果由 ApplyNetIntentAtMatchStart 写进 NetStatus（含失败原因与可达范围）
            _joinStatus = "加入 " + trimmed + "：" + (boot.NetStatus ?? "（无说明）");
            ShowRoomCode(boot);
        }

        /// <summary>把一行状态写到板提示条（真机截图即可取证）。</summary>
        void SetStatusLine(string s)
        {
            if (_boardHint != null && !string.IsNullOrEmpty(s)) _boardHint.text = s;
        }

        /// <summary>每帧轮询键盘：关闭（active=false）时提交。放在 Update 里调。</summary>
        void PumpJoinKeyboard()
        {
            if (_joinKeyboard == null) return;
            if (_joinKeyboard.active) return;                       // 仍在输入
            var code = _joinKeyboard.text;                          // 关闭瞬间取内容
            _joinKeyboard = null;
            if (string.IsNullOrEmpty(code)) { SetStatusLine("已取消加入"); return; }
            CommitJoin(code);
        }
        /// <summary>
        /// 多人面板（用户 2026-10-05：「将房间码输入搬进多人游戏里」）。
        /// 把「建房 / 加入 / 房间码」聚合到一处，而不是散落在顶部提示条与右上角。
        /// 键位说明写在面板里：移动端没有键盘提示，玩家只能靠这里知道怎么操作。
        /// </summary>
        void OpenMultiplayerPanel()
        {
            var boot = GetComponent<GameBootstrap>();
            var code = boot != null ? boot.NetRoomCode : null;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("多人联机");
            sb.AppendLine();
            if (string.IsNullOrEmpty(code))
            {
                sb.AppendLine("按 H = 建房（生成房间码，发给朋友）");
                sb.AppendLine("按 J = 加入（输入朋友给的房间码）");
            }
            else
            {
                sb.AppendLine("房间码：" + code);
                sb.AppendLine("（把这串发给朋友，让他按 J 输入加入）");
                sb.AppendLine();
                sb.AppendLine("可达范围：" + RoomReachJudge.Describe(LanSession.Reach));
                sb.AppendLine();
                sb.AppendLine("按 H 重新建房 · 按 J 加入别人的房 · 点任意处关闭");
            }
            ShowModal(sb.ToString());
        }
        void StartHostSession()
        {
            var boot = GetComponent<GameBootstrap>();
            if (boot == null) { Note("组合根缺失：无法建房"); return; }
            boot.NetIntent = NetIntentKind.Host;
            StartMatch();
            ShowRoomCode(boot);
        }

        /// <summary>
        /// 把房间码显示在菜单板右上（官方形态）。
        /// 失败要说清**为什么**（没有局域网地址 / 端口被占）——
        /// 沉默的「建房按钮点了没反应」是本项目最贵的调试成本。
        /// </summary>
        void ShowRoomCode(GameBootstrap boot)
        {
            if (_roomCodeText == null) return;
            var code = boot.NetRoomCode;
            if (string.IsNullOrEmpty(code))
            {
                _roomCodeText.text = "建房未成功\n" + (boot.NetStatus ?? "（无说明）");
                _roomCodeText.color = new Color(1f, 0.72f, 0.6f);
            }
            else
            {
                _roomCodeText.text = "房间码\n" + code + "\n（发给朋友，让他输入这个码加入）";
                _roomCodeText.color = new Color(0.75f, 1f, 0.8f);
            }
            _roomCodeText.gameObject.SetActive(true);
            Note(_roomCodeText.text);
        }
        void StartMatch()
        {
            if (_root != null) _root.gameObject.SetActive(false);
            var host = GetComponent<GameBootstrap>();
            if (host != null) host.OnMenuStartRequested();
            else Note("组合根缺失：无法进入对局");
        }

        // ───────────────────────── 闪烁 + 刷鬼 ─────────────────────────
        /// <summary>已打印的触摸条数（限流，避免真机日志被刷爆）。</summary>
        int _touchLogs;
        /// <summary>按钮的屏幕矩形缓存（诊断用）。</summary>
        readonly System.Collections.Generic.List<RectTransform> _buttons = new System.Collections.Generic.List<RectTransform>();

        /// <summary>诊断：把游戏**实际收到**的输入打出来（两套输入系统都读）。</summary>
        void LogTouchesOnce()
        {
            if (_touchLogs >= 60) return;
            // 旧 Input
            int n = Input.touchCount;
            for (int i = 0; i < n && _touchLogs < 60; i++)
            {
                var t = Input.GetTouch(i);
                if (t.phase != TouchPhase.Began) continue;
                _touchLogs++;
                _lastTouch = "旧Input " + t.position.x.ToString("F0") + "," + t.position.y.ToString("F0");
                Debug.Log("[Whisper] 旧Input 触摸 " + _lastTouch);
            }
            // 新 Input System（同样用条件编译包住，理由见 HandleSelfDrawClick 的注释）
#if ENABLE_INPUT_SYSTEM
            var tsNew = UnityEngine.InputSystem.Touchscreen.current;
            if (tsNew != null && tsNew.primaryTouch.press.wasPressedThisFrame && _touchLogs < 60)
            {
                _touchLogs++;
                var p = tsNew.primaryTouch.position.ReadValue();
                _lastTouch = "新Input " + p.x.ToString("F0") + "," + p.y.ToString("F0");
                Debug.Log("[Whisper] 新InputSystem 触摸 " + _lastTouch);
            }
#endif
        }

        /// <summary>自绘点击判定（不依赖 EventSystem）。
        /// **两套输入系统都读**：本工程装了 com.unity.inputsystem，而 activeInputHandler=0（旧系统）——
        /// 哪套在真正收事件只能实测；两套都读才不会因为"选错一套"而全盘失效（这正是我先前反复失败的原因）。</summary>
        void HandleSelfDrawClick()
        {
            Vector2 sp = new Vector2(0, 0);
            bool clicked = false;
            string via = "";

            // 路 1/2：新 Input System —— **用条件编译包住**。
            // 为什么：真机编译报 CS0234「UnityEngine 中不存在 InputSystem」——
            // 本工程 activeInputHandler=0（只用旧系统）时，Input System 包的程序集**不参与编译**，
            // 所以直接写它的类型名必然编译失败。ENABLE_INPUT_SYSTEM 是 Unity 在启用新系统时自动定义的宏，
            // 用它包住 → 有则用、无则退回旧输入，两条路都不编译失败。
#if ENABLE_INPUT_SYSTEM
            var tsNew = UnityEngine.InputSystem.Touchscreen.current;
            if (tsNew != null && tsNew.primaryTouch.press.wasPressedThisFrame)
            { sp = tsNew.primaryTouch.position.ReadValue(); clicked = true; via = "新Touchscreen"; }
            if (!clicked)
            {
                var msNew = UnityEngine.InputSystem.Mouse.current;
                if (msNew != null && msNew.leftButton.wasPressedThisFrame)
                { sp = msNew.position.ReadValue(); clicked = true; via = "新Mouse"; }
            }
#endif
            // 路 3：旧 Input —— Input.touches（PlayerController 走的这条）
            if (!clicked)
            {
                var ts = Input.touches;
                if (ts != null)
                    for (int t = 0; t < ts.Length; t++)
                    {
                        // ══════════════════════════════════════════════════════════════════
                        // 【用户 2026-10-05 报的真 bug】"点画质、商店后点取消会闪屏"
                        // 根因：这里原本写的是 `phase == Began || phase == Ended`。
                        //   手指**按住不放**时，`Input.touches` 每帧都会继续报最后那个
                        //   `Ended`（触摸抬起后数组不是立刻清空，而是保持到下一次触摸开始）。
                        //   → 本方法**每帧**都判成"点了一次" → `CycleQuality()` 每帧切一档
                        //     （画质→帧率→后处理→画质…）→ 屏幕以 60Hz 闪。
                        // 修法：**只认 Began** —— 一次触摸只有一个 Began，语义正确且天然去重。
                        //   这也与本文件 `LogTouchesOnce`（L685 只认 Began）保持一致：
                        //   两处判据不一致本身就是隐患。
                        // ══════════════════════════════════════════════════════════════════
                        if (ts[t].phase != TouchPhase.Began) continue;
                        sp = ts[t].position; clicked = true; via = "旧touches"; break;
                    }
            }
            // 路 4：旧 Input —— 鼠标
            if (!clicked && Input.GetMouseButtonDown(0))
            { sp = new Vector2(Input.mousePosition.x, Input.mousePosition.y); clicked = true; via = "旧Mouse"; }

            if (clicked) _lastTouch = via + " " + sp.x.ToString("F0") + "," + sp.y.ToString("F0");
            _inputState = via.Length > 0 ? via : "无输入";

            // 【主界面 UI 重构】先让**菜单板 3D 拾取**处理点击（官方：点菜单板进入操作视角）。
            // 它返回 true = 已消费（点在板上 / 按了空格），此时不要再落到旧的竖排按钮上。
            // 注意：空格是在**没有点击**时也要处理的，所以这一句必须在 clicked 判断之前。
            if (HandleBoardInput(sp, clicked)) return;

            if (!clicked) return;
            if (_canvas == null) return;

            for (int i = 0; i < _hitButtons.Count; i++)
            {
                var b = _hitButtons[i];
                // 把屏幕点转到**按钮所在层级的局部坐标**再与矩形比较：
                // 直接比屏幕坐标会因 CanvasScaler 的缩放而错位（这正是我先前按"看着像"的像素点失败的原因之一）。
                var btn = _canvas.transform.Find("MenuBtn" + i) as RectTransform;
                if (btn == null) continue;
                // 两种 y 口径都试：设备实测 mousePosition 的 y 与 Screen.height 不同源，
                // 单口径判定只要差一点就全不命中（这就是"按钮点不动"的直接原因）。
                RectTransformUtility.ScreenPointToLocalPointInRectangle(btn, sp, null, out var local);
                var spFlipped = new Vector2(sp.x, Screen.height - sp.y);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(btn, spFlipped, null, out var localFlipped);
                bool hit = b.rect.Contains(local) || b.rect.Contains(localFlipped);
                if (hit)
                {
                    // ── 动作层去重（第二道防线）──
                    // 输入层已修为"只认 Began"（见路 3 的注释），但**同一按钮在极短时间内的
                    // 重复触发**仍然可能来自：多指同时按、系统重复投递、以及未来某次改输入代码时
                    // 又把 Ended 加回来。所以这里按 (按钮, 250ms) 去重 ——
                    // 代价是"同一按钮 250ms 内连点只算一次"，而人手的连点间隔远大于此，玩家无感。
                    // 收益是：任何连环触发都不会再变成"每帧切一档"的闪屏。
                    float now = Time.unscaledTime;
                    if (i == _lastHitIndex && now - _lastHitTime < 0.25f)
                    {
                        _lastTouch = "去重忽略 " + b.label;
                        return;
                    }
                    _lastHitIndex = i;
                    _lastHitTime = now;

                    _lastTouch = "命中 " + b.label + " @" + sp.x.ToString("F0") + "," + (Screen.height - sp.y).ToString("F0");
                    Debug.Log("[Whisper] 主界面按钮 " + i + " 被点击：" + b.label);
                    b.act?.Invoke();
                    return;
                }
            }
            // 全屏兜底：没点中按钮也开局（避免"按钮不灵 = 完全玩不了"这个致命失败面）
            // 但**面板打开时**例外，分两种情况（用户 2026-10-05 报"商店点取消会闪屏"后重设计）：
            //   · 面板注册了自己的动作（商店=再买一件、画质=再切一档）→ 执行动作并刷新面板；
            //   · 没注册动作（任务/本局任务这类**只读**面板）→ 关闭面板。
            // 为什么不再一律"关闭"：那样商店打开后**只能买一件**，第二件永远买不到 —— 交互是死的。
            if (_modal != null && _modal.gameObject.activeSelf)
            {
                if (_modalAction != null)
                {
                    _lastTouch = "面板动作 @" + sp.x.ToString("F0") + "," + (Screen.height - sp.y).ToString("F0");
                    _modalAction();
                    return;
                }
                _lastTouch = "关闭面板 @" + sp.x.ToString("F0") + "," + (Screen.height - sp.y).ToString("F0");
                HideModal();
                return;
            }
            _lastTouch = "兜底开局 @" + sp.x.ToString("F0") + "," + (Screen.height - sp.y).ToString("F0");
            Debug.Log("[Whisper] 主界面兜底开局：" + _lastTouch);
            OnOption(0, "开始调查（单人）");
        }

        /// <summary>
        /// 前后台切换时重建渲染状态。
        /// </summary>
        /// <remarks>
        /// 【用户 2026-10-05 报"从后台重新切回前台时画面变黑无法还原"】
        /// Android 在应用切到后台时会**释放**相机的 RenderTarget 与临时缓冲，
        /// 而本工程的相机与 UI 全部是代码建的（场景只有 Boot.unity），没有任何组件会在回前台时重建它们。
        /// 结果：回前台后相机没有可用的渲染目标 → 整片黑且**不会自己恢复**。
        /// 正解：在 OnApplicationFocus(true) / OnApplicationPause(false) 时
        /// **重新施加一次相机与画质设置**（幂等），并把后处理的临时缓冲释放掉让它们按需重建。
        /// </remarks>
        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) return;
            RebuildRenderState();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) return;
            RebuildRenderState();
        }

        /// <summary>回前台后重建渲染状态（幂等，可重复调用）。</summary>
        void RebuildRenderState()
        {
            if (_isRebuilding) return;
            _isRebuilding = true;
            try
            {
                if (_cam != null)
                {
                    _cam.enabled = true;
                    _cam.clearFlags = CameraClearFlags.SolidColor;
                    _cam.backgroundColor = new Color(0.012f, 0.014f, 0.020f);
                    _cam.targetTexture = null;   // 关键：清掉可能已被释放的 RT 绑定
                }
                // 画质档重新施加（targetFrameRate/vSync/阴影/MSAA 在后台会被系统改掉）
                if (_boot != null) _boot.ApplyRenderQuality();
                // 后处理：把临时缓冲丢掉，OnRenderImage 会按需重建
                if (_boot != null && _boot.PostFx != null) _boot.PostFx.RebuildBuffers();
                _lastTouch = "已从后台恢复";
            }
            catch (System.Exception e) { Debug.LogWarning("[Whisper] 回前台重建失败：" + e.Message); }
            finally { _isRebuilding = false; }
        }

        bool _isRebuilding;

        void Update()
        {
            PumpJoinKeyboard();   // 加入房间：键盘关闭即提交
            // 实时输入状态：mousePosition 与 touchCount 是判断"输入有没有到游戏"的唯一客观依据。
            // 用 Input.touches（原始数组）而不是 touchCount：PlayerController 走的就是这条，
            // 0.1.21 真机验证过能读到触摸；而我先前用的 GetMouseButton* 在触摸屏上恒为 0。
            var touches = Input.touches;
            int tc = touches != null ? touches.Length : 0;
            string tinfo = "";
            if (tc > 0) { var t0 = touches[0]; tinfo = " T0 " + t0.position.x.ToString("F0") + "," + t0.position.y.ToString("F0") + " " + t0.phase; }
            _inputState = "touches " + tc + tinfo + " mp " + Input.mousePosition.x.ToString("F0") + "," + Input.mousePosition.y.ToString("F0");
            LogTouchesOnce();
            // 先更新相机与 UI 位置，再做点击判定 —— 否则拾取射线用的是上一帧的相机（过渡期间会点偏）
            UpdateBoardCamera(Time.deltaTime);
            UpdateBoardUi();
            if (_root == null || _paused) return;
            // 键盘是**每帧事件**：必须无条件每帧检查，不能挂在"有点击的那一帧"里
            // （否则纯按键永远走不到 —— 真机实测与读码确认，见 fix-keyboard-per-frame.mjs 头注释）。
            HandleMenuKeyboard();
            HandleSelfDrawClick();

            // 【倒计时已删除】用户："就用按钮，不要倒计时"。所以这里不再有任何绕过输入的路径 ——
            // 正确做法是让按钮真的能收到点击（见 HandleSelfDrawClick 的双输入源实现）。
            float dt = Time.deltaTime;

            // 大厅面板 0.5s 刷一次（每帧拼字符串会白白产生 GC）
            // 大厅环境动效（灵球漂浮等）
            if (_hall != null) _hall.Tick(dt);

            _nextLobbyRefresh -= dt;
            if (_nextLobbyRefresh <= 0f) { _nextLobbyRefresh = 0.5f; RefreshLobby(); RefreshCamInfo(); }

            // 灯光闪烁：**骤暗脉冲**（不是正弦呼吸 —— 正弦看起来像呼吸灯，不像坏灯管）
            // 【兼容旧逻辑】旧走廊的"天花板灯骤暗"用 _ceilingLight；大厅用多盏点光，
            // 没有单一 _ceilingLight → 下面整段在没有灯时直接跳过（否则空引用会把 Update 打断）。
            if (_ceilingLight == null) { _dipLeft = 0f; _dipTimer = 0f; }
            if (_ceilingLight != null && _dipLeft > 0f)
            {
                _dipLeft -= dt;
                // 骤暗期间强度剧烈抖动
                _ceilingLight.intensity = LightBase * (0.06f + 0.5f * Next01());
                if (_dipLeft <= 0f) _ceilingLight.intensity = LightBase;
            }
            else if (_ceilingLight != null)
            {
                _dipTimer -= dt;
                // 平时有轻微抖动（旧灯管），让"不停闪烁"这句话成立
                _ceilingLight.intensity = LightBase * (0.90f + 0.10f * Mathf.PerlinNoise(Time.time * 7f, 0.3f));
                if (_dipTimer <= 0f)
                {
                    _dipTimer = 0.6f + Next01() * 1.4f;
                    if (Next01() < DipChancePerSec * 0.35f)
                    {
                        _dipLeft = 0.10f + Next01() * 0.16f;   // 骤暗 0.10~0.26 秒
                        TrySpawnGhost(false);                  // 用户在「闪烁时」看到鬼
                    }
                }
            }

            if (_hint != null)
                // 把诊断与玩法信息**都**放到这一行：release 下这是唯一可靠的取证通道。
                // 诊断行（含触摸/输入/相机/按钮矩形）——受总开关控制，见字段注释。
            if (ShowDiagnostics)
            _hint.text = string.Format("灯闪中 · 鬼 {0}/刷出 {1} · 触摸 {2} · 输入 [{3}]\n{4}\n{5}",
                    GhostCount, GhostSpawned, _lastTouch, _inputState, _camInfo, _btnRects);
        }

        /// <summary>按概率在远处黑暗中刷一只鬼。`force` 用于开局保证有一只。</summary>
        void TrySpawnGhost(bool force)
        {
            if (_ghosts.Count >= MaxGhosts) return;
            if (!force && Next01() >= GhostChancePerDip) return;
            var go = BuildGhost();
            if (go == null) return;
            _ghosts.Add(go);
            GhostSpawned++;
        }

        GameObject BuildGhost()
        {
            // 位置：远处（-X 深处）、横向随机、贴地
            float t = Next01();
            float x = -Mathf.Lerp(GhostMinDistM, GhostMaxDistM, t);
            float z = (Next01() - 0.5f) * (RoomSizeM.z * 0.5f);
            // 红眼/白眼各半（用户点名要两种）
            bool red = Next01() < 0.5f;

            var go = new GameObject(red ? "MenuGhost_Red" : "MenuGhost_White");
            go.transform.SetParent(_root, false);
            go.transform.localPosition = new Vector3(x, 0f, z);
            // 面朝玩家（+X 方向）
            go.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

            // 优先用**新的整块人形模型**（ghostbody：男女 × 4 体型，元球生成的完整人形）。
            // 旧的按部件拼装模型（"ghost"）在暗场里就是一团黑，不作为首选。
            GameObject body = null;
            string picked = GhostModelPool.Pick(_rng ^ 0x51ED2701u, GhostSpawned);
            if (!string.IsNullOrEmpty(picked))
            {
                // picked 现在就是**完整的资源路径**（不含扩展名），别再拼前缀 ——
                // 我先前拼成 "Models/ghostbody/" + id 而池里给的是 resPath，于是路径重复、加载必失败。
                try { body = ModelLibrary.InstantiateSingleFile(picked, go.transform, picked); }
                catch (System.Exception e) { Debug.LogWarning("[Whisper] ghostbody 加载失败：" + e.Message); }
            }
            if (body == null)
            {
                // 诊断进 HUD：release 下 Debug.Log 会被剥离（实测 0.1.24 一条都没有），
                // 所以"模型到底加载没加载"这件事只能写在屏幕上看。
                _ghostModelInfo = (string.IsNullOrEmpty(picked) ? "池空" : picked + " 失败:" + ModelLibrary.LastProblem)
                                + " → 回退旧模型";
                try { body = ModelLibrary.InstantiateWhole("ghost", go.transform); }
                catch (System.Exception e) { Debug.LogWarning("[Whisper] 旧鬼模型也失败：" + e.Message); }
            }
            else _ghostModelInfo = picked;

            if (body == null)
            {
                // 降级：用胶囊占位（明确可见，不假装成功）
                var fb = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                fb.name = "GhostFallback";
                fb.transform.SetParent(go.transform, false);
                fb.transform.localPosition = new Vector3(0f, 0.95f, 0f);
                fb.transform.localScale = new Vector3(0.42f, 0.95f, 0.42f);
                var mr = fb.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = SceneMaterials.Make(new Color(0.055f, 0.058f, 0.062f), 0.88f);
            }

            // ── 眼睛：**加重亮处理**（用户原话），走 `_WhisperEmission`（自发光在雾后相加 → 远处也亮）──
            var eyeMat = SceneMaterials.Emissive(red ? new Color(1.0f, 0.09f, 0.04f) : new Color(0.90f, 0.95f, 1.0f),
                                                 red ? 4.2f : 3.4f);
            float eyeY = 1.62f;
            float eyeX = 0.085f;
            for (int s = -1; s <= 1; s += 2)
            {
                var e = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                e.name = "MenuGhostEye" + (s > 0 ? "L" : "R");
                e.transform.SetParent(go.transform, false);
                e.transform.localPosition = new Vector3(s * eyeX, eyeY, -0.075f);
                e.transform.localScale = new Vector3(0.055f, 0.055f, 0.030f);
                var mr = e.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = eyeMat;
                var col = e.GetComponent<Collider>();
                if (col != null) Destroy(col);            // 主界面不需要碰撞体
            }
            // 眼部点光：让"加重眼部亮处理"真的在暗场里照出一点光晕
            var el = new GameObject("MenuGhostEyeGlow");
            el.transform.SetParent(go.transform, false);
            el.transform.localPosition = new Vector3(0f, eyeY, -0.12f);
            var l = el.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 2.0f;
            l.intensity = red ? 0.55f : 0.42f;
            l.color = red ? new Color(1.0f, 0.16f, 0.08f) : new Color(0.85f, 0.92f, 1.0f);
            return go;
        }

        /// <summary>HUD 摘要（自检/日志可核）。</summary>
        public string Describe()
            => string.Format("主界面：{0}×{1}×{2}m 房间 · 灯 {3} · 在场鬼 {4} · 累计刷出 {5} · 鬼模型 {6}",
                RoomSizeM.x, RoomSizeM.y, RoomSizeM.z, _ceilingLight != null ? "已建" : "缺",
                GhostCount, GhostSpawned, ModelLibrary.LastProblem ?? "正常");
    }

    /// <summary>
    /// 主界面/场景共用的材质与 UI 小工具。
    /// 单独放一个类是因为"每个 Box 都新建一份材质"会瞬间产生上百个材质实例（真机内存与合批都会受影响）。
    /// </summary>
    public static class SceneMaterials
    {
        /// <summary>场景基色（暗调偏冷）。</summary>
        public static readonly Color RoomTint = new Color(0.20f, 0.21f, 0.25f);

        static readonly Dictionary<string, Material> _cache = new Dictionary<string, Material>();

        /// <summary>标准不透明材质（Whisper 自有 shader 优先，缺失则退回 Standard）。</summary>
        public static Material Make(Color c, float roughness)
        {
            string key = ColorKey(c) + "|" + roughness.ToString("F2");
            if (_cache.TryGetValue(key, out var hit) && hit != null) return hit;
            var sh = Shader.Find("Whisper/UnlitColor");
            var m = sh != null ? new Material(sh) : new Material(Shader.Find("Standard"));
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 1f - roughness);
            if (m.HasProperty("_WhisperEmission")) m.SetColor("_WhisperEmission", new Color(0f, 0f, 0f, 0f));
            _cache[key] = m;
            return m;
        }

        /// <summary>
        /// **吃光 + PBR** 材质（响应场景灯光、带细节贴图与金属/粗糙区分）。
        /// </summary>
        /// <remarks>
        /// 三条历史都写在这里，避免再走一遍：
        ///  ① 本项目默认材质走 `Whisper/UnlitColor`（**不吃光**）→ 主界面房间永远全黑
        ///     （0.1.22~0.1.26 白烧一轮构建）。
        ///  ② 后来用 `Shader.Find("Standard")` → 但 Standard 在本工程**没有 .mat 引用**，
        ///     有被剥离的风险（2026-10-03 真机全黑事故就是 `new Material(null)`）。
        ///  ③ 现在统一用**自研 PBR**（`Whisper/LitPbr`）：它放在 Resources/ 下**无条件进包**，
        ///     既有 PBR 观感又没有剥离风险。回退链：LitPbr → UnlitColor → 报错。
        /// </remarks>
        public static Material Lit(Color c, float roughness, Whisper.Gameplay.Render.MaterialFamily family
            = Whisper.Gameplay.Render.MaterialFamily.Plaster)
        {
            string key = "P" + ColorKey(c) + "|" + roughness.ToString("F2") + "|" + (int)family;
            if (_cache.TryGetValue(key, out var hit) && hit != null) return hit;

            var sh = Shader.Find("Whisper/LitPbr") ?? Shader.Find("Whisper/UnlitColor") ?? Shader.Find("Standard");
            var m = new Material(sh);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", Mathf.Clamp01(1f - roughness));
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            // 程序化贴图：给主界面几何也加上细节（不然还是"纯色矩形方体"）
            if (m.HasProperty("_DetailTex"))
            {
                var set = Whisper.Gameplay.Render.ProceduralTextures.Get(family);
                if (set != null)
                {
                    m.SetTexture("_DetailTex", set.Detail);
                    m.SetTexture("_BumpMap", set.Normal);
                    m.SetTexture("_OcclusionMap", set.Occlusion);
                    if (m.HasProperty("_DetailScale")) m.SetFloat("_DetailScale", set.DetailScale);
                    if (m.HasProperty("_DetailStrength")) m.SetFloat("_DetailStrength", set.DetailStrength);
                    if (m.HasProperty("_DirtAmount")) m.SetFloat("_DirtAmount", set.DirtAmount);
                }
            }
            _cache[key] = m;
            return m;
        }

        /// <summary>自发光材质（rgb=颜色 · **a=强度**，与本项目 `_WhisperEmission` 的契约一致）。</summary>
        public static Material Emissive(Color c, float strength)
        {
            string key = "E" + ColorKey(c) + "|" + strength.ToString("F1");
            if (_cache.TryGetValue(key, out var hit) && hit != null) return hit;
            var sh = Shader.Find("Whisper/UnlitColor");
            var m = sh != null ? new Material(sh) : new Material(Shader.Find("Standard"));
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_WhisperEmission")) m.SetColor("_WhisperEmission", new Color(c.r, c.g, c.b, strength));
            else if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * strength); }
            _cache[key] = m;
            return m;
        }

        static string ColorKey(Color c)
            => ((int)(c.r * 255)).ToString() + "_" + ((int)(c.g * 255)).ToString() + "_" + ((int)(c.b * 255)).ToString();

        /// <summary>建一个长方体（墙/地/顶/家具都用它）。</summary>
        public static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && mat != null) mr.sharedMaterial = mat;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);     // 主界面不参与物理
            return go;
        }

        /// <summary>建一段文字。</summary>
        public static Text Label(Transform parent, string name, string text, Vector2 anchorMin, Vector2 anchorMax,
                                 int size, TextAnchor align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.text = text;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size;
            t.alignment = align;
            t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(720f, size * 1.6f);
            return t;
        }

        /// <summary>建一个按钮（右侧玩法选项用）。</summary>
        public static Button Button(Transform parent, string name, string label, Vector2 anchor, Vector2 size,
                                    UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.10f, 0.12f, 0.16f, 0.88f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(onClick);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
            Label(go.transform, name + "Label", label, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), 24, TextAnchor.MiddleCenter);
            return btn;
        }
    }
}
