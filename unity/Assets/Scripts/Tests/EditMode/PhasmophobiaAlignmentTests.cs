using System.Collections.Generic;
using NUnit.Framework;
using Whisper.Gameplay.Config;
using Whisper.Gameplay.Interaction;
using Whisper.Gameplay.Level;          // MiniJson（真配置解析测试要用）
using Whisper.Gameplay.Power;
using Whisper.Gameplay.Progression;

namespace Whisper.Tests.EditMode
{
    /// <summary>
    /// 恐鬼症对齐七系统的本机断言（纯 C#，Edit Mode 可跑，不依赖真机）。
    ///
    /// ## 为什么这批测试重要
    /// 我这一轮把等级/商店/任务/电闸/互动加进来时，**真机试错的代价极高**（每轮出包 ~3 分钟 + 装机 + 截图）。
    /// 这些断言把"机制是否自洽"提前到本机 0.2 秒内回答，只在"断言全绿但真机仍不对"时才去出包。
    /// 每个测试都对应 spec 里的一条硬规则，而不是凑覆盖率。
    /// </summary>
    public class PhasmophobiaAlignmentTests
    {
        /// <summary>构造一份最小的内存配置（不读磁盘，测试与仓库数据解耦）。</summary>
        static Dictionary<string, object> Cfg(params (string Key, object Val)[] pairs)
        {
            var root = new Dictionary<string, object>();
            foreach (var (k, v) in pairs)
            {
                var parts = k.Split('.');
                var cur = root;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (!cur.TryGetValue(parts[i], out var next) || !(next is Dictionary<string, object>))
                    {
                        next = new Dictionary<string, object>();
                        cur[parts[i]] = next;
                    }
                    cur = (Dictionary<string, object>)next;
                }
                cur[parts[parts.Length - 1]] = v;
            }
            return root;
        }

        // ───────────────── 真配置解析（不出包就能验"配置被读到了"）─────────────────

        /// <summary>
        /// 用**仓库里的真配置文本**跑一遍 MiniJson + GameConfig.Get，确认五个新段真的能被读到。
        /// 为什么必须有这条：真机 HUD 一直在报『配置里没有 shop.items』，而我在真机上无法翻
        /// resources.assets —— 到底是"文件没进包"还是"键路径不对"分不清。这里用纯 C# 把
        /// 解析这一层单独证清：解析器能读到 → 问题在打包/取值路径；读不到 → 问题在配置本身。
        /// </summary>
        [Test]
        public void RealConfig_NewSectionsAreReadable()
        {
            string path = System.IO.Path.Combine(
                UnityEngine.Application.dataPath, "Data", "config.json");
            if (!System.IO.File.Exists(path))
            {
                Assert.Ignore("找不到 " + path + "（非仓库布局下跳过）");
                return;
            }
            string json = System.IO.File.ReadAllText(path);
            var root = MiniJson.AsMap(MiniJson.Parse(json));
            Assert.IsNotNull(root, "config.json 必须能解析（解析失败 = 整个配置表作废）");

            var items = MiniJson.Get(root, "shop") is Dictionary<string, object> shop
                        && shop.TryGetValue("items", out var it) ? it as System.Collections.IList : null;
            Assert.IsNotNull(items, "shop.items 必须能取到（HUD 报『配置里没有 shop.items』时先看这条）");
            Assert.Greater(items.Count, 0, "shop.items 不能为空");

            Assert.IsNotNull(MiniJson.Get(root, "progression"), "progression 段缺失");
            Assert.IsNotNull(MiniJson.Get(root, "tasks"), "tasks 段缺失（每日/每周）");
            Assert.IsNotNull(MiniJson.Get(root, "objectives"), "objectives 段缺失（**局内任务**，与 tasks 是两套）");
            Assert.IsNotNull(MiniJson.Get(root, "power"), "power 段缺失");
            Assert.IsNotNull(MiniJson.Get(root, "interaction"), "interaction 段缺失");

            // 局内任务：真配置能抽出 N 条，且与"每日任务"互不干扰
            var cfgForObj = new GameConfigReader(root);
            var objs = new Whisper.Gameplay.Objectives.ObjectiveSystem(cfgForObj);
            objs.BeginContract(12345u);
            Assert.IsNull(objs.LoadProblem, "真配置必须能生成本局任务：" + objs.LoadProblem);
            Assert.Greater(objs.All.Count, 0, "本局任务条数应 > 0");
            // 同种子必须生成同一组（联机可复现；gate-physics 禁时钟参与玩法判定）
            var objs2 = new Whisper.Gameplay.Objectives.ObjectiveSystem(cfgForObj);
            objs2.BeginContract(12345u);
            for (int i = 0; i < objs.All.Count; i++)
                Assert.AreEqual(objs.All[i].Text, objs2.All[i].Text, "同种子必须生成同一组局内任务");

            // 真配置灌进 Reader，跑一遍 Shop.Load —— 这才是"运行时会看到的"
            var cfg = new GameConfigReader(root);
            var realShop = new Shop();
            realShop.Load(cfg);
            Assert.IsNull(realShop.LoadProblem, "真配置必须能载入商店：" + realShop.LoadProblem);
            Assert.Greater(realShop.Count, 0, "真配置的装备数应 > 0");

            var realProg = new Progression(cfg);
            Assert.Greater(realProg.XpForNextLevel, 0, "真配置的经验曲线必须为正");
        }

