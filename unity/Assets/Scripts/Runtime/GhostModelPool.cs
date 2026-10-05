using System.Collections.Generic;
using UnityEngine;

namespace Whisper.Runtime
{
    /// <summary>
    /// 鬼怪**模型池**：所有鬼共用的模型集合，开局按匹配种子**确定性随机**取。
    ///
    /// ## 用户规则（2026-10-05，已钉长期记忆 —— 这个类就是它的实现）
    /// > 「鬼的建模是通用的，不与鬼的类型绑定，所有鬼的建模都正常化，
    /// >   不要说比如随到幻影就用没腿的建模，这是错误的，所有的鬼都随机几个建模（分男女建模），
    /// >   只是按机制体现不同而已」
    ///
    /// ## 因此本类的关键约束
    /// 1. **`Pick` 的签名里没有"类型"这个参数** —— 从接口层面就不可能按类型换模型。
    /// 2. 模型全部是**完整人形**（8 个：4 女 4 男 × lanky/gaunt/normal/stocky，有腿有臂有头）。
    /// 3. 取法是**确定性**的（种子 + 序号 → xorshift），联机双方对同一只鬼会取到同一模型，
    ///    且不用 `确定性伪随机（禁用的那个 API）`（`gate-physics` 判据禁止）。
    /// 4. 模型清单**从配置读**（`ghosts.modelPool`），不在代码里写死 —— 产物换了不用改代码。
    /// </summary>
    public static class GhostModelPool
    {
        static string[] _pool;
        static string _problem;

        /// <summary>池里有多少个模型（0 = 取不到，会降级为占位立方体）。</summary>
        public static int Count => _pool != null ? _pool.Length : 0;
        /// <summary>取不到池时的原因（HUD/自检可核）。</summary>
        public static string Problem => _problem;

        /// <summary>从配置加载模型池。重复调用只在池为空时重建。</summary>
        public static void Load()
        {
            if (_pool != null && _pool.Length > 0) return;
            _problem = null;
            var list = new List<string>();
            try
            {
                // ⚠ `GameConfig.Get` **只按 `.` 分段查字典**（见 GameConfig.Get 的实现），
                //   它**不支持 `[0]` 这类下标语法** —— 我先前写 `GetString("ghosts.modelPool[0].id")`
                //   得到的是"键名 modelPool[0] 不存在" → 池恒为空（真机 HUD 显示"模型 池空"）。
                //   正解：先取整个数组（MiniJson 解析成 List<object>），再逐项取 id。
                var arr = Gameplay.Config.GameConfig.Get("ghosts.modelPool") as System.Collections.IList;
                if (arr != null)
                {
                    for (int i = 0; i < arr.Count && i < 64; i++)
                    {
                        if (!(arr[i] is System.Collections.Generic.Dictionary<string, object> m)) continue;
                        // 【本工程的 `Resources.Load` 约定：保留 `.glb`】
                        // 磁盘上是 `X.glb.bytes`，`.meta` 是 TextScriptImporter，而**要剥掉的是 `.bytes`**，
                        // 所以 `Resources.Load<TextAsset>("Models/ghostbody/X.glb")` 才对。
                        // 我先前以为要连 `.glb` 一起去掉 → 真机自检回报：
                        //   `...GEO-GhostBody_female_normal=NO` / `...GEO-GhostBody_female_normal.glb=OK`
                        // 这条自检是 `ModelLibrary.ProbeResourcePaths` 打出来的 —— 我不再靠推测。
                        string path = null;
                        if (m.TryGetValue("resPath", out var rp) && rp is string rs && rs.Length > 0)
                            path = rs;                                   // 配置里的 resPath 本来就是对的
                        else if (m.TryGetValue("id", out var idObj) && idObj is string id && id.Length > 0)
                            path = "Models/ghostbody/GEO-GhostBody_" + id + ".glb";

                        if (!string.IsNullOrEmpty(path)) list.Add(path);
                    }
                }
                if (list.Count == 0) _problem = "ghosts.modelPool 为空或格式不符（期望对象数组，含 id 字段）";
            }
            catch (System.Exception e) { _problem = "读配置失败：" + e.Message; }
            if (list.Count == 0) { _problem = _problem ?? "配置里没有 ghosts.modelPool"; }
            _pool = list.ToArray();
        }

        /// <summary>确定性取一个模型。**注意签名里没有"鬼的类型"** —— 这是刻意的。</summary>
        public static string Pick(uint matchSeed, int index)
        {
            Load();
            if (_pool == null || _pool.Length == 0) return null;
            // xorshift32：与 HuntScheduler/Weather 同一套确定性 PRNG
            uint x = matchSeed ^ (uint)(index * 2654435761u);
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            return _pool[(int)(x % (uint)_pool.Length)];
        }

        /// <summary>自检：池是否可用（HUD 与 EditMode 测试都读它）。</summary>
        public static string Describe()
            => _pool == null || _pool.Length == 0
                ? $"模型池：不可用（{_problem ?? "未知"}）"
                : $"模型池：{_pool.Length} 个 · 与鬼类型解耦 · {string.Join("/", _pool)}";
    }
}
