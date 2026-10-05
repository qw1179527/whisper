// **错误驱动的补桩器**：读 `unity-syntax-check` 的输出，只补它点名的缺失成员。
//
// ## 为什么换成这个方法（前 7 版都失败）
// 我一直在"手工维护一份完整的 Unity API 桩"——那份文件很大、我改坏过 3 次
// （内容插到命名空间外、误删成员、把类型区间裁掉）。
// **正解：只补项目真正用到的那几个成员**，而"用到哪些"由编译器**精确告诉我们**：
// 语法门禁会输出 `CS0117 X 未包含 Y 的定义` / `CS1061` / `CS1503` / `CS1579` / `CS0246`。
// 于是流程变成：**跑门禁 → 解析错误 → 补那一个成员 → 重跑**，直到 0 真错误。
// 这样桩文件永远是最小的、每一行都被验证过，而且不会再出现"我猜错了 API"的情况。
//
// 用法：node tools/patch-stubs-from-errors.mjs [--dry]
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const STUB = path.join(ROOT, 'native/unity-stubs/UnityStubs.cs');
const DRY = process.argv.includes('--dry');
const BASH = process.env.WHISPER_BASH || 'C:\\Users\\qing_\\.dsh\\tools\\PortableGit\\bin\\bash.exe';

function runCheck() {
  try {
    return execFileSync(BASH, ['-c', 'cd /d/DSH专用/whisper && bash tools/unity-syntax-check.sh 2>&1'], { encoding: 'utf8', maxBuffer: 32 * 1024 * 1024 });
  } catch (e) {
    return (e.stdout || '') + (e.stderr || '');
  }
}

const out = runCheck();
// 解析形如：CS1061 PlayerController.cs(87): "Quaternion"未包含"eulerAngles"的定义
const need = new Map();   // `${type}.${member}` → { type, member, kind }
for (const line of out.split('\n')) {
  let m = /CS0117\s+(\S+?)\.cs\(\d+\):\s*[“"]([^”"]+)[”"]\s*未包含\s*[“"]([^”"]+)[”"]\s*的定义/.exec(line);
  if (m) { need.set(`${m[2]}.${m[3]}`, { type: m[2], member: m[3], kind: 'CS0117' }); continue; }
  m = /CS1061\s+(\S+?)\.cs\(\d+\):\s*[“"]([^”"]+)[”"]\s*未包含\s*[“"]([^”"]+)[”"]\s*的定义/.exec(line);
  if (m) { need.set(`${m[2]}.${m[3]}`, { type: m[2], member: m[3], kind: 'CS1061' }); continue; }
  m = /CS0246\s+(\S+?)\.cs\(\d+\):\s*未能找到类型或命名空间名\s*[“"]([^”"]+)[”"]/.exec(line);
  if (m) { need.set(`TYPE:${m[2]}`, { type: m[2], member: null, kind: 'CS0246' }); continue; }
}

console.log(`  门禁报出 ${need.size} 个缺失点`);
for (const [k] of need) console.log('    · ' + k);
if (need.size === 0) { console.log('  ✓ 没有需要补的（或输出格式变了）'); process.exit(0); }