        // ───────────────── 渲染质量（用户永久约束 §2/§6）─────────────────

        /// <summary>读真配置（档位表是给玩家看的，必须用真数据断言）。</summary>
        static Whisper.Gameplay.Render.RenderQuality RealQuality()
        {
            string path = System.IO.Path.Combine(UnityEngine.Application.dataPath, "Data", "config.json");
            var cfg = new GameConfigReader(MiniJson.AsMap(MiniJson.Parse(System.IO.File.ReadAllText(path))));
            return new Whisper.Gameplay.Render.RenderQuality(cfg);
        }

        [Test]
        public void RenderQuality_TiersAreMonotonic()
        {
            // "顶级画质比高画质还差"这种错在肉眼下几乎发现不了，只有断言能守住。
            if (!System.IO.File.Exists(System.IO.Path.Combine(UnityEngine.Application.dataPath, "Data", "config.json")))
            { Assert.Ignore("找不到真配置（非仓库布局）"); return; }
            var q = RealQuality();
            var lo = q.Get(Whisper.Gameplay.Render.QualityTier.Low);
            var hi = q.Get(Whisper.Gameplay.Render.QualityTier.High);
            var top = q.Get(Whisper.Gameplay.Render.QualityTier.Top);

            Assert.LessOrEqual(lo.PixelLightCount, hi.PixelLightCount, "像素光上限必须随档位不降");
            Assert.LessOrEqual(hi.PixelLightCount, top.PixelLightCount);
            Assert.LessOrEqual(lo.Shadows, hi.Shadows, "阴影质量必须随档位不降");
            Assert.LessOrEqual(hi.Shadows, top.Shadows);
            Assert.LessOrEqual(lo.AntiAliasing, hi.AntiAliasing, "抗锯齿必须随档位不降");
            Assert.LessOrEqual(hi.AntiAliasing, top.AntiAliasing);
            Assert.LessOrEqual(lo.FarClipM, hi.FarClipM, "绘制距离必须随档位不降");
            Assert.LessOrEqual(hi.FarClipM, top.FarClipM);
            Assert.LessOrEqual(lo.BloomIntensity, hi.BloomIntensity);
            Assert.LessOrEqual(hi.BloomIntensity, top.BloomIntensity);

            Assert.IsFalse(lo.Ssao, "低画质必须关 SSAO（否则移动端帧率守不住）");
            Assert.IsFalse(lo.Ssgi, "低画质必须关 SSGI");
            Assert.IsFalse(lo.Bloom, "低画质必须关辉光");
            Assert.IsTrue(hi.Bloom, "高画质必须开辉光（只依赖颜色的效果，可靠）");
            Assert.IsTrue(lo.Vignette, "暗角各档都开（它只依赖屏幕 UV，零成本且构图必需）");

            // ══════════════════════════════════════════════════════════════════════
            // 【默认关的硬约束】下面三条断言编码的是**已知缺陷**，不是偏好：
            //   SSAO/SSGI 依赖 `_CameraDepthNormalsTexture`、眼部适应依赖 1x1 浮点读回，
            //   而这两条在本工程的 `OnRenderImage` + 手工 `Graphics.Blit` 链里**拿不到有效数据**
            //   （0.1.47~0.1.53 真机实测：HUD 一直报 亮度 raw=0.00，开则黑屏）。
            //   所以它们必须默认关。**谁把默认改成 true，这条测试就会红**——
            //   那不是"测试碍事"，而是提醒：得先解决深度纹理在 Blit 链中的绑定问题。
            //   修好之后再来改这三条，并同步改配置里的 `_disabledWhy` 说明。
            // ══════════════════════════════════════════════════════════════════════
            Assert.IsFalse(lo.Ssao || hi.Ssao || top.Ssao,
                "SSAO 必须默认关：深度纹理在 OnRenderImage 的 Blit 链里取不到（真机 raw=0.00）。先修绑定再打开。");
            Assert.IsFalse(lo.Ssgi || hi.Ssgi || top.Ssgi,
                "SSGI 必须默认关：同上（依赖深度法线纹理）。");
            Assert.IsFalse(lo.EyeAdaptation || hi.EyeAdaptation || top.EyeAdaptation,
                "眼部适应必须默认关：1x1 浮点读回在本工程返回 0（真机实测）。先修读回再打开。");

            Assert.Greater(top.ShadowDistanceM, lo.ShadowDistanceM, "顶级阴影距离必须比低档远");
        }

