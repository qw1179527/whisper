// UnityEngine 最小桩（仅供 native/unity-syntax 的语义检查使用，**不参与任何构建**）
//
// 为什么需要它：没有 UnityEngine 时，Unity 类型无法解析 → 表达式被标为错误类型 →
// Roslyn 会**跳过其内部的成员检查**，于是项目自身的笔误（如 DesignTokens.ColorConcrete）
// 根本不报错，检查器变成假绿（实测踩过）。给出最小可解析的 API 面后，编译器才能完成语义分析，
// 从而真正拦住项目代码中的错误。
//
// 纪律：这里只放**本项目实际用到**的成员；缺什么就补什么，不要凭空扩面。
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public partial struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0, 0);
        public static Vector2 one => new Vector2(1, 1);
        public float magnitude => (float)Math.Sqrt(x * x + y * y);
        public float sqrMagnitude => x * x + y * y;
        public Vector2 normalized => magnitude > 1e-6f ? new Vector2(x / magnitude, y / magnitude) : zero;
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, float k) => new Vector2(a.x * k, a.y * k);
        public static Vector2 operator *(float k, Vector2 a) => new Vector2(a.x * k, a.y * k);
        public static implicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);
    }

    public partial struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 one => new Vector3(1, 1, 1);
    }

    public partial struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1);
        public static Color black => new Color(0, 0, 0);
        public static Color gray => new Color(.5f, .5f, .5f);
        public static Color operator *(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color(Color32 c) => new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
    }

    public struct Quaternion
    {
        public static Quaternion Euler(float x, float y, float z) => default;
        public static Quaternion identity => default;
        /// <summary>朝向由方向向量决定（文档：Quaternion.LookRotation）。</summary>
        public static Quaternion LookRotation(Vector3 forward) => default;
        public static Quaternion LookRotation(Vector3 forward, Vector3 upwards) => default;
        /// <summary>文档：Quaternion.eulerAngles — 返回以度为单位的欧拉角（0~360 折算）。</summary>
        public Vector3 eulerAngles => default;
    }

    public partial class Object
    {
        public string name { get; set; }
        public static void Destroy(Object o) { }
        public static void DestroyImmediate(Object o) { }
    }

    public class Component : Object
    {
        public Transform transform => null;
        public GameObject gameObject => null;
        public T GetComponent<T>() => default;
        public T GetComponentInParent<T>() => default;
        public T GetComponentInChildren<T>() => default;
    }

    public class Behaviour : Component { public bool enabled { get; set; } }
    public class MonoBehaviour : Behaviour { }

    public partial class Transform : Component, System.Collections.IEnumerable
    {
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Vector3 localScale { get; set; }
        public Quaternion rotation { get; set; }
        /// <summary>文档：Transform.localRotation — 相对父物体的旋转。</summary>
        public Quaternion localRotation { get; set; }
        public void SetParent(Transform parent, bool worldPositionStays) { }
        /// <summary>文档：Transform.childCount — 子物体数量。</summary>
        public int childCount => 0;
    }

    public partial class GameObject : Object
    {
        public GameObject() { }
        public GameObject(string name) { }
        public GameObject(string name, params Type[] components) { }
        public Transform transform => null;
        public T AddComponent<T>() where T : Component => default;
        public T GetComponent<T>() => default;
        public void SetActive(bool value) { }
        public bool activeSelf => false;
        public string tag { get; set; }
        public static GameObject CreatePrimitive(PrimitiveType type) => null;
    }

    public enum PrimitiveType { Sphere, Capsule, Cylinder, Cube, Plane, Quad }

    public partial class Mesh : Object { }
    public partial class Material : Object
    {
        public Material(Shader shader) { }
        public Color color { get; set; }
    }
    public class Shader : Object
    {
        public static Shader Find(string name) => null;
        /// <summary>文档：Shader.SetGlobalFloat — 设置全局 shader 常量（无需材质实例）。</summary>
        public static void SetGlobalFloat(string name, float value) { }
        /// <summary>文档：Shader.SetGlobalColor — 设置全局颜色常量。</summary>
        public static void SetGlobalColor(string name, Color value) { }
    }
    public class MeshFilter : Component { public Mesh sharedMesh { get; set; } }
    public class Renderer : Component
    {
        public Material sharedMaterial { get; set; }
        /// <summary>文档：Renderer.enabled — 关掉即不渲染（MeshRenderer 继承此属性）。</summary>
        public bool enabled { get; set; }
        /// <summary>
        /// 文档：Renderer.bounds — **世界空间**包围盒（含所有子网格）。
        /// 取证诊断用它量"几何到底落在哪"，这是把"相机是不是在几何内部"从推断变成测量的一步。
        /// </summary>
        public Bounds bounds { get; set; }
    }
    public class MeshRenderer : Renderer { }
    public class TextAsset : Object
    {
        public string text => null;
        /// <summary>文档：TextAsset.bytes — 原始字节（`.bytes` 后缀资源读 GLB 用这条）。</summary>
        public byte[] bytes => null;
    }

    public static partial class Resources
    {
        public static T Load<T>(string path) where T : Object => default;
        // 真实签名无 `where T : Object` 约束（内置资源含 Font/Material/Texture 等）
        public static T GetBuiltinResource<T>(string path) where T : Object => default;
    }

    /// <summary>内置字体（Resources.GetBuiltinResource&lt;Font&gt;("LegacyRuntime.ttf")）。</summary>
    public class Font : Object
    {
        public static Font CreateDynamicFontFromOSFont(string fontname, int size) => null;
    }

    public enum CameraClearFlags { Skybox = 1, Color = 2, SolidColor = 2, Depth = 3, Nothing = 4 }

    public partial class Camera : Behaviour
    {
        public CameraClearFlags clearFlags { get; set; }
        public Color backgroundColor { get; set; }
        public float fieldOfView { get; set; }
        public float nearClipPlane { get; set; }
        public float farClipPlane { get; set; }
        public static Camera main => null;
    }

    public enum LightType { Spot, Directional, Point, Area }

    /// <summary>触屏（真实类型 UnityEngine.Touch）。</summary>
    public struct Touch
    {
        public int fingerId { get; set; }
        public Vector2 position { get; set; }
        public Vector2 deltaPosition { get; set; }
        public TouchPhase phase { get; set; }
    }

    public enum TouchPhase { Began, Moved, Stationary, Ended, Canceled }

    /// <summary>
    /// 旧输入系统（PlayerController 用它做动态摇杆）。
    /// 注：项目当前 PlayerSettings 未显式配置 Active Input Handling，走默认的旧输入系统；
    /// 若将来切到新 Input System，本类需换成 InputSystem API（届时会有编译错误明确提示，不会静默失效）。
    /// </summary>
    public static partial class Input
    {
        public static int touchCount => 0;
        public static Touch GetTouch(int index) => default;
        public static bool GetKeyDown(KeyCode k) => false;
        public static bool GetMouseButtonDown(int b) => false;
        /// <summary>文档：Input.mousePosition — 像素坐标 Vector3（z 未用）。菜单点击判定要用。</summary>
        public static Vector3 mousePosition => default;
    }

    public enum KeyCode { None = 0, Escape = 27, Space = 32, Alpha1 = 49, E = 101, H = 104, J = 106 }

    /// <summary>屏幕尺寸（像素）。</summary>
    public static class Screen
    {
        /// <summary>文档：Screen.dpi — 屏幕 DPI（点击回执里记录，用于判断坐标是否被缩放）。</summary>
        public static float dpi => 160f;
        public static int width => 1920;
        public static int height => 1080;
    }

    public class Light : Behaviour
    {
        public LightType type { get; set; }
        public float intensity { get; set; }
        public Color color { get; set; }
        /// <summary>文档：Light.range — 点光/聚光的衰减半径。</summary>
        public float range { get; set; }
        /// <summary>文档：Light.spotAngle — 聚光锥角（度）。</summary>
        public float spotAngle { get; set; }
        /// <summary>文档：Light.shadows — 阴影模式（None/Hard/Soft）。</summary>
        public LightShadows shadows { get; set; }
        /// <summary>文档：Light.renderMode — Auto/ForcePixel/ForceVertex。</summary>
        public LightRenderMode renderMode { get; set; }
    }

    /// <summary>文档：LightShadows 枚举。</summary>
    public enum LightShadows { None = 0, Hard = 1, Soft = 2 }

    /// <summary>文档：LightRenderMode 枚举（Auto 在灯多时会被降级成顶点光）。</summary>
    public enum LightRenderMode { Auto = 0, ForcePixel = 1, ForceVertex = 2 }

    public static partial class Application
    {
        public static int targetFrameRate { get; set; }
        public static string unityVersion => "stub";
        /// <summary>文档：Application.runInBackground — 失焦时是否继续运行。</summary>
        public static bool runInBackground { get; set; }
    }

    public static partial class Time
    {
        public static float unscaledTime => 0f;
        public static float unscaledDeltaTime => 0f;
        public static float deltaTime => 0f;
    }

    public static class Debug
    {
        public static void Log(object msg) { }
        public static void LogError(object msg) { }
        public static void LogWarning(object msg) { }
    }

    public static partial class Mathf
    {
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Abs(float a) => a < 0 ? -a : a;
        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
        public static float Floor(float v) => (float)Math.Floor(v);
        public static float Sqrt(float v) => (float)Math.Sqrt(v);
        /// <summary>文档：Mathf.Sin(float f) — 参数为弧度，返回 [-1,1]。</summary>
        public static float Sin(float f) => (float)Math.Sin(f);
        /// <summary>文档：Mathf.Cos(float f) — 参数为弧度，返回 [-1,1]。</summary>
        public static float Cos(float f) => (float)Math.Cos(f);
        /// <summary>文档：Mathf.Atan2(float y, float x) — 返回弧度，常配 Rad2Deg。</summary>
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        /// <summary>文档：Mathf.Rad2Deg — 弧度转度的乘数常量。</summary>
        public const float Rad2Deg = 57.29578f;
        /// <summary>文档：Mathf.Deg2Rad — 度转弧度的乘数常量。</summary>
        public const float Deg2Rad = 0.017453292f;
        /// <summary>文档：Mathf.Clamp01(float) — 夹到 [0,1]。</summary>
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        /// <summary>文档：Mathf.Lerp(float a, float b, float t) — 未夹紧 t。</summary>
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }

    public partial class Canvas : Behaviour { public RenderMode renderMode { get; set; } }
    public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }

    public class CanvasScaler : Behaviour
    {
        public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize, ConstantPhysicalSize }
        public ScaleMode uiScaleMode { get; set; }
        public Vector2 referenceResolution { get; set; }
    }

    public class GraphicRaycaster : Behaviour { }

    public partial class RectTransform : Transform
    {
        public Vector2 anchorMin { get; set; }
        public Vector2 anchorMax { get; set; }
        public Vector2 offsetMin { get; set; }
        public Vector2 offsetMax { get; set; }
        public Vector2 pivot { get; set; }
        public Vector2 anchoredPosition { get; set; }
        public Vector2 sizeDelta { get; set; }
    }

    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }

    public class TooltipAttribute : Attribute { public TooltipAttribute(string text) { } }
    public class DisallowMultipleComponentAttribute : Attribute { }
    public class SerializeFieldAttribute : Attribute { }
}

