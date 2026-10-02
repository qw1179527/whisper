package com.whisper.probe;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * 极简 JSON 解析器（Java 镜像）：与 unity/.../MiniJson.cs 同一口径。
 * 含 MaxDepth 上限（C# 侧实测约 4 万层会 SIGSEGV；此处同守 64 层）。
 */
public final class MiniJson {
    public static final int MAX_DEPTH = 64;
    private final String s;
    private int i;
    private int depth;

    private MiniJson(String s) { this.s = s; }

    public static Object parse(String text) {
        MiniJson p = new MiniJson(text);
        Object v = p.value();
        p.ws();
        if (p.i != text.length()) throw new IllegalArgumentException("JSON 末尾有多余字符（位置 " + p.i + "）");
        return v;
    }

    private void ws() { while (i < s.length() && (s.charAt(i) == ' ' || s.charAt(i) == '\t' || s.charAt(i) == '\n' || s.charAt(i) == '\r')) i++; }

    private Object value() {
        ws();
        if (i >= s.length()) throw new IllegalArgumentException("JSON 意外结束");
        char c = s.charAt(i);
        switch (c) {
            case '{': return obj();
            case '[': return arr();
            case '"': return str();
            case 't': expect("true"); return Boolean.TRUE;
            case 'f': expect("false"); return Boolean.FALSE;
            case 'n': expect("null"); return null;
            default: return num();
        }
    }

    private Map<String, Object> obj() {
        if (++depth > MAX_DEPTH) throw new IllegalArgumentException("JSON 嵌套超过 " + MAX_DEPTH + " 层，拒绝解析");
        Map<String, Object> m = new LinkedHashMap<String, Object>();
        i++;
        ws();
        if (i < s.length() && s.charAt(i) == '}') { i++; depth--; return m; }
        while (true) {
            ws();
            if (i >= s.length() || s.charAt(i) != '"') throw new IllegalArgumentException("对象的键必须是字符串（位置 " + i + "）");
            String k = str();
            ws();
            if (i >= s.length() || s.charAt(i) != ':') throw new IllegalArgumentException("键 " + k + " 后缺冒号（位置 " + i + "）");
            i++;
            m.put(k, value());
            ws();
            if (i >= s.length()) throw new IllegalArgumentException("对象未闭合");
            char c = s.charAt(i);
            if (c == ',') { i++; continue; }
            if (c == '}') { i++; depth--; return m; }
            throw new IllegalArgumentException("对象内非法字符 " + c + "（位置 " + i + "）");
        }
    }

    private List<Object> arr() {
        if (++depth > MAX_DEPTH) throw new IllegalArgumentException("JSON 嵌套超过 " + MAX_DEPTH + " 层，拒绝解析");
        List<Object> l = new ArrayList<Object>();
        i++;
        ws();
        if (i < s.length() && s.charAt(i) == ']') { i++; depth--; return l; }
        while (true) {
            l.add(value());
            ws();
            if (i >= s.length()) throw new IllegalArgumentException("数组未闭合");
            char c = s.charAt(i);
            if (c == ',') { i++; continue; }
            if (c == ']') { i++; depth--; return l; }
            throw new IllegalArgumentException("数组内非法字符 " + c + "（位置 " + i + "）");
        }
    }

    private String str() {
        i++;
        StringBuilder sb = new StringBuilder();
        while (true) {
            if (i >= s.length()) throw new IllegalArgumentException("字符串未闭合");
            char c = s.charAt(i++);
            if (c == '"') return sb.toString();
            if (c != '\\') { sb.append(c); continue; }
            if (i >= s.length()) throw new IllegalArgumentException("转义符未完成");
            char e = s.charAt(i++);
            switch (e) {
                case '"': sb.append('"'); break;
                case '\\': sb.append('\\'); break;
                case '/': sb.append('/'); break;
                case 'b': sb.append('\b'); break;
                case 'f': sb.append('\f'); break;
                case 'n': sb.append('\n'); break;
                case 'r': sb.append('\r'); break;
                case 't': sb.append('\t'); break;
                case 'u':
                    if (i + 4 > s.length()) throw new IllegalArgumentException("\\u 转义不完整");
                    sb.append((char) Integer.parseInt(s.substring(i, i + 4), 16));
                    i += 4;
                    break;
                default: throw new IllegalArgumentException("非法转义 \\" + e);
            }
        }
    }

    private Object num() {
        int start = i;
        if (i < s.length() && (s.charAt(i) == '-' || s.charAt(i) == '+')) i++;
        boolean isFloat = false;
        while (i < s.length()) {
            char c = s.charAt(i);
            if (c >= '0' && c <= '9') { i++; continue; }
            if (c == '.' || c == 'e' || c == 'E' || c == '-' || c == '+') { isFloat = isFloat || c == '.' || c == 'e' || c == 'E'; i++; continue; }
            break;
        }
        String n = s.substring(start, i);
        if (n.isEmpty()) throw new IllegalArgumentException("位置 " + start + " 处不是合法值");
        if (!isFloat) { try { return Long.valueOf(Long.parseLong(n)); } catch (NumberFormatException ignore) { } }
        try { return Double.valueOf(Double.parseDouble(n)); }
        catch (NumberFormatException ex) { throw new IllegalArgumentException("非法数字：" + n); }
    }

    private void expect(String w) {
        if (i + w.length() > s.length() || !s.regionMatches(i, w, 0, w.length())) throw new IllegalArgumentException("位置 " + i + " 处期望 " + w);
        i += w.length();
    }

    // ── 取值辅助（与 C# 的 AsMap/AsList/AsString/AsFloat 同口径）──
    @SuppressWarnings("unchecked")
    public static Map<String, Object> asMap(Object v) {
        if (!(v instanceof Map)) throw new IllegalArgumentException("期望对象");
        return (Map<String, Object>) v;
    }
    @SuppressWarnings("unchecked")
    public static List<Object> asList(Object v) {
        if (!(v instanceof List)) throw new IllegalArgumentException("期望数组");
        return (List<Object>) v;
    }
    public static String asString(Object v) {
        if (!(v instanceof String)) throw new IllegalArgumentException("期望字符串");
        return (String) v;
    }
    public static float asFloat(Object v) {
        if (v instanceof Long) return ((Long) v).floatValue();
        if (v instanceof Double) return ((Double) v).floatValue();
        throw new IllegalArgumentException("期望数字");
    }
    public static int asInt(Object v) { return (int) asFloat(v); }
    public static Object get(Map<String, Object> m, String k) { return m.get(k); }
}