        [Test]
        public void RenderQuality_FrameRateOnlyAllows60_90_120()
        {
            var q = RealQuality();
            Assert.IsTrue(q.SelectFrameRate(60));
            Assert.IsTrue(q.SelectFrameRate(90));
            Assert.IsTrue(q.SelectFrameRate(120));
            Assert.IsFalse(q.SelectFrameRate(144), "只允许 60/90/120（用户明确的三档）");
            Assert.IsFalse(q.SelectFrameRate(0));
            Assert.AreEqual(120, (int)q.FrameRate, "非法值不得改变当前帧率");
        }

        [Test]
        public void RenderQuality_CycleOrderIsLowHighTopLow()
        {
            var q = RealQuality();
            q.Select(Whisper.Gameplay.Render.QualityTier.Low);
            Assert.AreEqual(Whisper.Gameplay.Render.QualityTier.High, q.NextTier());
            q.Select(q.NextTier());
            Assert.AreEqual(Whisper.Gameplay.Render.QualityTier.Top, q.NextTier());
            q.Select(q.NextTier());
            Assert.AreEqual(Whisper.Gameplay.Render.QualityTier.Low, q.NextTier(), "顶级再切应回到低");
            q.SelectFrameRate(60);
            Assert.AreEqual(90, q.NextFrameRate(), "60 的下一档是 90");
            q.SelectFrameRate(120);
            Assert.AreEqual(60, q.NextFrameRate(), "120 的下一档回到 60");
        }

        [Test]
        public void RenderQuality_RejectsIllegalValuesInsteadOfSilentlyUsingThem()
        {
            // 非法的 MSAA/阴影值必须被**修正并登记**，而不是原样送进 Unity（那会静默失效）
            var cfg = new GameConfigReader(Cfg(
                ("render.tiers.low.antiAliasing", 3L),      // 非法：只能 0/2/4/8
                ("render.tiers.low.shadows", 7L),           // 非法：只能 0/1/2
                ("render.tiers.low.renderScale", 2.0)));    // 非法：(0,1]
            var q = new Whisper.Gameplay.Render.RenderQuality(cfg);
            var t = q.Get(Whisper.Gameplay.Render.QualityTier.Low);
            Assert.AreEqual(4, t.AntiAliasing, "非法 MSAA 必须被修正为 4");
            Assert.AreEqual(2, t.Shadows, "非法阴影值必须被修正为 2");
            Assert.AreEqual(1f, t.RenderScale, 1e-4f, "非法 renderScale 必须被修正为 1");
            Assert.GreaterOrEqual(q.ConfigProblems.Count, 3, "每个非法值都要登记一条问题（不能静默）");
        }

