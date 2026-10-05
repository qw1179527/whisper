using System.Collections.Generic;
using UnityEngine;

namespace Whisper.Gameplay.Render
{
    /// <summary>材质族（决定 PBR 参数与细节贴图）。</summary>
    public enum MaterialFamily
    {
        Plaster,   // 灰泥墙（病房/走廊）—— 粗糙、微脏
        Concrete,  // 混凝土（地下室/太平间）—— 更粗糙、有明显斑驳
        Wood,      // 木（门/地板/家具）—— 有纹理方向、中等光滑
        Metal,     // 金属（配电箱/床架/门把手）—— 高金属度、中等光滑、有划痕
        RustMetal, // 锈蚀金属（老旧设备）—— 金属但非常粗糙
        Tile,      // 瓷砖（太平间/浴室）—— 光滑、接缝明显
        Fabric,    // 织物（床单/帘子）—— 粗糙、有编织纹
    }

    /// <summary>
    /// 程序化材质贴图工厂。
    ///
    /// ## 为什么不用美术资源
    /// 用户的永久约束 §3 点名「目前的建模上色都不行」。上色的瓶颈不在参数，而在**没有任何贴图**：
    /// 只有 `_Color` 的话，再好的 PBR 参数也只能得到"一个纯色块 + 一层高光"。
    /// 但本项目没有美术管线（没有 Texture 资产、没有 ProjectSettings 真源、Resources 里只有 glb）。
    /// 所以正解是：**用代码生成贴图**——确定性、零资产、进包体积为零（运行时生成）。
    ///
    /// ## 确定性纪律
    /// 全部用**整数哈希 + xorshift**，不用 `UnityEngine.Random`（本工程门禁禁止：
    /// 它依赖全局种子，跨端不一致）。同一个 family 在任何设备上生成的贴图**逐像素相同**。
    ///
    /// ## 生成三张图（每族一套，全局缓存）
    ///   · 细节图（R = 粗糙扰动，G = 色偏/污渍，B = 高度）：给着色器做"脏、旧、有层次"
    ///   · 法线图：从细节图的高度通道做 Sobel，得到真实可用的法线（比手写噪声更自然）
    ///   · 遮蔽图：从高度通道做大范围模糊后反相（低处更暗）
    /// </summary>
    public static class ProceduralTextures
    {
        /// <summary>贴图边长（必须是 2 的幂才能 mipmap；128 在手机上够用且生成 <10ms）。</summary>
        public const int Size = 128;

        static readonly Dictionary<MaterialFamily, MaterialSet> _cache = new Dictionary<MaterialFamily, MaterialSet>();

        /// <summary>一族的三张图。</summary>
        public sealed class MaterialSet
        {
            public Texture2D Detail;     // RGB: R=粗糙扰动 G=色偏/污渍 B=高度
            public Texture2D Normal;     // 法线（从高度做 Sobel）
            public Texture2D Occlusion;  // 遮蔽（从高度反相）
            /// <summary>PBR 参数（由族决定）。</summary>
            public float Metallic;
            public float Glossiness;
            public float DetailScale;
            public float DetailStrength;
            public float DirtAmount;
        }

        /// <summary>取一族的贴图与参数（首次调用时生成，之后走缓存）。</summary>
        public static MaterialSet Get(MaterialFamily family)
        {
            if (_cache.TryGetValue(family, out var cached)) return cached;
            var set = Build(family);
            _cache[family] = set;
            return set;
        }

        /// <summary>已生成的族数（HUD/自检用）。</summary>
        public static int CachedCount => _cache.Count;

        /// <summary>清缓存（质量档切换/场景重建时用；贴图会被真正 Destroy）。</summary>
        public static void Clear()
        {
            foreach (var kv in _cache)
            {
                if (kv.Value.Detail != null) Object.Destroy(kv.Value.Detail);
                if (kv.Value.Normal != null) Object.Destroy(kv.Value.Normal);
                if (kv.Value.Occlusion != null) Object.Destroy(kv.Value.Occlusion);
            }
            _cache.Clear();
        }

        // ────────────────────────────────────────────────────────────────────────
        // 生成
        // ────────────────────────────────────────────────────────────────────────

        static MaterialSet Build(MaterialFamily family)
        {
            var gray = new float[Size * Size];      // 粗糙扰动
            var tint = new float[Size * Size];      // 色偏/污渍
            var height = new float[Size * Size];    // 高度

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    int i = y * Size + x;
                    Pattern(family, x, y, out float g, out float t, out float h);
                    gray[i] = g; tint[i] = t; height[i] = h;
                }
            }

            var ms = new MaterialSet();
            var detailPx = new Color32[Size * Size];
            for (int i = 0; i < detailPx.Length; i++)
            {
                byte r = ToByte(gray[i]), gg = ToByte(tint[i]), b = ToByte(height[i]);
                detailPx[i] = new Color32(r, gg, b, 255);
            }
            ms.Detail = MakeTexture("WhisperDetail_" + family, detailPx);

