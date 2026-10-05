using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Whisper.Gameplay.Level
{
    /// <summary>
    /// 套件（kit）网格库：把包内的 <c>Kits/&lt;id&gt;.glb</c> 读成 Unity 网格。
    ///
    /// ## 为什么需要它（本项目的头号失效模式）
    /// 5 个套件 GLB 早已生成、过门禁、进清单，但**没有任何代码引用**它们 —— Unity 打包时
    /// 不含任何引用它们的资产，真机看到的全是程序化方块。历史上同类失效模式栽过四次
    /// （几何层 / 内容管线 / 怪物实例化 / 理智系统），判定标准是"真机截屏里有没有它"。
    ///
    /// ## 加载路径：StreamingAssets + File.ReadAllBytes（不是 Resources）
    /// 【2026-10-04 实测踩坑】最初把 GLB 放进 `Assets/Resources/Kits/`，看似最稳（Resources
    /// 无条件进包）——但 Unity 对 `.glb` 用 **ModelImporter** 导入，而 Unity 原生不支持 glTF：
    /// 结果那个资产既没有网格，**也不是文本资产**，按文本资产去取**恒为 null**。
    /// 更隐蔽的是：在构建产物里搜 `Kits/`/套件名**能搜到**（那是清单 JSON 里的路径字符串），
    /// 差点被当成"已进包"的假绿。
    /// 改成 StreamingAssets 后：文件不经导入器、原始字节进包，运行时直接读。
    ///
    /// ## 为什么自己解析 GLB 而不用 glTF 导入器
    /// Unity 官方 gltfast 是第三方包 → 违反 §13.1/C6 零第三方纪律；而套件是
    /// `tools/gen-kits.mjs` 生成的纯体块，用纯 C# 的 <see cref="GlbReader"/> 完全够用，
    /// 且**本机可断言**（见 native/csharp-verify [11]）。
    ///
    /// ## 失败行为
    /// 读不到就返回 null（**不抛异常**）：套件缺失时关卡仍要能起来（退回程序化体块），
    /// 但 <see cref="LastProblem"/> 与 <see cref="KitVerifier.Summary"/> 会留下确切原因。
    /// </summary>
    public static class KitMeshLibrary
    {
        static readonly Dictionary<string, Mesh[]> _cache = new Dictionary<string, Mesh[]>(StringComparer.Ordinal);
        /// <summary>套件材质表缓存（与 <see cref="_cache"/> 同源同寿命）。</summary>
        static readonly Dictionary<string, GlbReader.KitMaterial[]> _matCache = new Dictionary<string, GlbReader.KitMaterial[]>(StringComparer.Ordinal);
        /// <summary>「部件 → 材质下标」缓存（与 <see cref="GetParts"/> **同序**；-1 = 无材质）。</summary>
        static readonly Dictionary<string, int[]> _partMatCache = new Dictionary<string, int[]>(StringComparer.Ordinal);
        static string _rootDir;

        /// <summary>Resources 下的套件目录（与 tools/gen-kit-resources.mjs 的落点对应；扩展名为 .bytes）。</summary>
        public const string ResourceDirInResources = "Kits/";

        /// <summary>
        /// StreamingAssets 下的套件目录（原始文件；桌面端与产物取证走这条）。
        /// </summary>
        public static string RootDir
        {
            get
            {
                if (string.IsNullOrEmpty(_rootDir))
                    _rootDir = Path.Combine(Application.streamingAssetsPath, KitVerifier.KitDirInPackage);
                return _rootDir;
            }
        }

        /// <summary>最近一次失败原因（null = 没失败）；诊断与门禁用。</summary>
        public static string LastProblem { get; private set; }

        /// <summary>已构建的套件数（诊断用：>0 才说明真的读进来了）。</summary>
        public static int LoadedCount => _cache.Count;

        /// <summary>清缓存（关卡重建 / PlayMode 用例之间隔离）。</summary>
        public static void Clear() { _cache.Clear(); _matCache.Clear(); _partMatCache.Clear(); LastProblem = null; }

        /// <summary>
        /// 取套件的**全部部件网格**（无则返回 null）。同一 id 只构建一次。
        ///
        /// 为什么按部件返回而不是合并：套件部件语义不同（楼板 / 天花板灯槽 / 立柱 / 门框），
        /// 合并后无法分别上色与摆放（实测：GLB 顶点色是白的，合并后整块白色）。
        /// </summary>
        public static Mesh[] GetParts(string kitId)
        {
            if (string.IsNullOrEmpty(kitId)) { LastProblem = "kitId 为空"; return null; }
            if (_cache.TryGetValue(kitId, out var cached)) return cached;

            byte[] bytes = LoadBytes(kitId);
            if (bytes == null) return null;   // LastProblem 已在 LoadBytes 里写好

            if (!GlbReader.TryRead(bytes, out var model, out var reason))
            {
                LastProblem = $"套件 {kitId} 解析失败：{reason}";
                return null;
            }

            var parts = new Mesh[model.Primitives.Count];
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = BuildMesh($"{kitId}#{i}", model.Primitives[i]);
                if (parts[i] == null) { LastProblem = $"套件 {kitId} 部件 {i} 建网格失败"; return null; }
            }
            _cache[kitId] = parts;
            // 顺手把材质表与"部件→材质下标"记下来（同一次解析，不重复读盘/解析）
            // 不引入 System.Linq（本文件未 import 它）→ 显式拷贝，最小改动
            var matArr = new GlbReader.KitMaterial[model.Materials.Count];
            for (int mi = 0; mi < matArr.Length; mi++) matArr[mi] = model.Materials[mi];
            _matCache[kitId] = matArr;
            var idx = new int[parts.Length];
            for (int i = 0; i < idx.Length; i++) idx[i] = model.Primitives[i].MaterialIndex;
            _partMatCache[kitId] = idx;
            return parts;
        }

        /// <summary>
        /// 取套件字节。**两条路，各有存在的理由**：
        ///
        /// ① `Resources/Kits/&lt;id&gt;.glb.bytes`（首选 · 平台无关）
        ///    扩展名故意不是 `.glb`：`.glb` 会被 Unity 用 **ModelImporter** 当模型导入（Unity 原生
        ///    不支持 glTF）→ 既没网格也不是文本资产，按文本资产取恒为 null。`.bytes` 会被当
        ///    **二进制文本资产**导入，用「带类型参数的 Resources 文本资产加载」在桌面/Android/iOS 行为一致 ——
        ///    这是 Android 唯一走得通的路：Android 的 StreamingAssets 在 APK 内部，**不能用 File API
        ///    直接读**（官方手册明说），只能用 UnityWebRequest 走 `jar:file://…!/assets/…`，
        ///    而那是本工程 C6 禁入的第三方命名空间。
        ///
        ///    ⚠️ **Resources 的加载路径不含扩展名**（实测：按 `Kits/hall_main.glb` 这个名字取，
        ///    命中的正是磁盘上的 `Assets/Resources/Kits/hall_main.glb.bytes` —— 只剥掉最后一个扩展名）。
        ///    这条我连猜错两次，最后靠"把 Resources 下的资产**全部枚举出来**"才确定（见提交说明），
        ///    所以写死在这里：
        ///    **路径写成 `Kits/&lt;id&gt;.glb`，不要写 `Kits/&lt;id&gt;.glb.bytes`**。
        ///
        /// ② `StreamingAssets/Kits/&lt;id&gt;.glb`（兜底 · 原始文件）
        ///    桌面端与**构建产物取证**（tools/verify-packed-kits.mjs 直接读文件）走这条。
        /// </summary>
        static byte[] LoadBytes(string kitId)
        {
            // ① Resources 里的二进制文本资产（路径不含扩展名，见上方说明）
            var asset = Resources.Load<TextAsset>(ResourceDirInResources + kitId + ".glb");
            if (asset != null && asset.bytes != null && asset.bytes.Length > 0) return asset.bytes;

            // ② StreamingAssets 原始文件
            string file = Path.Combine(RootDir, kitId + ".glb");
            if (File.Exists(file))
            {
                try { return File.ReadAllBytes(file); }
                catch (Exception ex) { LastProblem = $"套件 {kitId} 读取失败：{ex.Message}"; return null; }
            }

            LastProblem = $"套件 {kitId} 不在包内（Resources/Kits/{kitId}.glb[.bytes] 与 {file} 都没有）";
            return null;
        }

        /// <summary>单部件版本（只需要"有没有"或取第 0 个时用）。</summary>
        /// <summary>
        /// 每个部件用的材质下标（与 <see cref="GetParts"/> **同序**）；-1 = 该部件没有材质。
        /// 装配端用法：`var parts = GetParts(id); var mi = GetPartMaterials(id);`
        /// 然后 `parts[i]` 配 `GetMaterials(id)[mi[i]]`。
        /// </summary>
        public static int[] GetPartMaterials(string kitId)
        {
            if (string.IsNullOrEmpty(kitId)) return null;
            if (_partMatCache.TryGetValue(kitId, out var cached)) return cached;
            // 未解析过 → 借 GetParts 触发一次解析（它会把材质一并缓存）
            if (GetParts(kitId) == null) return null;
            return _partMatCache.TryGetValue(kitId, out var again) ? again : null;
        }

        /// <summary>该套件声明的材质表（可能为空 = GLB 里没有材质，装配端应走平材质回退）。</summary>
        public static GlbReader.KitMaterial[] GetMaterials(string kitId)
        {
            if (string.IsNullOrEmpty(kitId)) return null;
            if (_matCache.TryGetValue(kitId, out var cached)) return cached;
            if (GetParts(kitId) == null) return null;
            return _matCache.TryGetValue(kitId, out var again) ? again : null;
        }

        /// <summary>该套件是否带材质（快速判断：装配端据此决定走材质分支还是平材质回退）。</summary>
        public static bool HasMaterials(string kitId)
        {
            var mats = GetMaterials(kitId);
            return mats != null && mats.Length > 0;
        }

        public static Mesh Get(string kitId)
        {
            var parts = GetParts(kitId);
            return parts != null && parts.Length > 0 ? parts[0] : null;
        }

        /// <summary>
        /// 包内套件自检：按清单加载全部套件并汇报（HUD / 日志 / 构建自检共用同一口径）。
        /// 这是"套件真的在产品里"的**运行时**证据——不是靠仓库里有没有文件来推断。
        /// </summary>
        public static string SelfTest()
        {
            var manifest = Resources.Load<TextAsset>("Data/asset-manifest");
            if (manifest == null) { LastProblem = "Resources/Data/asset-manifest 不在包里"; return LastProblem; }
            var loads = KitVerifier.LoadAll(RootDir, manifest.text);
            string summary = KitVerifier.Summary(loads);
            // 【2026-10-04 放宽】原为 `!summary.StartsWith("套件 5/5")` —— 把"恰好 5 个"写死。
            // 为什么改：套件数会随房型种类增长（一个套件被 4 种尺寸的房间复用时，单一尺寸的
            // 楼板/天花板必然缺口或悬挑）。判据改为**与条数无关**：只要没有任何一个套件加载失败即可。
            // `KitVerifier.Summary` 的失败形态是 `… · 失败：<id>(<原因>)`；正常形态不含"失败"。
            if (summary.Contains("失败", StringComparison.Ordinal)) LastProblem = summary;
            return summary;
        }

        /// <summary>
        /// 把**单个** GLB 部件建成 Unity 网格（顶点/UV/索引）。
        /// 法线交给 <c>RecalculateNormals</c>：套件部件是轴对齐方块，重算结果与自带法线一致。
        /// </summary>
        static Mesh BuildMesh(string name, GlbReader.Primitive p)
        {
            int vcount = p.VertexCount;
            if (vcount == 0 || p.Indices.Length == 0) return null;

            var vertices = new List<Vector3>(vcount);
            var uvs = new List<Vector2>(vcount);
            for (int i = 0; i < vcount; i++)
                vertices.Add(new Vector3(p.Positions[i * 3], p.Positions[i * 3 + 1], p.Positions[i * 3 + 2]));
            for (int i = 0; i < vcount; i++)
                uvs.Add(p.Uvs.Length >= (i + 1) * 2 ? new Vector2(p.Uvs[i * 2], p.Uvs[i * 2 + 1]) : Vector2.zero);

            var mesh = new Mesh { name = "Kit_" + name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(new List<int>(p.Indices), 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