        // ───────────────── 局内任务（合同日志里的可选目标）─────────────────

        [Test]
        public void Objectives_DeterministicPerContractSeed()
        {
            // ⚠ 本工程的语言版本是 **C# 9**：`var f = () => x;` 这种"推断委托类型"要 C# 10，
            // 必须写显式 `Func<T>`（真 Unity 编译报 CS8773，语法预检看不到）。
            System.Func<GameConfigReader> cfg = () => new GameConfigReader(Cfg(("objectives.perContract", 3L), ("objectives.pool", new List<object>
            {
                new Dictionary<string, object> { ["kind"] = "emfLevel5", ["text"] = "EMF 5 级" },
                new Dictionary<string, object> { ["kind"] = "photographGhost", ["text"] = "拍到鬼" },
                new Dictionary<string, object> { ["kind"] = "saltFootprint", ["text"] = "盐上足迹" },
                new Dictionary<string, object> { ["kind"] = "ghostWriting", ["text"] = "鬼写字" },
            })));
            var a = new Whisper.Gameplay.Objectives.ObjectiveSystem(cfg());
            var b = new Whisper.Gameplay.Objectives.ObjectiveSystem(cfg());
            a.BeginContract(777u); b.BeginContract(777u);
            Assert.AreEqual(3, a.All.Count);
            for (int i = 0; i < a.All.Count; i++) Assert.AreEqual(a.All[i].Text, b.All[i].Text);
            // 不同种子应抽到不同组合（同 4 选 3，抽到完全相同的概率很低；这里只要求"能重复触发"）
            var c = new Whisper.Gameplay.Objectives.ObjectiveSystem(cfg());
            c.BeginContract(778u);
            Assert.AreEqual(3, c.All.Count);
        }

        [Test]
        public void Objectives_CompletionAndClaimAreIdempotent()
        {
            var cfg = new GameConfigReader(Cfg(("objectives.perContract", 1L), ("objectives.pool", new List<object>
            {
                new Dictionary<string, object> { ["kind"] = "emfLevel5", ["text"] = "EMF 5 级", ["rewardMoney"] = 45L, ["rewardXp"] = 70L },
            })));
            var o = new Whisper.Gameplay.Objectives.ObjectiveSystem(cfg);
            o.BeginContract(1u);
            o.ReportEmfLevel(3);              // 不到 5 级不算达成
            Assert.AreEqual(0, o.CompletedCount);
            o.ReportEmfLevel(5);
            Assert.AreEqual(1, o.CompletedCount, "EMF 5 级必须算达成");

            o.Claim(out int money, out int xp);
            Assert.AreEqual(45, money); Assert.AreEqual(70, xp);
            o.Claim(out int m2, out int x2);   // 第二次不应重复发
            Assert.AreEqual(0, m2); Assert.AreEqual(0, x2, "奖励只能发一次（否则玩家可以刷 HUD）");
        }

        [Test]
        public void Objectives_EvidenceProgressOnlyCompletesAtTotal()
        {
            var cfg = new GameConfigReader(Cfg(("objectives.perContract", 1L), ("objectives.pool", new List<object>
            {
                new Dictionary<string, object> { ["kind"] = "findEvidence", ["target"] = 3L, ["text"] = "找齐 3 条证据" },
            })));
            var o = new Whisper.Gameplay.Objectives.ObjectiveSystem(cfg);
            o.BeginContract(2u);
            o.ReportEvidence(2, 3);
            Assert.AreEqual(0, o.CompletedCount, "2/3 不该完成");
            Assert.AreEqual(2f, o.All[0].Progress, 1e-4f);
            o.ReportEvidence(3, 3);
            Assert.AreEqual(1, o.CompletedCount, "3/3 必须完成");
        }

