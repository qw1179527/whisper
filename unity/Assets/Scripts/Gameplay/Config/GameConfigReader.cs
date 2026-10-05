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
            if (!_overrideActive) return GameConfig.GetFloat(path, fallback);
            object o = ReadOverride(path, null);
            if (o is float f) return f;
            if (o is long l) return l;
            if (o is int i) return i;
            if (o is double d) return (float)d;
            return fallback;
        }
        /// <summary>
        /// 取整数。
        /// </summary>
        /// <remarks>
        /// 【2026-10-05 修真 bug】原实现是 `_overrideActive ? (int)ReadOverride(path, fallback) : ...`，
        /// 两处会炸：
        /// ① `ReadOverride` 查不到时返回 **fallback**，而 fallback 是 `int` → 装箱成 `object` 后
        ///    直接 `(int)` unbox。若调用方传的 fallback 不是 int（例如 `Int(path, someString)` 之类
        ///    被误用），就是 `InvalidCastException`。
        /// ② MiniJson 把整数解析成 **`long`**，`(int)(object)long` 同样抛 `InvalidCastException`。
        /// 这两个问题在只被少数路径调用时没暴露，我这一轮大量使用才炸出来（EditMode 7 个红）。
        /// 正解：走统一的数值转换，任何非数值一律回 fallback，**绝不抛异常**（配置读取器崩掉 = 整局崩）。
        /// </remarks>
        public int Int(string path, int fallback)
        {
            if (!_overrideActive) return GameConfig.GetInt(path, fallback);
            object v = ReadOverride(path, null);
            if (v is long l) return (int)l;
            if (v is int i) return i;
            if (v is double d) return (int)d;
            if (v is float f) return (int)f;
            return fallback;
        }
        public string String(string path, string fallback) => _overrideActive ? (ReadOverride(path, fallback) as string ?? fallback) : GameConfig.GetString(path, fallback);
        public bool Bool(string path, bool fallback) => _overrideActive ? (ReadOverride(path, fallback) is bool b ? b : fallback) : GameConfig.GetBool(path, fallback);

        /// <summary>
        /// 取任意节点（数组/字典/标量）。
        /// </summary>
        /// <remarks>
        /// 【2026-10-05 新增】原先只有 Float/Int/String/Bool —— 于是**读数组的地方只能绕过本类**
        /// 直接调静态 `GameConfig.Get`。后果是：`Shop.Load(cfg)` 收下 cfg 却去读全局配置，
        /// 注入的配置完全无效（我的 EditMode 测试当场抓到："没有这件装备"）。这不是测试的问题，
        /// 是**接口缺陷**：读取器必须能读任何节点，否则"可注入"就是假的。
        /// </remarks>
        public object Get(string path, object fallback = null)
            => _overrideActive ? (ReadOverride(path, null) ?? fallback) : GameConfig.Get(path, fallback);

        bool _overrideActive => _override != null;

        object ReadOverride(string path, object fallback)
        {
            object cur = _override;
            foreach (var part in path.Split('.'))
            {
                if (!(cur is Dictionary<string, object> map) || !map.TryGetValue(part, out var next)) return fallback;
                cur = next;
            }
            // 【2026-10-05】原实现把 long/double 一律转成 float 再返回 —— 这让 `Int` 拿不到整数，
            // 也让"整数以浮点表示"成为隐式约定（123 会变成 123f，再转 int 有精度风险）。
            // 现在**原样返回**，由各自的取用方法做类型判断（Float/Int/Bool/String 都已处理 long）。
            return cur;
        }
    }
}
