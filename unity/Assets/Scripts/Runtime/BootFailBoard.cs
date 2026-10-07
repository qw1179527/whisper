using UnityEngine;

namespace Whisper.Runtime
{
    /// <summary>
    /// **启动失败看板**：把 `Boot()` 的失败原因**无条件显示在屏幕上**。
    ///
    /// ══════════════════════════════════════════════════════════════════════════════
    /// 为什么需要（2026-10-07 真机截图给出的硬证据）
    /// ══════════════════════════════════════════════════════════════════════════════
    /// 真机现象：屏幕上半是**启动探针的白字**（证明播放器活着、60fps、URP 正常），
    /// **下半全黑、一个字都没有**。两个叠层的显示情况：
    /// ```
    /// BootProbeOverlay（无条件安装）      → **显示了**（帧 514 / 管线 UniversalRenderPipelineAsset）
    /// GameBootstrap.BootOverlay（要 Boot 成功）→ **没显示**
    /// ```
    /// ⇒ `Boot()` 在跑到 `BuildMenu()` 之前就 return 了。
    ///
    /// 而 `Fail()` 当时的实现是：
    /// ```csharp
    /// _status.color = ...; _status.fontSize = 32; _status.text = body;   // ← _status 可能为 null
    /// ```
    /// 而 `_status` 是 **HUD 建好之后**才有的。启动链上
    /// `TryLoadConfig → TryInstallServices → TryLoadLevel` 任何一步失败，
    /// `_status` 都还是 null ⇒ **fail 路径本身静默** ⇒ 用户看到的就是"全黑、无提示"。
    ///
    /// **一个失败路径自己会静默，那它就不是失败路径。**
    ///
    /// ══════════════════════════════════════════════════════════════════════════════
    /// 设计约束（都来自本项目的实际教训）
    /// ══════════════════════════════════════════════════════════════════════════════
    /// · **不依赖任何游戏对象**：`OnGUI` 只需要一个 GameObject + 本组件，
    ///   而 `GameObject.AddComponent` 在 HUD/Canvas/EventSystem 都没建起来时照样能用。
    /// · **不依赖 Text/Canvas**：那两样正是当前语境下"可能还没建出来"的东西。
    /// · **幂等**：`Show()` 可被多次调用（每次失败都会调），复用同一个实例。
    /// · **够大够显眼**：红字 + 半透明黑底，跨半个屏幕都看得清（真机截图 2800×1280）。
    /// </summary>
    public sealed class BootFailBoard : MonoBehaviour
    {
        static BootFailBoard _instance;
        static string _text = "";

        /// <summary>显示（或更新）失败正文。可从任何地方调用，不需要前提条件。</summary>
        public static void Show(string body)
        {
            _text = body ?? "(无正文)";
            if (_instance != null) return;
            var go = new GameObject("BootFailBoard");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<BootFailBoard>();
            // 真机若能用 logcat 抓到，这一行就是最直接的证据
            Debug.LogError("[Whisper] BOOT FAIL BOARD: " + _text);
        }

        /// <summary>隐藏（重试成功时调用）。</summary>
        public static void Hide()
        {
            _text = "";
            if (_instance != null) { Destroy(_instance.gameObject); _instance = null; }
        }

        void OnGUI()
        {
            if (string.IsNullOrEmpty(_text)) return;
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(18, Screen.height / 56),
                wordWrap = true,
                richText = false,
            };
            style.normal.textColor = new Color(1f, 0.42f, 0.36f, 1f);

            // ⚠ 尺寸与位置：原为 12%~98%（86% 高）—— 真机截图实测它把阶段看板整块压住。
            // ⇒ 收窄到 **22%~46%**，与阶段看板（48% 起）**垂直分区**，两块都不被遮。
            float w = Screen.width * 0.96f;
            float h = Screen.height * 0.26f;
            var rect = new Rect(Screen.width * 0.02f, Screen.height * 0.22f, w, h);

            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.86f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;

            GUI.Label(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, rect.height - 16f),
                "⚠ 启动失败 —— 以下是从 Boot 全流程记录下来的原因\n\n" + _text, style);
        }
    }
}
