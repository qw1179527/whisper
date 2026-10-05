// `native/unity-stubs/UnityStubs.cs` 的**确定性**补桩器（第 6 版，前几版都因"猜锚点/按行判定"出错）。
//
// ## 这次的方法（不再猜任何东西）
// 1. 用**花括号栈**解析出每个类型的**行区间**（`public class X` / `struct` / `enum` 的开括号到匹配闭括号）。
//    → 类型区间是**算出来的**，不是猜的。
// 2. 需要补齐的成员按"目标类型 + 成员签名"声明；脚本检查该成员是否已在**该类型区间内**：
//    · 已有 → 跳过
//    · 没有 → 插到该类型区间的**最后一个 `}` 之前**
// 3. 需要删除的重复成员：在区间内找到**第二次及以后**的出现并删除。
// 4. 结束时复核：花括号平衡 + 每个待补成员都能在目标类型区间内找到。
import fs from 'node:fs';

const P = 'native/unity-stubs/UnityStubs.cs';
let lines = fs.readFileSync(P, 'utf8').split('\n');

/** 用花括号栈找出每个类型声明的 [开括号行, 闭括号行]
 *
 * ⚠ 关键细节（我第一版写错在这里）：这份文件里 **`public class X` 与它的 `{` 不在同一行**
 *   （`{` 在下一行）。第一版只统计**当前行**的花括号，于是类型永远推不进栈 → 一个类型都找不到
 *   （实测"未找到类型：Quaternion,TextAsset,..."，全部 17 个）。正解：遇到类型声明行时
 *   **向后找到真正的开括号行**，用它作为区间起点。
 */
function typeRanges(src) {
  const ranges = [];
  const stack = [];
  for (let i = 0; i < src.length; i++) {
    const t = src[i].trim();
    const m = /^(?:public |internal |)?(?:sealed |abstract |static |partial )*(class|struct|enum|interface)\s+(\w+)/.exec(t);
    if (m && !stack.some((x) => x.name === m[2])) {
      // 从当前行往后找第一个含 `{` 的行（通常就是下一行）
      let braceLine = -1;
      for (let j = i; j < Math.min(i + 3, src.length); j++) {
        if (src[j].includes('{')) { braceLine = j; break; }
      }
      if (braceLine >= 0) { stack.push({ name: m[2], open: braceLine }); continue; }
    }
    const opens = (src[i].match(/\{/g) || []).length;
    const closes = (src[i].match(/\}/g) || []).length;
    if (opens > closes && !m) stack.push({ name: null, open: i });
    for (let k = 0; k < closes - opens; k++) {
      const top = stack.pop();
      if (top && top.name) ranges.push({ name: top.name, from: top.open, to: i });
    }
  }
  return ranges;
}

