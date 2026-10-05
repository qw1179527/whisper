// **重新生成** `native/unity-stubs/UnityStubs.cs`（第 7 版）。
//
// ## 为什么不再打补丁
// 我在这个文件上连续失败 6 次：找大括号插入（把内容插到文件首尾）、按行去重（误删成员）、
// 花括号栈解析（类型检测失败，一个成员都没插进去）。**根本原因是我在改一个我没有完整心智模型的文件。**
// 正解：**按已知需要的 API 面从零生成一份**，结构由生成器保证正确。
//
// ## 这份桩的用途（写在文件头，别忘）
// 只供 `native/unity-syntax` 的语义检查使用，**不参与任何构建**。它的价值是：没有 UnityEngine 时，
// Unity 类型无法解析 → Roslyn 会跳过成员检查 → 项目自身的笔误不报错（检查器假绿）。
// 给出最小可解析的 API 面后，编译器才能完成语义分析，真正拦住项目代码的错误。
//
// ## 纪律
// 只放**本项目实际用到**的成员；缺什么补什么，不凭空扩面。
// ⚠ 本项目踩过的坑（写进注释，防止再犯）：CS0117 / CS1061 / CS1503 / CS0023 / CS1579 这五类
//    **先查桩、再改代码** —— 它们经常是"桩缺成员"而不是"代码错"。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// ⚠ 用绝对路径：CWD 不一定是仓库根（从别处调用时相对路径会写失败，实测踩过）
const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

const PY = String.raw;   // 仅用于标注：本文件不含 Python