// ── 已知成员 → 桩代码（**只写我确知签名的**；不确定的宁可留空也不臆造）──
const KNOWN = {
  'Vector3.operator -': null,
  'Quaternion.eulerAngles': '        public Vector3 eulerAngles { get; set; }',
  'Transform.localRotation': '        public Quaternion localRotation { get; set; }',
  'Transform.forward': '        public Vector3 forward { get; set; }',
  'Transform.right': '        public Vector3 right { get; set; }',
  'Transform.up': '        public Vector3 up { get; set; }',
  'Transform.parent': '        public Transform parent { get; set; }',
  'Transform.childCount': '        public int childCount => 0;',
  'Mesh.bounds': '        public Bounds bounds { get; set; }',
  'MeshRenderer.enabled': '        public bool enabled { get; set; }',
  // 真 Unity 这几对 API **数组与 List 两种重载都在**（出处 ScriptReference/Mesh.SetVertices 等）。
  // 桩里只声明一种时，另一种会报 CS1503/CS1061 —— 先查桩再改代码。
  'Mesh.SetVertices': '        public void SetVertices(System.Collections.Generic.List<Vector3> v) { }\n        public void SetVertices(Vector3[] v) { }',
  'Mesh.SetUVs': '        public void SetUVs(int ch, System.Collections.Generic.List<Vector2> v) { }\n        public void SetUVs(int ch, Vector2[] v) { }',
  'Mesh.SetTriangles': '        public void SetTriangles(System.Collections.Generic.List<int> t, int sub) { }\n        public void SetTriangles(int[] t, int sub) { }',
  'Transform.GetChild': '        public Transform GetChild(int index) => null;',
  'Mathf.Lerp': '        public static float Lerp(float a, float b, float t) => a + (b - a) * (t < 0f ? 0f : (t > 1f ? 1f : t));',
  'Mathf.Clamp01': '        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);',
  'Mathf.PerlinNoise': '        public static float PerlinNoise(float x, float y) => 0.5f;',
  'Mathf.RoundToInt': '        public static int RoundToInt(float v) => (int)Math.Round(v);',
  'Mathf.Round': '        public static float Round(float v) => (float)Math.Round(v);',
  'Mathf.MoveTowards': '        public static float MoveTowards(float cur, float target, float maxDelta) { float d = target - cur; if (Math.Abs(d) <= maxDelta) return target; return cur + (d > 0f ? maxDelta : -maxDelta); }',
  'Mathf.Sin': '        public static float Sin(float v) => (float)Math.Sin(v);',
  'Mathf.Cos': '        public static float Cos(float v) => (float)Math.Cos(v);',
  'Mathf.PI': '        public static float PI => 3.14159265f;',
  'Time.time': '        public static float time => 0f;',
  'Light.range': '        public float range { get; set; }',
  'Light.spotAngle': '        public float spotAngle { get; set; }',
  'Material.HasProperty': '        public bool HasProperty(string name) => false;',
  'Material.SetColor': '        public void SetColor(string name, Color value) { }',
  'Material.SetFloat': '        public void SetFloat(string name, float value) { }',
  'Material.EnableKeyword': '        public void EnableKeyword(string keyword) { }',
  'Button.targetGraphic': '        public Graphic targetGraphic { get; set; }',
  'TextAsset.bytes': '        public byte[] bytes => null;',
  'Shader.SetGlobalFloat': '        public static void SetGlobalFloat(string name, float value) { }',
  'Application.streamingAssetsPath': '        public static string streamingAssetsPath => null;',
  'Application.targetFrameRate': '        public static int targetFrameRate { get; set; }',
  'Application.unityVersion': '        public static string unityVersion => null;',
  'Application.dataPath': '        public static string dataPath => null;',
  'GameObject.GetComponentInChildren': '        public T GetComponentInChildren<T>() => default;',
  'Camera.WorldToScreenPoint': '        public Vector3 WorldToScreenPoint(Vector3 position) => position;',
  'Camera.ScreenPointToRay': '        public Ray ScreenPointToRay(Vector3 position) => new Ray();',
  'Camera.targetTexture': '        public RenderTexture targetTexture { get; set; }',
  'Camera.Render': '        public void Render() { }',
  'Texture2D.EncodeToPNG': '        public byte[] EncodeToPNG() => null;',
  'Texture2D.GetPixels32': '        public Color32[] GetPixels32() => null;',
  'Texture2D.ReadPixels': '        public void ReadPixels(Rect source, int destX, int destY) { }',
};

// 需要新增的**类型**（CS0246）
const KNOWN_TYPES = {
  Color32: `    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }`,
  Collider: `    public class Collider : Component { public bool enabled { get; set; } }
    public class BoxCollider : Collider { public Vector3 size { get; set; } }
    public class CapsuleCollider : Collider { public float radius { get; set; } public float height { get; set; } }`,
  Vector2: `    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0, 0);
        public static Vector2 one => new Vector2(1, 1);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, float s) => new Vector2(a.x * s, a.y * s);
    }`,
  Texture: `    public class Texture : Object { }
    public enum TextureFormat { Alpha8 = 1, RGB24 = 3, RGBA32 = 4, ARGB32 = 5 }`,
  Rect: `    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; }
    }`,
  IPostprocessBuild: `    /// <summary>构建后回调（出处 UnityEditor.Build.IPostprocessBuild 系列）。</summary>
    public interface IPostprocessBuild { int callbackOrder { get; } void OnPostprocessBuild(object report); }`,
  RenderTexture: `    public class RenderTexture : Texture
    {
        public RenderTexture(int w, int h, int depth) { }
        public static RenderTexture GetTemporary(int w, int h, int depth) => null;
        public static void ReleaseTemporary(RenderTexture rt) { }
        public static RenderTexture active { get; set; }
    }`,
  Sprite: `    public class Sprite : Object
    {
        public static Sprite Create(Texture2D tex, Rect rect, Vector2 pivot) => null;
        public static Sprite Create(Texture2D tex, Rect rect, Vector2 pivot, float ppu) => null;
    }`,
  Texture2D: `    public class Texture2D : Texture
    {
        public Texture2D(int w, int h) { }
        public Texture2D(int w, int h, TextureFormat fmt, bool mip) { }
        public void SetPixels32(Color32[] colors) { }
        public void Apply() { }
        public void Apply(bool updateMipmaps) { }
        public byte[] EncodeToPNG() => null;
        public Color32[] GetPixels32() => null;
        public void ReadPixels(Rect source, int destX, int destY) { }
    }`,
};

