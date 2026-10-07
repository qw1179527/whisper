using UnityEngine;

namespace Whisper.Runtime
{
    /// <summary>
    /// **Play 循环与 HUD 刷新**（从 `GameBootstrap.cs` 拆出，`gate-code` C5：单文件 ≤600 行）。
    ///
    /// 拆的是**内聚的一块**（每帧心跳 + 状态文本刷新），不是按行数硬切 ——
    /// 沿用项目既有的 partial 惯例（同 `GameBootstrap.Boot.cs` / `.Lifecycle.cs` / `.Spawn.cs`）。
    ///
    /// ══════════════════════════════════════════════════════════════════════════════
    /// 这里曾经是**每帧抛 NRE** 的地方（2026-10-07 真机三张截图定位）
    /// ══════════════════════════════════════════════════════════════════════════════
    /// `Update()` 里 `_status.text = string.Format(...)` **直接解引用 `_status`**，而它可能为 null
    /// （`BuildUi()` 失败时）。于是：
    ///   · **每帧**抛一次 `NullReferenceException`
    ///   · 堆栈被 IL2CPP 内联成 `GameBootstrap.Boot()` / `GameBootstrap.Start()` **两帧假象**
    ///     ⇒ 我据此找错了地方一整轮（以为是 Boot 内部炸了）
    ///   · `Ticks` 停在 512（截图里的帧数）—— 因为 `Ticks++` 在异常之前，之后的逻辑全没跑
    ///   · 屏幕全黑无提示（失败路径自己也在依赖 `_status`）
    ///
    /// ⇒ 现在：**先判 null、只报一次**（不刷屏），原因进阶段看板。
    ///   "一个会静默或刷屏的失败路径，等于没有失败路径。"
    /// </summary>
    public sealed partial class GameBootstrap
    {
        void Update()
        {
            TickInteractionAndTasks();
            TickSession();
            TickAutoStart();     // 排查构建：无操作 15 秒后自动开局（绕过"按钮点不到"这个中间环节）

            if (!Booted) return;
            Ticks++;

            // 温度系统每帧推进（用真实 dt，不用 0.5s 的 HUD 节流 —— 温度是连续量，
            // 按 HUD 节流推进会让降温速率随帧率/刷新间隔漂移）。
            if (_temperature != null)
            {
                UpdateGhostRoomForTemperature();
                _temperature.Tick(Time.deltaTime);
            }

            if (Time.unscaledTime < _nextHudRefresh) return;
            _nextHudRefresh = Time.unscaledTime + 0.5f;
            // 【用户要求：去除所有小字，不留字体】关闭时**连写入都不做**，并把容器整个隐藏。
            // 这比"清空文本"彻底：空 Text 仍占位、仍可能留描边/阴影残影。
            if (!ShowDiagnostics)
            {
                if (_status != null && _status.gameObject.activeSelf) _status.gameObject.SetActive(false);
                return;
            }
            if (_status == null)
            {
                // ⚠ **这里原先直接解引用 `_status`，而它可能为 null** ⇒ `Update()` 每帧抛 NRE。
                // 真机症状（2026-10-07 三张截图）：`System.NullReferenceException`，
                // 堆栈只有 `GameBootstrap.Boot()` / `GameBootstrap.Start()` 两帧可读 ——
                // 那是 IL2CPP 把 Update 内联后的假象，我因此找错了地方一整轮。
                // 且 `Ticks` 停在 512（截图里的帧数），因为 `Ticks++` 在异常之前、但之后的逻辑全没跑。
                // ⇒ 只在**第一次**报错（不刷屏），并把原因送进阶段看板。
                if (!_statusMissingReported)
                {
                    _statusMissingReported = true;
                    BootStageBoard.SetError("Update：`_status` 为 null ⇒ HUD 刷新被跳过"
                        + "\n（根因是 BuildUi 没建出 HUD 文本；此前这里每帧抛 NRE，把堆栈也搅乱了）");
                    Debug.LogError("[Whisper] Update 时 _status 为 null —— HUD 刷新已跳过（不再每帧抛异常）");
                }
                return;
            }
            if (!_status.gameObject.activeSelf) _status.gameObject.SetActive(true);
            _status.text = string.Format(
                "Project Whisper · 运行中\nTick {0} · {1} fps · tickRate={2}\n{3}\n关卡 {4}：房间 {5} · 走廊 {6}"
                + "\n几何着色器 {7} · 相机 {8} · Boot {9:0} ms"
                + "\n{10}\n{11}\n{12}\n{13}\n{14}",
                Ticks, (int)(1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f)),
                Services.HasNet ? Services.Net.TickRate : 0,
                DescribeServices(),
                Level != null ? Level.LevelId : "-",
                Level != null ? Level.Rooms.Count : 0,
                Level != null ? Level.Corridors.Count : 0,
                LevelBuilder.GeometryShader != null ? "✓" : "✗",
                _camera != null ? "✓" : "✗",
                BootMs,
                _player != null ? _player.Describe() : "玩家：—",
                _monsters != null ? _monsters.Describe() : "怪物：—",
                _objectives != null ? _objectives.OneLine() : "本局任务：—",
                DescribeTemperature(),
                // 【真机取证通道】玩法层状态追加在诊断末尾（**不覆盖**任何既有行）。
                // 判据：理智数字随时间变化 = Tick 真在跑，而不是只编译过。
                SessionStatus);
        }    }
}