            // 法线：对高度做中心差分（Sobel 的简化），再用"无切线法线重建"就能出细节
            var nrmPx = new Color32[Size * Size];
            float strength = family == MaterialFamily.Tile ? 2.2f
                           : family == MaterialFamily.Wood ? 1.4f
                           : family == MaterialFamily.Fabric ? 1.6f
                           : family == MaterialFamily.Metal ? 0.9f : 1.2f;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float hl = height[Wrap(y) * Size + Wrap(x - 1)], hr = height[Wrap(y) * Size + Wrap(x + 1)];
                    float hd = height[Wrap(y - 1) * Size + Wrap(x)], hu = height[Wrap(y + 1) * Size + Wrap(x)];
                    float dx = (hl - hr) * strength;
                    float dy = (hd - hu) * strength;
                    // 归一化 (dx, dy, 1)
                    float len = Mathf.Sqrt(dx * dx + dy * dy + 1f);
                    var c = new Color32(ToByte(dx / len * 0.5f + 0.5f),
                                        ToByte(dy / len * 0.5f + 0.5f),
                                        ToByte(1f / len * 0.5f + 0.5f), 255);
                    nrmPx[y * Size + x] = c;
                }
            }
            ms.Normal = MakeTexture("WhisperNormal_" + family, nrmPx);

            // 遮蔽：高度做多次盒式模糊后反相（低处/缝隙更暗）
            var occ = new float[Size * Size];
            for (int i = 0; i < occ.Length; i++) occ[i] = height[i];
            for (int pass = 0; pass < 3; pass++) BoxBlur(occ);
            var occPx = new Color32[Size * Size];
            for (int i = 0; i < occPx.Length; i++)
            {
                // 模糊后的高度越高 → 越"外露" → 遮蔽越弱
                byte v = ToByte(Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(occ[i])));
                occPx[i] = new Color32(v, v, v, 255);
            }
            ms.Occlusion = MakeTexture("WhisperOcc_" + family, occPx);

            // PBR 参数按族给定（取值依据见各族注释）
            switch (family)
            {
                case MaterialFamily.Plaster:
                    ms.Metallic = 0f; ms.Glossiness = 0.12f; ms.DetailScale = 0.6f; ms.DetailStrength = 0.40f; ms.DirtAmount = 0.35f; break;
                case MaterialFamily.Concrete:
                    ms.Metallic = 0f; ms.Glossiness = 0.08f; ms.DetailScale = 0.4f; ms.DetailStrength = 0.55f; ms.DirtAmount = 0.45f; break;
                case MaterialFamily.Wood:
                    ms.Metallic = 0f; ms.Glossiness = 0.38f; ms.DetailScale = 1.1f; ms.DetailStrength = 0.50f; ms.DirtAmount = 0.25f; break;
                case MaterialFamily.Metal:
                    ms.Metallic = 0.85f; ms.Glossiness = 0.58f; ms.DetailScale = 1.6f; ms.DetailStrength = 0.30f; ms.DirtAmount = 0.15f; break;
                case MaterialFamily.RustMetal:
                    ms.Metallic = 0.70f; ms.Glossiness = 0.18f; ms.DetailScale = 1.3f; ms.DetailStrength = 0.80f; ms.DirtAmount = 0.55f; break;
                case MaterialFamily.Tile:
                    ms.Metallic = 0f; ms.Glossiness = 0.72f; ms.DetailScale = 0.9f; ms.DetailStrength = 0.35f; ms.DirtAmount = 0.20f; break;
                case MaterialFamily.Fabric:
                    ms.Metallic = 0f; ms.Glossiness = 0.05f; ms.DetailScale = 2.4f; ms.DetailStrength = 0.60f; ms.DirtAmount = 0.30f; break;
            }
            return ms;
        }

        /// <summary>
        /// 各族的图案。全部是**确定性**函数（整数坐标 → 值），无随机、无时钟。
        /// 输出三个通道：g = 粗糙扰动，t = 色偏/污渍，h = 高度。
        /// </summary>
        static void Pattern(MaterialFamily family, int x, int y, out float g, out float t, out float h)
        {
            float fx = x / (float)Size, fy = y / (float)Size;
            switch (family)
            {
                case MaterialFamily.Tile:
                {
                    // 瓷砖：4x4 格，缝处高度低、边缘脏
                    float gx = fx * 4f, gy = fy * 4f;
                    float ex = Mathf.Min(Frac(gx), 1f - Frac(gx));
                    float ey = Mathf.Min(Frac(gy), 1f - Frac(gy));
                    float edge = Mathf.Min(ex, ey);              // 0 = 缝中心
                    float grout = Mathf.SmoothStep(0f, 0.10f, edge);
                    h = Mathf.Lerp(0.15f, 1f, grout);
                    g = Mathf.Lerp(0.75f, 0.15f, grout);        // 缝更粗糙
                    t = Mathf.Lerp(0.35f, 0.85f, grout * Fbm(fx * 8f, fy * 8f));
                    return;
                }
                case MaterialFamily.Wood:
                {
                    // 木：沿一个方向的年轮（正弦），叠加低频噪声做结节
                    float rings = Frac((fy * 9f) + Fbm(fx * 3f, fy * 1.5f) * 0.7f);
                    float grain = Mathf.Abs(rings * 2f - 1f);
                    h = Mathf.Lerp(0.35f, 0.95f, grain);
                    g = Mathf.Lerp(0.55f, 0.20f, grain);
                    t = Mathf.Lerp(0.45f, 0.75f, Fbm(fx * 5f, fy * 14f));
                    return;
                }
                case MaterialFamily.Metal:
                case MaterialFamily.RustMetal:
                {
                    // 金属：细密划痕（沿一个方向拉长的噪声）+ 锈斑（低频斑块）
                    float scratch = Fbm(fx * 3f, fy * 60f);
                    float rust = Fbm(fx * 4f + 11f, fy * 4f + 7f);
                    bool rusty = family == MaterialFamily.RustMetal;
                    h = Mathf.Lerp(0.5f, 0.9f, scratch * 0.5f + 0.5f);
                    g = Mathf.Lerp(rusty ? 0.85f : 0.35f, rusty ? 0.35f : 0.08f, scratch);
                    t = Mathf.Lerp(rusty ? 0.25f : 0.62f, rusty ? 0.70f : 0.85f, rust);
                    if (rusty) h = Mathf.Lerp(h, 0.25f, Mathf.SmoothStep(0.55f, 0.85f, rust));
                    return;
                }
                case MaterialFamily.Fabric:
                {
                    // 织物：经纬编织
                    float weave = Mathf.Abs(Mathf.Sin(fx * 3.14159f * 24f)) * 0.5f
                                + Mathf.Abs(Mathf.Sin(fy * 3.14159f * 24f)) * 0.5f;
                    h = Mathf.Lerp(0.4f, 0.9f, weave);
                    g = Mathf.Lerp(0.30f, 0.08f, weave);
                    t = Mathf.Lerp(0.5f, 0.8f, Fbm(fx * 6f, fy * 6f));
                    return;
                }
                case MaterialFamily.Concrete:
                {
                    // 混凝土：强斑驳（多尺度）+ 气孔
                    float n1 = Fbm(fx * 6f, fy * 6f);
                    float n2 = Fbm(fx * 20f + 3f, fy * 20f + 5f);
                    float pore = n2 > 0.72f ? 0.25f : 1f;
                    h = Mathf.Clamp01(n1 * 0.7f + n2 * 0.3f) * pore;
                    g = Mathf.Lerp(0.75f, 0.30f, n1);
                    t = Mathf.Lerp(0.35f, 0.8f, n2);
                    return;
                }
                default: // Plaster
                {
                    float n1 = Fbm(fx * 5f, fy * 5f);
                    float n2 = Fbm(fx * 16f + 9f, fy * 16f + 2f);
                    h = Mathf.Clamp01(n1 * 0.6f + n2 * 0.4f);
                    g = Mathf.Lerp(0.55f, 0.22f, n1);
                    t = Mathf.Lerp(0.5f, 0.85f, n2);
                    return;
                }
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        // 噪声与工具（全部确定性）
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>整数哈希 → [0,1)。用它代替随机数：同坐标永远同值，跨端一致。</summary>
        static float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        /// <summary>双线性插值的值噪声。</summary>
        static float ValueNoise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            float u = xf * xf * (3f - 2f * xf);
            float v = yf * yf * (3f - 2f * yf);
            float a = Hash(xi, yi), b = Hash(xi + 1, yi);
            float c = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        /// <summary>分形噪声（4 个倍频）。</summary>
        static float Fbm(float x, float y)
        {
            float sum = 0f, amp = 0.5f, fx = x, fy = y;
            for (int i = 0; i < 4; i++)
            {
                sum += ValueNoise(fx, fy) * amp;
                amp *= 0.5f;
                fx *= 2.03f; fy *= 2.01f;
            }
            return Mathf.Clamp01(sum);
        }

        static float Frac(float v) => v - Mathf.Floor(v);
        /// <summary>环形坐标（贴图要 Repeat，采样越界时必须回绕而不是夹边 —— 夹边会在接缝处出现硬边）。</summary>
        static int Wrap(int v) => ((v % Size) + Size) % Size;
        static byte ToByte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

        /// <summary>3x3 盒式模糊（原地）。</summary>
        static void BoxBlur(float[] data)
        {
            var tmp = new float[data.Length];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float s = 0f;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            s += data[Wrap(y + dy) * Size + Wrap(x + dx)];
                    tmp[y * Size + x] = s / 9f;
                }
            System.Array.Copy(tmp, data, data.Length);
        }

        static Texture2D MakeTexture(string name, Color32[] px)
        {
            var t = new Texture2D(Size, Size, TextureFormat.RGBA32, true);
            t.name = name;
            t.wrapMode = TextureWrapMode.Repeat;
            t.filterMode = FilterMode.Bilinear;
            t.SetPixels32(px);
            t.Apply(true);   // 生成 mipmap：远处不闪烁
            return t;
        }
    }
}
