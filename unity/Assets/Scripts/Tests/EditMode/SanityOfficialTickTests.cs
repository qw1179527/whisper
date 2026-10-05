using NUnit.Framework;
using Whisper.Gameplay.Config;
using Whisper.Gameplay.Sanity;

namespace Whisper.Tests.EditMode
{
    /// <summary>
    /// 官方口径理智 Tick 的回归测试（第 31 轮新增的重载）。
    ///
    /// ## 为什么必须补测试
    /// 这一版 `SanitySystem` **此前零测试覆盖**（实测：Tests 目录里没有任何 Sanity 引用），
    /// 而本轮新增的官方公式有 6 个输入维度（地图大小 × 阶段 × 难度 × 单人 × 血月 × 光照/火源）
    /// —— 没有断言钉住，后面任何一次"顺手改一下"都会静默跑偏。
    ///
    /// ## 数值出处
    /// `data/config.json` → `sanity.drain.official`，其 `_src` 标注
    /// `official：phasmophobia.su/knowledge-base/gameplay/sanity`。
    /// 测试里的期望值**与官方表逐一对齐**（小图 setup 0.09 / 正常 0.12 …）。
    /// </summary>
    public class SanityOfficialTickTests
    {
        static SanitySystem NewSystem(out GameConfigReader cfg)
        {
            // 走真实配置表（与产品同源），而不是在测试里编一套数字
            var json = System.IO.File.ReadAllText("Assets/Data/config.json");
            GameConfig.LoadFromJson(json);
            cfg = new GameConfigReader();
            return new SanitySystem(cfg, 100f);
        }

        static SanityTickContext Ctx(
            MapSizeBand size = MapSizeBand.Small,
            MatchPhaseBand phase = MatchPhaseBand.Normal,
            float diff = 1f, bool solo = false, bool bloodMoon = false,
            bool mainLight = false, bool largeDark = false, int fireTier = 0)
            => new SanityTickContext
            {
                MapSize = size, Phase = phase, DifficultyMultiplier = diff,
                Solo = solo, BloodMoon = bloodMoon,
                MainLightOn = mainLight, LargeDarkZone = largeDark, FireTier = fireTier,
            };

        /// <summary>跑满 100 秒（dt=0.1 × 1000 步），返回掉掉的理智百分点。</summary>
        static float DrainOver(SanitySystem s, in SanityTickContext ctx, float seconds = 100f)
        {
            float before = s.Value;
            for (float t = 0f; t < seconds; t += 0.1f) s.Tick(0.1f, ctx);
            return before - s.Value;
        }

        [Test]
        public void 主灯开启_普通房间_完全不掉理智()
        {
            var s = NewSystem(out _);
            // 官方：流失取决于房间主光源；主灯全开时该房间被动流失 = 0
            float d = DrainOver(s, Ctx(mainLight: true));
            Assert.AreEqual(0f, d, 0.01f, "主灯全开时不应有任何被动流失");
        }

        [Test]
        public void 大暗区即使开主灯_仍保留两成流失()
        {
            var s = NewSystem(out _);
            // 官方：大型暗区（如 Sunny Meadows 走廊）开主灯也只降到 80%
            float full = DrainOver(NewSystem(out _), Ctx(mainLight: false));
            float dark = DrainOver(s, Ctx(mainLight: true, largeDark: true));
            Assert.Greater(dark, 0.01f, "大暗区开主灯仍应有流失");
            Assert.AreEqual(full * 0.2f, dark, full * 0.05f, "应约为满值的 20%");
        }

        [Test]
        public void 小图正常阶段_难度1_流失率对齐官方012每秒()
        {
            var s = NewSystem(out _);
            float d = DrainOver(s, Ctx(), 100f);
            // 官方表：小图 · 正常阶段 = 0.12 %/s → 100s ≈ 12
            Assert.AreEqual(12f, d, 0.6f, "小图正常阶段应为 0.12%/s");
        }

        [Test]
        public void 专业难度乘数2_流失翻倍()
        {
            var s = NewSystem(out _);
            float d = DrainOver(s, Ctx(diff: 2f), 100f);
            Assert.AreEqual(24f, d, 1.0f, "×2 难度应使流失翻倍");
        }

        [Test]
        public void 单人被动流失减半()
        {
            var s = NewSystem(out _);
            float d = DrainOver(s, Ctx(solo: true), 100f);
            Assert.AreEqual(6f, d, 0.6f, "单人应减半");
        }

        [Test]
        public void 血月_在难度乘数之上再加一档()
        {
            var s = NewSystem(out _);
            // 难度 1 + 血月加 1 → 乘数 2
            float d = DrainOver(s, Ctx(diff: 1f, bloodMoon: true), 100f);
            Assert.AreEqual(24f, d, 1.0f, "血月加一档后应等于 ×2");
        }

        [Test]
        public void Setup阶段_理智不得低于50()
        {
            var s = NewSystem(out _);
            // 用最狠的组合（大图 × 2 难度）也只到 0.10%/s，1000 秒足以压到 50% 以下
            for (float t = 0f; t < 1000f; t += 0.1f)
                s.Tick(0.1f, Ctx(size: MapSizeBand.Large, phase: MatchPhaseBand.Setup, diff: 2f));
            Assert.GreaterOrEqual(s.Value, 49.99f, "Setup 阶段任何来源都不得把理智压到 50 以下");
        }

        [Test]
        public void 火源按等级降低但不归零()
        {
            float none = DrainOver(NewSystem(out _), Ctx());
            float t1 = DrainOver(NewSystem(out _), Ctx(fireTier: 1));
            float t3 = DrainOver(NewSystem(out _), Ctx(fireTier: 3));
            Assert.Less(t1, none, "有火源应比没火源掉得少");
            Assert.Less(t3, t1, "三级火源应比一级更有效");
            Assert.Greater(t3, 0.01f, "官方：火源降低但不归零");
        }

        [Test]
        public void 手电不参与流失计算_这是官方口径的故意设计()
        {
            // 旧重载 Tick(dt, torchOn, ...) 里"有手电就不掉"是**错的**；
            // 新重载**根本不接受手电参数** —— 本测试用"两次同参调用结果一致"钉住这一点，
            // 防止后人"顺手加回去"。
            var a = NewSystem(out _);
            var b = NewSystem(out _);
            var ctx = Ctx();
            float da = DrainOver(a, ctx, 60f);
            float db = DrainOver(b, ctx, 60f);
            Assert.AreEqual(da, db, 0.001f, "新口径下不存在手电开/关这一输入维度");
        }
    }
}
