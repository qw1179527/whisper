using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Whisper.Core;
using Whisper.Gameplay.Config;

namespace Whisper.Gameplay.Hud
{
    /// <summary>
    /// HUD 构建与绑定（V9 §19.1 C2「UI 全部代码构建，编辑器零参与」）。
    ///
    /// 分工：<see cref="HudModel"/> 负责"显示什么"（纯逻辑、本机可断言）；
    /// 本类只负责"贴到哪个控件"，不做任何玩法判断。
    ///
    /// 为什么坚持代码构建：C2 的目的是让 UI 可被 diff、可被 AI 安全修改、不会因编辑器操作丢失。
    /// 手拖 Canvas/Prefab 会让这份 UI 既不可评审也不可回滚。
    /// </summary>
    public sealed class HudBuilder : MonoBehaviour
    {
        public HudModel Model { get; private set; }
        Text _status;
        Text _log;
        Text _sanity;
        Text _clock;
        Text _settlement;
        Canvas _canvas;

        public void Build(Canvas canvas, HudModel model, Font font = null)
        {
            _canvas = canvas;
            Model = model;
            var root = canvas.transform;

            // 左上：状态一览（含档位/电量/证据/时钟/阶段）
            _status = MakeText(root, "Status", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -16f), new Vector2(640f, 120f), 24, TextAnchor.UpperLeft, font);

            // 右上：时钟 + 理智（独立控件，便于将来单独做大字强调）
            _sanity = MakeText(root, "Sanity", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-320f, -16f), new Vector2(300f, 48f), 28, TextAnchor.UpperRight, font);
            _clock = MakeText(root, "Clock", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-320f, -70f), new Vector2(300f, 48f), 32, TextAnchor.UpperRight, font);

            // 左下：事件日志（怪物听见/拾取/配电箱都进这里）
            _log = MakeText(root, "Log", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(16f, 16f), new Vector2(720f, 180f), 20, TextAnchor.LowerLeft, font);

            // 中：结算页（默认隐藏）
            _settlement = MakeText(root, "Settlement", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-360f, -120f), new Vector2(720f, 240f), 26, TextAnchor.MiddleCenter, font);
            _settlement.gameObject.SetActive(false);

            _status.color = HexToColor(DesignTokens.ColorPaper);
            _sanity.color = HexToColor(DesignTokens.ColorSignal);
            _clock.color = HexToColor(DesignTokens.ColorPaper);
            _log.color = HexToColor(DesignTokens.ColorHudDim, 0.86f);
            _settlement.color = HexToColor(DesignTokens.ColorPaper);
        }

        static Text MakeText(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 anchoredPos, Vector2 size, int fontSize, TextAnchor align, Font font)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.alignment = align;
            t.fontSize = fontSize;
            t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            if (font != null) t.font = font;
            var rt = t.rectTransform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = anchorMin;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            return t;
        }

        /// <summary>把模型刷新到控件（每帧或按节流调用）。</summary>
        public void Refresh()
        {
            if (Model == null || _status == null) return;
            _status.text = Model.FormatStatusLine();
            _sanity.text = $"{Model.SanityBandLabel} {Model.SanityPercent}";
            _clock.text = Model.ClockText;
            _log.text = string.Join("\n", Model.LogLines);
        }

        /// <summary>显示结算页（一局结束时调用）。</summary>
        public void ShowSettlement(string body)
        {
            if (_settlement == null) return;
            _settlement.text = body;
            _settlement.gameObject.SetActive(true);
        }

        public void HideSettlement()
        {
            if (_settlement != null) _settlement.gameObject.SetActive(false);
        }

        static Color HexToColor(string hex, float alpha = 1f)
        {
            if (string.IsNullOrEmpty(hex)) return Color.gray;
            if (hex.StartsWith("rgba("))
            {
                // DesignTokens 里 HUD 色用 rgba() 记法（含透明度）——C2 要求直接用 token，不做二次调色
                var inner = hex.Substring(5).TrimEnd(')');
                var parts = inner.Split(',');
                if (parts.Length >= 4 &&
                    float.TryParse(parts[0].Trim(), out var r) &&
                    float.TryParse(parts[1].Trim(), out var g) &&
                    float.TryParse(parts[2].Trim(), out var b) &&
                    float.TryParse(parts[3].Trim(), out var a))
                    return new Color(r / 255f, g / 255f, b / 255f, a);
                return Color.gray;
            }
            if (hex[0] == '#') hex = hex.Substring(1);
            if (hex.Length < 6) return Color.gray;
            return new Color(
                System.Convert.ToByte(hex.Substring(0, 2), 16) / 255f,
                System.Convert.ToByte(hex.Substring(2, 2), 16) / 255f,
                System.Convert.ToByte(hex.Substring(4, 2), 16) / 255f, alpha);
        }
    }
}
