// 追加程序化贴图需要的 Unity API 桩（**只追加**，不动既有内容）。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'native/unity-stubs/UnityStubs.cs');
let s = fs.readFileSync(P, 'utf8');

const probes = [
  ['SetPixels32', 'Texture2D.SetPixels32'],
  ['enum TextureWrapMode', 'TextureWrapMode'],
];
const missing = probes.filter(([sig]) => !s.includes(sig));
if (missing.length === 0) {
  console.log('  · 桩已齐全');
} else {
  const block = `

namespace UnityEngine
{
    /// <summary>贴图环绕模式（出处 ScriptReference/TextureWrapMode）。程序化贴图必须 Repeat。</summary>
    public enum TextureWrapMode { Repeat = 0, Clamp = 1, Mirror = 2, MirrorOnce = 3 }
}
`;
  // Texture2D 的成员要插进已有类里，所以单独补一段（C# 不允许部分类分散在多个文件里同名，故用扩展成员不可行）
  // → 直接把方法加进已有 Texture2D 类定义（用锚点定位）
  const anchor = '        public void ReadPixels(Rect source, int destX, int destY) { }';
  if (s.includes(anchor)) {
    s = s.replace(anchor,
`        public void ReadPixels(Rect source, int destX, int destY) { }
        /// <summary>整块写入像素（出处 ScriptReference/Texture2D.SetPixels32）。程序化生成贴图用它。</summary>
        public void SetPixels32(Color32[] colors) { }
        public void SetPixels(Color[] colors) { }
        public void Apply(bool updateMipmaps) { }
        /// <summary>环绕模式（Repeat 是程序化平铺贴图的必须项）。</summary>
        public TextureWrapMode wrapMode { get; set; }
        /// <summary>生成 mipmap（远处不闪烁）。</summary>
        public bool mipmapEnabled { get; set; }`);
    console.log('  ✓ 追加 Texture2D.SetPixels32 / Apply / wrapMode');
  } else {
    console.log('  ! Texture2D 锚点未中，需人工检查');
  }
  if (!s.includes('enum TextureWrapMode')) {
    s = s.trimEnd() + block;
    console.log('  ✓ 追加 TextureWrapMode');
  }
  fs.writeFileSync(P, s, 'utf8');
}
const open = (s.match(/\{/g) || []).length, close = (s.match(/\}/g) || []).length;
console.log(`  · 行数 ${s.split('\n').length} · 花括号 { ${open} / } ${close} ${open === close ? '平衡 OK' : '不平衡 BAD'}`);