namespace UnityEngine.UI
{
    // Canvas 是 Graphic 的祖先属性（CanvasRenderer → Graphic.canvas），按钮要挂到 HUD 的 Canvas 上
    public class Graphic : Behaviour
    {
        public Color color { get; set; }
        public RectTransform rectTransform => null;
        public Canvas canvas => null;
    }

    public enum HorizontalWrapMode { Wrap, Overflow }
    public enum VerticalWrapMode { Truncate, Overflow }

    public class Text : Graphic
    {
        public string text { get; set; }
        public int fontSize { get; set; }
        public TextAnchor alignment { get; set; }
        public Font font { get; set; }
        public HorizontalWrapMode horizontalOverflow { get; set; }
        public VerticalWrapMode verticalOverflow { get; set; }
        public bool raycastTarget { get; set; }
    }

    public partial class Image : Graphic { }

    /// <summary>按钮点击事件（真实类型为 Button.ButtonClickedEvent : UnityEvent）。</summary>
    public class ButtonClickedEvent { public void AddListener(UnityEngine.Events.UnityAction call) { } }

    public partial class Button : Behaviour
    {
        public ButtonClickedEvent onClick => null;
    }
}

namespace UnityEngine.Events
{
    public delegate void UnityAction();
}

// ── UnityEditor / SceneManagement 最小桩 ──
//
// 为什么补这一块：unity-syntax-check.sh 此前只扫 `unity/Assets/Scripts`，
// **整个 `unity/Assets/Editor/` 从未被本机检查过**。Editor 代码写错只能等 CI 构建
// （一次 ~47 分钟）才发现——正是本项目最想避免的"远程才发现"。
// 现在扫描范围扩到 Assets/Editor，这里补上它用到的 API 面。
//
// ⚠️⚠️ 这个桩有一个**结构性缺陷**，务必记住（CI #18 真实事故）：
//   桩是**我自己写的**。我编造一个不存在的 API（当时写了 `UnityEditor.SplashScreen`，
//   真 Unity 里其实是 `PlayerSettings.SplashScreen`），桩就替这个错误背书，
//   于是本机门禁全绿、CI 报 CS0103，白烧一次构建。
//   → 桩能验证的只有**内部一致性**（调用点与签名自洽），
//     它**永远无法验证"Unity 真的有这个成员"**。
//   → 因此这里的每个成员都必须有官方文档出处；出处见每个成员上方的注释。
//     没有出处的成员要么删掉，要么在注释里显式标 `⚠ 未核实`。
//   → tools/gate-api-trace.mjs 会检查 `⚠ 未核实` 的残留数量（只警告不判红），
//     用来防止"编造 API"这类错误悄悄堆积。
namespace UnityEngine.SceneManagement
{
    public struct Scene { public string name => null; public bool IsValid() => true; }
}

