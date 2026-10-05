// 一次性补全 `native/unity-stubs/UnityStubs.cs`（git 恢复冲掉了本轮的桩工作）。
//
// ## 纪律（前两次把文件改坏的教训）
// · **不用"找大括号插入"**（嵌套类 + 注释里的花括号必然错位，我因此让文件语法崩过 5 处）
// · 只用**精确整块字符串替换**，锚点先 `split().length-1 === 1` 校验命中次数
// · 单行成员用 `insertAfterUniqueLine`：要求该行在文件里**唯一**，否则拒绝
// · 全部改完打印"改动条数 + 花括号是否平衡"
import fs from 'node:fs';

const P = 'native/unity-stubs/UnityStubs.cs';
let s = fs.readFileSync(P, 'utf8');
const log = [];
let n = 0;

/** 唯一整行之后插入（该行必须唯一出现） */
function after(anchorLine, addLines, tag) {
  const hits = s.split(anchorLine).length - 1;
  if (hits !== 1) { log.push(`  ! ${tag}：锚点命中 ${hits} 次，跳过`); return false; }
  s = s.replace(anchorLine, anchorLine + '\n' + addLines.join('\n'));
  n++; log.push(`  ✓ ${tag}`); return true;
}
/** 整块替换（锚点必须唯一） */
function block(a, b, tag) {
  const hits = s.split(a).length - 1;
  if (hits !== 1) { log.push(`  ! ${tag}：块命中 ${hits} 次，跳过`); return false; }
  s = s.replace(a, b); n++; log.push(`  ✓ ${tag}`); return true;
}

// ── Vector2/3/4 与 Quaternion ──
after(`        public static Vector2 zero => new Vector2(0, 0);`,
  [`        public static Vector2 one => new Vector2(1, 1);`], 'Vector2.one');

after(`        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;`,
  [`        public static Vector3 forward => new Vector3(0, 0, 1);
        public static Vector3 right => new Vector3(1, 0, 0);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 Cross(Vector3 a, Vector3 b)
            => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);`],
  'Vector3 常量与 Cross');

// Quaternion：eulerAngles / Euler / identity / LookRotation
after(`        public static Quaternion identity => new Quaternion(0, 0, 0, 1);`,
  [`        /// <summary>欧拉角（出处 ScriptReference/Quaternion-eulerAngles）。第一人称视角读取用它。</summary>
        public Vector3 eulerAngles { get; set; }
        /// <summary>由欧拉角构造（出处 ScriptReference/Quaternion.Euler）。</summary>
        public static Quaternion Euler(float x, float y, float z) => new Quaternion(x, y, z, 1f);
        public static Quaternion Euler(Vector3 e) => new Quaternion(e.x, e.y, e.z, 1f);
        /// <summary>朝向目标（出处 ScriptReference/Quaternion.LookRotation）。</summary>
        public static Quaternion LookRotation(Vector3 forward) => identity;`],
  'Quaternion 成员');

// ── Transform：localRotation / forward / right / up / Rotate / childCount / GetEnumerator ──
if (!/public Quaternion localRotation/.test(s)) {
  after(`        public Quaternion rotation { get; set; }`,
    [`        public Quaternion localRotation { get; set; }
        /// <summary>朝向（出处 ScriptReference/Transform-forward）。跳脸把脸贴在相机正前方。</summary>
        public Vector3 forward { get; set; }
        public Vector3 right { get; set; }
        public Vector3 up { get; set; }
        /// <summary>子物体数量（出处 ScriptReference/Transform-childCount）。</summary>
        public int childCount => 0;
        public Transform GetChild(int index) => null;
        /// <summary>可枚举子物体（真 Unity 的 Transform 实现 IEnumerable）。
        /// 桩里若没有它，foreach (Transform c in t) 会报 CS1579 —— 先查桩再改代码。</summary>
        public System.Collections.Generic.IEnumerator<Transform> GetEnumerator() { yield break; }`],
    'Transform 成员');
}

// ── TextAsset.bytes ──
if (!/public byte\[\] bytes/.test(s)) {
  const ta = `    public class TextAsset`;
  const i = s.indexOf(ta);
  if (i >= 0) {
    const end = s.indexOf('\n    }', i);
    s = s.slice(0, end) + `\n        /// <summary>原始字节（出处 ScriptReference/TextAsset-bytes）。套件 GLB 走它读。</summary>` +
        `\n        public byte[] bytes => null;` + s.slice(end);
    n++; log.push('  ✓ TextAsset.bytes');
  } else log.push('  ! 找不到 TextAsset');
}

