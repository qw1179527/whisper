using System;
using System.Collections.Generic;
using UnityEngine;

namespace Whisper.Runtime
{
    /// <summary>
    /// Blender 产出的模型库（`Resources/Models/**/*.glb.bytes` → Unity Mesh + GameObject）。
    ///
    /// ## 为什么用"每部件一个文件"而不是"一个大 GLB + 按名拆"
    /// `GlbReader.Primitive` 只暴露 `Positions/Normals/Uvs/Indices` —— **没有节点名、没有材质索引**
    /// （节点世界变换已烘进顶点）。而我们要**按部件名寻址**：驱动水银柱动画（`GEO-ThermoMercury`）、
    /// 切换红/白眼（`GEO-GhostEyeRed*` / `White*`）、把手/道具挂到小臂（`GEO-PlayerArmR`）。
    /// 两条路：① 改 `GlbReader` 加节点名解析（它是 build-render 的文件，且要动它的结构）
    /// ② **每个部件单独导一个 GLB**，文件名即部件名 → 一个文件一个物体，`Primitives[0]` 就是它。
    /// 选 ②：**零改动、零歧义**，加载时不需要任何"在集合里按名找"的逻辑。
    ///
    /// ## 目录约定
    /// ```
    /// Resources/Models/player/GEO-PlayerArmR.glb.bytes   ← 部件（split 导出）
    /// Resources/Models/ghost/GEO-GhostEyeRedIrisL.glb.bytes
    /// Resources/Models/player.glb.bytes                  ← 整模（合并导出，主界面展示用）
    /// ```
    /// 路径**省略扩展名**（本工程约定：只剥最后一层扩展名），
    /// 所以部件文件的加载路径是 `Models/player/GEO-PlayerArmR.glb`。
    ///
    /// ## 材质
    /// `GlbReader` 不解析材质，所以材质**由本类按命名约定**给（这是刻意的：自发光是这几件道具的关键，
    /// 而扁平光照的着色器没有 emissive 通道 —— 见 `MaterialFor` 的注释）。
    ///
    /// ⚠ **导出后必须跑 `node tools/verify-models.mjs`**：Blender 对 `hiddenRender=true` 的物体会
    /// **静默导出 0 顶点**（本轮实测：6 个白眼部件全是 0.1KB 空几何）。只看"导出成功"会把空模型当资产交出去。
    /// </summary>
    public static class ModelLibrary
    {
        /// <summary>已知模型（目录名 = 模型 id）。</summary>
        /// <summary>可整体实例化的模型 id。
        /// 说明：player/ghost 是**按部件拼装**的旧模型；ghostbody 系列是新的**整块人形**（元球生成）。
        /// 主界面与对局都应优先用 ghostbody（见 GhostModelPool）。</summary>
        public static readonly string[] KnownIds = { "player", "ghost", "ghostbody" };

        /// <summary>部件名清单（与 Blender 里的对象名一致；导出脚本逐个导出，故这里就是文件名）。</summary>
        public static readonly Dictionary<string, string[]> Parts = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["player"] = new[]
            {
                "GEO-PlayerTorso", "GEO-PlayerHead", "GEO-PlayerNeck",
                "GEO-PlayerArmL", "GEO-PlayerArmR",
                "GEO-PlayerLegL", "GEO-PlayerLegR",
                "GEO-PlayerFootL", "GEO-PlayerFootR",
            },
            ["ghost"] = new[]
            {
                "GEO-GhostTorso", "GEO-GhostHead", "GEO-GhostArmL", "GEO-GhostArmR",
                "GEO-GhostEyeSocketL", "GEO-GhostEyeSocketR",
                "GEO-GhostEyeRedScleraL", "GEO-GhostEyeRedIrisL", "GEO-GhostEyeRedPupilL",
                "GEO-GhostEyeRedScleraR", "GEO-GhostEyeRedIrisR", "GEO-GhostEyeRedPupilR",
                "GEO-GhostEyeWhiteScleraL", "GEO-GhostEyeWhiteIrisL", "GEO-GhostEyeWhitePupilL",
                "GEO-GhostEyeWhiteScleraR", "GEO-GhostEyeWhiteIrisR", "GEO-GhostEyeWhitePupilR",
            },
        };