namespace UnityEditor
{
    /// <summary>目标平台分组（PlayerSettings 的按平台重载用）。</summary>
    public enum BuildTargetGroup { Unknown = 0, Standalone = 1, Android = 7, iOS = 4 }

    /// <summary>Unity 6 的按平台目标（取代 BuildTargetGroup 的新式重载）。</summary>
    public struct NamedBuildTarget
    {
        public static NamedBuildTarget Android => default;
        public static NamedBuildTarget Standalone => default;
    }

    public enum ScriptingImplementation { Mono2x = 0, IL2CPP = 1, WinRTDotNET = 2, CoreCLR = 3 }
    public enum ManagedStrippingLevel { Disabled = 0, Low = 1, Medium = 2, High = 3, Minimal = 4 }
    public enum AndroidArchitecture { None = 0, ARMv7 = 1, ARM64 = 2, X86 = 4, X86_64 = 8, All = unchecked((int)0xFFFFFFFF) }
    public enum AndroidSdkVersions
    {
        AndroidApiLevelAuto = 0, AndroidApiLevel23 = 23, AndroidApiLevel24 = 24, AndroidApiLevel25 = 25,
        AndroidApiLevel26 = 26, AndroidApiLevel27 = 27, AndroidApiLevel28 = 28, AndroidApiLevel29 = 29,
        AndroidApiLevel30 = 30, AndroidApiLevel31 = 31, AndroidApiLevel32 = 32, AndroidApiLevel33 = 33,
        AndroidApiLevel34 = 34, AndroidApiLevel35 = 35, AndroidApiLevel36 = 36,
    }
    public enum UIOrientation { Portrait = 0, PortraitUpsideDown = 1, LandscapeRight = 2, LandscapeLeft = 3, AutoRotation = 4 }
    public enum BuildTarget { NoTarget = -2, StandaloneWindows = 5, Android = 13, iOS = 9 }
    public enum BuildOptions { None = 0, Development = 1, AutoRunPlayer = 4 }
    public enum BuildResult { Unknown = 0, Succeeded = 1, Failed = 2, Cancelled = 3 }

