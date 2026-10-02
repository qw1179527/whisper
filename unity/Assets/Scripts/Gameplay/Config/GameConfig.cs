using System.Collections.Generic;
using Whisper.Gameplay.Level;

namespace Whisper.Gameplay.Config
{
    /// <summary>
    /// 运行时配置表读取器（V9 §13.3 / §19.5：**数值唯一真源**，代码内不得硬编码数值）。
    ///
    /// 位置说明：放在 **Gameplay** 而非 Core —— 它依赖 Level 层的 MiniJson 解析器，
    /// 而 V9 §13.1 规定 Core 无任何依赖（Core 只放纯契约：三接口 + 值类型 + DesignTokens + 定位器）。
    ///
    /// 语义与 0.6.0 产物里的 `__m0.cfg(path, fallback)` 一致（那套已在 WebView 侧验证），
    /// 便于两端交叉核对。数据来自 `Assets/Data/config.json`（镜像一致性由 tools/data-mirror.mjs 强制）。
    /// </summary>
    public static class GameConfig
    {
        static Dictionary<string, object> _root;

        public static bool IsLoaded => _root != null;

        /// <summary>从 JSON 文本加载（运行时与测试都走它）。</summary>
        public static void LoadFromJson(string json) => _root = MiniJson.AsMap(MiniJson.Parse(json));

        /// <summary>清空（测试隔离用）。</summary>
        public static void Reset() => _root = null;

        /// <summary>点路径查询 `a.b.c`；任一层缺失或类型不符返回 fallback（不抛异常）。</summary>
        public static object Get(string path, object fallback = null)
        {
            if (_root == null || string.IsNullOrEmpty(path)) return fallback;
            object cur = _root;
            var parts = path.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                if (!(cur is Dictionary<string, object> map) || !map.TryGetValue(parts[i], out var next)) return fallback;
                cur = next;
            }
            return cur;
        }

        public static float GetFloat(string path, float fallback)
        {
            var v = Get(path);
            if (v is long l) return l;
            if (v is double d) return (float)d;
            return fallback;
        }

        public static int GetInt(string path, int fallback)
        {
            var v = Get(path);
            if (v is long l) return (int)l;
            if (v is double d) return (int)d;
            return fallback;
        }

        public static string GetString(string path, string fallback) => Get(path) as string ?? fallback;

        public static bool GetBool(string path, bool fallback) => Get(path) is bool b ? b : fallback;
    }
}
