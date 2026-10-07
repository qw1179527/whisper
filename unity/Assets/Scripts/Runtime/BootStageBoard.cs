using UnityEngine;

namespace Whisper.Runtime
{
    /// <summary>
    /// **启动阶段看板**：把 `Boot()` 走到哪一步、有没有活下来，**实时**显示在屏幕上。
    ///
    /// ══════════════════════════════════════════════════════════════════════════════
    /// 为什么需要它（2026-10-07 两张真机截图逼出来的）
    /// ══════════════════════════════════════════════════════════════════════════════
    /// 现象（两张截图，间隔约几分钟，其它字段完全一致）：
    /// ```
    /// 版本 0.1.1 · Android · 帧 514 / 帧 671      ← 时间在走、没有崩
    /// 画面 2800x1280 · 目标帧率 60 · vSync 0
    /// 渲染管线：UniversalRenderPipelineAsset      ← 管线正常
    /// 场景：Boot
    /// （下半全黑，**红字失败看板也没出现**）
    /// ```
    /// 两个叠层的结论：
    /// | 叠层 | 显示 | 含义 |
    /// |---|---|---|
    /// | `BootProbeOverlay`（无条件装）| 显示 | 播放器活着、60fps、URP 已挂 |
    /// | `BootFailBoard`（`Fail()` 里挂）| **不显示** | **`Boot()` 没走到 `Fail()`** |
    /// | `GameBootstrap.BootOverlay`（要 Boot 成功）| 不显示 | **`Boot()` 也没成功** |
    ///
    /// ⇒ `Boot()` **既没成功、也没走失败路径**。而 `Start()` 里是裸的 `Boot();`：
    /// ```csharp
    /// void Start() { Application.runInBackground = true; Boot(); }   // ← 没有 try/catch
    /// ```
    /// **任何未捕获异常**（资源缺失、三维数学 NaN、第三方调用抛错…）都会让流程
    /// 停在那里：既不完成、也不进 `Fail()` ⇒ 用户看到"全黑无提示"，而我只能靠猜。
    ///
    /// ══════════════════════════════════════════════════════════════════════════════
    /// 它解决的问题是"**看得见**"，不是"修好了"
    /// ══════════════════════════════════════════════════════════════════════════════
    /// 本类**不修任何缺陷**。它只把两件原本不可观测的事变成屏幕上可读的字：
    ///   ① `Boot()` 走到**哪一个阶段**（阶段名由 `Boot()` 每步上报）
    ///   ② 若抛异常，**异常类型与消息**（由 `Start()` 的 try/catch 捕获后交给它）
    /// 有了这两样，"黑屏"就从"一个现象"变成"一个具体的断点"。
    ///
    /// ⚠ 与 `BootFailBoard` 的分工：那个显示**已知失败原因**（`Fail()` 的正文）；
    ///   本类显示**进度 + 未知异常**。两者的显示条件互补，可同时存在。
    /// </summary>
    public sealed class BootStageBoard : MonoBehaviour
    {
        static BootStageBoard _instance;
        static string _stage = "(尚未开始)";
        static string _error = "";
        static float _stageAt;          // 进入当前阶段的时间（用于判断"卡在第几步"）

        /// <summary>上报当前阶段（由 `Boot()` 每步调用）。</summary>
        public static void SetStage(string stage)
        {
            _stage = string.IsNullOrEmpty(stage) ? "(空)" : stage;
            _stageAt = Time.realtimeSinceStartup;
            Ensure();
        }

        /// <summary>上报一个**未捕获异常**（由 `Start()` 的 try/catch 调用）。</summary>
        public static void SetError(string error)
        {
            _error = error ?? "";
            Ensure();
        }

        /// <summary>清空（重试成功时）。</summary>
        public static void Clear()
        {
            _stage = "(尚未开始)"; _error = "";
        }

        static void Ensure()
        {
            if (_instance != null) return;
            var go = new GameObject("BootStageBoard");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<BootStageBoard>();
        }

        void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(18, Screen.height / 52),
                wordWrap = true,
            };
            style.normal.textColor = string.IsNullOrEmpty(_error)
                ? new Color(0.72f, 0.92f, 1f, 1f)         // 正常进度：淡蓝
                : new Color(1f, 0.45f, 0.38f, 1f);        // 有异常：红

            float w = Screen.width * 0.96f;
            // ⚠ 位置：放在**探针叠层（0~约 22%）之下、失败看板（12%~98%）之上会重叠**。
            // 真机截图实测：失败看板从 12% 起铺到 98%，把阶段看板整块压住 ⇒ 两块字叠在一起看不清。
            // ⇒ 阶段看板下移到 **48%**，只占中下段；失败看板则改为**从顶部 22% 起、只占约 26% 高**（见其注释），
            //   两者不再相交。宽度都取 96%，靠**垂直分区**避免重叠（比缩小字号更可靠）。
            float top = Screen.height * 0.48f;
            float h = Screen.height * 0.44f;
            var rect = new Rect(Screen.width * 0.02f, top, w, h);

            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;

            string body = "── 启动阶段看板 ──\n"
                + $"当前阶段：{_stage}\n"
                + $"进入该阶段后经过：{Mathf.Max(0f, Time.realtimeSinceStartup - _stageAt):0.0} 秒"
                + "（若持续增长且画面无变化 ⇒ 卡在这一步）\n";
            if (!string.IsNullOrEmpty(_error))
                body += "\n⚠ **未捕获异常**（Boot 既没成功也没走 Fail 路径的原因）\n" + _error;
            else
                body += "\n（暂无异常；若无红字失败看板且阶段长期不动 ⇒ 卡在该阶段的内部）";

            GUI.Label(new Rect(rect.x + 10f, rect.y + 6f, rect.width - 20f, rect.height - 12f), body, style);
        }
    }
}
