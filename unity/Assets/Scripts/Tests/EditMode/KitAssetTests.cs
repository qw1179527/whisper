using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Whisper.Gameplay.Level;

namespace Whisper.Tests.EditMode
{
    /// <summary>
    /// 套件资产（GLB）在**真 Unity 进程**里的测试。
    ///
    /// ## 为什么必须补这一组（组级自验收时发现的覆盖空洞）
    /// 本工程有两套断言，职责不同：
    ///   · `native/csharp-verify`（本机 .NET 跑手）—— 覆盖 `GlbReader` 的**纯逻辑**，快，不需要 Unity；
    ///   · Unity Test Framework（本文件）—— 覆盖"**在 Unity 里**能不能加载并解析"。
    /// 在补这组之前，Unity 侧对 `GlbReader`/`KitVerifier`/`KitMeshLibrary` 的引用数是 **0**：
    /// 也就是说"套件代码在 Unity 里跑得通"这件事**没有任何 Unity 侧判据**，
    /// 而历史事故 #18 恰恰是"本机跑手全绿、真 Unity 报 CS0103"。
    ///
    /// ## 判据落点（关键）
    /// 这里**故意**用产品同一条读取路径：按 `"Kits/&lt;id&gt;.glb"` 这个名字去取
    /// **带类型参数的文本资产**（磁盘上其实是 `Kits/&lt;id&gt;.glb.bytes`——Resources 的加载路径
    /// **不含扩展名**，只剥掉最后一个）。这条路径在 Android 上是唯一可行的（StreamingAssets 在 APK 内
    /// 不能用 File API 读），所以它值得被测试钉住。
    /// </summary>
    public class KitAssetTests
    {
        /// <summary>当前清单里的套件（<c>Assets/Data/asset-manifest.json</c> 的 <c>kits[].id</c>）。</summary>
        static readonly string[] KitIds = LoadKitIds();

        /// <summary>
        /// 从**清单**读套件 id，而不是写死一组。
        /// 【2026-10-04 修】原为写死的 5 个 id（`hall_main/hospital_ward/morgue/cabinet_a/bed_b`）。
        /// 为什么必须改：按房型出套件后条数从 5 变成 8（一个 hall 被 4 种尺寸的房间共用，
        /// 单一尺寸的楼板/天花板必然缺口或悬挑 12m），于是**本测试自己变红**
        /// （实测 `EditMode total=21 passed=19 failed=2`，两条都是 `Expected: 5 But was: 8`）。
        /// 写死条数的测试等于"资产一扩就红"，会把真实回归淹掉；改成读清单后，
        /// **清单几条就测几条**，且清单的条数正确性由 `native/csharp-verify`（下限 5 + 每个都能加载）
        /// 与 `gate-model`/`gate-asset-bbox`（逐套件）盯住，职责不再重叠。
        /// </summary>
        static string[] LoadKitIds()
        {
            // 这里**刻意不用 NUnit 断言**：本方法由静态字段初始化器调用，此时还没有测试上下文，
            // 断言失败会被包成 `TypeInitializationException`，把真实原因埋掉。
            // 用显式异常，报错信息直接可读。
            var manifest = Resources.Load<TextAsset>("Data/asset-manifest");
            if (manifest == null) throw new System.InvalidOperationException("Resources/Data/asset-manifest 取不到（清单是套件的唯一入口）");
            var root = MiniJson.AsMap(MiniJson.Parse(manifest.text));
            var kits = MiniJson.AsList(MiniJson.GetOrNull(root, "kits"));
            if (kits == null) throw new System.InvalidOperationException("清单里没有 kits[]");
            var ids = new List<string>();
            foreach (var k in kits)
            {
                var map = MiniJson.AsMap(k);
                if (map == null) continue;
                var id = MiniJson.AsString(MiniJson.GetOrNull(map, "id"));
                if (!string.IsNullOrEmpty(id)) ids.Add(id);
            }
            if (ids.Count < 5) throw new System.InvalidOperationException($"清单里的套件只有 {ids.Count} 个，少于下限 5");
            return ids.ToArray();
        }

        static byte[] LoadKitBytes(string id)
        {
            var asset = Resources.Load<TextAsset>("Kits/" + id + ".glb");
            return asset != null ? asset.bytes : null;
        }

