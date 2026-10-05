using System.Collections.Generic;
using Whisper.Core;                 // Services 定位器在 Whispers.Core（写成同文件内引用会 CS0103）
using Whisper.Core.Contracts;       // MatchPhase
using Whisper.Gameplay.Level;        // MiniJson（地图注册表读取用）           // LanSession / RoomReachJudge（零信令直连：可达范围与会话编排）
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
    }
}
