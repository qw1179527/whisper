using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Whisper.Gameplay.Level
{
    /// <summary>
    /// **纯 C# 的 GLB 解析器（零引擎依赖）** —— 只做"这个套件文件里有多少几何"这件事。
    ///
    /// ══════════════════════════════════════════════════════════════════════════════
    /// 与 Unity 版 `GlbReader` 的关系（为什么另写一个而不是搬）
    /// ══════════════════════════════════════════════════════════════════════════════
    /// Unity 版（539 行）会把顶点解成 `Mesh`、材质解成 `Material` ——
    /// 那是**渲染用**的，换引擎必须重写（Godot 有自己的 `GLTFDocument`，未必需要它）。
    ///
    /// 但 `KitVerifier`（资产清单门禁）要的只是三个计数：
    /// ```csharp
    /// load.Parts = model.Primitives.Count;
    /// load.Vertices = model.VertexCount;
    /// load.Triangles = model.TriangleCount;
    /// load.Ok = load.Parts > 0 && load.Vertices > 0 && load.Triangles > 0;
    /// ```
    /// ⇒ **这部分是纯逻辑**（读二进制容器、数 accessor），应当属于引擎无关层。
    ///   于是本文件只实现这一半：解析 GLB 容器 + 数各 primitive 的顶点/索引。
    ///
    /// ⚠ **如实界定**：本类**不解析顶点坐标、不解析材质、不做 mesh 构建**。
    ///   它只回答"几何计数"这一类问题。Unity 端仍然用原来那 539 行的 `GlbReader`。
    ///
    /// ══════════════════════════════════════════════════════════════════════════════
    /// GLB 容器格式（本类只依赖这几条，够用且稳定）
    /// ══════════════════════════════════════════════════════════════════════════════
    /// ```
    /// offset 0   uint32 magic   = 0x46546C67 ("glTF")
    /// offset 4   uint32 version = 2
    /// offset 8   uint32 length  = 整个文件字节数
    /// 之后是若干 chunk：
    ///   uint32 chunkLength · uint32 chunkType · byte[chunkLength] chunkData
    ///   chunkType 0x4E4F534A = JSON（glTF 的 JSON 描述）
    ///   chunkType 0x004E4942 = BIN（二进制缓冲，accessor 指向它）
    /// ```
    /// JSON 里我们只关心：
    /// ```json
    /// { "accessors": [ { "count": N, "type": "VEC3", ... } ],
    ///   "meshes": [ { "primitives": [ { "attributes": {"POSITION": 0}, "indices": 1 } ] } ] }
    /// ```
    /// · 顶点数 = 各 primitive 的 POSITION accessor 的 `count`
    /// · 三角形数 = indices accessor 的 `count` / 3；**没有 indices 时**按 POSITION.count / 3
    ///   （非索引网格 —— glTF 允许，实测真实套件里有这种）
    /// </summary>
    public static class GlbReaderPure
    {
        const uint MagicGlb = 0x46546C67;   // "glTF" 小端
        const uint ChunkJson = 0x4E4F534A;
        const uint ChunkBin = 0x004E4942;

        /// <summary>单个 primitive 的计数（与 Unity 版同形，但不含网格数据）。</summary>
        public sealed class Primitive
        {
            public int VertexCount;
            public int TriangleCount;
        }

        /// <summary>
        /// 解析结果。
        ///
        /// ⚠ **字段与属性必须与 Unity 版 `GlbReader.Model` 同名同型** ——
        /// 这是移植的最低要求：调用点（`KitVerifier`）要零改动。
        /// 我第一版把 `Primitives` 写成 `int`、`VertexCount` 写成字段，
        /// 于是 `model.Primitives.Count` 编译失败（CS0428/CS0266）——
        /// 原版里 `Primitives` 是 `List<Primitive>`、`VertexCount` 是**遍历求和的属性**。
        /// </summary>
        public sealed class Model
        {
            public string Name = "";
            public readonly List<Primitive> Primitives = new List<Primitive>();
            public int VertexCount { get { int n = 0; foreach (var p in Primitives) n += p.VertexCount; return n; } }
            public int TriangleCount { get { int n = 0; foreach (var p in Primitives) n += p.TriangleCount; return n; } }
            /// <summary>JSON 里的 `generator`（诊断用）。</summary>
            public string Generator = "";
            /// <summary>是否含 BIN chunk（有顶点数据必然有）。</summary>
            public bool HasBin;

            // ── 包围盒（局部坐标）────────────────────────────────────────────────
            // 【为什么需要】摆房间时要把套件对齐到房间地面：Unity 侧是
            //   房间根节点 = (CenterX, Floor*3.5, CenterZ)，套件部件按**局部坐标原样**摆进去。
            //   要在 Godot 里复刻同样口径，就必须知道套件自己的包围盒（尤其 yMin 是否贴 0）。
            // ⚠ 只累加 POSITION，**不做节点变换** —— 真实套件的节点多为单位变换（实测），
            //   但**如实标注**：若某个 GLB 有非单位节点变换，这里的包围盒会偏。
            public bool HasBounds;
            public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
            public float SizeX => MaxX - MinX;
            public float SizeY => MaxY - MinY;
            public float SizeZ => MaxZ - MinZ;
        }

        /// <summary>
        /// 读一个 .glb。失败时返回 false 并给出**可读原因**（不抛异常 —— 它被门禁调用，
        /// 抛异常会让"这个套件坏了"变成"门禁崩了"，两者要能分开）。
        /// </summary>
        public static bool TryRead(byte[] bytes, out Model model, out string reason)
        {
            model = null; reason = null;
            if (bytes == null || bytes.Length < 20) { reason = "文件太小（< 20 字节）"; return false; }
            if (ReadU32(bytes, 0) != MagicGlb) { reason = "魔数不是 glTF（可能不是 .glb）"; return false; }
            uint version = ReadU32(bytes, 4);
            if (version != 2) { reason = $"glTF 版本是 {version}，本解析器只支持 2"; return false; }
            uint declared = ReadU32(bytes, 8);
            if (declared > bytes.Length) { reason = $"头部声明 {declared} 字节 > 实际 {bytes.Length}"; return false; }

            var m = new Model();
            string json = null;
            byte[] binData = null;   // BIN chunk（POSITION 数据在里面）
            int p = 12;
            // 遍历 chunk（JSON 与 BIN 各一个；顺序按规范是 JSON 在前，但不假设）
            while (p + 8 <= bytes.Length)
            {
                uint len = ReadU32(bytes, p);
                uint type = ReadU32(bytes, p + 4);
                int data = p + 8;
                if (len > (uint)(bytes.Length - data)) { reason = $"chunk 声明长度 {len} 越界"; return false; }
                if (type == ChunkJson) json = Encoding.UTF8.GetString(bytes, data, (int)len);
                else if (type == ChunkBin) { m.HasBin = true; binData = new byte[len]; Array.Copy(bytes, data, binData, 0, (int)len); }
                p = data + (int)len;
                // chunk 按 4 字节对齐
                p = (p + 3) & ~3;
            }
            if (json == null) { reason = "没有 JSON chunk"; return false; }

            var root = MiniJson.AsMap(MiniJson.Parse(json));
            if (root == null) { reason = "JSON 解析结果不是对象"; return false; }

            // accessors：索引 → count / type / bufferView / byteOffset（后两项用于读 POSITION）
            var accCount = new Dictionary<long, long>();
            var accType = new Dictionary<long, string>();
            var accView = new Dictionary<long, long>();
            var accOff = new Dictionary<long, long>();
            if (MiniJson.Get(root, "accessors") is List<object> accessors)
            {
                for (int i = 0; i < accessors.Count; i++)
                {
                    var a = MiniJson.AsMap(accessors[i]);
                    if (a == null) continue;
                    accCount[i] = MiniJson.Get(a, "count") is long c ? c : 0L;
                    accType[i] = MiniJson.Get(a, "type") as string ?? "";
                    accView[i] = MiniJson.Get(a, "bufferView") is long bv ? bv : -1L;
                    accOff[i] = MiniJson.Get(a, "byteOffset") is long bo ? bo : 0L;
                }
            }

            // bufferViews：索引 → (byteOffset, byteLength) —— 用来定位 POSITION 在 BIN 里的位置
            var viewOff = new Dictionary<long, long>();
            if (MiniJson.Get(root, "bufferViews") is List<object> views)
            {
                for (int i = 0; i < views.Count; i++)
                {
                    var v = MiniJson.AsMap(views[i]);
                    if (v == null) continue;
                    viewOff[i] = MiniJson.Get(v, "byteOffset") is long o ? o : 0L;
                }
            }

            if (MiniJson.Get(root, "meshes") is List<object> meshes)
            {
                foreach (var mo in meshes)
                {
                    var mesh = MiniJson.AsMap(mo);
                    if (mesh == null) continue;
                    if (!(MiniJson.Get(mesh, "primitives") is List<object> prims)) continue;
                    foreach (var po in prims)
                    {
                        var prim = MiniJson.AsMap(po);
                        if (prim == null) continue;
                        var primModel = new Primitive();

                        // ⚠ `posIdx` 必须在**外层**声明：它后面还要用于读包围盒。
                        //   我第一版把它写在 `if (... out var posIdx ...)` 里 ⇒ 作用域只在 if 内 ⇒
                        //   后面用时报 `CS0165: Use of unassigned local variable 'posIdx'`。
                        long verts = 0;
                        long posIdx = -1;
                        if (MiniJson.Get(prim, "attributes") is Dictionary<string, object> attrs
                            && attrs.TryGetValue("POSITION", out var posRaw) && posRaw is long pi)
                        {
                            posIdx = pi;
                            if (accCount.TryGetValue(pi, out var pc)) verts = pc;
                        }
                        primModel.VertexCount = (int)verts;

                        if (MiniJson.Get(prim, "indices") is long ii && accCount.TryGetValue(ii, out var ic))
                            primModel.TriangleCount = (int)(ic / 3);
                        else
                            primModel.TriangleCount = (int)(verts / 3);   // 非索引网格
                        m.Primitives.Add(primModel);

                        // ── 累加 POSITION 到包围盒（只认 VEC3 + 有 BIN + 有 bufferView）──────
                        if (binData != null && posIdx >= 0
                            && accType.TryGetValue(posIdx, out var pType) && pType == "VEC3"
                            && accView.TryGetValue(posIdx, out var vIdx) && vIdx >= 0
                            && viewOff.TryGetValue(vIdx, out var vOff))
                        {
                            long baseOff = vOff + accOff[posIdx];
                            long cnt = accCount[posIdx];
                            for (long k = 0; k < cnt; k++)
                            {
                                long o = baseOff + k * 12;      // VEC3 float32 = 12 字节
                                if (o + 12 > binData.Length) break;
                                float x = BitConverter.ToSingle(binData, (int)o);
                                float y = BitConverter.ToSingle(binData, (int)(o + 4));
                                float z = BitConverter.ToSingle(binData, (int)(o + 8));
                                if (!m.HasBounds) { m.MinX = m.MaxX = x; m.MinY = m.MaxY = y; m.MinZ = m.MaxZ = z; m.HasBounds = true; }
                                else
                                {
                                    if (x < m.MinX) m.MinX = x; if (x > m.MaxX) m.MaxX = x;
                                    if (y < m.MinY) m.MinY = y; if (y > m.MaxY) m.MaxY = y;
                                    if (z < m.MinZ) m.MinZ = z; if (z > m.MaxZ) m.MaxZ = z;
                                }
                            }
                        }
                    }
                }
            }

            m.Generator = MiniJson.Get(root, "generator") as string ?? "";
            model = m;
            if (m.Primitives.Count == 0) { reason = "meshes/primitives 为空"; return false; }
            return true;
        }

        public static string Describe(Model m)
        {
            if (m == null) return "(未解析)";
            return string.Format(CultureInfo.InvariantCulture,
                "部件 {0} · 顶点 {1} · 三角形 {2}{3}{4}", m.Primitives.Count, m.VertexCount, m.TriangleCount,
                m.HasBin ? "" : " · **无 BIN chunk（几何数据缺失）**",
                m.HasBounds ? string.Format(CultureInfo.InvariantCulture, " · 包围盒 X[{0:0.00},{1:0.00}] Y[{2:0.00},{3:0.00}] Z[{4:0.00},{5:0.00}]", m.MinX, m.MaxX, m.MinY, m.MaxY, m.MinZ, m.MaxZ) : " · (无 POSITION 数据)");
        }

        static uint ReadU32(byte[] b, int off)
            => (uint)(b[off] | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24));
    }
}