        [Test]
        public void 清单里的每个套件都能经产品读取路径加载出非空字节()
        {
            foreach (var id in KitIds)
            {
                var bytes = LoadKitBytes(id);
                Assert.IsNotNull(bytes, $"套件 {id} 取不到：Resources/Kits/{id}.glb （磁盘上是 {id}.glb.bytes，" +
                                        "meta 必须是 TextScriptImporter —— .glb 会被 ModelImporter 当模型导入，取不到）");
                Assert.Greater(bytes.Length, 0, $"套件 {id} 字节为空");
            }
        }

        [Test]
        public void 清单里的每个套件都能被_GlbReader_解析且几何非空()
        {
            foreach (var id in KitIds)
            {
                var bytes = LoadKitBytes(id);
                Assert.IsNotNull(bytes, $"套件 {id} 取不到字节");

                GlbReader.Model model;
                string reason;
                bool ok = GlbReader.TryRead(bytes, out model, out reason);
                Assert.IsTrue(ok, $"套件 {id} 解析失败：{reason}");
                Assert.Greater(model.Primitives.Count, 0, $"套件 {id} 没有部件");
                Assert.Greater(model.VertexCount, 0, $"套件 {id} 没有顶点");
                Assert.Greater(model.TriangleCount, 0, $"套件 {id} 没有三角面");
            }
        }

        [Test]
        public void 套件部件数与清单记录一致()
        {
            var manifest = Resources.Load<TextAsset>("Data/asset-manifest");
            Assert.IsNotNull(manifest, "Resources/Data/asset-manifest 取不到");
            var root = MiniJson.AsMap(MiniJson.Parse(manifest.text));
            var kits = MiniJson.AsList(MiniJson.GetOrNull(root, "kits"));
            Assert.IsNotNull(kits, "清单缺 kits");

            int checkedKits = 0;
            foreach (var kitObj in kits)
            {
                var kit = MiniJson.AsMap(kitObj);
                string id = MiniJson.AsString(MiniJson.GetOrNull(kit, "id"));
                var bytes = LoadKitBytes(id);
                Assert.IsNotNull(bytes, $"套件 {id} 取不到字节");

                GlbReader.Model model;
                string reason;
                Assert.IsTrue(GlbReader.TryRead(bytes, out model, out reason), $"套件 {id} 解析失败：{reason}");

                // 字节数必须与清单一致（清单由 tools/gen-kits.mjs 回写，是资产的唯一入口）
                long expectBytes = (long)MiniJson.AsFloat(MiniJson.GetOrNull(kit, "bytes"));
                if (expectBytes > 0)
                    Assert.AreEqual(expectBytes, (long)bytes.Length, $"套件 {id} 字节数与清单不符");
                checkedKits++;
            }
            Assert.AreEqual(KitIds.Length, checkedKits, "清单里的套件数与本测试的期望不符");
        }

        [Test]
        public void GlbReader_对坏输入返回假且不抛异常()
        {
            var cases = new (string name, byte[] data)[]
            {
                ("null", null),
                ("空数组", new byte[0]),
                ("非 GLB", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 }),
                ("magic 对但版本 1", new byte[] { 0x67, 0x6C, 0x54, 0x46, 1, 0, 0, 0, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
            };
            foreach (var (name, data) in cases)
            {
                GlbReader.Model model;
                string reason;
                Assert.DoesNotThrow(() => GlbReader.TryRead(data, out model, out reason), $"坏输入「{name}」抛异常了");
                Assert.IsFalse(GlbReader.TryRead(data, out model, out reason), $"坏输入「{name}」竟然被接受");
            }
        }

        [Test]
        public void KitVerifier_从_StreamingAssets_也能加载全部套件()
        {
            var manifest = Resources.Load<TextAsset>("Data/asset-manifest");
            Assert.IsNotNull(manifest, "Resources/Data/asset-manifest 取不到");

            // 产品里的 rootDir 是 Application.streamingAssetsPath（兜底读取路径）
            var loads = KitVerifier.LoadAll(Application.streamingAssetsPath, manifest.text);
            Assert.AreEqual(KitIds.Length, loads.Count, "加载结果条数与清单套件数不符");
            foreach (var l in loads)
            {
                Assert.IsTrue(l.Ok, $"套件 {l.Id} 加载失败：{l.Problem}");
            }
        }
    }
}
