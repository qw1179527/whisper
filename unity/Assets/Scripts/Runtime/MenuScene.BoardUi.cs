using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Whisper.Net.Direct;

namespace Whisper.Runtime
{
    // 2026-10-06 从 MenuScene.cs 拆出（gate-code C5：单文件 ≤600 行）。
    // 按**方法边界**机械分块，方法体一行未改 —— 行为与拆分前等价。
    // MenuScene 是 partial（见 MenuScene.cs）。
    public sealed partial class MenuScene
    {
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
    }
}
