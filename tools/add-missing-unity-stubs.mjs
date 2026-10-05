#!/usr/bin/env node
/**
 * add-missing-unity-stubs.mjs — 补齐 `native/unity-stubs/UnityStubs.cs` 缺失的 Unity API 面
 *
 * ## 这个脚本解决什么
 * `bash tools/unity-syntax-check.sh` 判红时，真实错误几乎全是**桩缺 API 成员**
 * （CS1061「类型不含该成员」/ CS0117「类型不含该定义」/ CS0023「运算符缺失」/ CS1579「不可枚举」），
 * 不是游戏源码写错。判据只有一个：**补真实 API 面，让真错误自然降为 0**；
 * 绝不把错误码塞进检查器的 allowed 集合（那是把门禁改瞎）。
 *
 * ## 为什么是「生成一段扩充区」而不是「逐个锚点插入」
 * 前几版脚本（`add-missing-render-stubs.mjs` 等）按「宿主类里的锚点行」插入成员。
 * 它们的锚点依赖**另一个版本的桩**（例如要求 `Mathf.Exp` 已存在才能插 `Mathf.Pow`），
 * 桩一变老就全部失配、直接失败——不可复现。
 * 本脚本改为：
 *   ① 把需要扩充的既有类型改成 `partial`（确定性字符串改写，单行类也能改）；
 *   ② 需要扩值的 `enum`（KeyCode）就地改写成完整声明；
 *   ③ 文件末尾维护一段**生成区**，内容由本脚本的模板整段重建。
 * 重跑 = 删掉旧生成区 + 重建 → 结果逐字节一致（幂等），与桩的历史版本无关。
 *
 * ## 纪律（每条签名都查过官方文档）
 * - 每个新增类型上方有 `/// <summary>文档：<类型名> — <用途>。</summary>`。
 * - 只补**项目实际用到**的成员；实现给 `default`/空体即可（桩只用于编译期语义分析）。
 * - ⚠ 桩永远无法验证「Unity 真的有这个成员」（CI #18：臆造 `UnityEditor.SplashScreen`）。
 *   往这里加成员前必须查 https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ ；
 *   查不到就别加。
 *
 * ## 用法
 *   node tools/add-missing-unity-stubs.mjs           # 重建生成区（幂等）
 *   node tools/add-missing-unity-stubs.mjs --check   # 只校验：生成区与模板一致、partial/enum 已就位
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'native/unity-stubs/UnityStubs.cs');
const checkOnly = process.argv.includes('--check');

const MARK_BEGIN = '// @@UNITY-STUBS-EXTENSION-BEGIN@@';
const MARK_END = '// @@UNITY-STUBS-EXTENSION-END@@';

const fail = (m) => { console.error('[stubs+] ✗ ' + m); process.exit(1); };
const log = [];

const raw = fs.readFileSync(FILE, 'utf8');
const eol = raw.includes('\r\n') ? '\r\n' : '\n';
let lines = raw.split(/\r?\n/);
const hadTrailingNewline = lines.length > 0 && lines[lines.length - 1] === '';
if (hadTrailingNewline) lines.pop();

// ── 0. 先摘掉旧的生成区（若存在），保证后面的检查只看「原始桩」 ──────────────
{
  const bi = lines.findIndex((l) => l.includes(MARK_BEGIN));
  const ei = lines.findIndex((l) => l.includes(MARK_END));
  if (bi >= 0 || ei >= 0) {
    if (bi < 0 || ei < 0 || ei < bi) fail('生成区标记不成对（只有 BEGIN 或只有 END）');
    lines.splice(bi, ei - bi + 1);
    while (lines.length && lines[lines.length - 1].trim() === '') lines.pop();
    log.push(`  ✓ 摘掉旧生成区（原 ${ei - bi + 1} 行）`);
  }
}

// ── 1. 把既有类型改成 partial（确定性改写；只动声明前缀）────────────────────
// 为什么要 partial：成员写进生成区，生成区整段可重建 → 幂等；否则每次补桩都得找锚点。
// 声明探测必须严格：`Texture` 不能命中 `Texture2D`/`RenderTexture`，`Object` 不能命中 `: Object`，
// 所以类型名后面必须是 `:` / `{` / `,` / 行尾。
const declRx = (name, withPartial) => new RegExp(
  `^(\\s*)public\\s+((?:static\\s+|sealed\\s+|abstract\\s+)*)${withPartial ? 'partial\\s+' : ''}(class|struct)\\s+${name}(?=\\s*(?::|\\{|,|$))`);

function ensurePartial(name) {
  const withP = declRx(name, true);
  const hitsP = lines.map((l, i) => (withP.test(l) ? i : -1)).filter((i) => i >= 0);
  if (hitsP.length === 1) { log.push(`  · ${name} 已是 partial`); return; }
  if (hitsP.length > 1) fail(`${name} 的 partial 声明命中 ${hitsP.length} 次（期望 ≤1）`);
  const rx = declRx(name, false);
  const hits = lines.map((l, i) => (rx.test(l) ? i : -1)).filter((i) => i >= 0);
  if (hits.length !== 1) fail(`${name} 的声明行命中 ${hits.length} 次（期望 1）`);
  const i = hits[0];
  lines[i] = lines[i].replace(rx, (_m, ind, mods, kind) => `${ind}public ${mods}partial ${kind} ${name}`);
  log.push(`  ✓ ${name} → partial`);
}

const PARTIAL_TYPES = [
  'Vector2', 'Vector3', 'Color', 'Object', 'Transform', 'GameObject', 'Mesh', 'Material',
  'Camera', 'Canvas', 'RectTransform', 'Input', 'Mathf', 'Application', 'Time', 'Resources',
];
for (const name of PARTIAL_TYPES) ensurePartial(name);
// UnityEngine.UI 的两个类型（Image / Button）在另一个命名空间，但改写方式相同。
for (const name of ['Image', 'Button']) ensurePartial(name);

// Transform 要支持 `foreach (Transform child in t)`：真 Unity 里是 `Transform : Component, IEnumerable`。
{
  const i = lines.findIndex((l) => /^\s*public\s+partial\s+class\s+Transform\s*:\s*Component\s*$/.test(l));
  if (i < 0) {
    const j = lines.findIndex((l) => /^\s*public\s+partial\s+class\s+Transform\b.*IEnumerable/.test(l));
    if (j < 0) fail('未找到 Transform 声明（或 IEnumerable 未接上）');
    log.push('  · Transform 已实现 IEnumerable');
  } else {
    lines[i] = lines[i].replace(/:\s*Component\s*$/, ': Component, System.Collections.IEnumerable');
    log.push('  ✓ Transform : Component, System.Collections.IEnumerable');
  }
}

// ── 2. KeyCode 就地扩值（enum 不能 partial）──────────────────────────────────
// 代码用到 H / J / Alpha1（联机建房/加入 + 地图 1..9）。数值与 ASCII 一致（真 Unity 亦如此）。
{
  const KEYCODE = 'public enum KeyCode { None = 0, Escape = 27, Space = 32, Alpha1 = 49, E = 101, H = 104, J = 106 }';
  const hits = [];
  lines.forEach((l, i) => { if (/^\s*public\s+enum\s+KeyCode\b/.test(l)) hits.push(i); });
  if (hits.length !== 1) fail(`KeyCode 声明行命中 ${hits.length} 次（期望 1）`);
  const i = hits[0];
  const indent = lines[i].match(/^\s*/)[0];
  if (lines[i].trim() === KEYCODE) log.push('  · KeyCode 已是最新');
  else { lines[i] = indent + KEYCODE; log.push('  ✓ KeyCode 扩值（H/J/Alpha1）'); }
}

