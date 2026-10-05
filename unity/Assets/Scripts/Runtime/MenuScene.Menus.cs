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
            // 【P0-3 改造】原实现只记 60 次、只认 Began、且只 Debug.Log。
            // 三个问题：① 60 次早早就用完了，真正复现时反而没有记录；
            //          ② 只认 Began 会漏掉"抬手"——而"按下去没反应"的现场恰恰要看 Ended/Moved；
            //          ③ Debug.Log 要连 adb 才看得到，而本机没有 PC ⇒ 必须落盘。
            // 现在：**全相位 + 无上限（由 ClickReceipt 自己滚动截断）+ 落盘**。
            const int MaxReceiptTouches = 8;   // 单帧最多记几条（多指同时按时防止刷屏）
            // 旧 Input
            int n = Input.touchCount;
            for (int i = 0; i < n && _touchLogs < 60; i++)
            {
                var t = Input.GetTouch(i);
                if (i >= MaxReceiptTouches) break;
                _lastTouch = "旧Input " + t.phase + " " + t.position.x.ToString("F0") + "," + t.position.y.ToString("F0");
                // 全相位落盘（不再只认 Began）—— "按下去没反应"要看得到 Ended/Moved 才定得了位
                ClickReceipt.Write("touch", "旧 " + t.phase + " id=" + t.fingerId + " @" + t.position.x.ToString("F0") + "," + t.position.y.ToString("F0"));
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
        /// <summary>
        /// 判定**本帧有没有点击**，以及走的哪条输入路径。
        ///
        /// 为什么单独成方法（2026-10-06）：原先是 HandleSelfDrawClick 内联的一大段，
        /// 加上 P0-3 的点击回执后该方法超出 gate-code C5 的 120 行上限。
        /// 抽出来还有一个好处：那套 `#if ENABLE_INPUT_SYSTEM` 的条件编译复杂度被隔离在这里，
        /// 主流程只剩"拿到点击结果 → 交给板面判定"两件事。
        /// </summary>
        bool ResolveClick(out Vector2 sp, out string via)
        {
            sp = new Vector2(0, 0);
            bool clicked = false;
            via = "";
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
            return clicked;
        }

        void HandleSelfDrawClick()
        {
            Vector2 sp = new Vector2(0, 0);
            bool clicked = ResolveClick(out Vector2 sp, out string via);

            if (clicked)
            {
                _lastTouch = via + " " + sp.x.ToString("F0") + "," + sp.y.ToString("F0");
                // 【P0-3 点击回执】记下**走的哪条输入路径**与坐标。
                // 为什么这条最关键："点击失灵"的三种可能（输入没到 / 到了但没命中 / 命中了但没执行）
                // 在这条回执上会呈现为完全不同的组合 —— 没有它就只能猜。
                ClickReceipt.Write("click", via + " @" + sp.x.ToString("F0") + "," + sp.y.ToString("F0"));
            }
            _inputState = via.Length > 0 ? via : "无输入";

            // 【主界面 UI 重构】先让**菜单板 3D 拾取**处理点击（官方：点菜单板进入操作视角）。
            // 它返回 true = 已消费（点在板上 / 按了空格），此时不要再落到旧的竖排按钮上。
            // 注意：空格是在**没有点击**时也要处理的，所以这一句必须在 clicked 判断之前。
            // 【P0-3 点击回执】板面**是否消费**了这次输入。
            // 负结果同样要记："点了但没命中任何控件"与"压根没收到点击"是两回事。
            bool consumed = HandleBoardInput(sp, clicked);
            ClickReceipt.Write("board", (consumed ? "consumed" : "miss") + " clicked=" + clicked + " @" + sp.x.ToString("F0") + "," + sp.y.ToString("F0"));
            if (consumed) return;

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
    }
}
