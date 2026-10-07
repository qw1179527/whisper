using System.Text;
using UnityEngine;

namespace Whisper.Runtime
{
    /// <summary>
    /// **机内诊断叠层**（`OnGUI`）：黑屏时唯一能给出信息的通道。
    ///
    /// ══════════════════════════════════════════════════════════════════════════════
    /// 为什么必须有它（2026-10-07，第一次真机装机的结果）
    /// ══════════════════════════════════════════════════════════════════════════════
    /// 把 `whisper-android.apk`（0.1.83）装到手机上，现象是 **黑屏**。
    /// 而当时本机**没有任何日志通道**：
    /// ```
    /// adb devices        → List of devices attached（空；无 daemon）
    /// shz "logcat -d"    → shz: fetch failed（Shizuku 桥不可用）
    /// ```
    /// ⇒ 崩溃、卡住、渲染没起来**三种可能完全无法区分**。
    ///
    /// 编辑器里我有 `RENDER_EVIDENCE` 取证链，能证明"管线/相机/灯/几何都对"——
    /// 但那证明的是**编辑器**。真机黑屏恰恰是"编辑器对、真机不对"这类问题的典型形态，
    /// 必须有一份**真机可读**的状态输出。
    ///
    /// ⚠ 本叠层是**诊断**，不是 UI：
    ///   · 它不改动任何玩法/渲染状态（只读字段 + 一个"开始对局"按钮）
    ///   · 默认**关闭**（`ShowDiagnostics`），避免污染正式观感；
    ///     真机排查时用 `WhisperBootOverlay.ForceOn = true` 或按屏幕左上角 5 次开关
    ///   · 它读的是 `GameBootstrap` 自己的公开字段，**不新建第二份状态**
    ///     （本项目反复出现的"配了≠生效"就是第二份真源造成的）
    ///
    /// ⚠ 为什么要"按 5 次开关"而不是常显：黑屏时你连按钮都看不见，
    ///   但**触摸屏仍然收得到输入** ⇒ 用"无反馈的隐藏手势"来开叠层，
    ///   这样"黑屏但进程活着"和"进程死了"能立刻区分开。
    /// </summary>
    public sealed partial class GameBootstrap
    {
        // ⚠ **复用既有的 `ShowDiagnostics`**（GameBootstrap.cs:47 的实例字段，控制 HUD 小字）。
        // 我第一版在这里又声明了一个 `static bool ShowDiagnostics` ⇒ **CS0229 二义性**，
        // 而且同时会踩到 `GameBootstrap.cs:477` 那处既有用法。
        // ⇒ 这正是本项目反复出现的"造了第二份真源"：既有字段已经在管这件事，
        //   我应该复用它并把语义扩成"诊断显示总开关"，而不是另起一个。
        // 语义（合并后）：`ShowDiagnostics = true` ⇒ HUD 状态小字 + 本叠层**同时**显示。

        /// <summary>叠层开关手势：左上角这个像素区域内连点 5 次。</summary>
        const int HotCornerSize = 160;
        const int HotCornerTaps = 5;

        int _diagTaps;
        float _diagFirstTapAt;
        GUIStyle _diagStyle;
        Vector2 _diagScroll;

        void OnGUI()
        {
            HandleHotCorner();
            if (!ShowDiagnostics) return;

            // ══════════════════════════════════════════════════════════════════════════════
            // **从"铺满屏的大面板"改为"一行状态"**（2026-10-07 真机截图暴露的问题）
            // ══════════════════════════════════════════════════════════════════════════════
            // 原实现：8px 起、占满屏高，内含启动日志滚动区 + 一个"开始对局"按钮。
            // 真机后果：
            //   · 它与 `BootFailBoard`（原 12%~98%）、`BootStageBoard`（现 48% 起）**三块互相压住**
            //   · 那个"开始对局"按钮被压在最下面 → **点不到** → 我永远看不到 StartMatch 的结果
            // ⇒ 信息交给三块**按垂直分区**的叠层（探针 0~22% / 失败 22~46% / 阶段 48~92%），
            //   本类只保留**一行**：版本 · 帧 · 阶段 · 是否已开局 + 自动开局倒计时。
            //   既不再互相压，也把"点按钮"这个中间环节彻底移除（见 `TickAutoStart`）。
            if (_diagStyle == null)
            {
                _diagStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.Max(16, Screen.height / 52),
                    richText = false,
                    wordWrap = false,
                };
                _diagStyle.normal.textColor = new Color(0.65f, 1f, 0.72f, 1f);
            }

            int remain = -1;
            if (AutoStartAfterSec > 0f && Booted)
                remain = Mathf.CeilToInt(Mathf.Max(0f, AutoStartAfterSec - (Time.realtimeSinceStartup - _bootedAt)));

            string line = $"v{Application.version} · 帧 {Ticks} · {(Booted ? "Boot ✓" : "Boot 未完成")}"
                + (_matchStarted ? " · 已开局" : (remain >= 0 ? $" · {remain}s 后自动开局" : " · 等待开局"))
                + $" · {Screen.width}x{Screen.height}";
            GUI.Label(new Rect(10f, Screen.height * 0.005f, Screen.width - 20f, _diagStyle.fontSize * 1.6f), line, _diagStyle);
        }

        /// <summary>左上角连点 5 次开关叠层（黑屏时无反馈手势；见类注释）。</summary>
        void HandleHotCorner()
        {
            if (Input.touchCount > 0)
            {
                var t = Input.GetTouch(0);
                if (t.phase == TouchPhase.Began) RegisterTap(t.position);
                return;
            }
            // 编辑器/带鼠标的场合
            if (Input.GetMouseButtonDown(0)) RegisterTap(Input.mousePosition);
        }

        void RegisterTap(Vector2 screenPos)
        {
            // 屏幕坐标原点在左下 ⇒ 左上角是 y 接近 Screen.height
            bool inCorner = screenPos.x <= HotCornerSize && screenPos.y >= Screen.height - HotCornerSize;
            if (!inCorner) { _diagTaps = 0; return; }
            float now = Time.realtimeSinceStartup;
            if (_diagTaps == 0 || now - _diagFirstTapAt > 3f) { _diagTaps = 1; _diagFirstTapAt = now; return; }
            _diagTaps++;
            if (_diagTaps >= HotCornerTaps)
            {
                _diagTaps = 0;
                ShowDiagnostics = !ShowDiagnostics;
                Debug.Log($"[Diag] 诊断叠层 {(ShowDiagnostics ? "开" : "关")}");
            }
        }

        /// <summary>当前渲染管线名（黑屏排查第一问："管线挂上了吗"）。</summary>
        static string RenderPipelineName()
        {
            var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            return rp == null ? "**Built-in（URP 未挂！）**" : rp.GetType().Name;
        }

        static string Safe(string s) => string.IsNullOrEmpty(s) ? "(空)" : s;

        static string Tail(string s, int lines)
        {
            if (string.IsNullOrEmpty(s)) return "(无)";
            var all = s.Split('\n');
            if (all.Length <= lines) return s;
            var sb = new StringBuilder();
            for (int i = all.Length - lines; i < all.Length; i++) sb.Append(all[i]).Append('\n');
            return sb.ToString();
        }
    }
}