// ── 3. 生成区内容（整段由模板重建）───────────────────────────────────────────
const EXTENSION = `// ══════════════════════════════════════════════════════════════════════════════
// 扩充桩区 · 开始（由 tools/add-missing-unity-stubs.mjs 生成 —— 手改会被下次重跑覆盖）
//
// 出处：每条签名都对着官方文档核过
//   https://docs.unity3d.com/6000.0/Documentation/ScriptReference/<Type>[-<Member>].html
//   （少数文档页只列了部分重载的，另核过 UnityCsReference 源码，注释里注明）
//
// 收录口径：以「项目实际用到」为准；**成对的官方重载与运算符一并列出** ——
//   因为缺一个重载/运算符时 Roslyn 报的是 CS1503/CS0019（在检查器 allowed 集合里），
//   会被当成「Unity 缺失」放过，那是假绿。列全了才能真正完成语义分析。
//   实现一律给 default / 空体（桩仅用于编译期语义分析，不参与任何构建）。
//
// ⚠ 这段桩能验证的只有「调用点与签名自洽」，**永远无法验证 Unity 真的有这个成员**
//   （CI #18 事故：臆造的 UnityEditor.SplashScreen 被桩背书 → 本机全绿、CI 失败）。
//   加成员前先查文档；查不到就别加。
// ══════════════════════════════════════════════════════════════════════════════
namespace UnityEngine
{
    // ────────────────────────── 既有类型的 partial 扩充 ──────────────────────────

    public partial struct Vector2
    {
        /// <summary>文档：Vector2.Lerp — 线性插值（t 不夹紧）。摇杆平滑用它。</summary>
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => new Vector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);
    }

    public partial struct Vector3
    {
        /// <summary>文档：Vector3.up — 世界 +Y 单位向量（货车安全区沿 Y 上抬用它）。</summary>
        public static Vector3 up => new Vector3(0f, 1f, 0f);
        /// <summary>文档：Vector3.magnitude / Vector3.sqrMagnitude。</summary>
        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
        public float sqrMagnitude => x * x + y * y + z * z;
        /// <summary>文档：Vector3.normalized — 单位化（零向量返回 zero）。</summary>
        public Vector3 normalized { get { float m = magnitude; return m > 1e-6f ? new Vector3(x / m, y / m, z / m) : zero; } }
        /// <summary>文档：Vector3.operator + / - / * / / 与一元 -（真 Unity 用近似比较，这里用标准实现）。</summary>
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);
    }

    public partial struct Color
    {
        /// <summary>文档：Color.Lerp — 逐通道线性插值（菜单地板/墙面色阶用它）。</summary>
        public static Color Lerp(Color a, Color b, float t) => new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
    }

    public partial class Object
    {
        /// <summary>文档：Object.FindObjectOfType — 按类型找场景对象（Unity 6 已过时，但仍在）。</summary>
        public static T FindObjectOfType<T>() where T : Object => default;
    }

    public partial class Transform
    {
        /// <summary>文档：Transform.parent — 父物体（门拾取沿父链上溯）。
        /// 出处除 ScriptReference/Transform-parent 外，另核过 UnityCsReference
        /// Runtime/Transform/ScriptBindings/Transform.bindings.cs（public Transform parent { get; set; }）。</summary>
        public Transform parent { get; set; }
        /// <summary>文档：Transform.forward — 世界空间 +Z 朝向（相机自检与鬼体朝向用它）。</summary>
        public Vector3 forward => default;
        /// <summary>文档：Transform.LookAt — 让 +Z 朝向目标点 / 目标物体。</summary>
        public void LookAt(Vector3 worldPosition) { }
        public void LookAt(Transform target) { }
        /// <summary>文档：Transform.GetChild(int) — 按下标取子物体（按钮矩形诊断用它）。</summary>
        public Transform GetChild(int index) => null;
        /// <summary>文档：Transform.Find(string) — 按名字/路径找子物体。</summary>
        public Transform Find(string n) => null;
        /// <summary>文档：Transform.TransformPoint / InverseTransformPoint — 局部↔世界坐标。</summary>
        public Vector3 TransformPoint(Vector3 position) => default;
        public Vector3 InverseTransformPoint(Vector3 position) => default;
        /// <summary>文档：Transform 实现 IEnumerable.GetEnumerator — 允许 foreach (Transform child in t)。</summary>
        public System.Collections.IEnumerator GetEnumerator() => null;
    }

    public partial class GameObject
    {
        /// <summary>文档：GameObject.activeInHierarchy — 自身与所有祖先都激活。</summary>
        public bool activeInHierarchy => false;
        /// <summary>文档：GameObject.GetComponentInChildren(bool includeInactive = false)。</summary>
        public T GetComponentInChildren<T>(bool includeInactive = false) => default;
    }

    public partial class Mesh
    {
        /// <summary>文档：Mesh.bounds — 局部空间 AABB（套件部件按尺寸判语义用它）。</summary>
        public Bounds bounds { get; set; }
        /// <summary>文档：Mesh.SetVertices / SetNormals（List 与数组两种重载都在真 Unity 里）。</summary>
        public void SetVertices(System.Collections.Generic.List<Vector3> inVertices) { }
        public void SetVertices(Vector3[] inVertices) { }
        public void SetNormals(System.Collections.Generic.List<Vector3> inNormals) { }
        public void SetNormals(Vector3[] inNormals) { }
        /// <summary>文档：Mesh.SetUVs(int channel, List&lt;Vector2&gt;|Vector2[])。</summary>
        public void SetUVs(int channel, System.Collections.Generic.List<Vector2> uvs) { }
        public void SetUVs(int channel, Vector2[] uvs) { }
        /// <summary>文档：Mesh.SetTriangles(List&lt;int&gt;|int[], int submesh)。</summary>
        public void SetTriangles(System.Collections.Generic.List<int> triangles, int submesh) { }
        public void SetTriangles(int[] triangles, int submesh) { }
        /// <summary>文档：Mesh.RecalculateNormals / RecalculateBounds。</summary>
        public void RecalculateNormals() { }
        public void RecalculateBounds() { }
    }

    public partial class Material
    {
        /// <summary>文档：Material.HasProperty(string) — 先问再设，避免给不存在的属性赋值。</summary>
        public bool HasProperty(string name) => false;
        /// <summary>文档：Material.SetColor / SetFloat / SetVector / SetTexture / EnableKeyword。</summary>
        public void SetColor(string name, Color value) { }
        public void SetFloat(string name, float value) { }
        public void SetVector(string name, Vector4 value) { }
        public void SetTexture(string name, Texture value) { }
        public void EnableKeyword(string keyword) { }
    }

    public partial class Camera
    {
        /// <summary>文档：Camera.targetTexture — 离屏渲染目标（取证截图改绑 RT）。</summary>
        public RenderTexture targetTexture { get; set; }
        /// <summary>文档：Camera.allowHDR — 辉光需要 &gt;1 的亮部余量。</summary>
        public bool allowHDR { get; set; }
        /// <summary>文档：Camera.depthTextureMode — SSAO/SSGI 需要 DepthNormals。</summary>
        public DepthTextureMode depthTextureMode { get; set; }
        /// <summary>文档：Camera.Render() — 手工渲染一帧。</summary>
        public void Render() { }
        /// <summary>文档：Camera.WorldToScreenPoint(Vector3) — 世界点 → 屏幕像素。</summary>
        public Vector3 WorldToScreenPoint(Vector3 position) => default;
        /// <summary>文档：Camera.ScreenPointToRay(Vector3 pos) — 屏幕点 → 世界射线。</summary>
        public Ray ScreenPointToRay(Vector3 pos) => default;
    }

    public partial class Canvas
    {
        /// <summary>文档：Canvas.scaleFactor — 屏幕像素 → 画布单位的除数（不除会飞出屏幕）。</summary>
        public float scaleFactor { get; set; }
    }

    public partial class RectTransform
    {
        /// <summary>文档：RectTransform.rect — 本地空间矩形（锚点原点，宽高已含缩放）。</summary>
        public Rect rect { get; set; }
        /// <summary>文档：RectTransform.GetWorldCorners(Vector3[] fourCornersArray) — 顺序 左下/左上/右上/右下。</summary>
        public void GetWorldCorners(Vector3[] fourCornersArray) { }
    }

    public static partial class Input
    {
        /// <summary>文档：Input.touches — 本帧全部触摸（Touch[]，可能为空数组）。</summary>
        public static Touch[] touches => null;
    }

    public static partial class Mathf
    {
        /// <summary>文档：Mathf.Max(int a, int b) / Mathf.Min(int a, int b) — 整型重载。
        /// 为什么必须补：Mathf.Max(1, src.width / 4) 在真 Unity 里走 int 重载；
        /// 只给 float 重载会返回 float，赋给 int 直接报 CS0266（那是**桩不完整**造成的假错误）。
        /// 出处：ScriptReference/Mathf.Max、ScriptReference/Mathf.Min 与 UnityCsReference
        /// Runtime/Export/Math/Mathf.cs（public static int Max(int a, int b) 等 4 条）。</summary>
        public static int Max(int a, int b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        /// <summary>文档：Mathf.Pow(float f, float p) — 幂（眼部适应曲线）。</summary>
        public static float Pow(float f, float p) => (float)Math.Pow(f, p);
        /// <summary>文档：Mathf.Log(float f) / Mathf.Log(float f, float p) — 自然对数 / 指定底对数。</summary>
        public static float Log(float f) => (float)Math.Log(f);
        public static float Log(float f, float p) => (float)Math.Log(f, p);
        /// <summary>文档：Mathf.Exp(float power) — e 的幂（摇杆平滑系数）。</summary>
        public static float Exp(float power) => (float)Math.Exp(power);
        /// <summary>文档：Mathf.SmoothStep(float from, float to, float t) — 平滑阶跃（贴图过渡）。</summary>
        public static float SmoothStep(float from, float to, float t)
        {
            float c = Clamp01((t - from) / (to - from));
            return c * c * (3f - 2f * c);
        }
        /// <summary>文档：Mathf.FloorToInt / Mathf.RoundToInt。</summary>
        public static int FloorToInt(float f) => (int)Math.Floor(f);
        public static int RoundToInt(float f) => (int)Math.Round(f);
        /// <summary>文档：Mathf.MoveTowards(float current, float target, float maxDelta) — 匀速逼近。</summary>
        public static float MoveTowards(float current, float target, float maxDelta)
        {
            float d = target - current;
            if (Math.Abs(d) <= maxDelta) return target;
            return current + (d > 0f ? maxDelta : -maxDelta);
        }
        /// <summary>文档：Mathf.DeltaAngle(float current, float target) — 两角之间的最短差（度）。</summary>
        public static float DeltaAngle(float current, float target)
        {
            float d = (target - current) % 360f;
            if (d > 180f) d -= 360f;
            if (d < -180f) d += 360f;
            return d;
        }
        /// <summary>文档：Mathf.PerlinNoise(float x, float y) — 柏林噪声（吊灯闪烁）。</summary>
        public static float PerlinNoise(float x, float y) => 0.5f;
    }

    public static partial class Application
    {
        /// <summary>文档：Application.dataPath — 工程的 Assets 目录绝对路径。</summary>
        public static string dataPath => "";
        /// <summary>文档：Application.streamingAssetsPath — StreamingAssets 绝对路径（套件 GLB 从这里读）。</summary>
        public static string streamingAssetsPath => "";
        /// <summary>文档：Application.isMobilePlatform — 移动端才弹系统键盘。</summary>
        public static bool isMobilePlatform => false;
    }

    public static partial class Time
    {
        /// <summary>文档：Time.time — 自开始以来的秒数（闪烁相位）。</summary>
        public static float time => 0f;
        /// <summary>文档：Time.frameCount — 已渲染帧数（触摸去重用它）。</summary>
        public static int frameCount => 0;
    }

    public static partial class Resources
    {
        /// <summary>文档：Resources.LoadAll(string path) — 一次性列出目录下全部资源（Object[]）。</summary>
        public static Object[] LoadAll(string path) => null;
    }

    // ────────────────────────── 新类型（UnityEngine）──────────────────────────

    /// <summary>文档：Vector4 — 四维向量（后处理模糊方向/偏移）。</summary>
    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Vector4 zero => new Vector4(0f, 0f, 0f, 0f);
        public static Vector4 one => new Vector4(1f, 1f, 1f, 1f);
        public static Vector4 operator *(Vector4 a, float d) => new Vector4(a.x * d, a.y * d, a.z * d, a.w * d);
        public static Vector4 operator +(Vector4 a, Vector4 b) => new Vector4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);
    }

    /// <summary>文档：Ray — 射线（Camera.ScreenPointToRay → Physics.Raycast）。</summary>
    public struct Ray
    {
        public Ray(Vector3 origin, Vector3 direction) { this.origin = origin; this.direction = direction; }
        public Vector3 origin { get; set; }
        public Vector3 direction { get; set; }
        public Vector3 GetPoint(float distance) => origin;
    }

    /// <summary>文档：Rect — 二维矩形（uGUI 命中框与纹理区域）。</summary>
    public struct Rect
    {
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public Rect(Vector2 position, Vector2 size) { x = position.x; y = position.y; width = size.x; height = size.y; }
        public float x { get; set; }
        public float y { get; set; }
        public float width { get; set; }
        public float height { get; set; }
        public Vector2 position { get { return new Vector2(x, y); } set { x = value.x; y = value.y; } }
        public Vector2 size { get { return new Vector2(width, height); } set { width = value.x; height = value.y; } }
        public Vector2 center { get { return new Vector2(x + width * 0.5f, y + height * 0.5f); } set { x = value.x - width * 0.5f; y = value.y - height * 0.5f; } }
        public Vector2 min { get { return new Vector2(x, y); } set { x = value.x; y = value.y; } }
        public Vector2 max { get { return new Vector2(x + width, y + height); } set { width = value.x - x; height = value.y - y; } }
        public float xMin { get { return x; } set { float m = xMax; x = value; width = m - x; } }
        public float yMin { get { return y; } set { float m = yMax; y = value; height = m - y; } }
        public float xMax { get { return x + width; } set { width = value - x; } }
        public float yMax { get { return y + height; } set { height = value - y; } }
        public static Rect zero => new Rect(0f, 0f, 0f, 0f);
        /// <summary>文档：Rect.Contains(Vector2 point) — 含边界。</summary>
        public bool Contains(Vector2 point) => point.x >= xMin && point.x < xMax && point.y >= yMin && point.y < yMax;
        public bool Contains(Vector3 point) => Contains(new Vector2(point.x, point.y));
        public bool Overlaps(Rect other) => other.xMax > xMin && other.xMin < xMax && other.yMax > yMin && other.yMin < yMax;
    }

    /// <summary>文档：Bounds — 轴对齐包围盒（Mesh.bounds / 货车安全区）。</summary>
    public struct Bounds
    {
        public Bounds(Vector3 center, Vector3 size) { this.center = center; this.size = size; }
        public Vector3 center { get; set; }
        public Vector3 size { get; set; }
        public Vector3 extents { get { return new Vector3(size.x * 0.5f, size.y * 0.5f, size.z * 0.5f); } set { size = new Vector3(value.x * 2f, value.y * 2f, value.z * 2f); } }
        public Vector3 min { get { return new Vector3(center.x - size.x * 0.5f, center.y - size.y * 0.5f, center.z - size.z * 0.5f); } set { SetMinMax(value, max); } }
        public Vector3 max { get { return new Vector3(center.x + size.x * 0.5f, center.y + size.y * 0.5f, center.z + size.z * 0.5f); } set { SetMinMax(min, value); } }
        /// <summary>文档：Bounds.Contains(Vector3) — 安全区判定（货车周围 2.5m）。</summary>
        public bool Contains(Vector3 point) => point.x >= min.x && point.x <= max.x && point.y >= min.y && point.y <= max.y && point.z >= min.z && point.z <= max.z;
        public void Encapsulate(Vector3 point) { }
        public void Encapsulate(Bounds bounds) { }
        public void Expand(float amount) { }
        public void Expand(Vector3 amount) { }
        public bool Intersects(Bounds bounds) => false;
        public void SetMinMax(Vector3 min, Vector3 max) { size = max - min; center = min + size * 0.5f; }
        public Vector3 ClosestPoint(Vector3 point) => point;
        public float SqrDistance(Vector3 point) => 0f;
    }

    /// <summary>文档：Collider — 所有碰撞体的基类（代码只取它再 Destroy）。</summary>
    public class Collider : Component
    {
        public Bounds bounds => default;
        public bool enabled { get; set; }
        public bool isTrigger { get; set; }
    }

    /// <summary>
    /// 文档：MeshCollider —— 网格碰撞体。
    ///
    /// 为什么需要（P0-2 大厅碰撞体）：货车套件此前**只有 MeshFilter + MeshRenderer**，
    /// 玩家自由行走时会直接穿过货车。而货车是"移动基地/安全指挥中心"，**玩家要能走进车厢** ——
    /// 包住整车的实心 BoxCollider 会把车厢内部填实（看着能进、走进去被弹开），比没有碰撞体更糟。
    /// **非凸网格**才能表达"有外壳、内有空腔"；非凸只在静态物体上允许，货车是停放的、无 Rigidbody，故可行。
    /// </summary>
    public class MeshCollider : Collider
    {
        public Mesh sharedMesh { get; set; }
        public bool convex { get; set; }
    }

    /// <summary>文档：RaycastHit — 射线命中信息（门交互拾取）。</summary>
    public struct RaycastHit
    {
        public Vector3 point { get; set; }
        public Vector3 normal { get; set; }
        public float distance { get; set; }
        public Collider collider => null;
        public Transform transform => null;
        public int triangleIndex => 0;
        public Vector2 textureCoord => default;
        public Vector2 barycentricCoordinate { get; set; }
    }

    /// <summary>文档：Physics — 全局物理查询（射线拾取）。</summary>
    public static class Physics
    {
        /// <summary>文档：Physics.Raycast(Ray ray, out RaycastHit hitInfo, float maxDistance)。</summary>
        public static bool Raycast(Ray ray, out RaycastHit hitInfo, float maxDistance) { hitInfo = default; return false; }
    }

    /// <summary>文档：Sprite — 2D 精灵（Image.sprite 的圆形轮盘贴图）。</summary>
    public class Sprite : Object
    {
        public Rect rect => default;
        public Texture2D texture => null;
        public Vector2 pivot => default;
        public float pixelsPerUnit => 100f;
        public Bounds bounds => default;
        /// <summary>文档：Sprite.Create(Texture2D, Rect, Vector2 pivot, float pixelsPerUnit = 100)。</summary>
        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot) => null;
        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit) => null;
    }

    /// <summary>文档：Texture — 纹理基类（filterMode / wrapMode 在这里）。</summary>
    public class Texture : Object
    {
        public int width { get; set; }
        public int height { get; set; }
        public FilterMode filterMode { get; set; }
        public TextureWrapMode wrapMode { get; set; }
        public int anisoLevel { get; set; }
    }

    /// <summary>文档：Texture2D — CPU 可读写纹理（程序化贴图与取证截图）。</summary>
    public class Texture2D : Texture
    {
        public Texture2D(int width, int height) { }
        /// <summary>文档：Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain)。</summary>
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain) { }
        public TextureFormat format => TextureFormat.RGBA32;
        /// <summary>文档：Texture2D.SetPixels32(Color32[] colors, int miplevel = 0) / GetPixels32(int miplevel = 0)。</summary>
        public void SetPixels32(Color32[] colors) { }
        public Color32[] GetPixels32() => null;
        /// <summary>文档：Texture2D.GetPixel(int x, int y, int mipLevel = 0) — 1x1 亮度读回用它。</summary>
        public Color GetPixel(int x, int y) => default;
        public void SetPixel(int x, int y, Color color) { }
        /// <summary>文档：Texture2D.Apply(bool updateMipmaps = true, bool makeNoLongerReadable = false)。</summary>
        public void Apply() { }
        public void Apply(bool updateMipmaps) { }
        /// <summary>文档：Texture2D.ReadPixels(Rect source, int destX, int destY, bool recalculateMipMaps = true)。</summary>
        public void ReadPixels(Rect source, int destX, int destY) { }
        /// <summary>文档：Texture2D.blackTexture — 1x1 黑（关辉光时绑定，避免残影）。</summary>
        public static Texture2D blackTexture => null;
        public static Texture2D whiteTexture => null;
    }

    /// <summary>文档：ImageConversion — 纹理编码扩展方法（EncodeToPNG 是它对 Texture2D 的扩展）。</summary>
    public static class ImageConversion
    {
        /// <summary>文档：ImageConversion.EncodeToPNG(this Texture2D tex)。</summary>
        public static byte[] EncodeToPNG(this Texture2D tex) => null;
    }

    /// <summary>文档：RenderTexture — 离屏渲染目标（后处理缓冲与取证截图）。</summary>
    public class RenderTexture : Texture
    {
        public RenderTexture(int width, int height, int depth) { }
        /// <summary>文档：RenderTexture(int width, int height, int depth, RenderTextureFormat format = Default, ...)。</summary>
        public RenderTexture(int width, int height, int depth, RenderTextureFormat format) { }
        public RenderTextureFormat format { get; set; }
        public int depth { get; set; }
        public int antiAliasing { get; set; }
        public bool IsCreated() => false;
        public bool Create() => false;
        public void Release() { }
        /// <summary>文档：RenderTexture.active — 当前绑定的 RT（ReadPixels 前要设它）。</summary>
        public static RenderTexture active { get; set; }
        /// <summary>文档：RenderTexture.GetTemporary(int width, int height, int depthBuffer = 0, RenderTextureFormat format = Default, ...)。</summary>
        public static RenderTexture GetTemporary(int width, int height) => null;
        public static RenderTexture GetTemporary(int width, int height, int depthBuffer) => null;
        public static RenderTexture GetTemporary(int width, int height, int depthBuffer, RenderTextureFormat format) => null;
        /// <summary>文档：RenderTexture.ReleaseTemporary(RenderTexture temp)。</summary>
        public static void ReleaseTemporary(RenderTexture temp) { }
    }

    /// <summary>文档：TextureFormat — 创建纹理时的像素格式（数值取自官方枚举）。</summary>
    public enum TextureFormat
    {
        Alpha8 = 1, ARGB4444 = 2, RGB24 = 3, RGBA32 = 4, ARGB32 = 5, RGB565 = 7,
        R16 = 9, DXT1 = 10, DXT5 = 12, RGBA4444 = 13, BGRA32 = 14, RHalf = 15,
        RGHalf = 16, RGBAHalf = 17, RFloat = 18, RGFloat = 19, RGBAFloat = 20,
    }

    /// <summary>文档：RenderTextureFormat — 渲染纹理格式（数值取自官方枚举）。</summary>
    public enum RenderTextureFormat
    {
        ARGB32 = 0, Depth = 1, ARGBHalf = 2, Shadowmap = 3, RGB565 = 4, ARGB4444 = 5,
        ARGB1555 = 6, Default = 7, ARGB2101010 = 8, DefaultHDR = 9, ARGB64 = 10,
        ARGBFloat = 11, RGFloat = 12, RGHalf = 13, RFloat = 14, RHalf = 15, R8 = 16,
    }

    /// <summary>文档：TextureWrapMode — 纹理超出 [0,1] 的采样方式。</summary>
    public enum TextureWrapMode { Repeat = 0, Clamp = 1, Mirror = 2, MirrorOnce = 3 }

    /// <summary>文档：FilterMode — 纹理过滤方式。</summary>
    public enum FilterMode { Point = 0, Bilinear = 1, Trilinear = 2 }

    /// <summary>文档：DepthTextureMode — 相机的深度纹理生成模式（可位组合）。</summary>
    [Flags]
    public enum DepthTextureMode { None = 0, Depth = 1, DepthNormals = 2, MotionVectors = 4 }

    /// <summary>文档：Graphics — 底层绘制入口（Graphics.Blit 后处理链）。</summary>
    public static class Graphics
    {
        /// <summary>文档：Graphics.Blit(Texture source, RenderTexture dest[, Material mat, int pass = -1])。</summary>
        public static void Blit(Texture source, RenderTexture dest) { }
        public static void Blit(Texture source, RenderTexture dest, Material mat) { }
        public static void Blit(Texture source, RenderTexture dest, Material mat, int pass) { }
        public static void Blit(Texture source, Material mat) { }
    }

    /// <summary>文档：QualitySettings — 全局画质设置（GameBootstrap 按档位施加）。</summary>
    public static class QualitySettings
    {
        public static int vSyncCount { get; set; }
        public static int pixelLightCount { get; set; }
        public static ShadowQuality shadows { get; set; }
        public static ShadowResolution shadowResolution { get; set; }
        public static float shadowDistance { get; set; }
        public static int antiAliasing { get; set; }
        public static AnisotropicFiltering anisotropicFiltering { get; set; }
    }

    /// <summary>文档：ShadowQuality — 阴影质量档。</summary>
    public enum ShadowQuality { Disable = 0, HardOnly = 1, All = 2 }

    /// <summary>文档：ShadowResolution — 阴影贴图分辨率档。</summary>
    public enum ShadowResolution { Low = 0, Medium = 1, High = 2, VeryHigh = 3 }

    /// <summary>文档：AnisotropicFiltering — 各向异性过滤档。</summary>
    public enum AnisotropicFiltering { Disable = 0, Enable = 1, ForceEnable = 2 }

    /// <summary>文档：RenderSettings — 全局环境光与雾（大厅半球天光）。</summary>
    public static class RenderSettings
    {
        public static Rendering.AmbientMode ambientMode { get; set; }
        public static Color ambientLight { get; set; }
        public static bool fog { get; set; }
    }

    /// <summary>文档：RectTransformUtility — RectTransform 与屏幕/世界坐标互转（菜单点击判定）。</summary>
    public static class RectTransformUtility
    {
        /// <summary>文档：RectTransformUtility.ScreenPointToLocalPointInRectangle(RectTransform rect, Vector2 screenPoint, Camera cam, out Vector2 localPoint)。</summary>
        public static bool ScreenPointToLocalPointInRectangle(RectTransform rect, Vector2 screenPoint, Camera cam, out Vector2 localPoint) { localPoint = default; return false; }
        public static bool RectangleContainsScreenPoint(RectTransform rect, Vector2 screenPoint, Camera cam) => false;
        public static Vector2 WorldToScreenPoint(Camera cam, Vector3 worldPoint) => default;
        public static bool ScreenPointToWorldPointInRectangle(RectTransform rect, Vector2 screenPoint, Camera cam, out Vector3 worldPoint) { worldPoint = default; return false; }
    }

    /// <summary>文档：TouchScreenKeyboard — 移动端系统键盘（加入房间时输入房间码）。</summary>
    public class TouchScreenKeyboard
    {
        /// <summary>文档：TouchScreenKeyboard.Open(string text, TouchScreenKeyboardType, bool autocorrection, bool multiline, bool secure, bool alert, string textPlaceholder, int characterLimit)。</summary>
        public static TouchScreenKeyboard Open(string text, TouchScreenKeyboardType keyboardType = TouchScreenKeyboardType.Default,
            bool autocorrection = true, bool multiline = false, bool secure = false, bool alert = false,
            string textPlaceholder = "", int characterLimit = 0) => null;
        public static bool isSupported => false;
        public bool active { get; set; }
        public string text { get; set; }
        public bool done => true;
        public bool wasCanceled => false;
        public int characterLimit { get; set; }
    }

    /// <summary>文档：TouchScreenKeyboardType — 系统键盘类型（房间码用 ASCIICapable）。</summary>
    public enum TouchScreenKeyboardType
    {
        Default = 0, ASCIICapable = 1, NumbersAndPunctuation = 2, URL = 3, NumberPad = 4,
        PhonePad = 5, NamePhonePad = 6, EmailAddress = 7, NintendoNetworkAccount = 8,
        Social = 9, Search = 10, DecimalPad = 11, OneTimeCode = 12,
    }

    /// <summary>文档：RequireComponent — 组件依赖特性（PostFx 要求同物体上有 Camera）。</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class RequireComponent : Attribute
    {
        public RequireComponent(Type requiredComponent) { }
        public RequireComponent(Type requiredComponent, Type requiredComponent2) { }
        public RequireComponent(Type requiredComponent, Type requiredComponent2, Type requiredComponent3) { }
    }
}

namespace UnityEngine.UI
{
    public partial class Image
    {
        /// <summary>文档：Image.sprite — 不给 sprite 时画的是默认白方块（移动轮盘必须给圆形贴图）。</summary>
        public Sprite sprite { get; set; }
        /// <summary>文档：UI.Graphic.raycastTarget — 是否参与 uGUI 射线（HUD/轮盘要关掉，避免抢触摸）。</summary>
        public bool raycastTarget { get; set; }
    }

    public partial class Button
    {
        /// <summary>文档：UI.Selectable.targetGraphic — 状态过渡（换色/换图）作用的 Graphic。</summary>
        public Graphic targetGraphic { get; set; }
    }
}

namespace UnityEngine.Rendering
{
    /// <summary>文档：Rendering.AmbientMode — 环境光模式（大厅用 Flat + 单色天光）。</summary>
    public enum AmbientMode { Skybox = 0, Trilight = 1, Flat = 3, Custom = 4 }
}

namespace UnityEngine.EventSystems
{
    /// <summary>文档：EventSystems.EventSystem — uGUI 事件系统（无它按钮 onClick 永不触发）。</summary>
    public class EventSystem : MonoBehaviour
    {
        public static EventSystem current { get; set; }
        public void SetSelectedGameObject(GameObject selected) { }
    }

    /// <summary>文档：EventSystems.BaseInputModule — 输入模块基类。</summary>
    public class BaseInputModule : MonoBehaviour { }

    /// <summary>文档：EventSystems.PointerInputModule — 指针输入模块。</summary>
    public class PointerInputModule : BaseInputModule { }

    /// <summary>文档：EventSystems.StandaloneInputModule — 旧输入系统的独立输入模块（本工程 activeInputHandler=0）。</summary>
    public class StandaloneInputModule : PointerInputModule { }
}

namespace UnityEditor
{
    /// <summary>文档：EditorApplication — 编辑器应用级操作（取证脚本跑完就 Exit）。</summary>
    public static class EditorApplication
    {
        /// <summary>文档：EditorApplication.Exit(int returnValue)。</summary>
        public static void Exit(int returnValue) { }
        public static bool isPlaying { get; set; }
        public static bool isPlayingOrWillChangePlaymode => false;
        public static bool isCompiling => false;
    }

    /// <summary>文档：AssetDatabase — 资产数据库（取证脚本改文件后要 Refresh 才生效）。</summary>
    public static class AssetDatabase
    {
        /// <summary>文档：AssetDatabase.Refresh(ImportAssetOptions options = ImportAssetOptions.Default)。</summary>
        public static void Refresh() { }
        public static void Refresh(ImportAssetOptions options) { }
        public static void SaveAssets() { }
        public static void ImportAsset(string path) { }
        public static void ImportAsset(string path, ImportAssetOptions options) { }
    }

    /// <summary>文档：ImportAssetOptions — 资产导入选项（取证脚本用 ForceSynchronousImport 保证同步）。</summary>
    [Flags]
    public enum ImportAssetOptions
    {
        Default = 0, ForceUpdate = 1, ForceSynchronousImport = 8, ImportRecursive = 256,
        DontDownloadFromCacheServer = 8192, ForceUncompressedImport = 16384,
    }
}

namespace UnityEditor.Build
{
    /// <summary>文档：IOrderedCallback — 回调顺序接口（IPostprocessBuild 继承它）。</summary>
    public interface IOrderedCallback { int callbackOrder { get; } }

    /// <summary>文档：IPostprocessBuild — 构建后处理接口（Unity 6 已建议改用 IPostprocessBuildWithReport，本接口仍可用）。</summary>
    public interface IPostprocessBuild : IOrderedCallback
    {
        void OnPostprocessBuild(BuildTarget target, string path);
    }

    /// <summary>文档：BuildFailedException — 构建后处理失败时抛出，让整次构建失败。</summary>
    public class BuildFailedException : Exception
    {
        public BuildFailedException(string message) : base(message) { }
        public BuildFailedException(string message, Exception innerException) : base(message, innerException) { }
    }
}
${MARK_END}`;