// ── Mesh：bounds / SetVertices / SetUVs / SetTriangles（数组 + List 两套重载）──
{
  const mi = s.indexOf('    public class Mesh');
  if (mi >= 0) {
    const end = s.indexOf('\n    }', mi);
    const body = s.slice(mi, end);
    const add = [];
    if (!body.includes('public Bounds bounds')) add.push('        public Bounds bounds { get; set; }');
    // 真 Unity 数组与 List 两种重载都在；桩里只声明 List 会报 CS1503（先查桩）
    if (!body.includes('SetVertices(Vector3[]')) add.push(
      '        public void SetVertices(Vector3[] vertices) { }',
      '        public void SetUVs(int channel, Vector2[] uvs) { }',
      '        public void SetTriangles(int[] triangles, int submesh) { }',
      '        public void SetNormals(Vector3[] normals) { }');
    if (add.length) { s = s.slice(0, end) + '\n' + add.join('\n') + s.slice(end); n++; log.push('  ✓ Mesh 成员'); }
    else log.push('  · Mesh 成员已齐');
  } else log.push('  ! 找不到 Mesh');
}

// ── Material：HasProperty / SetColor / SetFloat / EnableKeyword ──
{
  const mi = s.indexOf('    public class Material');
  if (mi >= 0) {
    const end = s.indexOf('\n    }', mi);
    const body = s.slice(mi, end);
    const add = [];
    if (!body.includes('HasProperty')) add.push('        public bool HasProperty(string name) => false;');
    if (!body.includes('SetColor')) add.push('        public void SetColor(string name, Color value) { }');
    if (!body.includes('SetFloat')) add.push('        public void SetFloat(string name, float value) { }');
    if (!body.includes('EnableKeyword')) add.push('        public void EnableKeyword(string keyword) { }');
    if (add.length) { s = s.slice(0, end) + '\n' + add.join('\n') + s.slice(end); n++; log.push('  ✓ Material 成员'); }
    else log.push('  · Material 成员已齐');
  } else log.push('  ! 找不到 Material');
}

// ── Light：range / spotAngle / shadows ──
block(`    public class Light : Behaviour
    {
        public LightType type { get; set; }
        public float intensity { get; set; }
        public Color color { get; set; }
    }`,
`    public class Light : Behaviour
    {
        public LightType type { get; set; }
        public float intensity { get; set; }
        public Color color { get; set; }
        /// <summary>照射范围（出处 ScriptReference/Light-range）。主界面灯与跳脸补光靠它。</summary>
        public float range { get; set; }
        public float spotAngle { get; set; }
        public LightShadows shadows { get; set; }
    }

    /// <summary>阴影模式（出处 ScriptReference/LightShadows）。</summary>
    public enum LightShadows { None, Hard, Soft }`,
  'Light 成员 + LightShadows');

// ── Color：Lerp / black / white / gray ──
{
  const ci = s.indexOf('    public struct Color');
  if (ci >= 0) {
    const end = s.indexOf('\n    }', ci);
    const body = s.slice(ci, end);
    const add = [];
    if (!body.includes('Lerp(')) add.push('        /// <summary>线性插值（出处 ScriptReference/Color.Lerp）。</summary>',
      '        public static Color Lerp(Color a, Color b, float t) => new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t);');
    if (!/static Color black/.test(body)) add.push('        public static Color black => new Color(0f, 0f, 0f);');
    if (!/static Color white/.test(body)) add.push('        public static Color white => new Color(1f, 1f, 1f);');
    if (!/static Color gray/.test(body)) add.push('        public static Color gray => new Color(0.5f, 0.5f, 0.5f);');
    if (add.length) { s = s.slice(0, end) + '\n' + add.join('\n') + s.slice(end); n++; log.push('  ✓ Color 成员'); }
    else log.push('  · Color 成员已齐');
  } else log.push('  ! 找不到 Color');
}

