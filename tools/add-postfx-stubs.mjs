// 追加后处理需要的 Unity API 桩（**只追加**，不动既有内容 —— 桩文件被我改坏过多次）。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'native/unity-stubs/UnityStubs.cs');
let s = fs.readFileSync(P, 'utf8');
const need = [];

const block = `

namespace UnityEngine
{
    /// <summary>渲染纹理（出处 ScriptReference/RenderTexture）。后处理的多级缓冲用它。</summary>
    public class RenderTexture : Texture
    {
        public RenderTexture(int width, int height, int depth) { this.width = width; this.height = height; this.depth = depth; }
        public RenderTexture(int width, int height, int depth, RenderTextureFormat format)
            : this(width, height, depth) { this.format = format; }
        public int depth { get; set; }
        public RenderTextureFormat format { get; set; }
        public bool IsCreated() => true;
        public bool Create() => true;
        public void Release() { }
        public static RenderTexture active { get; set; }
        public FilterMode filterMode { get; set; }
    }

    public enum RenderTextureFormat { Default = 0, ARGB32 = 0, RFloat = 1, ARGBHalf = 2 }

    /// <summary>深度纹理模式（出处 ScriptReference/DepthTextureMode）。SSAO/SSGI 要 DepthNormals。</summary>
    [System.Flags]
    public enum DepthTextureMode { None = 0, Depth = 1, DepthNormals = 2, MotionVectors = 4 }

    /// <summary>低层绘制（出处 ScriptReference/Graphics.Blit）。后处理的每一级都走它。</summary>
    public static class Graphics
    {
        public static void Blit(Texture source, RenderTexture dest) { }
        public static void Blit(Texture source, RenderTexture dest, Material mat, int pass) { }
    }

    /// <summary>纹理基类（出处 ScriptReference/Texture）。</summary>
    public class Texture : Object
    {
        public int width { get; set; }
        public int height { get; set; }
        public FilterMode filterMode { get; set; }
    }

    public enum FilterMode { Point = 0, Bilinear = 1, Trilinear = 2 }

    /// <summary>可读纹理（出处 ScriptReference/Texture2D）。眼部适应读回 1x1 亮度用它。</summary>
    public class Texture2D : Texture
    {
        public Texture2D(int width, int height) { this.width = width; this.height = height; }
        public Texture2D(int width, int height, TextureFormat format, bool mipChain)
        { this.width = width; this.height = height; }
        public static Texture2D blackTexture => null;
        public static Texture2D whiteTexture => null;
        public void ReadPixels(Rect source, int destX, int destY) { }
        public void Apply() { }
        public Color GetPixel(int x, int y) => new Color(1f, 1f, 1f, 1f);
    }

    public enum TextureFormat { RFloat = 0, RGBA32 = 1, ARGB32 = 2 }

    /// <summary>矩形（出处 ScriptReference/Rect）。</summary>
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
    }
}
`;

// 逐个检查需要补的符号，报告补了什么（不猜，缺什么补什么）
const probes = [
  ['class RenderTexture', 'RenderTexture'],
  ['enum DepthTextureMode', 'DepthTextureMode'],
  ['static class Graphics', 'Graphics.Blit'],
  ['class Texture2D', 'Texture2D'],
  ['struct Rect', 'Rect'],
];
const missing = probes.filter(([sig]) => !s.includes(sig));
if (missing.length === 0) {
  console.log('  · 桩已齐全，无需追加');
} else {
  s = s.trimEnd() + block;
  fs.writeFileSync(P, s, 'utf8');
  console.log('  ✓ 追加桩：' + missing.map(([, n]) => n).join(', '));
}
const open = (s.match(/\{/g) || []).length, close = (s.match(/\}/g) || []).length;
console.log(`  · 行数 ${s.split('\n').length} · 花括号 { ${open} / } ${close} ${open === close ? '平衡 OK' : '不平衡 BAD'}`);