        /// <summary>道具：三件合并导出的**同一个文件**里的三个 x 偏移组（见 `PropOffsets`）。</summary>
        public static readonly string[] PropIds = { "dotsProjector", "spiritBox", "thermometer" };

        static readonly Dictionary<string, Mesh> _meshCache = new Dictionary<string, Mesh>(StringComparer.Ordinal);
        static readonly Dictionary<string, Material> _matCache = new Dictionary<string, Material>(StringComparer.Ordinal);

        /// <summary>最近一次失败原因（进 HUD/日志，不静默）。</summary>
        public static string LastProblem { get; private set; }
        /// <summary>已成功加载的部件网格数（自检用）。</summary>
        public static int LoadedMeshCount => _meshCache.Count;

        public static void Clear() { _meshCache.Clear(); _matCache.Clear(); LastProblem = null; }

        // ── 部件级 ────────────────────────────────────────────────────────────

        /// <summary>取一个部件的网格（首次加载后缓存）。失败返回 null 并写 `LastProblem`。</summary>
        public static Mesh GetPartMesh(string modelId, string partName)
        {
            string key = modelId + "/" + partName;
            if (_meshCache.TryGetValue(key, out var cached)) return cached;

            var asset = Resources.Load<TextAsset>("Models/" + key + ".glb");
            if (asset == null)
            {
                LastProblem = $"找不到 Models/{key}.glb（split 导出缺失，或 .bytes 未随包）";
                return null;
            }

            if (!Whisper.Gameplay.Level.GlbReader.TryRead(asset.bytes, out var model, out string reason))
            {
                LastProblem = $"Models/{key}.glb 解析失败：{reason}";
                return null;
            }
            if (model.Primitives.Count == 0)
            {
                LastProblem = $"Models/{key}.glb **空几何**（0 primitive）—— 很可能是 Blender 侧对象被隐藏导致静默跳过导出";
                return null;
            }

            var mesh = BuildMesh(key, model.Primitives[0]);
            _meshCache[key] = mesh;
            LastProblem = null;
            return mesh;
        }

        /// <summary>
        /// 实例化一个部件。`parent` 为 null 时它就是根。
        /// 部件的**世界位置已烘在顶点里**（Blender 导出时 `export_apply=True` 但保留了对象变换），
        /// 所以挂到父节点时要**扣掉部件自身原点**才能当"局部零件"用 —— 见 `PivotOffset`。
        /// </summary>
        public static GameObject InstantiatePart(string modelId, string partName, Transform parent, bool subtractPivot = true)
        {
            var mesh = GetPartMesh(modelId, partName);
            if (mesh == null) return null;

            var go = new GameObject(partName);
            if (parent != null) go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mat = MaterialFor(partName);
            if (mat != null) mr.sharedMaterial = mat;

            if (subtractPivot && PartPivot.TryGetValue(partName, out var pivot))
                go.transform.localPosition = -pivot;
            return go;
        }