        // ───────────────── 等级 ─────────────────

        [Test]
        public void Progression_LevelsUp_AndCarriesRemainder()
        {
            var p = new Progression(new GameConfigReader(Cfg(
                ("progression.maxLevel", 10L), ("progression.xp.base", 100L), ("progression.xp.perLevel", 0L))));
            Assert.AreEqual(1, p.Level);
            Assert.AreEqual(100, p.XpForNextLevel);

            p.AddXp(150);
            Assert.AreEqual(2, p.Level, "150 经验应升到 2 级");
            Assert.AreEqual(50, p.XpInLevel, "余数必须保留（否则每次升级都白丢经验）");
            Assert.AreEqual(2, p.BestLevel);
        }

        [Test]
        public void Progression_MultiLevelUp_InOneGrant()
        {
            var p = new Progression(new GameConfigReader(Cfg(
                ("progression.maxLevel", 50L), ("progression.xp.base", 100L), ("progression.xp.perLevel", 0L))));
            p.AddXp(350);
            Assert.AreEqual(4, p.Level, "350 经验 = 连升 3 级（用 if 会只升 1 级）");
            Assert.AreEqual(50, p.XpInLevel);
        }

        [Test]
        public void Progression_Prestige_ResetsLevelButKeepsMoneyAndFragments()
        {
            var p = new Progression(new GameConfigReader(Cfg(
                ("progression.maxLevel", 2L), ("progression.xp.base", 50L), ("progression.xp.perLevel", 0L),
                ("progression.prestige.atLevel", 2L))));
            p.AddMoney(500);
            p.AddFragments(30);
            p.AddXp(100);                       // 升到 2（满级）
            Assert.IsTrue(p.CanPrestige);
            Assert.IsTrue(p.TryPrestige());
            Assert.AreEqual(1, p.Level, "声望后等级归 1");
            Assert.AreEqual(1, p.Prestige);
            Assert.AreEqual(500, p.Money, "钱必须保留（官方语义）");
            Assert.AreEqual(30, p.Fragments, "碎片必须保留");
            Assert.AreEqual(2, p.BestLevel, "历史最高等级不清零");
        }

        [Test]
        public void Progression_MoneyNeverGoesNegative()
        {
            var p = new Progression(new GameConfigReader(Cfg(("progression.maxLevel", 10L))));
            p.AddMoney(100);
            Assert.IsFalse(p.SpendMoney(101), "钱不够必须拒绝");
            Assert.AreEqual(100, p.Money, "拒绝时不得扣钱");
            Assert.IsTrue(p.SpendMoney(100));
            Assert.AreEqual(0, p.Money);
        }

        // ───────────────── 商店 ─────────────────

        static Shop MakeShop()
        {
            var shop = new Shop();
            // Shop.Load 读静态 GameConfig —— 为了让测试不依赖仓库数据，
            // 这里直接构造并注入条目（Load 之外不假设任何全局状态）。
            shop.Load(new GameConfigReader(Cfg(("shop.items", new List<object>
            {
                new Dictionary<string, object> { ["id"] = "emf_t1", ["tier"] = 1L, ["price"] = 0L, ["label"] = "EMF I" },
                new Dictionary<string, object> { ["id"] = "emf_t2", ["tier"] = 2L, ["price"] = 100L, ["label"] = "EMF II" },
                new Dictionary<string, object> { ["id"] = "emf_t3", ["tier"] = 3L, ["price"] = 300L, ["label"] = "EMF III" },
            }))));
            return shop;
        }

