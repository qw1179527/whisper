#!/usr/bin/env node
/**
 * add-missing-render-stubs.mjs — 补齐后处理/大堂/取证捕获用到的 Unity 入口（幂等·可重验）
 *
 * ## 为什么需要
 * `bash tools/unity-syntax-check.sh` 在修掉「同一类型定义两次」之后，剩余真实错误全是
 * **桩缺 API 成员**（CS0117/CS1061/CS1501/CS0246）——上一轮新写的
 * `PostFx` / `ProceduralTextures` / `GameBootstrap` / `HallScene` / `MenuScene` /
 * `KitVisibilityCapture` / `RenderEvidenceCapture` 用到的接口，桩没跟上（不是逻辑错误）。
 *
 * ## 本脚本补/修的入口（全部带官方出处）
 * | 入口 | 用处 | 官方出处 |
 * |---|---|---|
 * | `Vector4`（struct） | 后处理模糊方向与偏移 | ScriptReference/Vector4 |
 * | `Mathf.Pow` / `Mathf.Log` / `Mathf.SmoothStep` | 眼部适应曲线 / 亮度映射 / 贴图过渡 | Mathf.Pow · Mathf.Log · Mathf.SmoothStep |
 * | `Camera.allowHDR` / `Camera.depthTextureMode` | 渲染三档切 HDR；SSAO/SSGI 要深度 | Camera-allowHDR · Camera-depthTextureMode |
 * | `Material.SetVector` | 后处理传参 | ScriptReference/Material.SetVector |
 * | `Light.renderMode` + `LightRenderMode` | 吊灯强制像素光 | Light-renderMode · LightRenderMode |
 * | `Canvas.scaleFactor` | 屏幕像素 → 画布单位（本项目硬教训：不除它会飞出屏幕） | ScriptReference/Canvas-scaleFactor |
 * | `Texture2D.Apply(bool)` / `GetPixels32` / `EncodeToPNG` | 取证截图读回与编码 | Texture2D.Apply · GetPixels32 · EncodeToPNG |
 * | `RenderTexture.GetTemporary(w,h,depth,format)` | 多级后处理缓冲 | RenderTexture.GetTemporary |
 *
 * ## 实现要点（上一版两个坑，已修）
 * 1. **宿主类可能是单行**（如 `public class Canvas : Behaviour { public RenderMode renderMode { get; set; } }`）
 *    —— 此时「找同缩进闭合大括号」的块定位不成立；改为**显式展开单行类**。
 * 2. **锚点必须真实存在**：探测一律用精确签名；锚点找不到就报错停下，不猜。
 *
 * ## 纪律
 * 每条插入前断言「锚点唯一 + 目标未存在」，任一不中即停止且**不写文件**（不留半改状态）；LF/CRLF 保持原样。
 *
 * 用法：node tools/add-missing-render-stubs.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'native/unity-stubs/UnityStubs.cs');
const checkOnly = process.argv.includes('--check');

const raw = fs.readFileSync(FILE, 'utf8');
const eol = raw.includes('\r\n') ? '\r\n' : '\n';
const lines = raw.split(/\r?\n/);
const log = [];
const fail = (m) => { console.error('[stubs+] ✗ ' + m); process.exit(1); };
const has = (rx) => lines.some((l) => rx.test(l));

/** 定位类的行范围 [start, end]（end=闭合大括号行）；单行类 → [i, i] */
function classRange(classRx) {
  const i = lines.findIndex((l) => classRx.test(l));
  if (i < 0) return null;
  if (lines[i].includes('{') && lines[i].trimEnd().endsWith('}')) return [i, i, true];  // 单行类
  const indent = lines[i].match(/^\s*/)[0];
  for (let j = i + 1; j < lines.length; j++) if (lines[j] === indent + '}') return [i, j, false];
  return null;
}

/** 在类块内、锚点行后插入；单行类先展开为多行块 */
function insertInClass(classRx, anchorRx, newLines, what) {
  const r = classRange(classRx);
  if (!r) fail(`未找到 ${what} 的宿主类：${classRx}`);
  const [i, , oneLine] = r;
  const indent = lines[i].match(/^\s*/)[0];
  if (oneLine) {
    const t = lines[i].trim();
    const open = t.indexOf('{');
    const body = t.slice(open + 1, t.lastIndexOf('}')).trim();
    lines.splice(i, 1,
      indent + t.slice(0, open).trimEnd(),
      indent + '{',
      indent + '    ' + body,
      ...newLines,
      indent + '}');
    log.push(`  ✓ ${what}（单行类已展开 + 插入）`);
    return;
  }
  const [s, e] = classRange(classRx);
  if (anchorRx) {
    const a = lines.findIndex((l, idx) => idx > s && idx < e && anchorRx.test(l));
    if (a < 0) fail(`未找到 ${what} 的锚点：${anchorRx}`);
    lines.splice(a + 1, 0, ...newLines);
    log.push(`  ✓ ${what}（锚点后，行 ${a + 2}）`);
  } else {
    lines.splice(e, 0, ...newLines);
    log.push(`  ✓ ${what}（块尾，行 ${e + 1}）`);
  }
}