    public static class PlayerSettings
    {
        public static string companyName { get; set; }
        public static string productName { get; set; }
        public static string bundleVersion { get; set; }
        public static UIOrientation defaultInterfaceOrientation { get; set; }
        public static bool allowedAutorotateToPortrait { get; set; }
        public static bool allowedAutorotateToPortraitUpsideDown { get; set; }
        public static bool allowedAutorotateToLandscapeLeft { get; set; }
        public static bool allowedAutorotateToLandscapeRight { get; set; }
        public static bool useAnimatedAutorotation { get; set; }

        public static void SetApplicationIdentifier(NamedBuildTarget target, string identifier) { }
        public static void SetApplicationIdentifier(BuildTargetGroup targetGroup, string identifier) { }
        public static string GetApplicationIdentifier(NamedBuildTarget target) => "";
        public static void SetScriptingBackend(NamedBuildTarget target, ScriptingImplementation impl) { }
        public static ScriptingImplementation GetScriptingBackend(NamedBuildTarget target) => default;
        public static void SetManagedStrippingLevel(NamedBuildTarget target, ManagedStrippingLevel level) { }
        public static ManagedStrippingLevel GetManagedStrippingLevel(NamedBuildTarget target) => default;

        public static class Android
        {
            public static AndroidArchitecture targetArchitectures { get; set; }
            public static int bundleVersionCode { get; set; }
            public static AndroidSdkVersions minSdkVersion { get; set; }
            public static AndroidSdkVersions targetSdkVersion { get; set; }
        }
    }

    /// <summary>
    /// 启动画面开关**不再在这里声明**（CI #18 事故）：
    /// 我曾在这里写 `public static class SplashScreen`，而真 Unity 里它是
    /// `PlayerSettings.SplashScreen`（嵌套类型），`UnityEditor.SplashScreen` **并不存在**。
    /// 桩替我的臆造背书 → 本机绿灯、CI CS0103 失败。
    /// 现在 BuildConfigurator 改用**反射**动态找 `show` 属性，不依赖具体类型名，
    /// 所以这里也就不该再放一个假类型（放了就会再次掩盖同类错误）。
    /// </summary>