        /// <summary>
        /// 部件原点（Blender 里的对象位置）。用于"把部件当零件挂到别的坐标系"时扣掉自身偏移 ——
        /// 例如把手挂到小臂末端：`InstantiatePart("player","GEO-PlayerArmR", 手部节点, subtractPivot:true)`。
        /// 数值取自 Blender 场景（`blender_object_info` 实测），改模型时要同步。
        /// </summary>
        public static readonly Dictionary<string, Vector3> PartPivot = new Dictionary<string, Vector3>(StringComparer.Ordinal)
        {
            ["GEO-PlayerTorso"] = new Vector3(0f, 0f, 1.02f),
            ["GEO-PlayerHead"] = new Vector3(0f, 0f, 1.715f),
            ["GEO-PlayerNeck"] = new Vector3(0f, 0f, 1.60f),
            ["GEO-PlayerArmL"] = new Vector3(-0.245f, 0.01f, 1.55f),
            ["GEO-PlayerArmR"] = new Vector3(0.245f, 0.01f, 1.55f),
            ["GEO-PlayerLegL"] = new Vector3(-0.095f, 0.005f, 0.88f),
            ["GEO-PlayerLegR"] = new Vector3(0.095f, 0.005f, 0.88f),
            ["GEO-PlayerFootL"] = new Vector3(-0.095f, -0.03f, 0.035f),
            ["GEO-PlayerFootR"] = new Vector3(0.095f, -0.03f, 0.035f),
            ["GEO-GhostTorso"] = new Vector3(0f, 0f, 1.06f),
            ["GEO-GhostHead"] = new Vector3(0f, 0f, 1.72f),
            ["GEO-GhostArmL"] = new Vector3(-0.255f, 0.005f, 1.60f),
            ["GEO-GhostArmR"] = new Vector3(0.255f, 0.005f, 1.60f),
            ["GEO-GhostEyeSocketL"] = new Vector3(-0.056f, -0.098f, 1.74f),
            ["GEO-GhostEyeSocketR"] = new Vector3(0.056f, -0.098f, 1.74f),
            ["GEO-GhostEyeRedScleraL"] = new Vector3(-0.056f, -0.144f, 1.74f),
            ["GEO-GhostEyeRedScleraR"] = new Vector3(0.056f, -0.144f, 1.74f),
            ["GEO-GhostEyeRedIrisL"] = new Vector3(-0.056f, -0.159f, 1.74f),
            ["GEO-GhostEyeRedIrisR"] = new Vector3(0.056f, -0.159f, 1.74f),
            ["GEO-GhostEyeRedPupilL"] = new Vector3(-0.056f, -0.1665f, 1.74f),
            ["GEO-GhostEyeRedPupilR"] = new Vector3(0.056f, -0.1665f, 1.74f),
            ["GEO-GhostEyeWhiteScleraL"] = new Vector3(-0.056f, -0.144f, 1.74f),
            ["GEO-GhostEyeWhiteScleraR"] = new Vector3(0.056f, -0.144f, 1.74f),
            ["GEO-GhostEyeWhiteIrisL"] = new Vector3(-0.056f, -0.159f, 1.74f),
            ["GEO-GhostEyeWhiteIrisR"] = new Vector3(0.056f, -0.159f, 1.74f),
            ["GEO-GhostEyeWhitePupilL"] = new Vector3(-0.056f, -0.1665f, 1.74f),
            ["GEO-GhostEyeWhitePupilR"] = new Vector3(0.056f, -0.1665f, 1.74f),
        };

        /// <summary>实例化整只鬼或整个玩家（用 `Parts` 里列的全部部件，相对位置由各自 pivot 还原）。</summary>
        /// <summary>
        /// 资源路径自检：把一个路径的**几种可能写法**逐个试，返回"哪个真的能加载"。
        /// 为什么需要：真机上 `Resources.Load` 失败时，我无法用 adb 去翻 resources.assets，
        /// 只能让程序自己把结论算出来 —— 这比我再改一版构建去猜要快得多，也符合"失败必须可见"。
        /// </summary>
        public static string ProbeResourcePaths(string resPathNoExt)
        {
            var sb = new System.Text.StringBuilder();
            string dir = resPathNoExt;
            string leaf = resPathNoExt;
            int slash = resPathNoExt.LastIndexOf('/');
            if (slash > 0) { dir = resPathNoExt.Substring(0, slash); leaf = resPathNoExt.Substring(slash + 1); }

            string[] candidates =
            {
                resPathNoExt,                          // 原样
                resPathNoExt + ".bytes",               // 补 .bytes
                dir + "/" + leaf + ".bytes",           // 同上（等价，留作对照）
                leaf,                                  // 只给文件名（Resources.Load 支持递归查找）
                dir + "/" + leaf + ".glb",             // 补 .glb
                resPathNoExt.Replace("/", "."),        // 点号写法
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                var a = Resources.Load<TextAsset>(candidates[i]);
                sb.Append(candidates[i]).Append(a != null ? "=OK " : "=NO ");
            }
            // 再列一次整个 Models/ghostbody 下能加载到的东西（用 Resources.LoadAll 一次性问清）
            var all = Resources.LoadAll("Models/ghostbody");
            sb.Append("· 该目录 LoadAll=").Append(all != null ? all.Length : 0);
            return sb.ToString();
        }