// ── 4. 拼装 ──────────────────────────────────────────────────────────────────
const extLines = EXTENSION.split('\n');
const out = lines.concat([''], [MARK_BEGIN], extLines);
if (hadTrailingNewline) out.push('');
const text = out.join(eol);

// ── 5. 自检（生成区内容与结构）──────────────────────────────────────────────
{
  const count = (rx) => out.filter((l) => rx.test(l)).length;
  const has1 = (rx) => {
    const n = count(rx);
    if (n !== 1) fail(`自检：${rx} 命中 ${n} 次（应为 1）`);
  };
  // partial 类型**应该**出现 2 次声明行：原始桩里那次（已改写）+ 生成区里那次 —— 这正是 partial 的用法。
  const has2 = (rx) => {
    const n = count(rx);
    if (n !== 2) fail(`自检：${rx} 命中 ${n} 次（应为 2 = 原始 + 生成区）`);
  };
  for (const t of PARTIAL_TYPES) has2(new RegExp(`^\\s*public\\s+(?:static\\s+)?partial\\s+(?:class|struct)\\s+${t}(?=\\s*(?::|\\{|,|$))`));
  for (const t of ['Image', 'Button']) has2(new RegExp(`^\\s*public\\s+partial\\s+(?:class|struct)\\s+${t}(?=\\s*(?::|\\{|,|$))`));
  has1(/^\s*public\s+partial\s+class\s+Transform\s*:\s*Component,\s*System\.Collections\.IEnumerable\s*$/);
  has1(/Alpha1 = 49/);
  has1(new RegExp(MARK_BEGIN.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
  has1(new RegExp(MARK_END.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
  // 新增类型各恰好一个声明（防 CS0101 重复定义）
  for (const t of ['Vector4', 'Ray', 'Rect', 'Bounds', 'Collider', 'MeshCollider', 'RaycastHit', 'Physics', 'Sprite',
    'Texture', 'Texture2D', 'ImageConversion', 'RenderTexture', 'Graphics', 'QualitySettings',
    'RenderSettings', 'RectTransformUtility', 'TouchScreenKeyboard', 'RequireComponent',
    'EditorApplication', 'AssetDatabase', 'EventSystem', 'StandaloneInputModule',
    'IPostprocessBuild', 'BuildFailedException']) {
    has1(new RegExp(`^\\s*public\\s+(?:static\\s+|sealed\\s+|abstract\\s+)*(?:class|struct|interface|enum)\\s+${t}(?=\\s*(?::|\\{|,|$))`));
  }
  // 关键成员探针（防止模板被误删）
  for (const probe of [
    /public static Vector2 Lerp\(Vector2 a, Vector2 b, float t\)/,
    /public static Vector3 up =>/,
    /public static Vector3 operator -\(Vector3 a\)/,
    /public static Color Lerp\(Color a, Color b, float t\)/,
    /public static T FindObjectOfType<T>\(\) where T : Object/,
    /public System\.Collections\.IEnumerator GetEnumerator\(\)/,
    /public Bounds bounds \{ get; set; \}/,
    /public bool HasProperty\(string name\)/,
    /public RenderTexture targetTexture \{ get; set; \}/,
    /public bool allowHDR \{ get; set; \}/,
    /public DepthTextureMode depthTextureMode \{ get; set; \}/,
    /public Ray ScreenPointToRay\(Vector3 pos\)/,
    /public float scaleFactor \{ get; set; \}/,
    /public Rect rect \{ get; set; \}/,
    /public static Touch\[\] touches =>/,
    /public static float MoveTowards\(float current, float target, float maxDelta\)/,
    /public static int Max\(int a, int b\) =>/,
    /^\s*public Transform parent \{ get; set; \}/,
    /public static float DeltaAngle\(float current, float target\)/,
    /public static float PerlinNoise\(float x, float y\)/,
    /public static string streamingAssetsPath =>/,
    /public static int frameCount =>/,
    /public static Object\[\] LoadAll\(string path\)/,
    /public bool Contains\(Vector2 point\)/,
    /public static bool Raycast\(Ray ray, out RaycastHit hitInfo, float maxDistance\)/,
    /public static Sprite Create\(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit\)/,
    /public void SetPixels32\(Color32\[\] colors\)/,
    /public static byte\[\] EncodeToPNG\(this Texture2D tex\)/,
    /public static RenderTexture GetTemporary\(int width, int height, int depthBuffer, RenderTextureFormat format\)/,
    /public static void Blit\(Texture source, RenderTexture dest, Material mat, int pass\)/,
    /public static bool ScreenPointToLocalPointInRectangle\(RectTransform rect, Vector2 screenPoint, Camera cam, out Vector2 localPoint\)/,
    /public static TouchScreenKeyboard Open\(string text, TouchScreenKeyboardType keyboardType = TouchScreenKeyboardType\.Default,/,
    /void OnPostprocessBuild\(BuildTarget target, string path\);/,
  ]) {
    const n = out.filter((l) => probe.test(l)).length;
    if (n !== 1) fail(`自检：成员探针 ${probe} 命中 ${n} 次（应为 1）`);
  }
  log.push(`  ✓ 自检通过（${PARTIAL_TYPES.length + 2} 个 partial · KeyCode · ${extLines.length} 行生成区 · 关键成员探针全中）`);
}

// ── 6. 写出 / 校验 ───────────────────────────────────────────────────────────
console.log('[stubs+] 补齐 UnityStubs.cs 缺失 API 面' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);

if (checkOnly) {
  if (text !== raw) {
    console.error('[stubs+] ✗ 文件与生成模板不一致（跑 `node tools/add-missing-unity-stubs.mjs` 重建）');
    process.exit(1);
  }
  console.log('  ✓ --check：文件已是生成结果（幂等）');
} else {
  if (text === raw) console.log('  · 无需改动（已是生成结果）');
  else { fs.writeFileSync(FILE, text, 'utf8'); console.log(`  已写回：${raw.length} → ${text.length} 字节`); }
}