    public class MenuItemAttribute : Attribute { public MenuItemAttribute(string itemName) { } }

    public class EditorBuildSettingsScene
    {
        public EditorBuildSettingsScene(string path, bool enabled) { }
    }

    public static class EditorBuildSettings
    {
        public static EditorBuildSettingsScene[] scenes { get; set; }
    }
}

namespace UnityEditor.SceneManagement
{
    public enum NewSceneSetup { EmptyScene = 0, DefaultGameObjects = 1 }
    public enum NewSceneMode { Single = 0, Additive = 1 }

    public static class EditorSceneManager
    {
        public static UnityEngine.SceneManagement.Scene NewScene(NewSceneSetup setup, NewSceneMode mode) => default;
        public static bool SaveScene(UnityEngine.SceneManagement.Scene scene, string dstScenePath) => true;
    }
}

namespace UnityEditor.Build.Reporting
{
    public class BuildSummary
    {
        public UnityEditor.BuildResult result { get; set; }
        public int totalErrors { get; set; }
    }

    public class BuildReport { public BuildSummary summary => null; }
}

namespace UnityEditor
{
    public struct BuildPlayerOptions
    {
        public string[] scenes { get; set; }
        public string locationPathName { get; set; }
        public BuildTarget target { get; set; }
        public BuildOptions options { get; set; }
    }

    public static class BuildPipeline
    {
        public static UnityEditor.Build.Reporting.BuildReport BuildPlayer(BuildPlayerOptions opts) => null;
    }
}

