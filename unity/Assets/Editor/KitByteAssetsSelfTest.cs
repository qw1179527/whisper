using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Whisper.Editor
{
    /// <summary>
    /// 套件字节资产自检：确认 **Android 运行时读取路径**（`Resources.Load&lt;TextAsset&gt;` 读
    /// `Kits/&lt;id&gt;.glb.bytes`）在 Unity 里真的成立。
    ///
    /// ## 为什么要单独测这一条
    /// Android 的 StreamingAssets 位于 APK 内部，**不能用 File API 直接读**（官方手册明说），
    /// 要用 UnityWebRequest 走 `jar:file://…!/assets/…` —— 而 `UnityEngine.Networking` 是本工程
    /// 门禁 C6 禁入的第三方命名空间。所以 Android 唯一走得通的路是"把 GLB 当**二进制文本资产**"：
    ///   · 扩展名必须是 `.bytes`（不是 `.glb` —— 那会被 ModelImporter 当模型导入，实测取到 null）
    ///   · 对应的 `.meta` 必须是 `TextScriptImporter`（`DefaultImporter` **不是** TextAsset，
    ///     实测也会取到 null —— 这一步已经踩过一次）
    /// 这两条都靠"约定"维持，所以必须有断言把它们钉住：本方法就是那条断言。
    ///
    /// 用法：
    ///   Unity -quit -batchmode -projectName "&lt;proj&gt;" -executeMethod Whisper.Editor.KitByteAssetsSelfTest.Run -logFile &lt;log&gt;
    /// 通过 → 打印 `KIT_BYTES_SELFTEST OK`；失败 → `FAIL` 并以退出码 1 结束。
    /// </summary>
    public static class KitByteAssetsSelfTest
    {
        /// <summary>
        /// 套件 id 从**清单**读，不再硬编码。
        /// 【2026-10-04 修·独立质检指出】原为写死的 5 个 id（`hall_main/hospital_ward/morgue/cabinet_a/bed_b`）。
        /// 为什么必须改：按房型出套件后套件数会增长（一个 hall 被 4 种尺寸的房间共用，
        /// 单一尺寸的楼板/天花板必然缺口或悬挑 12m），而**写死的列表会让新套件永远不会被
        /// "真的在包里"这条断言覆盖** —— 那正是本项目记录在案的头号失效模式
        /// （套件没进包、或进了但读不到，而门禁全绿）。
        /// 归零风险的写法：以 `Resources/Data/asset-manifest` 的 `kits[].id` 为唯一真源。
        /// </summary>
        static string[] LoadKitIds()
        {
            var manifest = Resources.Load<TextAsset>("Data/asset-manifest");
            if (manifest == null)
                throw new System.InvalidOperationException("Resources/Data/asset-manifest 不在包里 —— 套件清单读不到，无法自检");

            var ids = new System.Collections.Generic.List<string>();
            var root = Whisper.Gameplay.Level.MiniJson.AsMap(Whisper.Gameplay.Level.MiniJson.Parse(manifest.text));
            // `MiniJson` 的公开面是 Parse / AsMap / AsList / AsString / GetOrNull（**没有 StringOf**——
            // 我第一版凭印象写了 StringOf，本机 Roslyn 立刻拦下 CS0117。这正是"预检胜于真机构建"的价值）。
            var kits = Whisper.Gameplay.Level.MiniJson.AsList(Whisper.Gameplay.Level.MiniJson.GetOrNull(root, "kits"));
            if (kits != null)
            {
                foreach (var k in kits)
                {
                    var map = Whisper.Gameplay.Level.MiniJson.AsMap(k);
                    if (map == null) continue;
                    var id = Whisper.Gameplay.Level.MiniJson.AsString(Whisper.Gameplay.Level.MiniJson.GetOrNull(map, "id"));
                    if (!string.IsNullOrEmpty(id)) ids.Add(id);
                }
            }
            if (ids.Count == 0) throw new System.InvalidOperationException("清单里没有任何套件 id");
            return ids.ToArray();
        }

        public static void Run()
        {
            int ok = 0;
            var problems = new System.Collections.Generic.List<string>();
            var kitIds = LoadKitIds();
            Debug.Log($"[KIT_BYTES] 清单里有 {kitIds.Length} 个套件：{string.Join(", ", kitIds)}");

            foreach (var id in kitIds)
            {
                // ⚠️ Resources 路径**不含扩展名**（实测：只剥掉最后一个扩展名）→ 写 "Kits/<id>.glb"
                string resPath = $"Kits/{id}.glb";
                var asset = Resources.Load<TextAsset>(resPath);
                if (asset == null) { problems.Add($"{id}: Resources.Load<TextAsset>(\"{resPath}\") 返回 null（应为 Assets/Resources/Kits/{id}.glb.bytes，导入器须为 TextScriptImporter）"); continue; }
                byte[] bytes = asset.bytes;
                if (bytes == null || bytes.Length == 0) { problems.Add($"{id}: TextAsset.bytes 为空"); continue; }

                if (!Whisper.Gameplay.Level.GlbReader.TryRead(bytes, out var model, out var reason))
                {
                    problems.Add($"{id}: GlbReader 解析失败（{reason}）");
                    continue;
                }
                ok++;
                Debug.Log($"[KIT_BYTES] {id,-15} {bytes.Length} B · {model.Primitives.Count} 部件 · {model.TriangleCount} 面");
            }

            // 【踩坑记录 · 不要把这条改成"期待 null"】
            // 我一度以为 "Kits/hall_main.glb" 应当取不到（因为磁盘上叫 `.glb.bytes`），
            // 于是写了条"反向对照"期待 null —— 结果它**非 null 且正确**，因为
            // **Resources 的加载路径不含扩展名**：Unity 只剥掉最后一个扩展名，
            // 所以 "Kits/hall_main.glb" 命中的正是 `Assets/Resources/Kits/hall_main.glb.bytes`。
            // 现在的写法（对象名 = 文件名去掉最后一个扩展名）是对的；这条对照已删除，
            // 改由"能否加载并解析"来判（见上方的 5 个套件循环）。
            var crossCheck = Resources.Load<TextAsset>("Kits/hall_main.glb");
            Debug.Log($"[KIT_BYTES] 路径口径确认：Load<TextAsset>(\"Kits/hall_main.glb\") → "
                    + (crossCheck == null ? "null（异常：套件应可加载）" : $"{crossCheck.bytes.Length} B（命中 Assets/Resources/Kits/hall_main.glb.bytes）"));
            if (crossCheck == null) problems.Add("路径口径异常：Kits/hall_main.glb 取不到（Resources 路径应为'文件名去掉最后一个扩展名'）");

            if (problems.Count > 0)
            {
                Debug.LogError($"[KIT_BYTES] KIT_BYTES_SELFTEST FAIL（{problems.Count} 项）：\n  - " + string.Join("\n  - ", problems.ToArray()));
                EditorApplication.Exit(1);
                return;
            }
            Debug.Log($"[KIT_BYTES] KIT_BYTES_SELFTEST OK：{ok}/{kitIds.Length} 个套件可经 Resources 文本资产读取并解析"
                    + "（这是 Android 唯一可行的读取路径：StreamingAssets 在 APK 内不能用 File API 读）");
            EditorApplication.Exit(0);
        }
    }
}
