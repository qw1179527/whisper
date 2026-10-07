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

            if (_diagStyle == null)
            {
                _diagStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.Max(16, Screen.height / 42),
                    richText = false,
                    wordWrap = true,
                };
                _diagStyle.normal.textColor = Color.white;
            }

            float w = Mathf.Min(Screen.width * 0.96f, 1100f);
            var rect = new Rect(8f, 8f, w, Screen.height - 16f);
            // 半透明底：黑屏时也要能看清字（纯黑背景 + 白字对比最强，但加一点底色能盖住杂色）
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;

            var sb = new StringBuilder(2048);
            sb.Append("Whisper 诊断叠层  v").Append(Application.version)
              .Append("  ").Append(Application.platform).Append('\n');
            sb.Append("帧 ").Append(Ticks)
              .Append(" · 分辨率 ").Append(Screen.width).Append('x').Append(Screen.height)
              .Append(" · 目标帧率 ").Append(Application.targetFrameRate)
              .Append(" · 启动耗时 ").Append(BootMs.ToString("0")).Append("ms\n");
            sb.Append("Booted=").Append(Booted)
              .Append(" · Win=").Append(Screen.width > 0 && Screen.height > 0 ? "有" : "**无表面**")
              .Append(" · 管线=").Append(RenderPipelineName()).Append('\n');
            if (!string.IsNullOrEmpty(LastError)) sb.Append("★ 错误：").Append(LastError).Append('\n');
            sb.Append("地图：").Append(Safe(DescribeMaps())).Append('\n');
            sb.Append("对局：").Append(Safe(SessionStatus)).Append('\n');
            sb.Append("网络：").Append(Safe(NetStatus)).Append('\n');
            sb.Append("渲染档：").Append(Safe(RenderTierApplier.LastApplied)).Append('\n');
            sb.Append("菜单：").Append(Safe(DescribeMenuAndScare())).Append('\n');

            // 按钮：黑屏时无法用鼠标，但触摸可点。放在最上方、够大。
            float by = 8f + _diagStyle.fontSize * 6.6f;
            var btn = new Rect(16f, by, Mathf.Min(360f, w - 16f), _diagStyle.fontSize * 2.4f);
            var big = new GUIStyle(GUI.skin.button) { fontSize = _diagStyle.fontSize };
            if (GUI.Button(btn, Booted ? "开始对局（进图）" : "重新启动流程"))
            {
                if (Booted) OnMenuStartRequested();
                else Boot();
            }

            // 启动日志尾巴（真机没有 logcat ⇒ 这是唯一能看到 Boot 分阶段结果的地方）
            float logTop = btn.yMax + 8f;
            var logRect = new Rect(16f, logTop, w - 16f, rect.yMax - logTop - 8f);
            GUILayout.BeginArea(logRect);
            _diagScroll = GUILayout.BeginScrollView(_diagScroll);
            GUILayout.Label("─── 启动日志（尾 40 行）───\n" + Tail(Safe(BootLog), 40), _diagStyle);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
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