// @@UNITY-STUBS-EXTENSION-BEGIN@@
// ══════════════════════════════════════════════════════════════════════════════
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
        /// <summary>文档：Vector3.Min / Vector3.Max — 逐分量取小/大（取证量场景包围盒用它）。</summary>
        public static Vector3 Min(Vector3 a, Vector3 b) => new Vector3(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Min(a.z, b.z));
        public static Vector3 Max(Vector3 a, Vector3 b) => new Vector3(Math.Max(a.x, b.x), Math.Max(a.y, b.y), Math.Max(a.z, b.z));
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

    /// <summary>
    /// 文档：ScriptableObject — 可序列化资产基类（`UnityEngine.Object` 的子类）。
    /// 【为什么补】`AssetDatabase.LoadAssetAtPath&lt;T&gt; where T : Object` 要求 URP 资产
    /// （URP Asset / RendererData / VolumeProfile）**是 Object 的子类** —— 没有这个桩，
    /// 本机语法门禁会报 CS0311（泛型约束不满足），而那**不是**产品的错，是桩不全。
    /// 补上之后门禁才真的在校验"我写的 URP API 名对不对"。
    /// </summary>
    public partial class ScriptableObject : Object
    {
        /// <summary>文档：ScriptableObject.CreateInstance&lt;T&gt;() — 按类型创建资产实例。</summary>
        public static T CreateInstance<T>() where T : ScriptableObject => default;
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
        /// <summary>文档：GameObject.GetComponentsInChildren&lt;T&gt;(bool includeInactive) — 取证遍历场景光源用。</summary>
        public T[] GetComponentsInChildren<T>(bool includeInactive) => new T[0];
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
        /// <summary>文档：Camera.orthographic — 是否正交投影（诊断要用它判断取景方式）。</summary>
        public bool orthographic { get; set; }
        /// <summary>文档：Camera.cullingMask — 逐层遮罩（诊断要确认没有把几何层裁掉）。</summary>
        public int cullingMask { get; set; }
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
        /// <summary>文档：Input.touchSupported — 设备是否支持触摸（点击回执用它区分"没有触摸硬件"与"触摸没到"）。</summary>
        public static bool touchSupported => true;
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
        /// <summary>文档：Application.isBatchMode — 是否 -batchmode 运行（取证判红时据此决定能否退出）。</summary>
        public static bool isBatchMode => false;
        /// <summary>文档：Application.persistentDataPath — 跨更新保留的可写目录（点击回执落盘在这里）。</summary>
        public static string persistentDataPath => "/tmp";
    }

    public static partial class Time
    {
        /// <summary>文档：Time.time — 自开始以来的秒数（闪烁相位）。</summary>
        public static float time => 0f;
        /// <summary>文档：Time.frameCount — 已渲染帧数（触摸去重用它）。</summary>
        public static int frameCount => 0;
        /// <summary>文档：Time.realtimeSinceStartup — 真实经过秒数（不受 timeScale 影响；点击回执用它记时）。</summary>
        public static float realtimeSinceStartup => 0f;
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
        /// <summary>文档：Texture2D.LoadImage(byte[]) — 从 PNG/JPG 字节读回纹理（像素比对用）。</summary>
        public bool LoadImage(byte[] data) => true;
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
        /// <summary>
        /// 文档：QualitySettings.renderPipeline — 按质量档覆盖渲染管线。
        /// 【为什么必须一起挂】本工程当前档是 Very Low（`m_CurrentQuality: 5`）；
        /// 只设 `GraphicsSettings.defaultRenderPipeline` 而不设这个，遇到"该档有覆盖"时会回落成 Built-in。
        /// </summary>
        public static Rendering.RenderPipelineAsset renderPipeline { get; set; }
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

    /// <summary>文档：Rendering.GraphicsSettings — 全局渲染设置（挂 Render Pipeline Asset 的入口）。</summary>
    /// <remarks>
    /// 【为什么补这组桩 · 2026-10-06】`GraphicsSettings.defaultRenderPipeline` 为 null 时默认管线即
    /// **Built-in** —— 这正是本工程画质链的根因（`m_CustomRenderPipeline: {fileID: 0}`）。
    /// 补桩让 `UrpSetup.cs` 能被**本机语法门禁真校验**，而不是被归入"允许的 Unity 缺失"（那等于没校验）。
    /// </remarks>
    public static class GraphicsSettings
    {
        public static Rendering.RenderPipelineAsset defaultRenderPipeline { get; set; }
        public static Rendering.RenderPipelineAsset currentRenderPipeline => defaultRenderPipeline;
    }

    /// <summary>文档：Rendering.RenderPipelineAsset — 渲染管线资产基类（UnityEngine.Object 的子类）。</summary>
    public class RenderPipelineAsset : ScriptableObject { }

    /// <summary>文档：Rendering.VolumeComponent — 后处理组件基类（VolumeProfile 里的一项）。</summary>
    public class VolumeComponent : ScriptableObject { }

    /// <summary>文档：Rendering.VolumeProfile — 后处理配置集合。</summary>
    /// <remarks>
    /// `Add&lt;T&gt;(bool overrides)` 的约束按**官方签名**只要求 `T : VolumeComponent`
    /// （不要求 `new()`）。实现走 `ScriptableObject.CreateInstance&lt;T&gt;()`——
    /// 这正是真实 URP 的做法，也是这里唯一不需要 `new()` 约束的构造方式。
    /// </remarks>
    public class VolumeProfile : ScriptableObject
    {
        public List<VolumeComponent> components = new List<VolumeComponent>();
        /// <summary>文档：VolumeProfile.Has&lt;T&gt;() — 是否已含某类组件（幂等判断用）。</summary>
        public bool Has<T>() where T : VolumeComponent
        {
            for (int i = 0; i < components.Count; i++) if (components[i] is T) return true;
            return false;
        }
        /// <summary>文档：VolumeProfile.Add&lt;T&gt;(bool overrides) — 添加组件并返回它。</summary>
        public T Add<T>(bool overrides = false) where T : VolumeComponent
            => ScriptableObject.CreateInstance<T>();
    }

    /// <summary>文档：Rendering.ClampedFloatParameter 等 — Volume 参数（value + overrideState 成对写）。</summary>
    public class VolumeParameter<T>
    {
        public T value { get; set; }
        public bool overrideState { get; set; }
    }
    public class MinFloatParameter : VolumeParameter<float> { }
    public class ClampedFloatParameter : VolumeParameter<float> { }
    public class ColorParameter : VolumeParameter<Color> { }
    public class BoolParameter : VolumeParameter<bool> { }
    public class FloatParameter : VolumeParameter<float> { }
    public class Vector2Parameter : VolumeParameter<Vector2> { }
}

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// 文档：URP 17 API 页。
    /// https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset.html
    /// </summary>
    /// <remarks>
    /// 【补桩理由】`UrpSetup.cs` 是画质链根因的修复脚本（把工程从 Built-in 切到 URP）。
    /// 补桩后本机语法门禁会**真校验**我用到的每个成员名 —— 凭记忆写 URP API 正是 CI #18 的失效模式。
    /// 属性可写性以官方 API 页的 Declaration 行为准：带 set 的写成属性，
    /// 只读的（`supportsMainLightShadows` 等）**刻意不补** —— 它们在真实 URP 里只能走 SerializedObject，
    /// 补成可写属性反而会诱导写出编译得过、真机不生效的代码。
    /// </remarks>
    public class UniversalRenderPipelineAsset : RenderPipelineAsset
    {
        public static UniversalRenderPipelineAsset Create(ScriptableRendererData rendererData = null) => new UniversalRenderPipelineAsset();
        public bool supportsHDR { get; set; }
        public int msaaSampleCount { get; set; }
        public float renderScale { get; set; }
        public UpscalingFilterSelection upscalingFilter { get; set; }
        public bool supportsCameraDepthTexture { get; set; }
        public bool supportsCameraOpaqueTexture { get; set; }
        public bool useSRPBatcher { get; set; }
        public bool supportsDynamicBatching { get; set; }
        public float shadowDistance { get; set; }
        public int shadowCascadeCount { get; set; }
        public int mainLightShadowmapResolution { get; set; }
        public int maxAdditionalLightsCount { get; set; }
        public ColorGradingMode colorGradingMode { get; set; }
        public int colorGradingLutSize { get; set; }
        public VolumeProfile volumeProfile { get; set; }
    }

    /// <summary>文档：UniversalRendererData — URP 渲染器数据（URP 17 的 ScriptableRendererData 实现）。</summary>
    public class UniversalRendererData : ScriptableRendererData
    {
        public RenderingMode renderingMode { get; set; }
        public DepthPrimingMode depthPrimingMode { get; set; }
        public IntermediateTextureMode intermediateTextureMode { get; set; }
    }

    /// <summary>文档：ScriptableRendererData — 渲染器数据基类。</summary>
    public class ScriptableRendererData : ScriptableObject { }

    /// <summary>文档：RenderingMode — Forward / Deferred 等。</summary>
    public enum RenderingMode { Forward = 0, Deferred = 1, ForwardPlus = 2 }

    /// <summary>文档：DepthPrimingMode — 深度预判（官方明说 Auto 不支持 Android）。</summary>
    public enum DepthPrimingMode { Disabled = 0, Auto = 1, Forced = 2 }

    /// <summary>文档：IntermediateTextureMode — 中间纹理模式。</summary>
    public enum IntermediateTextureMode { Auto = 0, Always = 1 }

    /// <summary>文档：UpscalingFilterSelection — 升频滤镜（FSR 需 shader model 4.5）。</summary>
    public enum UpscalingFilterSelection { Auto = 0, Linear = 1, Point = 2, FSR = 3, STP = 4 }

    /// <summary>文档：ColorGradingMode — 分级模式。</summary>
    public enum ColorGradingMode { LowDynamicRange = 0, HighDynamicRange = 1 }

    /// <summary>文档：Bloom — 辉光（Volume 组件；URP 17 参数名以 EffectList 为准）。</summary>
    public class Bloom : VolumeComponent
    {
        public MinFloatParameter intensity = new MinFloatParameter();
        public MinFloatParameter threshold = new MinFloatParameter();
        public ClampedFloatParameter scatter = new ClampedFloatParameter();
        public BoolParameter highQualityFiltering = new BoolParameter();
    }

    /// <summary>文档：Vignette — 暗角。</summary>
    public class Vignette : VolumeComponent
    {
        public ClampedFloatParameter intensity = new ClampedFloatParameter();
        public ClampedFloatParameter smoothness = new ClampedFloatParameter();
    }

    /// <summary>文档：ColorAdjustments — 色泽（饱和/对比/曝光）。</summary>
    public class ColorAdjustments : VolumeComponent
    {
        public ClampedFloatParameter saturation = new ClampedFloatParameter();
        public ClampedFloatParameter contrast = new ClampedFloatParameter();
        public FloatParameter postExposure = new FloatParameter();
    }

    /// <summary>文档：Tonemapping — 色调映射。</summary>
    public class Tonemapping : VolumeComponent
    {
        public TonemappingModeParameter mode = new TonemappingModeParameter();
    }
    public class TonemappingModeParameter : VolumeParameter<TonemappingMode> { }
    public enum TonemappingMode { None = 0, Neutral = 1, ACES = 2 }

    /// <summary>文档：FilmGrain — 胶片颗粒。</summary>
    public class FilmGrain : VolumeComponent
    {
        public ClampedFloatParameter intensity = new ClampedFloatParameter();
        public ClampedFloatParameter response = new ClampedFloatParameter();
    }

    /// <summary>文档：ChromaticAberration — 色差（URP 17 只有 intensity 一个参数）。</summary>
    public class ChromaticAberration : VolumeComponent
    {
        public ClampedFloatParameter intensity = new ClampedFloatParameter();
    }
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

        // ── URP 资产装配需要的一组（2026-10-06 补）──────────────────────────────
        // 文档：AssetDatabase.IsValidFolder / CreateFolder / LoadAssetAtPath / CreateAsset /
        //       GetAssetPath / AddObjectToAsset
        // 为什么补：`UrpSetup.cs` 要在 CI 里**生成** URP 的 .asset（ProjectSettings 是编辑器生成的
        // YAML，README 明令禁止手写）。补桩后本机语法门禁能真校验这些调用，而不是归入"允许的缺失"。
        public static bool IsValidFolder(string path) => false;
        public static string CreateFolder(string parentFolder, string newFolderName) => "";
        public static T LoadAssetAtPath<T>(string assetPath) where T : UnityEngine.Object => null;
        public static void CreateAsset(UnityEngine.Object asset, string path) { }
        public static string GetAssetPath(UnityEngine.Object asset) => "";
        public static void AddObjectToAsset(UnityEngine.Object objectToAdd, UnityEngine.Object assetObject) { }
    }

    /// <summary>
    /// 文档：EditorUtility — 编辑器工具方法。
    /// 【为什么补】`UrpSetup.cs` 用它把改过的 URP 资产标记为脏（不标就落不了盘）。
    /// 补桩后本机语法门禁会真校验这个方法名，而不是归入"允许的 Unity 缺失"。
    /// </summary>
    public static class EditorUtility
    {
        /// <summary>文档：EditorUtility.SetDirty(Object) — 标记资产已修改，等待 SaveAssets 落盘。</summary>
        public static void SetDirty(UnityEngine.Object target) { }
    }

    /// <summary>
    /// 文档：InitializeOnLoadMethodAttribute — 编辑器加载/脚本重编译后自动执行静态方法。
    /// 【为什么这个桩很关键】`UrpSetup.EnsureOnEditorLoad` 靠它让**凡是能在 CI 里跑起来的东西**
    /// （构建 / 取证 / 测试 / 任意 -executeMethod）都看到同一套渲染管线设置。
    /// 没有这个桩，那行特性会被归入"允许的 Unity 缺失" ⇒ 本机门禁**验不到**拼写错误，
    /// 而拼错特性名在真机上的表现是"方法根本不执行"（静默失效，最难查的一类）。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class InitializeOnLoadMethodAttribute : Attribute { }

    /// <summary>
    /// 文档：SerializedObject — 绕过属性访问器直接读写序列化字段。
    /// 【为什么必须补这个桩 · 这条是本轮最关键的"防假绿"】
    /// URP 17 里 `supportsMainLightShadows`/`supportsSoftShadows`/`rendererDataList` 等**一大批关键开关
    /// 只有 getter**，官方 API 页明确写 `{ get; }` ⇒ 只能改**序列化字段名**（`m_MainLightShadowsSupported` …）。
    /// 而字段名会随版本漂移。如果本机门禁把它们归入"允许的 Unity 缺失"，那么
    /// **我写错字段名的代码在本机永远是绿的**，只在真机静默不生效 —— 正是本项目最忌讳的失效形态。
    /// 补桩之后，字段名通过字符串传入（那时才由 UrpSetup 自己在运行时抛异常），
    /// 而**方法名与调用形态**则被真校验。
    /// </summary>
    public class SerializedObject
    {
        public SerializedObject(UnityEngine.Object obj) { }
        /// <summary>文档：SerializedObject.FindProperty(string) — 找不到返回 null（UrpSetup 据此抛异常）。</summary>
        public SerializedProperty FindProperty(string propertyPath) => null;
        /// <summary>文档：SerializedObject.GetIterator() — 遍历全部可见字段（用于 dump 真实字段名）。</summary>
        public SerializedProperty GetIterator() => null;
        /// <summary>文档：SerializedObject.ApplyModifiedPropertiesWithoutUndo()。</summary>
        public bool ApplyModifiedPropertiesWithoutUndo() => true;
    }

    /// <summary>文档：SerializedProperty — 序列化字段的读写句柄。</summary>
    public class SerializedProperty
    {
        public string propertyPath => "";
        public bool boolValue { get; set; }
        public int intValue { get; set; }
        public float floatValue { get; set; }
        public int arraySize { get; set; }
        public UnityEngine.Object objectReferenceValue { get; set; }
        public bool NextVisible(bool enterChildren) => false;
        public SerializedProperty GetArrayElementAtIndex(int index) => null;
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
// @@UNITY-STUBS-EXTENSION-END@@
