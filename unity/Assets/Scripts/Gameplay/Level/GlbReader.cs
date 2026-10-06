using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Whisper.Gameplay.Level
{
    /// <summary>
    /// GLB（glTF 2.0 二进制容器）**最小读取器**：只解出"建网格需要的那几样"——
    /// 每个 primitive 的 POSITION / NORMAL / TEXCOORD_0 / indices，以及节点世界变换。
    ///
    /// ## 为什么要自己读 GLB，而不是装一个 glTF 导入器
    /// V9 §19.1 C3 的纪律是**资产零手工导入**（清单驱动、脚本化入库），而本工程还额外禁止
    /// 玩法层引用第三方命名空间（§13.1 / 门禁 C6）。Unity 自带 **不**支持 glTF，官方
    /// `com.unity.cloud.gltfast` 是第三方包 —— 于是有两条路：
    ///   ① 引第三方 glTFFast：违反零第三方纪律，且它进包后的行为无法在本机断言；
    ///   ② 自己读 GLB：本工程的套件是 `tools/gen-kits.mjs` **确定性生成的纯体块**
    ///      （每个 primitive 都是 24 顶点 / 36 索引的立方体，只有 POSITION/NORMAL/UV），
    ///      解析量极小 —— 选 ②。
    ///
    /// ## 纯 C# 纪律（这条决定了本类能不能被真验证）
    /// 本文件**不引用 UnityEngine** → 能进 `native/csharp-verify` 在电脑上真编译真跑，
    /// 于是"GLB 到底能不能被解析、顶点数对不对"是可断言的，而不是等真机才发现。
    /// 顶点变换成 Unity 网格的那一步（Vector3/Mesh）在 <c>KitMeshLibrary</c> 里，属 Unity 侧。
    ///
    /// ## 支持范围与拒绝行为（宁缺勿错）
    ///   · 支持：版本 2、`bufferViews` 无 stride 或 stride 紧凑、accessor 为 float32/uint32/uint16
    ///   · 拒绝（返回 false 并给出原因）：加密/分块缺失/压缩扩展/浮点以外的 POSITION 类型
    ///   · 从不抛异常：资产管线里一次未捕获异常 = 一次构建失败（用户看不到原因）
    /// </summary>
    public static class GlbReader
    {
        /// <summary>一个 primitive 解出来的网格数据（已按 node 世界变换烘焙到模型空间）。</summary>
        public sealed class Primitive
        {
            /// <summary>顶点位置，XYZ 交错（3 float/顶点）。</summary>
            public float[] Positions;
            /// <summary>法线，XYZ 交错；缺失时为空数组。</summary>
            public float[] Normals;
            /// <summary>UV0，UV 交错；缺失时为空数组。</summary>
            public float[] Uvs;
            /// <summary>三角索引（每 3 个一个面）。</summary>
            public int[] Indices;
            /// <summary>
            /// 本 primitive 用的材质下标（对应 <see cref="Model.Materials"/>）；**-1 = 没有材质**。
            /// 为什么给默认值 -1：既有 11 个套件的 GLB 里没有材质，必须让它们的行为**完全不变**
            /// （装配逻辑见到 -1 就照旧用平材质）。
            /// </summary>
            public int MaterialIndex = -1;
            /// <summary>
            /// GLB 里引用本 mesh 的**节点名**（null = 该 mesh 无节点或节点无名）。
            ///
            /// 【为什么需要它 · 2026-10-06】套件 GLB 的节点名就是**部件语义**：
            /// `floor` / `ceiling` / `skirt_*`（墙裙）/ `cornice_*`（顶角线）/ `doorjamb_*`（门套）/
            /// `light_panel`（灯带）/ `pilaster_*`（壁柱）/ `conduit_*`（线管）/ `ceil_beam_*`（顶梁）/
            /// `window_*`（窗）/ `radiator_*`（暖气片）/ `rack_*`（柜体）。
            /// 而**材质名只是 `role_*` 粗分组** —— 实测 `role_trim` 一个分组就吞掉 318 个节点
            /// （墙裙/顶角线/门套/窗套语义完全不同），只按材质名选不出「木地板 vs 金属门框」。
            /// 装配端按本字段选材质族，把「整块平色」变成真材质（用户点名的「上色」）。
            /// </summary>
            public string NodeName;
            /// <summary>顶点数。</summary>
            public int VertexCount => Positions != null ? Positions.Length / 3 : 0;
            /// <summary>面数。</summary>
            public int TriangleCount => Indices != null ? Indices.Length / 3 : 0;
        }

        /// <summary>行主序 4×4 矩阵（够用且不必依赖 UnityEngine.Matrix4x4）。</summary>
        public struct Mat4
        {
            public float M00, M01, M02, M03;
            public float M10, M11, M12, M13;
            public float M20, M21, M22, M23;
            public static Mat4 Identity => new Mat4 { M00 = 1, M11 = 1, M22 = 1 };
            /// <summary>this * other（列向量约定：结果把 other 的变换先作用）。</summary>
            public static Mat4 Mul(in Mat4 a, in Mat4 b) => new Mat4
            {
                M00 = a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20,
                M01 = a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21,
                M02 = a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22,
                M03 = a.M00 * b.M03 + a.M01 * b.M13 + a.M02 * b.M23 + a.M03,
                M10 = a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20,
                M11 = a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21,
                M12 = a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22,
                M13 = a.M10 * b.M03 + a.M11 * b.M13 + a.M12 * b.M23 + a.M13,
                M20 = a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20,
                M21 = a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21,
                M22 = a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22,
                M23 = a.M20 * b.M03 + a.M21 * b.M13 + a.M22 * b.M23 + a.M23,
            };
            /// <summary>变换一个点（含平移）。</summary>
            public void XformPoint(float x, float y, float z, out float ox, out float oy, out float oz)
            {
                ox = M00 * x + M01 * y + M02 * z + M03;
                oy = M10 * x + M11 * y + M12 * z + M13;
                oz = M20 * x + M21 * y + M22 * z + M23;
            }
            /// <summary>变换一条方向（不含平移；只用 3×3 部分，足以覆盖套件的平移/旋转）。</summary>
            public void XformDir(float x, float y, float z, out float ox, out float oy, out float oz)
            {
                ox = M00 * x + M01 * y + M02 * z;
                oy = M10 * x + M11 * y + M12 * z;
                oz = M20 * x + M21 * y + M22 * z;
            }
            /// <summary>由 T·R·S 组装（四元数 xyzw、缩放三维）。</summary>
            public static Mat4 FromTrs(float[] t, float[] q, float[] s)
            {
                float tx = t != null && t.Length >= 3 ? t[0] : 0f, ty = t != null && t.Length >= 3 ? t[1] : 0f, tz = t != null && t.Length >= 3 ? t[2] : 0f;
                float x = q != null && q.Length >= 4 ? q[0] : 0f, y = q != null && q.Length >= 4 ? q[1] : 0f,
                      z = q != null && q.Length >= 4 ? q[2] : 0f, w = q != null && q.Length >= 4 ? q[3] : 1f;
                float sx = s != null && s.Length >= 3 ? s[0] : 1f, sy = s != null && s.Length >= 3 ? s[1] : 1f, sz = s != null && s.Length >= 3 ? s[2] : 1f;

                float xx = x * x, yy = y * y, zz = z * z, xy = x * y, xz = x * z, yz = y * z, wx = w * x, wy = w * y, wz = w * z;
                return new Mat4
                {
                    M00 = (1f - 2f * (yy + zz)) * sx,
                    M01 = (2f * (xy - wz)) * sy,
                    M02 = (2f * (xz + wy)) * sz,
                    M03 = tx,
                    M10 = (2f * (xy + wz)) * sx,
                    M11 = (1f - 2f * (xx + zz)) * sy,
                    M12 = (2f * (yz - wx)) * sz,
                    M13 = ty,
                    M20 = (2f * (xz - wy)) * sx,
                    M21 = (2f * (yz + wx)) * sy,
                    M22 = (1f - 2f * (xx + yy)) * sz,
                    M23 = tz,
                };
            }
        }

        /// <summary>读出的整个 GLB。</summary>
        /// <summary>
        /// 套件材质（glTF 标准 `pbrMetallicRoughness` 的核心三项）。
        /// 出处：glTF 2.0 规范 `materials[].pbrMetallicRoughness`。
        /// **为什么只取这三项**：它们是决定"看起来像什么材料"的主因；
        /// 贴图（baseColorTexture 等）需要 UIImage 资源通道，本仓目前没有，故不假装支持。
        /// </summary>
        public struct KitMaterial
        {
            /// <summary>基色（RGBA，线性 0..1）。glTF 默认 [1,1,1,1]。</summary>
            public float R, G, B, A;
            /// <summary>金属度 0..1。glTF 默认 1.0。</summary>
            public float Metallic;
            /// <summary>粗糙度 0..1。glTF 默认 1.0。</summary>
            public float Roughness;
            /// <summary>材质名（诊断用；glTF 里可选）。</summary>
            public string Name;
            /// <summary>glTF 规范默认值：白、金属 1、粗糙 1。</summary>
            public static KitMaterial Default => new KitMaterial { R = 1f, G = 1f, B = 1f, A = 1f, Metallic = 1f, Roughness = 1f };
        }

        /// <summary>
        /// 一个 glTF 模型（`meshes[]` 的一项 + 它引用的 `materials[]`）。
        ///
        /// 为什么自己带 <see cref="Materials"/> 而不是只留索引：GLB 里的材质是**共享表**，
        /// 而套件加载是逐模型进行的 —— 把解析结果按模型固化下来，
        /// 调用方（<c>KitMeshLibrary</c>）就不必再回头查全局表，也就不会出现"索引越界才发现表没读完"。
        /// </summary>
        public sealed class Model
        {
            public string Name;
            public readonly List<Primitive> Primitives = new List<Primitive>();
            /// <summary>本模型声明的材质（glTF `materials[]`）；空 = 该 GLB 没有材质。</summary>
            public readonly List<KitMaterial> Materials = new List<KitMaterial>();
            public int VertexCount { get { int n = 0; foreach (var p in Primitives) n += p.VertexCount; return n; } }
            public int TriangleCount { get { int n = 0; foreach (var p in Primitives) n += p.TriangleCount; return n; } }
            /// <summary>所有 primitive 的并集包围盒（模型空间，用于与清单 footprint 对照）。</summary>
            public float[] BoundsMin = { float.MaxValue, float.MaxValue, float.MaxValue };
            public float[] BoundsMax = { float.MinValue, float.MinValue, float.MinValue };
        }

        const uint Magic = 0x46546C67;   // "glTF"
        const uint ChunkJson = 0x4E4F534A;
        const uint ChunkBin = 0x004E4942;

        /// <summary>
        /// 解析 GLB 字节。成功返回 true 并填充 <paramref name="model"/>；
        /// 失败返回 false 并把原因写进 <paramref name="reason"/>（**从不抛异常**）。
        /// </summary>
        public static bool TryRead(byte[] glb, out Model model, out string reason)
        {
            model = null;
            reason = null;
            if (!TryReadChunks(glb, out string json, out byte[] bin, out reason)) return false;

            Dictionary<string, object> root;
            try { root = MapOf(MiniJson.Parse(json)); }
            catch (Exception ex) { reason = "JSON 解析失败：" + ex.Message; return false; }

            var meshes = ListOf(MiniJson.GetOrNull(root, "meshes"));
            var accessors = ListOf(MiniJson.GetOrNull(root, "accessors"));
            var views = ListOf(MiniJson.GetOrNull(root, "bufferViews"));
            var nodes = ListOf(MiniJson.GetOrNull(root, "nodes"));
            if (meshes == null || accessors == null || views == null || bin == null)
            {
                reason = "缺少 meshes/accessors/bufferViews/BIN 块（meshes=" + (meshes != null) + " bin=" + (bin != null) + "）";
                return false;
            }

            var m = new Model { Name = "glb" };

            ParseMaterials(root, m);
            // mesh 下标 → 该 mesh 首个使用它的节点（含世界变换）。
            // 为什么必须走这层：套件的每个部件在 glTF 里是"顶点在原点、位置靠 node.translation"，
            // 只读顶点会把 4 根柱子/门框全塌到原点 —— 读得出来不等于建得对。
            var nodeWorld = new Dictionary<int, Mat4>();
            var nodeNames = new Dictionary<int, string>();   // mesh 下标 → 节点名（部件语义，见 Primitive.NodeName）
            BuildNodeWorlds(nodes, nodeWorld, nodeNames);

            for (int meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
            {
                var mesh = MapOf(meshes[meshIndex]);
                var prims = ListOf(MiniJson.GetOrNull(mesh, "primitives"));
                if (prims == null) continue;
                Mat4 world = nodeWorld.TryGetValue(meshIndex, out var w) ? w : Mat4.Identity;
                foreach (var primObj in prims)
                {
                    var prim = MapOf(primObj);
                    var attrs = MapOf(MiniJson.GetOrNull(prim, "attributes"));
                    object posRef = attrs != null ? MiniJson.GetOrNull(attrs, "POSITION") : null;
                    if (posRef == null) { reason = "primitive 缺 POSITION"; return false; }

                    // 记录本 primitive 的材质下标（缺省 -1 = 没有材质，装配端据此走平材质回退）
                    int materialIndex = -1;
                    var matRef = MiniJson.GetOrNull(prim, "material");
                    if (matRef != null)
                    {
                        int mi = IntOf(matRef);
                        if (mi >= 0 && mi < m.Materials.Count) materialIndex = mi;
                    }

                    if (!TryReadFloats(accessors, views, bin, IntOf(posRef), out var positions, out reason)) return false;
                    if (positions.Length % 3 != 0) { reason = "POSITION 元素数不是 3 的倍数"; return false; }
                    // 烘焙节点世界变换（套件的平移在这里生效）
                    for (int i = 0; i < positions.Length; i += 3)
                    {
                        world.XformPoint(positions[i], positions[i + 1], positions[i + 2], out var px, out var py, out var pz);
                        positions[i] = px; positions[i + 1] = py; positions[i + 2] = pz;
                    }

                    float[] normals = Array.Empty<float>();
                    object nrmRef = MiniJson.GetOrNull(attrs, "NORMAL");
                    if (nrmRef != null && !TryReadFloats(accessors, views, bin, IntOf(nrmRef), out normals, out reason)) return false;
                    for (int i = 0; normals.Length > 0 && i < normals.Length; i += 3)
                    {
                        world.XformDir(normals[i], normals[i + 1], normals[i + 2], out var nx, out var ny, out var nz);
                        float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                        if (len > 1e-6f) { normals[i] = nx / len; normals[i + 1] = ny / len; normals[i + 2] = nz / len; }
                    }

                    float[] uvs = Array.Empty<float>();
                    object uvRef = MiniJson.GetOrNull(attrs, "TEXCOORD_0");
                    if (uvRef != null && !TryReadFloats(accessors, views, bin, IntOf(uvRef), out uvs, out reason)) return false;

                    int[] indices = Array.Empty<int>();
                    object idxRef = MiniJson.GetOrNull(prim, "indices");
                    if (idxRef != null && !TryReadIndices(accessors, views, bin, IntOf(idxRef), out indices, out reason)) return false;
                    if (indices.Length > 0 && indices.Length % 3 != 0) { reason = "indices 不是 3 的倍数"; return false; }

                    var p = new Primitive
                    {
                        Positions = positions, Normals = normals, Uvs = uvs, Indices = indices,
                        MaterialIndex = materialIndex,
                        // 节点名（部件语义）——装配端据此选材质族，见 Primitive.NodeName 的说明
                        NodeName = nodeNames.TryGetValue(meshIndex, out var nodeName) ? nodeName : null,
                    };
                    m.Primitives.Add(p);

                    for (int i = 0; i < positions.Length; i += 3)
                        for (int a = 0; a < 3; a++)
                        {
                            float v = positions[i + a];
                            if (v < m.BoundsMin[a]) m.BoundsMin[a] = v;
                            if (v > m.BoundsMax[a]) m.BoundsMax[a] = v;
                        }
                }
            }

            if (m.Primitives.Count == 0) { reason = "没有可用的 primitive"; return false; }
            model = m;
            return true;
        }

        /// <summary>
        /// 校验 GLB 头并拆出 JSON 块与 BIN 块。
        ///
        /// 为什么单独成方法（2026-10-06）：给 <see cref="Primitive.NodeName"/> 补「节点名」后
        /// `TryRead` 涨到 122 行，越过 gate-code C5 的 120 行上限。
        /// 「校验容器格式」与「解析内容」本就是两件事 —— 抽出来让 TryRead 回到可评审长度，
        /// 也让格式校验规则只有一处可改。
        /// </summary>
        static bool TryReadChunks(byte[] glb, out string json, out byte[] bin, out string reason)
        {
            json = null; bin = null; reason = null;
            if (glb == null || glb.Length < 20) { reason = "字节数不足（<20）"; return false; }
            if (ReadU32(glb, 0) != Magic) { reason = "magic 不是 glTF"; return false; }
            uint version = ReadU32(glb, 4);
            if (version != 2) { reason = "只支持 glTF 2.0，实际 " + version; return false; }
            uint total = ReadU32(glb, 8);
            if (total != glb.Length) { reason = "头长度 " + total + " 与实际字节 " + glb.Length + " 不一致"; return false; }

            int off = 12;
            while (off + 8 <= glb.Length)
            {
                uint len = ReadU32(glb, off);
                uint type = ReadU32(glb, off + 4);
                int dataStart = off + 8;
                if (dataStart + (int)len > glb.Length) { reason = "块越界（off=" + off + " len=" + len + "）"; return false; }
                if (type == ChunkJson) json = Encoding.UTF8.GetString(glb, dataStart, (int)len);
                else if (type == ChunkBin) { bin = new byte[len]; Buffer.BlockCopy(glb, dataStart, bin, 0, (int)len); }
                off = dataStart + (int)len;
            }
            if (json == null) { reason = "没有 JSON 块"; return false; }
            return true;
        }

        /// <summary>
        /// 建立 mesh 下标 → 首个引用它的节点世界变换（深度优先遍历场景图，逐层左乘父变换）。
        /// 套件的每个部件都由独立节点带 translation 摆放，不读这一层就会全部塌在原点。
        /// </summary>
        /// <summary>
        /// 解析 glTF 的 <c>materials[]</c> 并填入 <paramref name="m"/>.Materials。
        ///
        /// 为什么单独成方法（2026-10-06）：原先是 TryRead 内联的一大段，而 TryRead 长到 145 行
        /// （gate-code C5 上限 120）。材质解析与读块/校验/拼几何本就是三件事 ——
        /// 单独抽出来既让 TryRead 回到可评审的长度，也让材质规则只有一处可改。
        /// </summary>
        static void ParseMaterials(Dictionary<string, object> root, Model m)
        {
            // ── 材质解析（glTF `materials[]`）────────────────────────────────
            // 为什么在这里做：装配端要按 `primitive.material` 索引取参数；
            // 不读出来，程序化几何之外的模型就永远是"一整块纯色"（本项目既有 11 个套件正是如此）。
            var mats = ListOf(MiniJson.GetOrNull(root, "materials"));
            if (mats != null)
            {
                foreach (var matObj in mats)
                {
                    var mm = MapOf(matObj);
                    var km = KitMaterial.Default;
                    if (mm != null)
                    {
                        km.Name = MiniJson.GetOrNull(mm, "name") as string;
                        var pbr = MapOf(MiniJson.GetOrNull(mm, "pbrMetallicRoughness"));
                        if (pbr != null)
                        {
                            var bc = ListOf(MiniJson.GetOrNull(pbr, "baseColorFactor"));
                            if (bc != null && bc.Count >= 3)
                            {
                                km.R = FloatOf(bc[0]); km.G = FloatOf(bc[1]); km.B = FloatOf(bc[2]);
                                km.A = bc.Count >= 4 ? FloatOf(bc[3]) : 1f;
                            }
                            var mf = MiniJson.GetOrNull(pbr, "metallicFactor");
                            if (mf != null) km.Metallic = FloatOf(mf);
                            var rf = MiniJson.GetOrNull(pbr, "roughnessFactor");
                            if (rf != null) km.Roughness = FloatOf(rf);
                        }
                    }
                    m.Materials.Add(km);
                }
            }
        }

        static void BuildNodeWorlds(List<object> nodes, Dictionary<int, Mat4> outWorld, Dictionary<int, string> outNames)
        {
            if (nodes == null) return;
            var scene = new List<object>();      // 无 scenes 段时按"所有根节点"处理
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = MapOf(nodes[i]);
                var children = ListOf(MiniJson.GetOrNull(n, "children"));
                bool isChild = false;
                foreach (var other in nodes)
                {
                    var oc = ListOf(MiniJson.GetOrNull(MapOf(other), "children"));
                    if (oc == null) continue;
                    foreach (var c in oc) if (IntOf(c) == i) { isChild = true; break; }
                    if (isChild) break;
                }
                if (!isChild) scene.Add(i);
            }
            foreach (var root in scene) Walk(IntOf(root), Mat4.Identity, nodes, outWorld, outNames);
        }

        static void Walk(int index, in Mat4 parent, List<object> nodes, Dictionary<int, Mat4> outWorld, Dictionary<int, string> outNames)
        {
            if (index < 0 || index >= nodes.Count) return;
            var n = MapOf(nodes[index]);
            var local = Mat4.FromTrs(
                FloatArray(MiniJson.GetOrNull(n, "translation"), 3, 0f),
                FloatArray(MiniJson.GetOrNull(n, "rotation"), 4, 0f, 0f, 0f, 1f),
                FloatArray(MiniJson.GetOrNull(n, "scale"), 3, 1f));
            var world = Mat4.Mul(parent, local);

            object meshRef = MiniJson.GetOrNull(n, "mesh");
            if (meshRef != null)
            {
                int mi = IntOf(meshRef);
                if (!outWorld.ContainsKey(mi)) outWorld[mi] = world;   // 首个引用者即该 mesh 的摆放
                if (outNames != null && !outNames.ContainsKey(mi))
                    outNames[mi] = MiniJson.GetOrNull(n, "name") as string;   // 部件名（材质族判据）
            }
            var children = ListOf(MiniJson.GetOrNull(n, "children"));
            if (children != null) foreach (var c in children) Walk(IntOf(c), world, nodes, outWorld, outNames);
        }

        /// <summary>
        /// 空安全的取列表：MiniJson.AsList 对 null 会抛 FormatException（它的语义是"必须是数组"），
        /// 而 GLB 里 "children"/"primitives" 这类键**允许缺失** —— 解析器一旦抛异常就等于构建失败，
        /// 所以这里统一用返回 null 的版本，由调用方按"缺省即空"处理。
        /// </summary>
        static List<object> ListOf(object v) => v as List<object>;

        /// <summary>空安全的取对象。</summary>
        static Dictionary<string, object> MapOf(object v) => v as Dictionary<string, object>;

        /// <summary>空安全的取整数（MiniJson.AsInt 对 null/非数字会抛异常）。</summary>
        static int IntOf(object v, int fallback = 0)
        {
            switch (v)
            {
                case long l: return (int)l;
                case double d: return (int)d;
                case int i2: return i2;
                default: return fallback;
            }
        }

        /// <summary>空安全的取浮点。</summary>
        static float FloatOf(object v, float fallback = 0f)
        {
            switch (v)
            {
                case long l: return l;
                case double d: return (float)d;
                case float f: return f;
                case int i2: return i2;
                default: return fallback;
            }
        }

        /// <summary>空安全的取字符串。</summary>
        static string StringOf(object v) => v as string;
        /// <summary>
        /// 把 JSON 数组读成定长 float 数组；缺失/不足的部分用 <paramref name="defaults"/> 补齐。
        ///
        /// ⚠️ 【踩过的坑，实测定位】调用点写的是 `FloatArray(scale, 3, 1f)` —— `params` 只给了**一个**默认值。
        /// 旧实现 `i &lt; defaults.Length ? defaults[i] : 0f` 于是让 `s[1]=s[2]=0`，
        /// 即 scale=(1,0,0) → 矩阵第三列被乘成 0 → **整个模型的所有 Z 坐标变成 0**
        /// （现象：套件"楼板深度 0.00"、所有部件塌在 z=0 平面上）。
        ///
        /// 修法：默认值不足时**用最后一个默认值补满**（1f → 1,1,1；0f → 0,0,0）。
        /// 这比"按位取 defaults[i]"更符合调用意图：调用方给的是一类分量的默认值，不是逐位表。
        /// </summary>
        static float[] FloatArray(object v, int len, params float[] defaults)
        {
            var list = ListOf(v);
            var result = new float[len];
            for (int i = 0; i < len; i++)
            {
                if (list != null && i < list.Count) { result[i] = FloatOf(list[i]); continue; }
                if (defaults == null || defaults.Length == 0) { result[i] = 0f; continue; }
                result[i] = i < defaults.Length ? defaults[i] : defaults[defaults.Length - 1];
            }
            return result;
        }

        /// <summary>读一个 float accessor（VEC2/VEC3/VEC4，componentType=5126）。</summary>
        static bool TryReadFloats(List<object> accessors, List<object> views, byte[] bin, int accIndex, out float[] data, out string reason)
        {
            data = null; reason = null;
            if (!TryAccessor(accessors, views, bin, accIndex, out var acc, out var view, out var start, out reason)) return false;
            int comps = ComponentCount(StringOf(MiniJson.GetOrNull(acc, "type")));
            if (comps == 0) { reason = "accessor.type 非法"; return false; }
            int compType = IntOf(MiniJson.GetOrNull(acc, "componentType"));
            if (compType != 5126) { reason = "POSITION/NORMAL/UV 必须是 float32(5126)，实际 " + compType; return false; }
            int count = IntOf(MiniJson.GetOrNull(acc, "count"));
            int need = count * comps * 4;
            int stride = view.TryGetValue("byteStride", out var sv) ? IntOf(sv) : 0;
            if (stride != 0 && stride != comps * 4) { reason = "不支持非紧凑 stride=" + stride; return false; }
            if (start + need > bin.Length) { reason = "accessor 越界"; return false; }
            data = new float[count * comps];
            Buffer.BlockCopy(bin, start, data, 0, need);
            return true;
        }

        /// <summary>读一个索引 accessor（componentType=5123/5125）。</summary>
        static bool TryReadIndices(List<object> accessors, List<object> views, byte[] bin, int accIndex, out int[] data, out string reason)
        {
            data = null; reason = null;
            if (!TryAccessor(accessors, views, bin, accIndex, out var acc, out _, out var start, out reason)) return false;
            int compType = IntOf(MiniJson.GetOrNull(acc, "componentType"));
            int count = IntOf(MiniJson.GetOrNull(acc, "count"));
            data = new int[count];
            if (compType == 5125)          // uint32
            {
                if (start + count * 4 > bin.Length) { reason = "索引越界"; return false; }
                for (int i = 0; i < count; i++) data[i] = (int)ReadU32(bin, start + i * 4);
            }
            else if (compType == 5123)     // uint16
            {
                if (start + count * 2 > bin.Length) { reason = "索引越界"; return false; }
                for (int i = 0; i < count; i++) data[i] = bin[start + i * 2] | (bin[start + i * 2 + 1] << 8);
            }
            else { reason = "索引必须是 uint16/uint32，实际 " + compType; return false; }
            return true;
        }

        static bool TryAccessor(List<object> accessors, List<object> views, byte[] bin, int index,
            out Dictionary<string, object> accessor, out Dictionary<string, object> view, out int start, out string reason)
        {
            accessor = null; view = null; start = 0; reason = null;
            if (index < 0 || index >= accessors.Count) { reason = "accessor 下标越界：" + index; return false; }
            accessor = MapOf(accessors[index]);
            int viewIndex = IntOf(MiniJson.GetOrNull(accessor, "bufferView"));
            if (viewIndex < 0 || viewIndex >= views.Count) { reason = "bufferView 下标越界：" + viewIndex; return false; }
            view = MapOf(views[viewIndex]);
            int viewOffset = view.TryGetValue("byteOffset", out var vo) ? IntOf(vo) : 0;
            int accOffset = accessor.TryGetValue("byteOffset", out var ao) ? IntOf(ao) : 0;
            start = viewOffset + accOffset;
            if (start < 0 || start > bin.Length) { reason = "accessor 起始越界"; return false; }
            return true;
        }

        static int ComponentCount(string type)
        {
            switch (type)
            {
                case "SCALAR": return 1;
                case "VEC2": return 2;
                case "VEC3": return 3;
                case "VEC4": return 4;
                default: return 0;
            }
        }

        static uint ReadU32(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
    }
}
