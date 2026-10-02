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
    }

    public class Font : Object { }
    public class Image : Graphic { }
    public class Button : Behaviour { }
}
