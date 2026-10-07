using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Whisper.Gameplay.Level
{
    /// <summary>
    /// 极简 JSON 解析器（零第三方依赖，V9 §19 零依赖纪律；亦不依赖 UnityEngine，
    /// 因此可在 Edit Mode 测试里独立运行）。
    ///
    /// 支持子集：object / array / string（含 \u 转义与代理对）/ number(long|double) / true / false / null。
    /// 不支持：注释、尾随逗号、单引号（DSL 文件不得使用这些——tools/validate-levels.mjs 会先拦下）。
    /// </summary>
    public static class MiniJson
    {
        /// <summary>
        /// 嵌套深度上限。为什么必须有：本解析器是递归下降，深层嵌套会耗尽栈
        /// （独立验证轨实测：C# 侧约 4 万层即 SIGSEGV，进程硬崩、无任何输出；Node JSON.parse 到 200 万层仍正常）。
        /// 而 V9 §19.3 的关卡/事件清单正是 **AI 生成的不可信输入** —— 必须有硬上限。
        /// 64 层远高于真实关卡需求（实测疗养院关卡最大深度 6）。
        /// </summary>
        public const int MaxDepth = 64;

        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            int i = 0;
            int depth = 0;
            var value = ParseValue(text, ref i, ref depth);
            SkipWhitespace(text, ref i);
            if (i != text.Length) throw new FormatException($"JSON 末尾有多余字符（位置 {i}）");
            return value;
        }

        static object ParseValue(string s, ref int i, ref int depth)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON 意外结束");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i, ref depth);
                case '[': return ParseArray(s, ref i, ref depth);
                case '"': return ParseString(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default: return ParseNumber(s, ref i);
            }
        }

        static Dictionary<string, object> ParseObject(string s, ref int i, ref int depth)
        {
            if (++depth > MaxDepth) throw new FormatException($"JSON 嵌套超过 {MaxDepth} 层，拒绝解析");
            try
            {
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            i++; // {
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return map; }
            while (true)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException($"对象的键必须是字符串（位置 {i}）");
                string key = ParseString(s, ref i);
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException($"键 {key} 后缺冒号（位置 {i}）");
                i++;
                map[key] = ParseValue(s, ref i, ref depth);
                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("对象未闭合");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return map; }
                throw new FormatException($"对象内非法字符 {s[i]}（位置 {i}）");
            }
            }
            finally { depth--; }
        }

        static List<object> ParseArray(string s, ref int i, ref int depth)
        {
            if (++depth > MaxDepth) throw new FormatException($"JSON 嵌套超过 {MaxDepth} 层，拒绝解析");
            try
            {
            var list = new List<object>();
            i++; // [
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(ParseValue(s, ref i, ref depth));
                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("数组未闭合");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return list; }
                throw new FormatException($"数组内非法字符 {s[i]}（位置 {i}）");
            }
            }
            finally { depth--; }
        }

        static string ParseString(string s, ref int i)
        {
            i++; // 开引号
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("字符串未闭合");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new FormatException("转义符未完成");
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("\\u 转义不完整");
                        sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                        break;
                    default: throw new FormatException($"非法转义 \\{e}");
                }
            }
        }

        static object ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            bool isFloat = false;
            while (i < s.Length)
            {
                char c = s[i];
                if (c >= '0' && c <= '9') { i++; continue; }
                if (c == '.' || c == 'e' || c == 'E' || c == '-' || c == '+') { isFloat = isFloat || c == '.' || c == 'e' || c == 'E'; i++; continue; }
                break;
            }
            string num = s.Substring(start, i - start);
            if (num.Length == 0) throw new FormatException($"位置 {start} 处不是合法值");
            if (!isFloat && long.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)) return l;
            if (double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
            throw new FormatException($"非法数字：{num}");
        }

        static void Expect(string s, ref int i, string word)
        {
            if (i + word.Length > s.Length || s.Substring(i, word.Length) != word)
                throw new FormatException($"位置 {i} 处期望 {word}");
            i += word.Length;
        }

        static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        // ── 读取辅助（供 LevelData 使用，避免到处强转）──
        public static Dictionary<string, object> AsMap(object v) =>
            v as Dictionary<string, object> ?? throw new FormatException("期望对象");
        public static List<object> AsList(object v) =>
            v as List<object> ?? throw new FormatException("期望数组");
        public static string AsString(object v) =>
            v as string ?? throw new FormatException("期望字符串");
        public static float AsFloat(object v) => v switch
        {
            long l => l,
            double d => (float)d,
            _ => throw new FormatException("期望数字"),
        };
        public static int AsInt(object v) => (int)AsFloat(v);
        public static bool AsBool(object v) => v is bool b ? b : throw new FormatException("期望布尔");
        public static object Get(Dictionary<string, object> map, string key) =>
            map.TryGetValue(key, out var v) ? v : null;
        public static object GetOrNull(Dictionary<string, object> map, string key) => Get(map, key);
    }
}
