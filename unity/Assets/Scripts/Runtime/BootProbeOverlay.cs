using UnityEngine;
using UnityEngine.Rendering;

namespace Whisper.Runtime
{
    /// <summary>
    /// **最小启动探针叠层**（真机黑屏排查的"最后一道可见性"）。
    ///
    /// ══════════════════════════════════════════════════════════════════════════════
    /// 为什么要有它（与 `GameBootstrap.BootOverlay.cs` 的分工）
    /// ══════════════════════════════════════════════════════════════════════════════
    /// 2026-10-07 第一次真机装机（0.1.83）现象是 **黑屏**，而本机**没有任何日志通道**：
    /// ```
    /// adb devices   → 空（无 daemon）
    /// shz logcat    → shz: fetch failed
    /// ```
    /// 崩溃 / 卡住 / 渲染没起来 **三者无法区分**。
    ///
    /// `GameBootstrap.BootOverlay` 依赖 `GameBootstrap` 自己跑起来（它读 BootLog 等实例字段）。
    /// 但如果**黑屏的原因是 Boot 根本没执行**（脚本剥离、组件没挂上、Awake 就抛异常），
    /// 那个叠层也不会出现 —— 于是"没显示"同时意味着"还没到那一步"和"整个都没跑"，
    /// **仍然无法区分**。
    ///
    /// 本类解决这个歧义：它由 `[RuntimeInitializeOnLoadMethod]` 在**首个场景加载后**
    /// 无条件创建一个 GameObject + 本组件，**不依赖任何游戏代码先成功**。
    /// 只要 Unity 播放器起来了、脚本没被整体剥离，它就一定会显示。
    ///
    /// ⚠ 因此它的显示内容刻意极简且**全部来自引擎自身**：
    ///   分辨率 / 目标帧率 / 当前渲染管线名 / 场景名 / 是否 Editor。
    ///   这些字段**不需要游戏逻辑参与**，所以"显示了但内容不对"与"完全没显示"
    ///   能立刻分成两类问题。
    ///
    /// ⚠ 默认**开**：它是排查构建，不是正式构建。定稿前把 `Active` 置 false。
    /// </summary>
    public sealed class BootProbeOverlay : MonoBehaviour
    {
        /// <summary>总开关（排查构建保持 true；正式构建置 false）。</summary>
        public static bool Active = true;

        /// <summary>左上角连点 5 次可隐藏（真机全屏体验时用）。</summary>
        public static bool Hidden;

        const int HotCornerSize = 200;
        int _taps;
        float _firstTapAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!Active) return;
            var go = new GameObject("BootProbeOverlay");
            DontDestroyOnLoad(go);
            go.AddComponent<BootProbeOverlay>();
            // 同时往日志里打一行：真机若能用 `logcat -s Unity` 抓，第一行就是它
            Debug.Log($"[BootProbe] 已装上 · 版本 {Application.version} · 平台 {Application.platform}"
                + $" · 分辨率 {Screen.width}x{Screen.height} · 管线 {PipelineName()}");
        }

        static string PipelineName()
        {
            // 这一行是**黑屏排查的第一判据**：
            // 若显示 "Built-in" 而工程用的是 URP 着色器 ⇒ 着色器不兼容 ⇒ 画面必然不对。
            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp == null) return "**Built-in（URP 未挂！）**";
            return rp.GetType().Name;
        }

        void OnGUI()
        {
            HandleTap();
            if (Hidden) return;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(20, Screen.height / 34),
                wordWrap = false,
            };
            style.normal.textColor = Color.white;

            const float w = 900f;
            float h = style.fontSize * 7.2f;
            var rect = new Rect(0f, 0f, Mathf.Min(w, Screen.width), h);
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.78f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;

            GUI.Label(new Rect(10f, 4f, rect.width - 12f, h),
                $"Whisper 启动探针（排查构建）\n"
                + $"版本 {Application.version} · {Application.platform} · 帧 {Time.frameCount}\n"
                + $"画面 {Screen.width}x{Screen.height} · 目标帧率 {Application.targetFrameRate} · vSync {QualitySettings.vSyncCount}\n"
                + $"渲染管线：{PipelineName()}\n"
                + $"场景：{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}\n"
                + $"（左上角连点 5 次可隐藏本叠层）", style);
        }

        void HandleTap()
        {
            if (Input.touchCount > 0)
            {
                var t = Input.GetTouch(0);
                if (t.phase == TouchPhase.Began) Tap(t.position);
                return;
            }
            if (Input.GetMouseButtonDown(0)) Tap(Input.mousePosition);
        }

        void Tap(Vector2 p)
        {
            if (!(p.x <= HotCornerSize && p.y >= Screen.height - HotCornerSize)) { _taps = 0; return; }
            float now = Time.realtimeSinceStartup;
            if (_taps == 0 || now - _firstTapAt > 3f) { _taps = 1; _firstTapAt = now; return; }
            if (++_taps >= 5) { _taps = 0; Hidden = !Hidden; }
        }
    }
}
