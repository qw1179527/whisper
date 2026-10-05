using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Whisper.Editor
{
    /// <summary>
    /// 出包后的**包内自检**：对"这个包里到底有没有那 5 个套件"给出可复核的结论。
    ///
    /// ## 为什么必须由构建流程来做（本项目的头号失效模式）
    /// "验证过 ≠ 在产品里"在本项目栽过四次。套件这一轮更隐蔽：
    ///   · 仓库里文件在、断言绿 —— 但断言当时读的是**磁盘**，不是包；
    ///   · 把 GLB 放 Resources 时，在产物里搜 `Kits/` 与套件名**能搜到** ——
    ///     那其实是 `asset-manifest.json` 里的路径字符串，**GLB 本体并没进包**
    ///     （`.glb` 被 Unity 当 ModelImporter 资产导入，而 Unity 不支持 glTF）。
    /// 所以判据必须落在**产物**上，而不是仓库上。
    ///
    /// ## 判据（三条，任一条不满足 → 构建失败）
    ///   ① 产物目录里能找到 StreamingAssets/Kits
    ///   ② 5 个套件文件真实存在且字节数非零
    ///   ③ 字节级可解析：复用**产品同一段代码** <c>GlbReader</c>（不是另写一套校验）
    /// 宁可不产出包，也不产出"少了套件却没人知道"的包。
    ///
    /// 报告写到产物目录的 `kit-selftest.json`，供 CI/真机核对。
    ///
    /// 用旧的 <see cref="IPostprocessBuild"/> 接口而不是 <c>IPostprocessBuildWithReport</c>：
    /// 后者在 Unity 6 已过时，且其 <c>BuildReport.files</c> 成员面更易随版本变化；
    /// 这个签名在所有版本都稳定（出处：docs.unity3d.com PostProcessBuildAttribute）。
    /// </summary>
    public class KitPackSelfTest : IPostprocessBuild
    {
        public int callbackOrder => 1000;   // 排在其它构建后处理之后，确保 StreamingAssets 已拷进产物

        /// <summary>
        /// 套件 id 从**清单**读，不再硬编码。
        /// 【2026-10-04 修·独立质检指出】原为写死的 5 个 id。为什么必须改：按房型出套件后
        /// 套件数会增长（一个 hall 被 4 种尺寸的房间共用 → 单一尺寸楼板必然缺口/悬挑 12m），
        /// 而**写死的列表会让新套件永远不被"真的在包里"覆盖**：那正是本项目头号失效模式
        /// （套件没进包、或进了但读不到，门禁却全绿）。
        /// 这里在 Editor 侧，直接读磁盘上的清单真源（`Assets/Data/asset-manifest.json`），
        /// 与产品侧 `GameBootstrap.LoadKitIds()` 同源同口径。
        /// </summary>
        internal static string[] LoadKitIds()
        {
            const string manifestPath = "Assets/Data/asset-manifest.json";
            if (!File.Exists(manifestPath)) return new string[0];
            var root = Whisper.Gameplay.Level.MiniJson.AsMap(
                Whisper.Gameplay.Level.MiniJson.Parse(File.ReadAllText(manifestPath)));
            var kits = Whisper.Gameplay.Level.MiniJson.AsList(
                Whisper.Gameplay.Level.MiniJson.GetOrNull(root, "kits"));
            var ids = new System.Collections.Generic.List<string>();
            if (kits != null)
                foreach (var k in kits)
                {
                    var map = Whisper.Gameplay.Level.MiniJson.AsMap(k);
                    if (map == null) continue;
                    var id = Whisper.Gameplay.Level.MiniJson.AsString(
                        Whisper.Gameplay.Level.MiniJson.GetOrNull(map, "id"));
                    if (!string.IsNullOrEmpty(id)) ids.Add(id);
                }
            return ids.ToArray();
        }

        public void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
        {
            string outDir = Path.GetDirectoryName(pathToBuiltProject);
            if (string.IsNullOrEmpty(outDir)) return;

            // 【实测事故 · 独立复核者发现】Android 上 `pathToBuiltProject` 就是 **.apk 文件本身**，
            // 不是"解包后的目录"。原实现按目录去找 StreamingAssets/Kits → 必然找不到 →
            // 抛 BuildFailedException → **Android 构建恒失败**，而且报错方向还是错的
            // （"套件未进包"，其实套件在 APK 里、只是我没解包看）。
            // 所以这里按产物形态分流：
            //   · 目录形态（Windows/Linux 独立、或已解包的 APK）→ 逐文件核验（本方法的强判据）
            //   · .apk 形态 → 本方法**不做**结论，交给 CI 的"解包 APK → verify-packed-kits.mjs"那一步
            //     ⚠️ **能力边界（R6 只读审计指出，别再说"判据同源"）**：那一步只看得到
            //        `assets/Kits/*.glb`（StreamingAssets 落点），而产品运行时**优先**走
            //        `Resources.Load<TextAsset>`（APK 里在 `data.unity3d` 内）——**结构上够不到**。
            //        覆盖那条路径的是 `KitByteAssetsSelfTest`（真 Unity 里跑），现在由
            //        `unity-check.sh` 第 23 步与 `tools/kit-bytes-selftest.sh` 执行；
            //        APK 内的 Resources 落点至今**没有产物级取证**（已登记为缺口）。
            bool isApk = pathToBuiltProject.EndsWith(".apk", StringComparison.OrdinalIgnoreCase);
            if (isApk)
            {
                Debug.Log($"[Whisper] KIT_PACK_SELFTEST SKIP（Android APK 形态）：产物是 {Path.GetFileName(pathToBuiltProject)}"
                        + "，未解包时本方法无法逐文件核验。APK 级取证由 CI 步骤"
                        + "「包内取证（APK 里真的能查到 5 个套件资产）」执行：解包后跑 tools/verify-packed-kits.mjs。");
                return;
            }

            var problems = new System.Collections.Generic.List<string>();
            var results = new System.Collections.Generic.List<string>();

            // ① 在产物目录里**扫描**出套件目录。
            // 【踩坑】不要用 `Application.productName + "_Data"` 拼路径：数据目录名来自
            // **可执行文件名**（whisper.exe → whisper_Data），而 productName 是 "Project Whisper"，
            // 两者不一致会让自检误报"套件没进包"（实测踩过，构建被正确地拦下但原因是判据写错了）。
            string kitDir = null;
            foreach (var dataDir in Directory.GetDirectories(outDir, "*_Data"))
            {
                string candidate = Path.Combine(dataDir, "StreamingAssets", "Kits");
                if (Directory.Exists(candidate)) { kitDir = candidate; break; }
            }
            // Android：APK 未解包时没有 *_Data，退化为在产物目录里找 assets/Kits
            if (kitDir == null)
            {
                string apkLayout = Path.Combine(outDir, "assets", "Kits");
                if (Directory.Exists(apkLayout)) kitDir = apkLayout;
            }

            // 清单里的套件 id 要在**报告拼装**处也用到（L140/L150），所以提到 if/else 之外。
            // 读不到就给空数组：让"缺清单"由下面的 problems 报错，而不是在这里抛异常打断整个构建。
            var kitIds = LoadKitIds();
            if (kitIds.Length == 0) problems.Add("读不到套件清单（Assets/Data/asset-manifest.json 缺失或无 kits[]）—— 无法核验套件是否进包");

            if (kitDir == null)
            {
                problems.Add("产物里找不到 StreamingAssets/Kits 目录");
            }
            else
            {
                foreach (var id in kitIds)
                {
                    string f = Path.Combine(kitDir, id + ".glb");
                    if (!File.Exists(f)) { problems.Add($"产物缺套件文件：{id}.glb"); continue; }
                    long len = new FileInfo(f).Length;
                    if (len <= 0) { problems.Add($"产物套件为空文件：{id}.glb"); continue; }
                    // ②③ 字节级可解析：复用**产品同一段代码**（不是另写一套校验）
                    if (!Whisper.Gameplay.Level.GlbReader.TryRead(File.ReadAllBytes(f), out var model, out var reason))
                    {
                        problems.Add($"产物套件无法解析：{id}.glb（{reason}）");
                        continue;
                    }
                    results.Add($"{id}: {len} B · {model.Primitives.Count} 部件 · {model.TriangleCount} 面");
                }
            }

            string text = "{\n  \"target\": \"" + target + "\""
                        + ",\n  \"kitDirFound\": " + (kitDir != null ? "true" : "false")
                        + ",\n  \"parsedKits\": " + results.Count + " / " + kitIds.Length
                        + ",\n  \"kits\": [\n    " + string.Join(",\n    ", results.ConvertAll(Json).ToArray())
                        + "\n  ],\n  \"problems\": [" + string.Join(", ", problems.ConvertAll(Json).ToArray()) + "]\n}\n";
            try { File.WriteAllText(Path.Combine(outDir, "kit-selftest.json"), text); } catch (Exception) { }

            if (problems.Count > 0)
            {
                Debug.LogError("[Whisper] KIT_PACK_SELFTEST FAIL：\n  - " + string.Join("\n  - ", problems.ToArray()));
                throw new BuildFailedException("套件未进包（" + problems.Count + " 项）：" + string.Join("；", problems.ToArray()));
            }
            Debug.Log($"[Whisper] KIT_PACK_SELFTEST OK：产物 {results.Count}/{kitIds.Length} 个套件可解析\n  " + string.Join("\n  ", results.ToArray()));
        }

        static string Json(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
