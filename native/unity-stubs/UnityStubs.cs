// UnityEngine 最小桩（仅供 native/unity-syntax 的语义检查使用，**不参与任何构建**）
//
// 为什么需要它：没有 UnityEngine 时，Unity 类型无法解析 → 表达式被标为错误类型 →
// Roslyn 会**跳过其内部的成员检查**，于是项目自身的笔误（如 DesignTokens.ColorConcrete）
// 根本不报错，检查器变成假绿（实测踩过）。给出最小可解析的 API 面后，编译器才能完成语义分析，
// 从而真正拦住项目代码中的错误。
//
// 纪律：这里只放**本项目实际用到**的成员；缺什么就补什么，不要凭空扩面。
using System;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0, 0);
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 one => new Vector3(1, 1, 1);
    }

    public struct Color
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
    }

    public class Object
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

    public class Transform : Component
    {
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Vector3 localScale { get; set; }
        public Quaternion rotation { get; set; }
        public void SetParent(Transform parent, bool worldPositionStays) { }
    }

    public class GameObject : Object
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

    public class Mesh : Object { }
    public class Material : Object
    {
        public Material(Shader shader) { }
        public Color color { get; set; }
    }
    public class Shader : Object { public static Shader Find(string name) => null; }
    public class MeshFilter : Component { public Mesh sharedMesh { get; set; } }
    public class Renderer : Component { public Material sharedMaterial { get; set; } }
    public class MeshRenderer : Renderer { }
    public class TextAsset : Object { public string text => null; }

    public static class Resources
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

    public class Camera : Behaviour
    {
        public CameraClearFlags clearFlags { get; set; }
        public Color backgroundColor { get; set; }
        public float fieldOfView { get; set; }
        public float nearClipPlane { get; set; }
        public float farClipPlane { get; set; }
        public static Camera main => null;
    }

    public enum LightType { Spot, Directional, Point, Area }

    public class Light : Behaviour
    {
        public LightType type { get; set; }
        public float intensity { get; set; }
        public Color color { get; set; }
    }

    public static class Application
    {
        public static int targetFrameRate { get; set; }
        public static string unityVersion => "stub";
    }

    public static class Time
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

    public static class Mathf
    {
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Abs(float a) => a < 0 ? -a : a;
        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
        public static float Floor(float v) => (float)Math.Floor(v);
        public static float Sqrt(float v) => (float)Math.Sqrt(v);
    }

    public class Canvas : Behaviour { public RenderMode renderMode { get; set; } }
    public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }

    public class CanvasScaler : Behaviour
    {
        public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize, ConstantPhysicalSize }
        public ScaleMode uiScaleMode { get; set; }
        public Vector2 referenceResolution { get; set; }
    }

    public class GraphicRaycaster : Behaviour { }

    public class RectTransform : Transform
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
    public class Graphic : Behaviour { public Color color { get; set; } public RectTransform rectTransform => null; }

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

    public class Image : Graphic { }
    public class Button : Behaviour { }
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