        /// <summary>加载**单文件整模型**（如 Resources/Models/ghostbody/GEO-GhostBody_male_lanky.glb）。
        /// 与 InstantiateWhole 的区别：那条走 Parts 表按部件拼；这条直接读一个 glb。</summary>
        public static GameObject InstantiateSingleFile(string resPathNoExt, Transform parent, string name)
        {
            var asset = Resources.Load<TextAsset>(resPathNoExt);
            if (asset == null)
            {
                // 【诊断】只报"找不到 X"没有信息量；必须报"X 不行，那 Y / Z 行不行"。
                // 真机 HUD 会把这行显示出来（release 下 Debug.Log 不可靠，HUD 才是取证通道）。
                LastProblem = "找不到 " + resPathNoExt + " · 自检 " + ProbeResourcePaths(resPathNoExt);
                return null;
            }
            if (!Whisper.Gameplay.Level.GlbReader.TryRead(asset.bytes, out var model, out var reason))
            { LastProblem = "解析失败 " + reason; return null; }
            var root = new GameObject(string.IsNullOrEmpty(name) ? "Model" : name);
            if (parent != null) root.transform.SetParent(parent, false);
            for (int i = 0; i < model.Primitives.Count; i++)
            {
                // 复用 BuildMesh：它已经处理了 GlbReader 的 float[] → Vector3[]/Vector2[] 转换、
                // 法线缺失时 Recalculate、UV 长度校验、索引设置与边界重算。
                // 我第一次在 InstantiateSingleFile 里重新写了一遍，漏了转换 → 真 Unity 报 CS1503（float[] → List<Vector3>）。
                var mesh = BuildMesh(name + "#" + i, model.Primitives[i]);
                var go = new GameObject("Part" + i, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(root.transform, false);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                go.GetComponent<MeshRenderer>().sharedMaterial = MaterialFor("GEO-GhostTorso");   // 参数是**部件名**（MaterialFor(string partName)），不是 modelId
            }
            return root;
        }

        public static GameObject InstantiateWhole(string modelId, Transform parent)
        {
            if (!Parts.TryGetValue(modelId, out var names)) { LastProblem = $"未知模型 {modelId}"; return null; }
            var root = new GameObject(modelId);
            if (parent != null) root.transform.SetParent(parent, false);
            int ok = 0;
            foreach (var n in names)
            {
                var go = new GameObject(n);
                go.transform.SetParent(root.transform, false);
                var mesh = GetPartMesh(modelId, n);
                if (mesh == null) continue;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = MaterialFor(n);
                // **整模组装**时部件要保持自己的世界位置 → 直接用 pivot 作为局部位置
                if (PartPivot.TryGetValue(n, out var pivot)) go.transform.localPosition = pivot;
                ok++;
            }
            if (ok == 0) return null;
            return root;
        }

        /// <summary>
        /// 按部件名给材质。
        ///
        /// ## 为什么在这里重建材质（而不是从 GLB 读）
        /// `GlbReader` **不解析材质**（套件 GLB 只有 POSITION/NORMAL/UV），所以材质必须由代码按约定给。
        /// ## 自发光（关键）
        /// 液晶屏 / 绿色激光 / 水银柱这三类**必须在暗场里自己亮**，否则在恐怖场景里根本看不见。
        /// 自研 Lit 着色器**没有 emissive 通道**，所以这里用**颜色提亮 + 不做雾衰减**来近似
        /// （`_WhisperFogOff` 是该着色器已有的全局开关）。真正的 emissive pass 由 build-render 加
        /// （它已确认要加 `_WhisperEmission`）。
        /// </summary>
        static Material MaterialFor(string partName)
        {
            string kind = MaterialKindOf(partName);
            if (_matCache.TryGetValue(kind, out var cached)) return cached;

            // ── 优先用 **PBR 着色器**（用户永久约束 §3：「建模上色都不行」）──
            // 为什么换成 PBR：旧的 `Whisper/UnlitColor` 只有 `albedo × (环境 + 主光×NdotL)`，
            // 即"Lambert 平涂"——金属不像金属、布不像布，再好的建模也是纯色塑料感。
            // 新着色器补上金属度/粗糙度/GGX 高光/法线/遮蔽/细节，并配合
            // `ProceduralTextures` 生成的贴图（程序化、零资产、确定性）。
            //
            // 回退链（**每一级都必须存在**，否则真机是整屏品红或全黑）：
            //   Whisper/LitPbr → Whisper/UnlitColor → 报错并返回 null
            var shader = Shader.Find(PbrShaderName);
            if (shader == null) shader = Shader.Find(Whisper.Gameplay.Level.LevelBuilder.UnlitShaderName);
            if (shader == null) shader = Shader.Find("Whisper/UnlitColor");
            if (shader == null) { LastProblem = "找不到任何自研着色器（LitPbr / UnlitColor）"; return null; }

            var mat = new Material(shader) { name = "ModelMat_" + kind };
            Color c = ColorFor(kind);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);

            // 只在 PBR 着色器上设材质族参数（UnlitColor 没有这些属性，设了也是白设）
            if (mat.HasProperty("_Metallic")) ApplyFamily(mat, kind);

            _matCache[kind] = mat;
            return mat;
        }