let src = fs.readFileSync(STUB, 'utf8');
const lines = src.split('\n');
let added = 0, unknown = [];

/** 找类型区间：先定位声明行，再向后找它的开括号行，然后用**花括号计数**扫到匹配的闭括号。
 *  这样写比"栈 + 类型名配对"稳得多（前一版因为注释里的花括号与"class 与 { 不同行"而失效）。 */
function rangeOf(name) {
  const decl = new RegExp('(^|\\s)(class|struct|enum|interface)\\s+' + name + '\\b');
  let di = -1;
  for (let i = 0; i < lines.length; i++) {
    if (decl.test(lines[i]) && !lines[i].trim().startsWith('//') && !lines[i].trim().startsWith('///')) { di = i; break; }
  }
  if (di < 0) return null;
  let oi = -1;
  for (let j = di; j < Math.min(di + 3, lines.length); j++) if (lines[j].includes('{')) { oi = j; break; }
  if (oi < 0) return null;
  let depth = 0, started = false;
  for (let i = oi; i < lines.length; i++) {
    const o = (lines[i].match(/\{/g) || []).length, c = (lines[i].match(/\}/g) || []).length;
    if (o > 0) started = true;
    depth += o - c;
    if (started && depth <= 0) return { open: oi, close: i };
  }
  return null;
}

// 按类型分组，一次插入（避免多次改行号）
const byType = new Map();
for (const [key, info] of need) {
  if (info.kind === 'CS0246') continue;
  const code = KNOWN[`${info.type}.${info.member}`];
  if (!code) { unknown.push(`${info.type}.${info.member}`); continue; }
  if (!byType.has(info.type)) byType.set(info.type, []);
  byType.get(info.type).push(code);
}

// 从后往前插（行号不漂移）
const targets = [...byType.entries()].map(([t, codes]) => ({ t, codes, r: rangeOf(t) }))
  .filter((x) => x.r).sort((a, b) => b.r.close - a.r.close);
const skipped = [...byType.keys()].filter((t) => !rangeOf(t));
for (const { codes, r } of targets) {
  lines.splice(r.close, 0, ...codes);
  added += codes.length;
}

// 新增类型：插到 namespace UnityEngine 的末尾（最后一个顶层 } 之前）
const typeCodes = [];
for (const [key, info] of need) {
  if (info.kind !== 'CS0246') continue;
  const code = KNOWN_TYPES[info.type];
  if (code) typeCodes.push(code); else unknown.push('TYPE:' + info.type);
}
if (typeCodes.length) {
  const last = lines.length - 1;
  while (last > 0 && !lines[last].startsWith('}')) { /* 找最后一行顶层 } */ break; }
  let closeIdx = -1;
  for (let i = lines.length - 1; i >= 0; i--) if (lines[i] === '}') { closeIdx = i; break; }
  if (closeIdx > 0) { lines.splice(closeIdx, 0, ...typeCodes); added += typeCodes.length; }
}

if (!DRY) fs.writeFileSync(STUB, lines.join('\n'), 'utf8');
console.log(`  ✓ 补 ${added} 个成员/类型${skipped.length ? ' · 未找到类型区间：' + skipped.join(',') : ''}`);
if (unknown.length) console.log('  ! 我还不确知签名的（未臆造，请人工确认）：\n    ' + unknown.join('\n    '));
