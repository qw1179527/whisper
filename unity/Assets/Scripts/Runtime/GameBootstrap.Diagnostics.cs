using UnityEngine;
using UnityEngine.UI;                  // Text
using Whisper.Core;                    // Services（接口容器）
using Whisper.Gameplay.Config;         // GameConfig
using Whisper.Gameplay.Level;          // LevelBuilder / LevelData

// ⚠ **拆 partial 必须把 using 一起搬** —— 本文件是第二次拆（第一次漏了 using，
//    导致 build-dev-mono #11 与 unity-android #88 双双失败，见 GameBootstrap.Hud.cs 的注释）。
//    为防再犯：拆出的每个 partial 文件都从原文件复制**同一组 using**，不做删减。
namespace Whisper.Runtime
{
    /// <summary>
    /// **启动收尾与诊断输出**（从 `GameBootstrap.cs` 拆出，`gate-code` C5：单文件 ≤600 行）。
    ///
    /// 拆的是**内聚的一块**：`FinishBoot`（收尾 + 一行摘要日志）与 `DescribeServices`（接口状态描述）。
    /// 两者都是"把已经发生的事汇报出去"，与启动流程本身无关。
    ///
    /// ══════════════════════════════════════════════════════════════════════════════
    /// 这里曾经是**第二个 NRE** 的位置（2026-10-07 真机堆栈首次带行号）
    /// ══════════════════════════════════════════════════════════════════════════════
    /// ```
    /// System.NullReferenceException
    ///   at Whisper.Runtime.GameBootstrap.FinishBoot (...) [0x00123] in .../GameBootstrap.cs:446
    ///   at Whisper.Runtime.GameBootstrap.Boot ()     [0x00088] in .../GameBootstrap.Boot.cs:48
    /// ```
    /// 因果链：⑦ 直接进对局 → `TrySpawnPlayer` 抛 NRE（自己 catch 了）⇒
    /// **`_levelBuilder` 从未被赋值** ⇒ 本方法里 `_levelBuilder.DoorObjects.Count` 等
    /// **裸解引用**再抛一次 ⇒ Boot 整个崩掉。
    ///
    /// 上一版我只给 geoSummary 那一段加了判空，紧接的 `Debug.Log` 里却又裸用了三次 ——
    /// **同一行代码里一半有守卫、一半没有**。⇒ 现在整段改为**每段自带守卫的拼装**。
    /// </summary>
    public sealed partial class GameBootstrap
    {
        /// <summary>收尾：进入 Play 循环并把摘要打进 logcat（真机验收唯一要 grep 的一行）。</summary>
        void FinishBoot(System.Diagnostics.Stopwatch t0, System.Text.StringBuilder lines)
        {
            Booted = true;
            BootMs = t0.Elapsed.TotalMilliseconds;
            // ⚠ `_status` 可能为 null（`BuildUi` 失败时）——与 `Fail()` 里刚补的守卫同类。
            // 我在 `Fail()` 补了 `if (_status != null)` 却漏了这里，正是"同一类洞只补一处"。
            // 排查构建里补上**可见的诊断**：看板直接告诉我们"HUD 文本没建出来"。
            if (_status != null)
            {
                _status.color = HexToColor(DesignTokens.ColorPaper);
                _status.text = lines.ToString();
            }
            else
            {
                BootStageBoard.SetError("FinishBoot：`_status` 为 null —— HUD 文本没建出来（BuildUi 失败）"
                    + "\n这会让 Update() 每帧 NRE（堆栈看起来像 Start/Boot，实际是 Update）");
                Debug.LogError("[Whisper] FinishBoot 时 _status 为 null —— BuildUi 没建出 HUD 文本");
            }
            BootLog = lines.ToString();
            // 【为什么要把门/温度写进这一行】这些数值以前**只进 HUD 文本、不进日志**，
            // 于是"开局到底开了几扇门""温度系统有没有装配"在真机上**无法核验**——
            // 只能靠肉眼看截图，而那正是本工程反复踩的"汇报与事实不符"。
            // 现在收进同一行，`adb logcat -s Unity:I` 就能直接读。
            // ══════════════════════════════════════════════════════════════════════════════
            // **这一整段必须"零解引用"**（2026-10-07 真机堆栈指到本方法）
            // ══════════════════════════════════════════════════════════════════════════════
            // 真机堆栈（终于带行号了）：
            //   System.NullReferenceException
            //     at Whisper.Runtime.GameBootstrap.FinishBoot (...) [0x00123] in .../GameBootstrap.cs:446
            //     at Whisper.Runtime.GameBootstrap.Boot ()     [0x00088] in .../GameBootstrap.Boot.cs:48
            // 因果链：
            //   ⑦ 直接进对局 → `TrySpawnPlayer` 抛 NRE（它自己 catch 了、只写 Fail）
            //   ⇒ **`_levelBuilder` 从未被赋值**（几何装配在 `TryBuildGeometry`，也在同一条失败链上）
            //   ⇒ 本方法里 `_levelBuilder.DoorObjects.Count` 等**裸解引用**再抛一次 ⇒ Boot 整个崩掉。
            //
            // 上一版我只给 `Geometry` 加了判空（`_levelBuilder != null && _levelBuilder.Geometry != null`
            // 只用在那一段 geoSummary 上），紧接着的 `Debug.Log` 里却又裸用了三次 ——
            // **同一行代码里，一半有守卫、一半没有**。这正是本项目反复出现的形态：
            // "同一类洞只补了一处"。⇒ 现在整段改为**每段自带守卫的拼装**，不再依赖"我记得哪几个可能为 null"。
            var sb = new System.Text.StringBuilder();
            sb.Append($"[Whisper] BOOT OK · {BootMs:0} ms");
            sb.Append(Level != null ? $" · 房间 {Level.Rooms.Count}" : " · 房间 **Level 为 null**");
            if (_levelBuilder != null && _levelBuilder.Geometry != null)
            {
                sb.Append($" · 门 {_levelBuilder.DoorObjects.Count} · 道具 {_levelBuilder.PropObjects.Count}");
                sb.Append($" · 可走格 {_levelBuilder.Geometry.PassableCount()}");
                sb.Append($" · 门扇 {_levelBuilder.DoorObjects.Count} · 门键 {_levelBuilder.Geometry.DoorCount}");
                sb.Append($" · 洞口 {_levelBuilder.Geometry.DoorOpeningCount}");
                sb.Append($" · 已开 {CountOpenDoors(_levelBuilder.Geometry)}/{_levelBuilder.Geometry.DoorOpeningCount}");
            }
            else
            {
                // 这一条本身就是**重要证据**：几何没装配 ⇒ ⑦ 直接进对局失败了
                sb.Append(" · **几何未装配**（_levelBuilder 或 Geometry 为 null ⇒ TryBuildGeometry 没跑到/失败）");
                BootStageBoard.SetError("FinishBoot：几何未装配（_levelBuilder 为 null）"
                    + "\n⇒ ⑦ 直接进对局那一步失败了；真正的失败原因见『真正的原因』那一行");
            }
            sb.Append(_temperature != null ? " · 温度系统已装配" : " · 温度系统未装配");
            sb.Append($" · 着色器 {LevelBuilder.UnlitShaderName}");
            sb.Append(_player != null ? " · 玩家已就位" : " · **玩家未就位**");
            Debug.Log(sb.ToString());
        }

        static string DescribeServices()
        {
            string net = Services.HasNet ? "已注入" : "未注入";
            string voice = Services.HasVoice ? "已注入" : "未注入";
            string backend = Services.HasBackend
                ? (Services.Backend.IsAvailable ? "已注入（可用）" : "已注入（无后端模式，§15.2）")
                : "未注入";
            return $"接口：INetService {net} · IVoiceService {voice} · IBackendService {backend}";
        }    }
}