        /// <summary>PBR 着色器名（与 Assets/Resources/Shaders/WhisperLitPbr.shader 里的 Shader 名逐字一致）。</summary>
        public const string PbrShaderName = "Whisper/LitPbr";

        /// <summary>把材质族（贴图 + PBR 参数）套到材质上。</summary>
        static void ApplyFamily(Material mat, string kind)
        {
            var fam = FamilyFor(kind);
            var set = Whisper.Gameplay.Render.ProceduralTextures.Get(fam);
            if (set == null) return;
            if (mat.HasProperty("_DetailTex")) mat.SetTexture("_DetailTex", set.Detail);
            if (mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", set.Normal);
            if (mat.HasProperty("_OcclusionMap")) mat.SetTexture("_OcclusionMap", set.Occlusion);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", set.Metallic);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", set.Glossiness);
            if (mat.HasProperty("_DetailScale")) mat.SetFloat("_DetailScale", set.DetailScale);
            if (mat.HasProperty("_DetailStrength")) mat.SetFloat("_DetailStrength", set.DetailStrength);
            if (mat.HasProperty("_DirtAmount")) mat.SetFloat("_DirtAmount", set.DirtAmount);
        }

        /// <summary>材质种类 → 材质族（决定 PBR 参数与细节贴图）。</summary>
        static Whisper.Gameplay.Render.MaterialFamily FamilyFor(string kind)
        {
            switch (kind)
            {
                case "metal": return Whisper.Gameplay.Render.MaterialFamily.Metal;
                case "screen":
                case "laser":
                case "mercury": return Whisper.Gameplay.Render.MaterialFamily.Metal;      // 仪器外壳偏金属
                case "glass": return Whisper.Gameplay.Render.MaterialFamily.Tile;         // 玻璃/瓷质：光滑
                case "uniform": return Whisper.Gameplay.Render.MaterialFamily.Fabric;
                case "skin":
                case "ghostBody": return Whisper.Gameplay.Render.MaterialFamily.Fabric;   // 皮肤/鬼体：高粗糙、弱高光
                case "shoe": return Whisper.Gameplay.Render.MaterialFamily.Fabric;
                case "eyeRed":
                case "eyeWhite": return Whisper.Gameplay.Render.MaterialFamily.Tile;      // 眼球：光滑
                default: return Whisper.Gameplay.Render.MaterialFamily.Plaster;
            }
        }