// ── Mathf：Lerp / Clamp01 / PerlinNoise / MoveTowards / Sin / Cos / PI / Round ──
{
  const mi = s.indexOf('    public static class Mathf');
  if (mi >= 0) {
    const end = s.indexOf('\n    }', mi);
    const body = s.slice(mi, end);
    const add = [];
    if (!body.includes('float Lerp(')) add.push('        public static float Lerp(float a, float b, float t) => a + (b - a) * (t < 0f ? 0f : (t > 1f ? 1f : t));');
    if (!body.includes('Clamp01')) add.push('        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);');
    if (!body.includes('PerlinNoise')) add.push('        /// <summary>Perlin 噪声（出处 ScriptReference/Mathf.PerlinNoise）。灯光抖动用它。</summary>',
      '        public static float PerlinNoise(float x, float y) => 0.5f;');
    if (!body.includes('MoveTowards')) add.push('        /// <summary>朝目标推进（出处 ScriptReference/Mathf.MoveTowards）。门扇开合用它。</summary>',
      '        public static float MoveTowards(float cur, float target, float maxDelta)\n        {\n            float d = target - cur;\n            if (Math.Abs(d) <= maxDelta) return target;\n            return cur + (d > 0f ? maxDelta : -maxDelta);\n        }');
    if (!body.includes('float Sin(')) add.push('        public static float Sin(float v) => (float)Math.Sin(v);',
      '        public static float Cos(float v) => (float)Math.Cos(v);');
    if (!body.includes('float PI')) add.push('        public static float PI => 3.14159265f;');
    if (!body.includes('float Round(')) add.push('        public static float Round(float v) => (float)Math.Round(v);',
      '        public static int RoundToInt(float v) => (int)Math.Round(v);');
    if (add.length) { s = s.slice(0, end) + '\n' + add.join('\n') + s.slice(end); n++; log.push('  ✓ Mathf 成员'); }
    else log.push('  · Mathf 成员已齐');
  } else log.push('  ! 找不到 Mathf');
}

// ── Time：time ──
{
  const ti = s.indexOf('    public static class Time');
  if (ti >= 0) {
    const end = s.indexOf('\n    }', ti);
    const body = s.slice(ti, end);
    if (!body.includes('float time')) {
      s = s.slice(0, end) + '\n        /// <summary>自游戏开始的时间（出处 ScriptReference/Time-time）。</summary>\n        public static float time => 0f;' + s.slice(end);
      n++; log.push('  ✓ Time.time');
    } else log.push('  · Time.time 已齐');
  } else log.push('  ! 找不到 Time');
}

// ── GameObject.GetComponentInChildren ──
{
  const gi = s.indexOf('    public class GameObject');
  if (gi >= 0) {
    const end = s.indexOf('\n    }', gi);
    const body = s.slice(gi, end);
    if (!body.includes('GetComponentInChildren')) {
      s = s.slice(0, end) + '\n        /// <summary>在子物体里找组件（出处 ScriptReference/GameObject.GetComponentInChildren）。</summary>\n        public T GetComponentInChildren<T>() => default;' + s.slice(end);
      n++; log.push('  ✓ GameObject.GetComponentInChildren');
    } else log.push('  · GameObject 已齐');
  } else log.push('  ! 找不到 GameObject');
}

// ── Resources.GetBuiltinResource ──
{
  const ri = s.indexOf('    public class Resources');
  if (ri >= 0) {
    const end = s.indexOf('\n    }', ri);
    const body = s.slice(ri, end);
    if (!body.includes('GetBuiltinResource')) {
      s = s.slice(0, end) + '\n        /// <summary>取内置资源（出处 ScriptReference/Resources.GetBuiltinResource）。\n        /// 文本要字体：Resources.GetBuiltinResource&lt;Font&gt;("LegacyRuntime.ttf")。</summary>\n        public static T GetBuiltinResource<T>(string path) where T : Object => default;' + s.slice(end);
      n++; log.push('  ✓ Resources.GetBuiltinResource');
    } else log.push('  · Resources 已齐');
  } else log.push('  ! 找不到 Resources');
}

fs.writeFileSync(P, s, 'utf8');
console.log(log.join('\n'));
const open = (s.match(/\{/g) || []).length, close = (s.match(/\}/g) || []).length;
console.log(`  ✓ 共 ${n} 处 · 行数 ${s.split('\n').length} · 花括号 { ${open} / } ${close} ${open === close ? '平衡 ✓' : '不平衡 ✗'}`);