        [Test]
        public void Shop_RequiresPreviousTier()
        {
            var shop = MakeShop();
            var p = new Progression(new GameConfigReader(Cfg(("progression.maxLevel", 100L))));
            p.AddMoney(10000);

            Assert.IsFalse(shop.CanBuy(p, "emf_t2", out var why), "没买 Tier I 就不该能买 Tier II");
            StringAssert.Contains("Tier 1", why);

            Assert.IsTrue(shop.TryBuy(p, "emf_t1", out _));
            Assert.IsTrue(shop.CanBuy(p, "emf_t2", out _), "买了 Tier I 后 Tier II 应可买");
            Assert.IsFalse(shop.CanBuy(p, "emf_t3", out _), "Tier III 仍需先有 Tier II");
        }

        [Test]
        public void Shop_DoesNotSpendWhenRejected()
        {
            var shop = MakeShop();
            var p = new Progression(new GameConfigReader(Cfg(("progression.maxLevel", 100L))));
            p.AddMoney(50);
            Assert.IsFalse(shop.TryBuy(p, "emf_t2", out _));
            Assert.AreEqual(50, p.Money, "购买失败不得扣钱");
        }

        [Test]
        public void Shop_SlotAndEquip()
        {
            var shop = MakeShop();
            var p = new Progression(new GameConfigReader(Cfg(("progression.maxLevel", 100L))));
            p.AddMoney(1000);
            shop.TryBuy(p, "emf_t1", out _);
            Assert.AreEqual("emf", Shop.SlotOf("emf_t1"));
            Assert.IsTrue(shop.TryEquip("emf_t1", out _));
            Assert.AreEqual("emf_t1", shop.Equipped("emf"));
            shop.TryBuy(p, "emf_t2", out _);
            shop.TryEquip("emf_t2", out _);
            Assert.AreEqual("emf_t2", shop.Equipped("emf"), "同槽位应被替换");
        }

        // ───────────────── 任务 ─────────────────

        [Test]
        public void Tasks_DeterministicForSameDay()
        {
            var a = new TaskSystem(new GameConfigReader(Cfg(("tasks.dailyCount", 2L), ("tasks.weeklyCount", 1L))));
            var b = new TaskSystem(new GameConfigReader(Cfg(("tasks.dailyCount", 2L), ("tasks.weeklyCount", 1L))));
            a.RollForDay(7); b.RollForDay(7);
            Assert.AreEqual(a.Daily.Count, b.Daily.Count);
            for (int i = 0; i < a.Daily.Count; i++)
                Assert.AreEqual(a.Daily[i].Text, b.Daily[i].Text, "同一天必须生成同一组任务（跨端一致）");
        }

        [Test]
        public void Tasks_RollIsIdempotent_AndProgressSurvives()
        {
            var t = new TaskSystem(new GameConfigReader(Cfg(("tasks.dailyCount", 1L), ("tasks.weeklyCount", 1L))));
            t.RollForDay(3);
            t.ReportPhoto();
            float before = t.Daily.Count > 0 ? t.Daily[0].Progress : 0f;
            t.RollForDay(3);   // 同一天重复调用
            Assert.AreEqual(before, t.Daily.Count > 0 ? t.Daily[0].Progress : 0f,
                "同一天重复 Roll 不得重掷（否则玩家会丢进度）");
        }

        [Test]
        public void Tasks_SwitchingDayRegenerates()
        {
            var t = new TaskSystem(new GameConfigReader(Cfg(("tasks.dailyCount", 2L), ("tasks.weeklyCount", 1L))));
            t.RollForDay(1);
            int n1 = t.Daily.Count;
            t.RollForDay(2);
            Assert.AreEqual(n1, t.Daily.Count);
            Assert.AreEqual(2, t.DayIndex);
        }

        // ───────────────── 电闸 ─────────────────

        static PowerSystem MakePower()
            => new PowerSystem(new GameConfigReader(Cfg(("power.breaker.interactRadiusM", 2f))));