        /// <summary>按部件名归类材质（红眼/白眼/皮肤/工作服/鞋/液晶/激光/水银/金属/玻璃/机体）。</summary>
        static string MaterialKindOf(string partName)
        {
            if (partName == null) return "body";
            if (partName.Contains("EyeRed")) return "eyeRed";
            if (partName.Contains("EyeWhite")) return "eyeWhite";
            if (partName.Contains("EyeSocket")) return "ghostBody";
            if (partName.StartsWith("GEO-Ghost")) return "ghostBody";
            if (partName.Contains("Skin") || partName.Contains("Head") || partName.Contains("Neck")) return "skin";
            if (partName.Contains("Foot")) return "shoe";
            if (partName.Contains("Screen")) return "screen";
            if (partName.Contains("Laser") || partName.Contains("Window")) return "laser";
            if (partName.Contains("Mercury") || partName.Contains("Bulb")) return "mercury";
            if (partName.Contains("Metal") || partName.Contains("Scale") || partName.Contains("Antenna") || partName.Contains("Knob") || partName.Contains("Foot")) return "metal";
            if (partName.Contains("Tube")) return "glass";
            return "body";
        }

        static Color ColorFor(string kind)
        {
            switch (kind)
            {
                // 鬼怪躯体：**近黑**（暗场里只剩轮廓 —— 用户要的"暗淡不明显的轮廓"）
                case "ghostBody": return new Color(0.055f, 0.058f, 0.062f, 1f);
                case "eyeRed": return new Color(1.00f, 0.14f, 0.08f, 1f);      // 红眼（亮度拉高，暗场可见）
                case "eyeWhite": return new Color(0.90f, 0.94f, 1.00f, 1f);    // 白眼
                case "skin": return new Color(0.52f, 0.40f, 0.33f, 1f);
                case "uniform": return new Color(0.085f, 0.105f, 0.135f, 1f);
                case "shoe": return new Color(0.045f, 0.045f, 0.05f, 1f);
                case "screen": return new Color(0.35f, 1.00f, 0.50f, 1f);      // 液晶绿（提亮以近似自发光）
                case "laser": return new Color(0.40f, 1.00f, 0.45f, 1f);       // 激光绿
                case "mercury": return new Color(0.95f, 0.18f, 0.15f, 1f);     // 水银红
                case "metal": return new Color(0.34f, 0.35f, 0.37f, 1f);
                case "glass": return new Color(0.30f, 0.36f, 0.42f, 1f);
                default: return new Color(0.085f, 0.105f, 0.135f, 1f);
            }
        }

