using System.Collections.Generic;

namespace Whisper.Gameplay.Config
{
    /// <summary>
    /// 配置表读取器的实例化外壳（V9 §13.3）：把静态 GameConfig 包成可注入对象，
    /// 便于测试注入替身、也便于将来换成 Remote Config / Addressables 热更实现。
    /// </summary>
    public sealed class GameConfigReader
    {
        /// <summary>使用已加载的静态 GameConfig（运行时默认路径）。</summary>
        public GameConfigReader() { }

        /// <summary>用一份现成的配置字典构造（测试用）。</summary>
        public GameConfigReader(Dictionary<string, object> root) => _override = root;

        readonly Dictionary<string, object> _override;

        public float Float(string path, float fallback)
        {
            if (_overrideActive) { var o = ReadOverride(path, fallback); return o is float f ? f : (o is long l ? l : fallback); }
            return GameConfig.GetFloat(path, fallback);
        }
        public int Int(string path, int fallback) => _overrideActive ? (int)ReadOverride(path, fallback) : GameConfig.GetInt(path, fallback);
        public string String(string path, string fallback) => _overrideActive ? (ReadOverride(path, fallback) as string ?? fallback) : GameConfig.GetString(path, fallback);
        public bool Bool(string path, bool fallback) => _overrideActive ? (ReadOverride(path, fallback) is bool b ? b : fallback) : GameConfig.GetBool(path, fallback);

        bool _overrideActive => _override != null;

        object ReadOverride(string path, object fallback)
        {
            object cur = _override;
            foreach (var part in path.Split('.'))
            {
                if (!(cur is Dictionary<string, object> map) || !map.TryGetValue(part, out var next)) return fallback;
                cur = next;
            }
            if (cur is long l) return (float)l;
            if (cur is double d) return (float)d;
            return cur;
        }
    }
}