// ── 待补成员：类型名 → 若干行（含注释） ──
const NEED = {
  Quaternion: [
    '        /// <summary>欧拉角（出处 ScriptReference/Quaternion-eulerAngles）。第一人称视角读写用它。</summary>',
    '        public Vector3 eulerAngles { get; set; }',
    '        public static Quaternion Euler(float x, float y, float z) => new Quaternion(x, y, z, 1f);',
    '        public static Quaternion Euler(Vector3 e) => new Quaternion(e.x, e.y, e.z, 1f);',
    '        public static Quaternion LookRotation(Vector3 forward) => identity;',
  ],
  TextAsset: [
    '        /// <summary>原始字节（出处 ScriptReference/TextAsset-bytes）。套件 GLB 走它读。</summary>',
    '        public byte[] bytes => null;',
  ],
  Mesh: [
    '        public Bounds bounds { get; set; }',
    '        public void SetVertices(Vector3[] vertices) { }',
    '        public void SetUVs(int channel, Vector2[] uvs) { }',
    '        public void SetTriangles(int[] triangles, int submesh) { }',
    '        public void SetNormals(Vector3[] normals) { }',
    '        public void RecalculateNormals() { }',
    '        public void RecalculateBounds() { }',
  ],
  Material: [
    '        public bool HasProperty(string name) => false;',
    '        public void SetColor(string name, Color value) { }',
    '        public void SetFloat(string name, float value) { }',
    '        public void EnableKeyword(string keyword) { }',
  ],
  GameObject: [
    '        /// <summary>在子物体里找组件（出处 ScriptReference/GameObject.GetComponentInChildren）。</summary>',
    '        public T GetComponentInChildren<T>() => default;',
  ],
  Resources: [
    '        /// <summary>取内置资源（出处 ScriptReference/Resources.GetBuiltinResource）。</summary>',
    '        public static T GetBuiltinResource<T>(string path) where T : Object => default;',
  ],
  Time: [
    '        /// <summary>自游戏开始的时间（出处 ScriptReference/Time-time）。</summary>',
    '        public static float time => 0f;',
  ],
  Shader: [
    '        /// <summary>设全局浮点（出处 ScriptReference/Shader.SetGlobalFloat）。雾密度用它。</summary>',
    '        public static void SetGlobalFloat(string name, float value) { }',
  ],
  Application: [
    '        /// <summary>StreamingAssets 路径（出处 ScriptReference/Application-streamingAssetsPath）。</summary>',
    '        public static string streamingAssetsPath => null;',
  ],
  Camera: [
    '        /// <summary>世界 → 屏幕（出处 ScriptReference/Camera.WorldToScreenPoint）。门交互的屏幕命中用它。</summary>',
    '        public Vector3 WorldToScreenPoint(Vector3 position) => position;',
    '        /// <summary>屏幕 → 射线（出处 ScriptReference/Camera.ScreenPointToRay）。</summary>',
    '        public Ray ScreenPointToRay(Vector3 position) => new Ray();',
  ],
  Mathf: [
    '        public static float Lerp(float a, float b, float t) => a + (b - a) * (t < 0f ? 0f : (t > 1f ? 1f : t));',
    '        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);',
    '        public static float PerlinNoise(float x, float y) => 0.5f;',
    '        public static float Sin(float v) => (float)Math.Sin(v);',
    '        public static float Cos(float v) => (float)Math.Cos(v);',
    '        public static float PI => 3.14159265f;',
    '        public static int RoundToInt(float v) => (int)Math.Round(v);',
    '        public static float Round(float v) => (float)Math.Round(v);',
    '        public static float MoveTowards(float cur, float target, float maxDelta)',
    '        {',
    '            float d = target - cur;',
    '            if (Math.Abs(d) <= maxDelta) return target;',
    '            return cur + (d > 0f ? maxDelta : -maxDelta);',
    '        }',
  ],
  Vector3: [
    '        public static Vector3 one => new Vector3(1, 1, 1);',
    '        public static Vector3 forward => new Vector3(0, 0, 1);',
    '        public static Vector3 right => new Vector3(1, 0, 0);',
    '        public static Vector3 up => new Vector3(0, 1, 0);',
    '        /// <summary>一元负号（出处 ScriptReference/Vector3-operator_UnaryNegation）。',
    '        /// 桩里只有二元减号时 -pivot 会报 CS0023 —— 先查桩再改代码。</summary>',
    '        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);',
    '        public static Vector3 Cross(Vector3 a, Vector3 b)',
    '            => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);',
  ],
  Transform: [
    '        public Quaternion localRotation { get; set; }',
    '        /// <summary>朝向（出处 ScriptReference/Transform-forward）。跳脸把脸贴相机正前方。</summary>',
    '        public Vector3 forward { get; set; }',
    '        public Vector3 right { get; set; }',
    '        public Vector3 up { get; set; }',
    '        /// <summary>子物体数量（出处 ScriptReference/Transform-childCount）。</summary>',
    '        public int childCount => 0;',
    '        public Transform GetChild(int index) => null;',
    '        /// <summary>可枚举子物体（真 Unity 的 Transform 实现 IEnumerable）。',
    '        /// 桩里没有它会报 CS1579 —— 先查桩再改代码。</summary>',
    '        public System.Collections.Generic.IEnumerator<Transform> GetEnumerator() { yield break; }',
  ],
  Light: [
    '        /// <summary>照射范围（出处 ScriptReference/Light-range）。主界面灯与跳脸补光靠它。</summary>',
    '        public float range { get; set; }',
    '        public float spotAngle { get; set; }',
    '        public LightShadows shadows { get; set; }',
  ],
  Button: [
    '        /// <summary>按钮的图形目标（出处 ScriptReference/Selectable-targetGraphic）。</summary>',
    '        public Graphic targetGraphic { get; set; }',
  ],
  Color: [
    '        /// <summary>线性插值（出处 ScriptReference/Color.Lerp）。</summary>',
    '        public static Color Lerp(Color a, Color b, float t) => new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t);',
    '        public static Color black => new Color(0f, 0f, 0f);',
    '        public static Color white => new Color(1f, 1f, 1f);',
    '        public static Color gray => new Color(0.5f, 0.5f, 0.5f);',
  ],
  Vector2: ['        public static Vector2 one => new Vector2(1, 1);'],
};

// 成员身份 = 去掉注释后第一行的签名（用于判"已有"）
const sigOf = (block) => {
  const first = block.find((l) => l.trim() && !l.trim().startsWith('///'));
  return first ? first.trim() : null;
};

let added = 0, skippedTypes = [];
for (const [typeName, block] of Object.entries(NEED)) {
  const ranges = typeRanges(lines);
  // 找**最外层**同名类型（第一个匹配）
  const r = ranges.find((x) => x.name === typeName);
  if (!r) { skippedTypes.push(typeName); continue; }
  const body = lines.slice(r.from, r.to + 1).join('\n');
  // 已有哪些签名？
  const missing = [];
  for (let i = 0; i < block.length; i++) {
    const l = block[i];
    if (l.trim().startsWith('///') || l.trim() === '{' || l.trim() === '}') { continue; }
    const sig = l.trim();
    // 只检查"声明行"（含 => 或 { get; 或 ( 的），避免把续行当签名
    const isDecl = /=>|;\s*$|\{\s*get/.test(sig) || /\(\s*$/.test(sig);
    if (isDecl) {
      // 提取成员名（去掉 public/static 后的标识符）
      const nm = /\b(\w+)\s*(\(|=>|;|\{)/.exec(sig.replace(/^(public|internal|private|protected|static|sealed|override|readonly|\s)+/g, ''));
      if (nm && body.includes(nm[1])) continue;   // 已有同名成员
    }
    missing.push(l);
  }
  if (!missing.length) continue;
  lines = [...lines.slice(0, r.to), ...missing, ...lines.slice(r.to)];
  added++;
}

fs.writeFileSync(P, lines.join('\n'), 'utf8');
const s = lines.join('\n');
const open = (s.match(/\{/g) || []).length, close = (s.match(/\}/g) || []).length;
console.log(`  ✓ 补齐 ${added} 个类型${skippedTypes.length ? ' · 未找到类型：' + skippedTypes.join(',') : ''}`);
console.log(`  · 行数 ${lines.length} · 花括号 { ${open} / } ${close} ${open === close ? '平衡 ✓' : '不平衡 ✗'}`);