// ── 1. Vector4（Vector3 之后）─────────────────────────────────────────
if (!has(/^\s*public struct Vector4\b/)) {
  const r = classRange(/^\s*public struct Vector3\b/);
  if (!r) fail('未找到 Vector3');
  const indent = lines[r[0]].match(/^\s*/)[0];
  lines.splice(r[1] + 1, 0, '',
    indent + '/// <summary>四维向量（出处 ScriptReference/Vector4）。后处理的模糊方向与偏移用它。</summary>',
    indent + 'public struct Vector4',
    indent + '{',
    indent + '    public float x, y, z, w;',
    indent + '    public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }',
    indent + '    public static Vector4 zero => new Vector4(0f, 0f, 0f, 0f);',
    indent + '    public static Vector4 one => new Vector4(1f, 1f, 1f, 1f);',
    indent + '    public static Vector4 operator *(Vector4 a, float s) => new Vector4(a.x * s, a.y * s, a.z * s, a.w * s);',
    indent + '    public static Vector4 operator +(Vector4 a, Vector4 b) => new Vector4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);',
    indent + '}');
  log.push('  ✓ Vector4');
}

// ── 2. Mathf：Pow / Log / SmoothStep ─────────────────────────────────
{
  const add = [];
  if (!has(/public static float Pow\(float f, float p\)/)) add.push(
    '        /// <summary>幂（出处 ScriptReference/Mathf.Pow）。眼部适应曲线用它。</summary>',
    '        public static float Pow(float f, float p) => (float)Math.Pow(f, p);');
  if (!has(/public static float Log\(float f\)/)) add.push(
    '        /// <summary>自然对数（出处 ScriptReference/Mathf.Log）。亮度映射用它。</summary>',
    '        public static float Log(float f) => (float)Math.Log(f);',
    '        public static float Log(float f, float p) => (float)Math.Log(f, p);');
  if (!has(/public static float SmoothStep\(/)) add.push(
    '        /// <summary>平滑阶跃（出处 ScriptReference/Mathf.SmoothStep）。程序化贴图过渡用它。</summary>',
    '        public static float SmoothStep(float from, float to, float t)',
    '        {',
    '            float c = Clamp01((t - from) / (to - from));',
    '            return c * c * (3f - 2f * c);',
    '        }');
  if (add.length) insertInClass(/^\s*public static class Mathf\b/, /public static float Exp\(float v\)/, add, 'Mathf.Pow/Log/SmoothStep');
}

// ── 3. Camera：allowHDR / depthTextureMode ───────────────────────────
{
  const add = [];
  if (!has(/public bool allowHDR\b/)) add.push(
    '        /// <summary>允许 HDR 缓冲（出处 ScriptReference/Camera-allowHDR）。渲染三档会切它。</summary>',
    '        public bool allowHDR { get; set; }');
  if (!has(/public DepthTextureMode depthTextureMode\b/)) add.push(
    '        /// <summary>深度纹理模式（出处 ScriptReference/Camera-depthTextureMode）。SSAO/SSGI 需要 DepthNormals。</summary>',
    '        public DepthTextureMode depthTextureMode { get; set; }');
  if (add.length) insertInClass(/^\s*public class Camera : Behaviour\b/, /public RenderTexture targetTexture \{ get; set; \}/, add, 'Camera.allowHDR/depthTextureMode');
}

// ── 4. Material.SetVector ───────────────────────────────────────────
if (!has(/public void SetVector\(string name, Vector4 value\)/)) {
  insertInClass(/^\s*public class Material : Object\b/, /public void SetTexture\(string name, Texture value\)/,
    ['        /// <summary>设四维向量（出处 ScriptReference/Material.SetVector）。后处理传参用它。</summary>',
     '        public void SetVector(string name, Vector4 value) { }'],
    'Material.SetVector');
}

// ── 5. Light.renderMode + LightRenderMode 枚举 ───────────────────────
{
  if (!has(/public LightRenderMode renderMode\b/)) {
    insertInClass(/^\s*public class Light : Behaviour\b/, /public LightShadows shadows \{ get; set; \}/,
      ['        /// <summary>渲染模式（出处 ScriptReference/Light-renderMode）。吊灯要 ForcePixel 才出像素光。</summary>',
       '        public LightRenderMode renderMode { get; set; }'],
      'Light.renderMode');
  }
  if (!has(/public enum LightRenderMode\b/)) {
    const r = classRange(/^\s*public class Light : Behaviour\b/);
    if (!r) fail('未找到 Light 类');
    const indent = lines[r[0]].match(/^\s*/)[0];
    lines.splice(r[1] + 1, 0, '',
      indent + '/// <summary>灯的渲染模式（出处 ScriptReference/LightRenderMode）。',
      indent + '/// ⚠ 与 UnityEngine.UI.RenderMode（Canvas 用）不是同一枚举，故单列，避免命名冲突。</summary>',
      indent + 'public enum LightRenderMode { Auto = 0, ForcePixel = 1, ForceVertex = 2 }');
    log.push('  ✓ LightRenderMode 枚举');
  }
}

// ── 6. Canvas.scaleFactor（宿主是**单行类**）───────────────────────────
if (!has(/public float scaleFactor\b/)) {
  insertInClass(/^\s*public class Canvas : Behaviour\b/, null,
    ['    /// <summary>画布缩放因子（出处 ScriptReference/Canvas-scaleFactor）。',
     '    /// ⚠ 本项目硬教训：WorldToScreenPoint 给的是屏幕像素，写 anchoredPosition 必须除以它。</summary>',
     '    public float scaleFactor { get; set; }'],
    'Canvas.scaleFactor');
}

// ── 7. Texture2D：Apply(bool) / GetPixels32 / EncodeToPNG ────────────
{
  const add = [];
  if (!has(/public void Apply\(bool updateMipmaps\)/)) add.push('        public void Apply(bool updateMipmaps) { }');
  if (!has(/public Color32\[\] GetPixels32\(\)/)) add.push(
    '        /// <summary>读回像素（出处 ScriptReference/Texture2D.GetPixels32）。取证截图用它。</summary>',
    '        public Color32[] GetPixels32() => null;');
  if (!has(/public byte\[\] EncodeToPNG\(\)/)) add.push(
    '        /// <summary>编码 PNG（出处 ScriptReference/ImageConversion.EncodeToPNG）。</summary>',
    '        public byte[] EncodeToPNG() => null;');
  if (add.length) insertInClass(/^\s*public class Texture2D : Texture\b/, /public void Apply\(\)/, add, 'Texture2D.Apply(bool)/GetPixels32/EncodeToPNG');
}

// ── 8. RenderTexture.GetTemporary(4 参) ──────────────────────────────
if (!has(/public static RenderTexture GetTemporary\(int w, int h, int depth, RenderTextureFormat/)) {
  insertInClass(/^\s*public class RenderTexture : Texture\b/, /public static RenderTexture GetTemporary\(int w, int h, int depth\)/,
    ['        /// <summary>带格式的临时 RT（出处 ScriptReference/RenderTexture.GetTemporary）。HDR 后处理链用它。</summary>',
     '        public static RenderTexture GetTemporary(int w, int h, int depth, RenderTextureFormat format) => null;'],
    'RenderTexture.GetTemporary(4参)');
}

// ── 9. 顺带清掉重复的 CanvasScaler（与 Rect 等同类问题）──────────────
{
  const idx = lines.map((l, i) => (/^\s*public class CanvasScaler : Behaviour\b/.test(l) ? i : -1)).filter((i) => i >= 0);
  if (idx.length > 1) {
    // 保留第一个完整块，删掉后续重复块
    for (let k = idx.length - 1; k >= 1; k--) {
      const r = classRangeFrom(idx[k], /^\s*public class CanvasScaler : Behaviour\b/);
      if (!r) fail('CanvasScaler 重复块的边界未找到');
      lines.splice(r[0], r[1] - r[0] + 1);
      // 顺带删掉块后的空行
      if (lines[r[0]] === '') lines.splice(r[0], 1);
      log.push(`  ✓ 删除重复的 CanvasScaler（原行 ${r[0] + 1}）`);
    }
  }
  function classRangeFrom(i, rx) {
    if (!rx.test(lines[i])) return null;
    if (lines[i].includes('{') && lines[i].trimEnd().endsWith('}')) return [i, i];
    const indent = lines[i].match(/^\s*/)[0];
    for (let j = i + 1; j < lines.length; j++) if (lines[j] === indent + '}') return [i, j];
    return null;
  }
}

// ── 10. 自检：这些入口各自恰好 1 处 ───────────────────────────────────
for (const rx of [/^\s*public struct Vector4\b/, /public static float Pow\(float f, float p\)/,
  /public static float Log\(float f\)/, /public static float SmoothStep\(/, /public bool allowHDR\b/,
  /public DepthTextureMode depthTextureMode\b/, /public void SetVector\(string name, Vector4 value\)/,
  /public LightRenderMode renderMode\b/, /public enum LightRenderMode\b/, /public float scaleFactor\b/,
  /public void Apply\(bool updateMipmaps\)/, /public Color32\[\] GetPixels32\(\)/, /public byte\[\] EncodeToPNG\(\)/,
  /public static RenderTexture GetTemporary\(int w, int h, int depth, RenderTextureFormat/,
  /^\s*public class CanvasScaler : Behaviour\b/]) {
  const n = lines.filter((l) => rx.test(l)).length;
  if (n !== 1) fail(`自检：${rx} 命中 ${n} 次（应为 1）`);
}
log.push('  ✓ 自检通过：15 个条目各恰好 1 处');

console.log('[stubs+] 补齐渲染相关桩' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
if (!checkOnly) {
  const out = lines.join(eol);
  fs.writeFileSync(FILE, out, 'utf8');
  console.log(`  已写回：${raw.length} → ${out.length} 字节`);
}
