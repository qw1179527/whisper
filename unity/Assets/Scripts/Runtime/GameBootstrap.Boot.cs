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
    // 拆的是**内聚的一块**（Boot 与每帧、渲染质量、供电），不是按行数硬切：Boot/Tick*/渲染质量/总闸 —— 运行期"把系统接起来"的一组。
    // 用项目既有的 partial 惯例（同 GameBootstrap.Spawn.cs / .Temperature.cs）。
    public sealed partial class GameBootstrap
    {
        void Boot()
        {
            var t0 = System.Diagnostics.Stopwatch.StartNew();
            var lines = new System.Text.StringBuilder();
            AppendBootHeader(lines);

            if (!TryLoadConfig(lines)) return;
            if (!TryInstallServices(lines)) return;
            if (!TryLoadLevel(lines)) return;

            // ── 主界面先出：对局**等玩家点「开始调查」再建** ──
            // 为什么必须分开（真机实测）：我第一版让 Boot 一路跑完（建玩家/怪物/HUD）**同时**再建主界面，
            // 结果是"主界面标题与 HUD/走廊叠在一起"—— 两套 UI 同时活着，玩家看到的是混乱。
            // 正解：Boot 到"关卡已加载"为止 → 建主界面 → **停在这里**等 OnMenuStartRequested()。
            // 等级/商店/任务/电力/互动必须在**建主界面之前**就绪：主界面要读它们（否则面板显示"档案未就绪"）。
            InitProgressionSystems(lines);
            BuildMenu(lines);
            // 【可见性】BootLog 原先只在失败路径赋值 → 成功启动时 lines 没人看得到，
            // "玩法层已接线"这类证据等于没留。这里在成功路径也把它固化下来。
            lines.AppendLine(SessionStatus);
            FinishBoot(t0, lines);
        }

        /// <summary>把一段文字同时写进 HUD 与日志（HUD 可能尚未建好，故日志是兜底而不是唯一出路）。</summary>
        void AppendStatus(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Debug.Log("[Whisper] " + text);
            if (_status != null) _status.text = text;
        }

        /// <summary>建主界面（3D 空间 + 闪烁灯 + 右下角手电筒 + 概率刷鬼 + 右侧玩法选项）。</summary>
        /// <summary>每帧推进：鬼互动节拍 + 任务进度（鬼房停留秒数）。</summary>
        /// <summary>
        /// 驱动玩法层（本轮为**并行驱动**：既有简化逻辑照旧跑，玩法层同时推进并暴露状态）。
        /// 玩家位置每帧喂进去 —— 玩法层需要知道玩家在哪（安全区、证据点邻近、房间停留都在用它）。
        /// </summary>
        void TickSession()
        {
            if (Session == null) return;
            if (_player != null) { Session.PlayerX = _player.X; Session.PlayerZ = _player.Z; }
            // 安全区：组合根存的是 Bounds（Runtime 有 Unity），玩法层收**裸浮点**
            // （Whisper.Gameplay 不引用 UnityEngine —— 实测 CS0246）。
            var safe = _truckSafeZone;
            Session.TruckSafeCenterX = safe.center.x;
            Session.TruckSafeCenterZ = safe.center.z;
            Session.TruckSafeSizeX = safe.size.x;
            Session.TruckSafeSizeZ = safe.size.z;
            Session.Tick(Time.deltaTime);
            // 【不要写 _status】曾用 AppendStatus(SessionStatus) 每帧写 _status，那会**覆盖既有 HUD 诊断行**
            // （接口/关卡/玩家/交互次数那套真机取证通道）。改为由 HUD 刷新处统一追加 —— 见 _status.text 的
            // string.Format 里末尾那条 SessionStatus。
        }

        void TickInteractionAndTasks()
        {
            if (_interaction == null || _power == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // 确定性随机源（xorshift32）—— 禁 UnityEngine.Random：它依赖全局种子，跨端不一致
            float Roll()
            {
                _interactRng ^= _interactRng << 13; _interactRng ^= _interactRng >> 17; _interactRng ^= _interactRng << 5;
                return (_interactRng & 0xFFFFFF) / 16777216f;
            }

            // ⚠ `_ghostRoom` 是 `GhostRoom` **对象**，不是字符串（房间 id 在 `RoomId` 属性上）。
            // 我第一版直接把它当 string 用 → CS0029。这类"跨文件字段类型"错误语法预检抓不到，
            // 只有真 Unity 编译会报 —— 所以每轮改动都要过一次 EditMode/出包。
            string ghostRoom = _ghostRoom != null ? _ghostRoom.RoomId : null;
            float gx = 0f, gz = 0f;
            if (_monsters != null && _monsters.LastViews != null && _monsters.LastViews.Length > 0)
            { gx = _monsters.LastViews[0].X; gz = _monsters.LastViews[0].Z; }

            var rooms = new System.Collections.Generic.List<string>();
            if (Level != null) foreach (var r in Level.Rooms) rooms.Add(r.Id);

            _interaction.Tick(dt, Roll, ghostRoom, gx, gz, rooms);

            // 任务：在鬼房停留秒数（用玩家位置与鬼房比对）
            if (_tasks != null && _player != null && !string.IsNullOrEmpty(ghostRoom)
                && RoomIdAt(_player.X, _player.Z) == ghostRoom)
            {
                _ghostRoomDwellAcc += dt;
                if (_ghostRoomDwellAcc >= 1f) { _tasks.ReportGhostRoomDwell(1f); _ghostRoomDwellAcc -= 1f; }
            }
        }

        /// <summary>初始化等级/商店/任务/电力/互动（一次性；Boot 阶段调用）。</summary>
        void InitProgressionSystems(System.Text.StringBuilder lines)
        {
            EnsureConfig();
            var cfg = _cfg;   // 复用唯一那份（EnsureConfig 幂等；不新建第二个 reader）
            _progression = new Progression(cfg);
            _shop = new Shop();
            _shop.Load(cfg);
            _tasks = new TaskSystem(cfg);
            // ⚠ 任务按**日期种子**生成，但 gate-physics 禁止 DateTime 参与玩法判定
            // （跨端不一致）→ 这里用"会话序号"作为 dayIndex 的**可信来源占位**：
            // 联机时它应由网络层同步；单机用 0 表示"今天"。取到官方正文后再决定真实日历口径。
            _tasks.RollForDay(0);
            _power = new Whisper.Gameplay.Power.PowerSystem(cfg);
            InitRenderQuality(cfg);
            _objectives = new ObjectiveSystem(cfg);
            // **主界面就要能看到本局任务**（恐鬼症里合同日志是出发前读的，不是进场后才知道）。
            // 所以这里就用当前 MatchSeed 抽一次；StartMatch 会再按当时的 MatchSeed 抽 ——
            // 同种子幂等，不会换任务，玩家看到的就是进局后的那三条。
            _objectives.BeginContract((uint)MatchSeed ^ 0x5F3759DFu);
            _interaction = new Whisper.Gameplay.Interaction.InteractionSystem(cfg, _power);

            lines.AppendLine(_progression.Describe());
            lines.AppendLine(_shop.Describe());
            lines.AppendLine(_tasks.Describe().Split('\n')[0]);
            lines.AppendLine(_power.Describe());
            lines.AppendLine(_interaction.Describe());
            lines.AppendLine(_objectives.Describe().Split('\n')[0]);
            WirePowerToLights();
            PlaceBreaker();
        }

        /// <summary>
        /// 初始化渲染质量与后处理（用户永久约束 §2/§6）。
        /// </summary>
        /// <remarks>
        /// 三条关键点，每条都是"不这么做就静默失效"：
        /// ① **vSyncCount 必须为 0** —— 移动端 vSync 会覆盖 targetFrameRate，
        ///    不关就会出现"设了 120 却锁在 60"（而且不报错）。
        /// ② **后处理必须挂在同一台相机上**，且 allowHDR 打开（辉光需要 >1 的亮部余量）。
        /// ③ **帧率与画质是两条独立的轴**（用户并列提出）：换画质不该动帧率，反之亦然。
        /// </remarks>
        void InitRenderQuality(Whisper.Gameplay.Config.GameConfigReader cfg)
        {
            _quality = new Whisper.Gameplay.Render.RenderQuality(cfg);
            foreach (var p in _quality.ConfigProblems) Debug.LogWarning("[Whisper] 画质配置：" + p);

            int fps = cfg.Int("render.defaultFrameRate", 60);
            _quality.SelectFrameRate(fps);
            string key = cfg.String("render.defaultTier", "high");
            _quality.Select(key == "low" ? Whisper.Gameplay.Render.QualityTier.Low
                        : key == "top" ? Whisper.Gameplay.Render.QualityTier.Top
                        : Whisper.Gameplay.Render.QualityTier.High);

            if (_camera != null)
            {
                _postFx = _camera.gameObject.GetComponent<PostFx>();
                if (_postFx == null) _postFx = _camera.gameObject.AddComponent<PostFx>();
                _postFx.Quality = _quality;
                _camera.allowHDR = true;   // 辉光要有 >1 的亮部余量，否则高光被截断成死白
            }
            ApplyRenderQuality();
        }

        /// <summary>把当前档位**真的**施加到 Unity 的全局设置上。</summary>
        public void ApplyRenderQuality()
        {
            if (_quality == null) return;
            var t = _quality.CurrentTier;

            Application.targetFrameRate = (int)_quality.FrameRate;
            // vSyncCount=0 是 targetFrameRate 生效的前提（见 InitRenderQuality 的注释①）
            QualitySettings.vSyncCount = 0;

            QualitySettings.pixelLightCount = t.PixelLightCount;
            QualitySettings.shadows = t.Shadows <= 0 ? ShadowQuality.Disable
                                    : t.Shadows == 1 ? ShadowQuality.HardOnly : ShadowQuality.All;
            QualitySettings.shadowResolution = t.ShadowResolution <= 0 ? ShadowResolution.Low
                                             : t.ShadowResolution == 1 ? ShadowResolution.Medium
                                             : t.ShadowResolution == 2 ? ShadowResolution.High
                                             : ShadowResolution.VeryHigh;
            QualitySettings.shadowDistance = t.ShadowDistanceM;
            QualitySettings.antiAliasing = t.AntiAliasing;
            QualitySettings.anisotropicFiltering = t.AnisotropicFiltering <= 0 ? AnisotropicFiltering.Disable
                                                 : t.AnisotropicFiltering == 1 ? AnisotropicFiltering.Enable
                                                 : AnisotropicFiltering.ForceEnable;
            if (_camera != null)
            {
                _camera.farClipPlane = t.FarClipM;
                _camera.allowHDR = true;
            }
            Debug.Log("[Whisper] 画质已施加：" + _quality.Describe());
            // 相机自检：0.1.47 开后处理变黑时，"相机到底在哪、清屏模式是什么"是第一批要问的问题。
            // 这些值平时不打印（日志噪声），只在施加画质时打一次。
            if (_camera != null)
            {
                var cp = _camera.transform.position;
                var cf = _camera.transform.forward;
                Debug.Log(string.Format(
                    "[Whisper] 相机自检：pos=({0:F1},{1:F1},{2:F1}) forward=({3:F2},{4:F2},{5:F2}) clear={6} bg={7} hdr={8} depthTex={9} postFx={10}",
                    cp.x, cp.y, cp.z, cf.x, cf.y, cf.z, _camera.clearFlags, _camera.backgroundColor,
                    _camera.allowHDR, _camera.depthTextureMode, _postFx != null ? _postFx.enabled : false));
            }
        }

        /// <summary>把电力系统接到灯光上：总闸与房间开关的事件 → LightRig 的平滑开关。</summary>
        /// <remarks>
        /// 为什么用事件而不是每帧轮询：LightRig 内部已有 On→Target 的平滑过渡（ToggleSpeed），
        /// 每帧硬设 intensity 会把过渡打掉（灯会"跳"而不是"亮起来"）。
        /// </remarks>
        void WirePowerToLights()
        {
            if (_power == null) return;
            _power.OnBreakerChanged += on =>
            {
                if (_lightRig != null) _lightRig.SetAllLights(on);
            };
            _power.OnRoomLightChanged += (roomId, on) =>
            {
                if (_lightRig != null) _lightRig.SetRoomLights(roomId, on);
            };
        }

        /// <summary>把总闸放到关卡数据给的位置（没有就退回入口区，保证玩家找得到）。</summary>
        void PlaceBreaker()
        {
            if (_power == null || Level == null) return;
            // 关卡里没有专门的"总闸位置"字段 → 用**最深的房间**（离入口最远）作为总闸位置：
            // 这正是官方"必须深入才有电"的意图，而且是从数据推出来的，不是硬编码坐标。
            string far = null; float best = -1f;
            var entrance = Level.Rooms.Find(r => r.Id == (Level.Extraction?.Standard ?? ""));
            float ex = entrance?.MinX ?? 0f, ez = entrance?.MinZ ?? 0f;
            foreach (var r in Level.Rooms)
            {
                float dx = (r.MinX + r.MaxX) * 0.5f - ex, dz = (r.MinZ + r.MaxZ) * 0.5f - ez;
                float d = dx * dx + dz * dz;
                if (d > best) { best = d; far = r.Id; }
            }
            var target = Level.Rooms.Find(r => r.Id == far);
            if (target != null)
            {
                _power.PlaceBreaker(target.Id, (target.MinX + target.MaxX) * 0.5f, (target.MinZ + target.MaxZ) * 0.5f);
            }
            // 每个房间都登记"有灯"（LightRig 是按房间建灯的；没有灯的房间自然收不到 SetRoomLights 的效果）
            foreach (var r in Level.Rooms) _power.RegisterRoomLight(r.Id, true);
            Debug.Log("[Whisper] 总闸放在 " + far + "（离入口最远的房间）· 房间灯 " + Level.Rooms.Count + " 个");
        }
    }
}