// ── UnityEngine.UI 的类型（真实 Unity 里它们就在这个命名空间）──
// 为什么要单独一块：若把它们留在 UnityEngine 命名空间，`using UnityEngine.UI;` 会报 CS0234，
// 于是**所有 UI 用法都被归为"Unity 缺失"而放行** —— 检查器假绿。搬进正确命名空间后才真被验证。
const UI_BLOCK = `
    public class Canvas : Behaviour { public RenderMode renderMode { get; set; } }
    public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }

    public class CanvasScaler : Behaviour
    {
        public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize, ConstantPhysicalSize }
        public ScaleMode uiScaleMode { get; set; }
        public Vector2 referenceResolution { get; set; }
        public float matchWidthOrHeight { get; set; }
    }

    public class GraphicRaycaster : Behaviour { }

    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public enum HorizontalWrapMode { Wrap, Overflow }
    public enum VerticalWrapMode { Truncate, Overflow }

    public class Graphic : Behaviour
    {
        public Color color { get; set; }
        public RectTransform rectTransform => null;
        public bool enabled { get; set; }
        public bool raycastTarget { get; set; }
        public Canvas canvas => null;
    }

    public class Text : Graphic
    {
        public string text { get; set; }
        public Font font { get; set; }
        public int fontSize { get; set; }
        public TextAnchor alignment { get; set; }
        public HorizontalWrapMode horizontalOverflow { get; set; }
        public VerticalWrapMode verticalOverflow { get; set; }
    }

    public class Image : Graphic { public Sprite sprite { get; set; } public float fillAmount { get; set; } public ImageType type { get; set; } }
    public enum ImageType { Simple, Sliced, Tiled, Filled }

    public class ButtonClickedEvent { public void AddListener(UnityEngine.Events.UnityAction call) { } }

    public class Button : Behaviour
    {
        public ButtonClickedEvent onClick => null;
        /// <summary>按钮的图形目标（出处 ScriptReference/Selectable-targetGraphic）。</summary>
        public Graphic targetGraphic { get; set; }
        public bool interactable { get; set; }
    }
`;
let HEAD = `// UnityEngine 最小桩（仅供 native/unity-syntax 的语义检查使用，**不参与任何构建**）
//
// 为什么需要它：没有 UnityEngine 时，Unity 类型无法解析 → 表达式被标为错误类型 →
// Roslyn 会**跳过其内部的成员检查**，于是项目自身的笔误（如 DesignTokens.ColorConcrete）
// 根本不报错，检查器变成假绿（实测踩过）。给出最小可解析的 API 面后，编译器才能完成语义分析，
// 从而真正拦住项目代码中的错误。
//
// 纪律：这里只放**本项目实际用到**的成员；缺什么就补什么，不要凭空扩面。
//
// ⚠ 本项目反复踩过的坑：**CS0117 / CS1061 / CS1503 / CS0023 / CS1579 这五类先查桩、再改代码**
//    —— 它们经常是"桩里没声明这个成员"，而不是"代码写错了"。
//    （实例：Mesh 只有 List 重载没数组重载 → CS1503；Vector3 只有二元减号 → CS0023；
//      Transform 没实现 IEnumerable → CS1579。）
//
// 本文件由 tools/gen-unity-stubs.mjs **生成**，手改会被覆盖。
using System;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0, 0);
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => a + (b - a) * (t < 0f ? 0f : (t > 1f ? 1f : t));
        public static Vector2 one => new Vector2(1, 1);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, float s) => new Vector2(a.x * s, a.y * s);
        public float magnitude => (float)Math.Sqrt(x * x + y * y);
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public Vector3(float x, float y) { this.x = x; this.y = y; this.z = 0f; }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public static Vector3 right => new Vector3(1, 0, 0);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        /// <summary>一元负号（出处 ScriptReference/Vector3-operator_UnaryNegation）。
        /// 桩里只有二元减号时，-pivot 会报 CS0023 —— 先查桩再改代码。</summary>
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float s) => new Vector3(a.x * s, a.y * s, a.z * s);
        public static Vector3 operator *(float s, Vector3 a) => new Vector3(a.x * s, a.y * s, a.z * s);
        public static Vector3 operator /(Vector3 a, float s) => new Vector3(a.x / s, a.y / s, a.z / s);
        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
        public float sqrMagnitude => x * x + y * y + z * z;
        public Vector3 normalized => this;
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * (t < 0f ? 0f : (t > 1f ? 1f : t));
        public static Vector3 Cross(Vector3 a, Vector3 b)
            => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public override string ToString() => "(" + x + ", " + y + ", " + z + ")";
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1);
        public static Color black => new Color(0, 0, 0);
        public static Color gray => new Color(.5f, .5f, .5f);
        public static Color red => new Color(1, 0, 0);
        public static Color green => new Color(0, 1, 0);
        public static Color blue => new Color(0, 0, 1);
        public static Color yellow => new Color(1, 1, 0);
        public static Color operator *(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);
        /// <summary>线性插值（出处 ScriptReference/Color.Lerp）。</summary>
        public static Color Lerp(Color a, Color b, float t)
            => new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity => new Quaternion(0, 0, 0, 1);
        /// <summary>欧拉角（出处 ScriptReference/Quaternion-eulerAngles）。第一人称视角读写用它。</summary>
        public Vector3 eulerAngles { get; set; }
        public static Quaternion Euler(float x, float y, float z) => new Quaternion(x, y, z, 1f);
        public static Quaternion Euler(Vector3 e) => new Quaternion(e.x, e.y, e.z, 1f);
        public static Quaternion LookRotation(Vector3 forward) => identity;
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => a;
    }

    public struct Matrix4x4
    {
        public static Matrix4x4 identity => new Matrix4x4();
        public static Matrix4x4 TRS(Vector3 pos, Quaternion q, Vector3 s) => identity;
    }

    public struct Bounds
    {
        public Vector3 center, size, min, max;
        public Bounds(Vector3 c, Vector3 s) { center = c; size = s; min = c; max = c; }
        public Vector3 extents => size;
    }

    public struct Ray
    {
        public Vector3 origin, direction;
        public Ray(Vector3 o, Vector3 d) { origin = o; direction = d; }
    }

    public struct RaycastHit
    {
        public Vector3 point;
        public float distance;
        public Collider collider;
        public Transform transform;
    }

    public class Object
    {
        public string name { get; set; }
        public static void Destroy(Object o) { }
        public static void DestroyImmediate(Object o) { }
        public static T Instantiate<T>(T o) where T : Object => o;
        public override string ToString() => name;
    }

    public class Component : Object
    {
        public GameObject gameObject => null;
        public Transform transform => null;
        public T GetComponent<T>() => default;
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; }
        public T GetComponentInChildren<T>() => default;
    }
    public class MonoBehaviour : Behaviour { }

    public class Transform : Component
    {
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Vector3 localScale { get; set; }
        public Quaternion rotation { get; set; }
        /// <summary>本地旋转（出处 ScriptReference/Transform-localRotation）。</summary>
        public Quaternion localRotation { get; set; }
        /// <summary>朝向（出处 ScriptReference/Transform-forward）。跳脸把脸贴相机正前方。</summary>
        public Vector3 forward { get; set; }
        public Vector3 right { get; set; }
        public Vector3 up { get; set; }
        public void SetParent(Transform parent, bool worldPositionStays) { }
        public void LookAt(Vector3 worldPosition) { }
        public Transform parent { get; set; }
        /// <summary>子物体数量（出处 ScriptReference/Transform-childCount）。</summary>
        public int childCount => 0;
        public Transform GetChild(int index) => null;
        /// <summary>可枚举子物体（真 Unity 的 Transform 实现 IEnumerable）。
        /// 桩里没有它会报 CS1579 —— 先查桩再改代码。</summary>
        public System.Collections.Generic.IEnumerator<Transform> GetEnumerator() { yield break; }
    }

    public class GameObject : Object
    {
        public GameObject() { }
        public GameObject(string name) { }
        public GameObject(string name, params Type[] components) { }
        public Transform transform => null;
        public T AddComponent<T>() where T : Component => default;
        public T GetComponent<T>() => default;
        /// <summary>在子物体里找组件（出处 ScriptReference/GameObject.GetComponentInChildren）。</summary>
        public T GetComponentInChildren<T>() => default;
        public T[] GetComponentsInChildren<T>() => new T[0];
        public void SetActive(bool value) { }
        public bool activeSelf => false;
        public static GameObject CreatePrimitive(PrimitiveType type) => null;
        public static GameObject Find(string name) => null;
    }

    public enum PrimitiveType { Sphere, Capsule, Cylinder, Cube, Plane, Quad }

    public class Mesh : Object
    {
        public string name { get; set; }
        public Bounds bounds { get; set; }
        public Vector3[] vertices => null;
        public int[] triangles => null;
        public int vertexCount => 0;
        /// <summary>真 Unity 数组与 List **两种重载都在**（出处 ScriptReference/Mesh.SetVertices 等）。
        /// 桩里只声明 List 版时，mesh.SetVertices(vector3Array) 会报 CS1503 —— 先查桩再改代码。</summary>
        public void SetVertices(System.Collections.Generic.List<Vector3> vertices) { }
        public void SetVertices(Vector3[] vertices) { }
        public void SetUVs(int channel, System.Collections.Generic.List<Vector2> uvs) { }
        public void SetUVs(int channel, Vector2[] uvs) { }
        public void SetTriangles(System.Collections.Generic.List<int> triangles, int submesh) { }
        public void SetTriangles(int[] triangles, int submesh) { }
        public void SetNormals(Vector3[] normals) { }
        public void SetNormals(System.Collections.Generic.List<Vector3> normals) { }
        public void RecalculateNormals() { }
        public void RecalculateBounds() { }
        public void Clear() { }
    }

    public class Material : Object
    {
        public Material(Shader shader) { }
        public Color color { get; set; }
        public Shader shader { get; set; }
        /// <summary>是否有该属性（出处 ScriptReference/Material.HasProperty）。
        /// 不同着色器属性名不同，**必须先问再设**，否则设值静默无效。</summary>
        public bool HasProperty(string name) => false;
        public void SetColor(string name, Color value) { }
        public void SetFloat(string name, float value) { }
        public void SetInt(string name, int value) { }
        public void SetTexture(string name, Texture value) { }
        /// <summary>启用着色器关键字（出处 ScriptReference/Material.EnableKeyword）。</summary>
        public void EnableKeyword(string keyword) { }
        public void DisableKeyword(string keyword) { }
    }

    public class Shader : Object
    {
        public static Shader Find(string name) => null;
        /// <summary>设全局浮点（出处 ScriptReference/Shader.SetGlobalFloat）。雾密度用它。</summary>
        public static void SetGlobalFloat(string name, float value) { }
        public static void SetGlobalColor(string name, Color value) { }
    }

    public enum TextureWrapMode { Repeat, Clamp, Mirror }
    public enum FilterMode { Point, Bilinear, Trilinear }

    public class Texture : Object { }
    public class Texture2D : Texture
    {
        public Texture2D(int w, int h) { }
        public Texture2D(int w, int h, TextureFormat fmt, bool mip) { }
        public TextureWrapMode wrapMode { get; set; }
        public FilterMode filterMode { get; set; }
        public void SetPixels32(Color32[] colors) { }
        public void SetPixel(int x, int y, Color c) { }
        public void Apply() { }
        public byte[] EncodeToPNG() => null;
        public void ReadPixels(Rect source, int destX, int destY) { }
        public Color32[] GetPixels32() => null;
        public Color GetPixel(int x, int y) => Color.black;
        public void Apply(bool updateMipmaps) { }
        public static RenderTexture GetTemporary(int w, int h, int depth) => null;
        public static void ReleaseTemporary(RenderTexture rt) { }
        public static RenderTexture active { get; set; }
    }

    public enum TextureFormat { Alpha8 = 1, RGBA32 = 4, ARGB32 = 5, RGB24 = 3 }

    public class Sprite : Object
    {
        public static Sprite Create(Texture2D tex, Rect rect, Vector2 pivot) => null;
        public static Sprite Create(Texture2D tex, Rect rect, Vector2 pivot, float pixelsPerUnit) => null;
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; }
    }
    public class MeshFilter : Component { public Mesh sharedMesh { get; set; } public Mesh mesh { get; set; } }

    /// <summary>渲染器基类（出处 ScriptReference/Renderer）。enabled 与材质在它上面。</summary>
    public class Renderer : Component
    {
        /// <summary>启用/禁用渲染（出处 ScriptReference/Renderer-enabled）。
        /// 第一人称要藏头/躯干（否则相机在头里，满屏内壁）就靠它。</summary>
        public bool enabled { get; set; }
        public Material sharedMaterial { get; set; }
        public Material material { get; set; }
    }
    public class MeshRenderer : Renderer { }

    public class TextAsset : Object
    {
        public string text => null;
        /// <summary>原始字节（出处 ScriptReference/TextAsset-bytes）。套件 GLB 走它读。</summary>
        public byte[] bytes => null;
    }

    public static class Resources
    {
        public static T Load<T>(string path) where T : Object => default;
        /// <summary>取内置资源（出处 ScriptReference/Resources.GetBuiltinResource）。
        /// 文本要字体：Resources.GetBuiltinResource&lt;Font&gt;("LegacyRuntime.ttf")。</summary>
        public static T GetBuiltinResource<T>(string path) where T : Object => default;
    }

    /// <summary>内置字体（Resources.GetBuiltinResource&lt;Font&gt;("LegacyRuntime.ttf")）。</summary>
    public class Font : Object
    {
        public static Font CreateDynamicFontFromOSFont(string name, int size) => null;
        public static Font CreateDynamicFontFromOSFont(string[] names, int size) => null;
    }

    public enum CameraClearFlags { Skybox = 1, Color = 2, SolidColor = 2, Depth = 3, Nothing = 4 }

    public class Camera : Behaviour
    {
        public static Camera main => null;
        public float fieldOfView { get; set; }
        public float nearClipPlane { get; set; }
        public float farClipPlane { get; set; }
        public CameraClearFlags clearFlags { get; set; }
        public Color backgroundColor { get; set; }
        public bool orthographic { get; set; }
        public float orthographicSize { get; set; }
        /// <summary>世界 → 屏幕（出处 ScriptReference/Camera.WorldToScreenPoint）。门交互的屏幕命中用它。</summary>
        public Vector3 WorldToScreenPoint(Vector3 position) => position;
        /// <summary>屏幕 → 射线（出处 ScriptReference/Camera.ScreenPointToRay）。</summary>
        public Ray ScreenPointToRay(Vector3 position) => new Ray();
        public RenderTexture targetTexture { get; set; }
        public void Render() { }
        public Matrix4x4 projectionMatrix { get; set; }
        public Matrix4x4 worldToCameraMatrix { get; set; }
    }

    public enum LightType { Spot, Directional, Point, Area }
    /// <summary>阴影模式（出处 ScriptReference/LightShadows）。</summary>
    public enum LightShadows { None, Hard, Soft }

    public class Light : Behaviour
    {
        public LightType type { get; set; }
        public float intensity { get; set; }
        public Color color { get; set; }
        /// <summary>照射范围（出处 ScriptReference/Light-range）。主界面灯与跳脸补光靠它。</summary>
        public float range { get; set; }
        public float spotAngle { get; set; }
        public LightShadows shadows { get; set; }
    }

    public struct Touch
    {
        public int fingerId;
        public Vector2 position, deltaPosition;
        public TouchPhase phase;
    }

    public enum TouchPhase { Began, Moved, Stationary, Ended, Canceled }

    public static class Input
    {
        public static int touchCount => 0;
        public static Touch GetTouch(int index) => new Touch();
        public static bool GetKeyDown(KeyCode k) => false;
        public static bool GetMouseButtonDown(int b) => false;
    }

    public enum KeyCode { None = 0, Escape = 27, Space = 32, E = 101 }

    public static class Screen
    {
        public static int width => 0;
        public static int height => 0;
    }

    public static class Application
    {
        public static string productName => null;
        /// <summary>StreamingAssets 路径（出处 ScriptReference/Application-streamingAssetsPath）。</summary>
        public static string streamingAssetsPath => null;
        public static bool isEditor => false;
        public static void Quit() { }
        public static int targetFrameRate { get; set; }
        public static string persistentDataPath => null;
        public static string unityVersion => null;
        public static string dataPath => null;
    }

    public static class Time
    {
        public static float unscaledTime => 0f;
        public static float unscaledDeltaTime => 0f;
        public static float deltaTime => 0f;
        public static float fixedDeltaTime => 0f;
        public static float timeScale { get; set; }
        /// <summary>自游戏开始的时间（出处 ScriptReference/Time-time）。灯光抖动相位用它。</summary>
        public static float time => 0f;
        public static int frameCount => 0;
    }

    public static class Debug
    {
        public static void Log(object m) { }
        public static void LogWarning(object m) { }
        public static void LogError(object m) { }
    }

    public static class Mathf
    {
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Abs(float a) => a < 0 ? -a : a;
        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static float Floor(float v) => (float)Math.Floor(v);
        public static float Ceil(float v) => (float)Math.Ceiling(v);
        public static float Sqrt(float v) => (float)Math.Sqrt(v);
        public static float Exp(float v) => (float)Math.Exp(v);
        /// <summary>线性插值（出处 ScriptReference/Mathf.Lerp）。</summary>
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float InverseLerp(float a, float b, float v) => b == a ? 0f : Clamp01((v - a) / (b - a));
        /// <summary>Perlin 噪声（出处 ScriptReference/Mathf.PerlinNoise）。灯光抖动用它。</summary>
        public static float PerlinNoise(float x, float y) => 0.5f;
        public static float Sin(float v) => (float)Math.Sin(v);
        public static float Cos(float v) => (float)Math.Cos(v);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float PI => 3.14159265f;
        public static float Deg2Rad => 0.0174533f;
        public static float Rad2Deg => 57.29578f;
        public static float Round(float v) => (float)Math.Round(v);
        public static int RoundToInt(float v) => (int)Math.Round(v);
        public static int FloorToInt(float v) => (int)Math.Floor(v);
        public static int CeilToInt(float v) => (int)Math.Ceiling(v);
        /// <summary>朝目标推进（出处 ScriptReference/Mathf.MoveTowards）。门扇开合用它。</summary>
        public static float MoveTowards(float cur, float target, float maxDelta)
        {
            float d = target - cur;
            if (Math.Abs(d) <= maxDelta) return target;
            return cur + (d > 0f ? maxDelta : -maxDelta);
        }
        public static float Repeat(float t, float length) => t - Floor(t / length) * length;
        public static float PingPong(float t, float length) => length - Abs(Repeat(t, length * 2f) - length);
    }

    public class Collider : Component { public bool enabled { get; set; } }
    public class BoxCollider : Collider { public Vector3 size { get; set; } }
    public class CapsuleCollider : Collider { public float radius { get; set; } public float height { get; set; } }

    public static class Physics
    {
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float maxDistance)
        { hit = new RaycastHit(); return false; }
        public static bool Raycast(Ray ray, out RaycastHit hit, float maxDistance)
        { hit = new RaycastHit(); return false; }
    }
}

namespace UnityEngine.Events
{
    public delegate void UnityAction();
    public delegate void UnityAction<T>(T arg0);
    public class UnityEvent { public void AddListener(UnityAction call) { } public void Invoke() { } }
    public class UnityEvent<T> { public void AddListener(UnityAction<T> call) { } public void Invoke(T a) { } }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene { public string name => null; public bool IsValid() => true; }
    public static class SceneManager
    {
        public static Scene GetActiveScene() => new Scene();
        public static void LoadScene(string name) { }
    }
}

namespace UnityEditor
{
    using UnityEngine;

    public enum BuildTargetGroup { Unknown = 0, Standalone = 1, Android = 7, iOS = 4 }
    public enum ScriptingImplementation { Mono2x = 0, IL2CPP = 1, WinRTDotNET = 2, CoreCLR = 3 }
    public enum ManagedStrippingLevel { Disabled = 0, Low = 1, Medium = 2, High = 3, Minimal = 4 }
    public enum AndroidArchitecture { None = 0, ARMv7 = 1, ARM64 = 2, X86 = 4, X86_64 = 8, All = unchecked((int)0xFFFFFFFF) }
    public enum AndroidSdkVersions { AndroidApiLevelAuto = 0, AndroidApiLevel23 = 23, AndroidApiLevel30 = 30, AndroidApiLevel33 = 33, AndroidApiLevel34 = 34, AndroidApiLevel24 = 24, AndroidApiLevel25 = 25, AndroidApiLevel26 = 26, AndroidApiLevel27 = 27, AndroidApiLevel28 = 28, AndroidApiLevel29 = 29, AndroidApiLevel31 = 31, AndroidApiLevel32 = 32, AndroidApiLevel35 = 35, AndroidApiLevel36 = 36 }
    public enum UIOrientation { Portrait = 0, PortraitUpsideDown = 1, LandscapeRight = 2, LandscapeLeft = 3, AutoRotation = 4 }
    public enum BuildTarget { NoTarget = -2, StandaloneWindows = 5, Android = 13, iOS = 9 }
    public enum BuildOptions { None = 0, Development = 1, AutoRunPlayer = 4 }
    public enum BuildResult { Unknown = 0, Succeeded = 1, Failed = 2, Cancelled = 3 }

    public struct NamedBuildTarget
    {
        public static NamedBuildTarget Android => new NamedBuildTarget();
        public static NamedBuildTarget Standalone => new NamedBuildTarget();
    }

    public static class PlayerSettings
    {
        public static string companyName { get; set; }
        public static string productName { get; set; }
        public static string applicationIdentifier { get; set; }
        public static UIOrientation defaultInterfaceOrientation { get; set; }
        public static UIOrientation allowedAutorotateToPortrait { get; set; }
        public static UIOrientation allowedAutorotateToPortraitUpsideDown { get; set; }
        public static UIOrientation allowedAutorotateToLandscapeLeft { get; set; }
        public static UIOrientation allowedAutorotateToLandscapeRight { get; set; }
        public static bool useAnimatedAutorotation { get; set; }
        public static string bundleVersion { get; set; }
        public static string GetApplicationIdentifier(NamedBuildTarget t) => null;
        public static void SetScriptingBackend(NamedBuildTarget t, ScriptingImplementation i) { }
        public static void SetManagedStrippingLevel(NamedBuildTarget t, ManagedStrippingLevel l) { }
        public static ManagedStrippingLevel GetManagedStrippingLevel(NamedBuildTarget t) => ManagedStrippingLevel.Disabled;
        public static ScriptingImplementation GetScriptingBackend(NamedBuildTarget t) => ScriptingImplementation.Mono2x;
        public static void SetArchitecture(AndroidArchitecture a) { }
        public static void SetApplicationIdentifier(NamedBuildTarget t, string id) { }
        public static void SetIl2CppCompilerConfiguration(NamedBuildTarget t, object c) { }

        public static class Android
        {
            public static AndroidSdkVersions minSdkVersion { get; set; }
            public static AndroidSdkVersions targetSdkVersion { get; set; }
            public static bool useCustomKeystore { get; set; }
            public static int bundleVersionCode { get; set; }
            public static AndroidArchitecture targetArchitectures { get; set; }
        }
    }

    public class MenuItemAttribute : Attribute { public MenuItemAttribute(string itemName) { } }

    public class EditorBuildSettingsScene
    {
        public EditorBuildSettingsScene(string path, bool enabled) { }
        public string path { get; set; }
        public bool enabled { get; set; }
    }

    public static class EditorBuildSettings
    {
        public static EditorBuildSettingsScene[] scenes { get; set; }
    }

    public enum NewSceneSetup { EmptyScene = 0, DefaultGameObjects = 1 }
    public enum NewSceneMode { Single = 0, Additive = 1 }

    public static class EditorSceneManager
    {
        public static UnityEngine.SceneManagement.Scene NewScene(NewSceneSetup setup) => new UnityEngine.SceneManagement.Scene();
        public static void SaveScene(UnityEngine.SceneManagement.Scene s, string path) { }
        public static UnityEngine.SceneManagement.Scene NewScene(NewSceneSetup setup, NewSceneMode mode) => new UnityEngine.SceneManagement.Scene();
    }

    public static class AssetDatabase
    {
        public static void Refresh() { }
        public static string[] FindAssets(string filter) => new string[0];
        public static string GUIDToAssetPath(string guid) => null;
        public static void SaveAssets() { }
        public static void CreateAsset(Object asset, string path) { }
    }

    public class BuildSummary
    {
        public BuildResult result { get; set; }
        public int totalSize { get; set; }
        public int totalErrors { get; set; }
        public int totalWarnings { get; set; }
    }

    public class BuildReport { public BuildSummary summary => null; }

    public struct BuildPlayerOptions
    {
        public string[] scenes;
        public string locationPathName;
        public BuildTarget target;
        public BuildOptions options;
    }

    public static class BuildPipeline
    {
        public static UnityEditor.Build.Reporting.BuildReport BuildPlayer(BuildPlayerOptions opts) => null;
    }
}

namespace UnityEditor.Build.Reporting
{
    public class BuildReport { public UnityEditor.BuildSummary summary => null; }
}
`;

// ── UnityEngine.UI 命名空间（真实 Unity 里 UI 类型就在这）──
// 为什么单独成块：这些类型若留在 UnityEngine 命名空间，`using UnityEngine.UI;` 会报 CS0234，
// 于是**所有 UI 用法都被归为"Unity 缺失"而放行**（检查器假绿）。搬进正确命名空间后它们才真被验证。
HEAD += "namespace UnityEngine.UI\n{\n    using UnityEngine;\n\n" + UI_BLOCK + "}\n";

fs.writeFileSync(path.join(ROOT, 'native/unity-stubs/UnityStubs.cs'), HEAD, 'utf8');
const open = (HEAD.match(/\{/g) || []).length, close = (HEAD.match(/\}/g) || []).length;
console.log('  ✓ 已重新生成桩：' + HEAD.split('\\n').length + ' 行 · 花括号 { ' + open + ' / } ' + close + (open === close ? ' 平衡 ✓' : ' 不平衡 ✗'));