        // ── 道具（三件合并在 `props.glb.bytes` 里，按 x 偏移分组）──────────────
        //
        // ⚠ 我没有把道具拆成每件一个文件（玩家/鬼怪拆了，因为要按部件寻址）。
        // 道具只需要"整件取用"，所以按**文件内的 x 偏移**分组即可 —— 代价是下面这张偏移表
        // 必须与 Blender 场景一致，改模型时要同步（`verify-models` 会报顶点数变化提醒）。
        static readonly Dictionary<string, string[]> PropPartPrefixes = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["dotsProjector"] = new[] { "GEO-Dots" },
            ["spiritBox"] = new[] { "GEO-Box" },
            ["thermometer"] = new[] { "GEO-Thermo" },
        };

        /// <summary>道具在 `props.glb.bytes` 里的**文件内偏移**（x 轴分组；导零后即可独立摆放）。</summary>
        public static readonly Dictionary<string, Vector3> PropOffsets = new Dictionary<string, Vector3>(StringComparer.Ordinal)
        {
            ["dotsProjector"] = new Vector3(0.30f, 0f, 0f),
            ["spiritBox"] = new Vector3(0f, 0f, 0f),
            ["thermometer"] = new Vector3(-0.30f, 0f, 0f),
        };

        /// <summary>取一件道具的网格（合并文件里的多个 primitive → 按名匹配不了，故按索引区间给 —— 见注释）。</summary>
        public static Mesh[] GetPropMeshes(string propId)
        {
            var asset = Resources.Load<TextAsset>("Models/props.glb");
            if (asset == null) { LastProblem = "找不到 Models/props.glb"; return null; }
            if (!Whisper.Gameplay.Level.GlbReader.TryRead(asset.bytes, out var model, out string reason))
            { LastProblem = "props.glb 解析失败：" + reason; return null; }

            // `GlbReader` 不保留节点名 → 只能按**导出顺序**取。导出顺序 = Blender 集合里的顺序，
            // 已在 `verify-models` 里按顶点数核对过（点阵 5 件 / 通灵盒 4 件 / 温度计 4 件）。
            // 这是本方案最脆的一点，所以**导出顺序一变就要同步这张表**（工具会因顶点数不符而报警）。
            int start, count;
            switch (propId)
            {
                case "dotsProjector": start = 0; count = 5; break;
                case "spiritBox": start = 5; count = 4; break;
                case "thermometer": start = 9; count = 4; break;
                default: LastProblem = "未知道具 " + propId; return null;
            }
            if (model.Primitives.Count < start + count) { LastProblem = $"props.glb 只有 {model.Primitives.Count} 个 primitive，不足取 {propId}"; return null; }

            var meshes = new Mesh[count];
            for (int i = 0; i < count; i++) meshes[i] = BuildMesh($"{propId}[{i}]", model.Primitives[start + i]);
            return meshes;
        }

        // ── 自检 ──────────────────────────────────────────────────────────────

        public static string SelfTest()
        {
            var sb = new System.Text.StringBuilder("模型：");
            foreach (var id in KnownIds)
            {
                if (!Parts.TryGetValue(id, out var names)) continue;
                int ok = 0;
                foreach (var n in names) if (GetPartMesh(id, n) != null) ok++;
                sb.Append(id).Append('=').Append(ok).Append('/').Append(names.Length).Append(' ');
            }
            if (!string.IsNullOrEmpty(LastProblem)) sb.Append("· 问题：").Append(LastProblem);
            return sb.ToString().TrimEnd();
        }

        static Mesh BuildMesh(string name, Whisper.Gameplay.Level.GlbReader.Primitive p)
        {
            var mesh = new Mesh { name = "Model_" + name };
            var vertices = new Vector3[p.Positions.Length / 3];
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = new Vector3(p.Positions[i * 3], p.Positions[i * 3 + 1], p.Positions[i * 3 + 2]);
            mesh.SetVertices(vertices);

            if (p.Normals != null && p.Normals.Length == p.Positions.Length)
            {
                var normals = new Vector3[vertices.Length];
                for (int i = 0; i < normals.Length; i++)
                    normals[i] = new Vector3(p.Normals[i * 3], p.Normals[i * 3 + 1], p.Normals[i * 3 + 2]);
                mesh.SetNormals(normals);
            }
            else mesh.RecalculateNormals();

            if (p.Uvs != null && p.Uvs.Length >= vertices.Length * 2)
            {
                var uv = new Vector2[vertices.Length];
                for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(p.Uvs[i * 2], p.Uvs[i * 2 + 1]);
                mesh.SetUVs(0, uv);
            }

            if (p.Indices != null && p.Indices.Length > 0) mesh.SetTriangles(p.Indices, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