        [Test]
        public void Power_StartsOff_AndLightsNeedBothBreakerAndSwitch()
        {
            var pw = MakePower();
            pw.RegisterRoomLight("hall", true);
            pw.ToggleRoomLight("hall", true);
            Assert.IsFalse(pw.IsRoomLit("hall"), "总闸没合，灯不该亮（官方的开局压力）");

            pw.ToggleBreaker(true);
            Assert.IsTrue(pw.IsRoomLit("hall"), "合闸 + 开关 ON → 亮");
            Assert.IsTrue(pw.IsSwitchOn("hall"), "开关状态与是否亮着是两件事");

            pw.ToggleBreaker(false);
            Assert.IsFalse(pw.IsRoomLit("hall"));
            Assert.IsTrue(pw.IsSwitchOn("hall"), "断电不该丢掉开关位置");
            Assert.AreEqual(0, pw.LitRoomCount);
        }

        [Test]
        public void Power_RoomWithoutLight_NeverLit()
        {
            var pw = MakePower();
            pw.RegisterRoomLight("closet", false);
            pw.ToggleBreaker(true);
            Assert.IsFalse(pw.ToggleRoomLight("closet"));
            Assert.IsFalse(pw.IsRoomLit("closet"));
        }

        [Test]
        public void Power_BreakerInteractRadius()
        {
            var pw = MakePower();
            pw.PlaceBreaker("morgue", 10f, 10f);
            Assert.IsTrue(pw.PlayerNearBreaker(11f, 10f), "1m 内算够得着");
            Assert.IsFalse(pw.PlayerNearBreaker(13f, 10f), "3m 外够不着");
        }

        // ───────────────── 互动 ─────────────────

        static InteractionSystem MakeInteraction(PowerSystem pw)
            => new InteractionSystem(new GameConfigReader(Cfg(
                ("interaction.ghost.intervalMinSec", 1f),
                ("interaction.ghost.intervalMaxSec", 1f))), pw);

        [Test]
        public void Interaction_TicksOnInterval_AndProducesSound()
        {
            var pw = MakePower();
            pw.RegisterRoomLight("hall", true);
            pw.PlaceBreaker("hall", 0f, 0f);
            var ix = MakeInteraction(pw);
            var rooms = new List<string> { "hall" };

            float r = 0.1f;
            float Roll() { r += 0.13f; if (r >= 1f) r -= 1f; return r; }

            Assert.AreEqual(0, ix.GhostInteractCount);
            ix.Tick(0.5f, Roll, "hall", 0f, 0f, rooms);
            Assert.AreEqual(0, ix.GhostInteractCount, "间隔未到不该触发");
            ix.Tick(0.6f, Roll, "hall", 0f, 0f, rooms);
            Assert.AreEqual(1, ix.GhostInteractCount, "间隔到了必须触发（否则鬼完全没有存在感）");
            Assert.IsFalse(string.IsNullOrEmpty(ix.LastText), "每次互动都必须有可读文案");
        }

        [Test]
        public void Interaction_StimulusCarriesSourceAndRadius()
        {
            var pw = MakePower();
            var ix = MakeInteraction(pw);
            var rooms = new List<string> { "hall" };
            float r = 0.85f;   // 落在 ThrowObject 区间
            float Roll() { r += 0.01f; return r >= 1f ? 0.99f : r; }
            ix.Tick(1.1f, Roll, "hall", 3f, 4f, rooms);
            var st = InteractionSystem.ToStimulus(ix.Last, tick: 99);
            StringAssert.StartsWith("ghost_", st.SourceKey, "声纹必须标明来自鬼互动");
            Assert.AreEqual("ghost", st.Type);
            Assert.Greater(st.Intensity, 0f, "扔东西必须发声（这是玩家唯一的线索）");
            Assert.AreEqual(3f, st.X, 1e-4f);
            Assert.AreEqual(4f, st.Z, 1e-4f);
        }

        [Test]
        public void Interaction_BreakerOffOnlyWhenOn()
        {
            var pw = MakePower();
            pw.RegisterRoomLight("hall", true);
            pw.PlaceBreaker("hall", 0f, 0f);
            Assert.IsFalse(pw.ToggleBreaker(false), "总闸本来就断，再断一次不该算状态变化");
            pw.ToggleBreaker(true);
            Assert.IsTrue(pw.ToggleBreaker(false), "开着的时候被关掉才算一次事件");
        }
    }
}
