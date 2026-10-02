using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Whisper.Core;
using Whisper.Gameplay.Config;
using Whisper.Runtime;

namespace Whisper.Tests.PlayMode
{
    /// <summary>
    /// Boot 冒烟（Play Mode，需 UnityEngine）。
    ///
    /// 覆盖对象：组合根 GameBootstrap 的四步启动链 ——
    ///   ① 读配置表 ② 注入三接口 ③ 装载关卡 ④ 进入 Play 循环
    /// 这正是不在本机 .NET 跑手覆盖范围内的那部分（它引用 UnityEngine），必须靠本测试与真机验收。
    /// </summary>
    public class BootSmokeTests
    {
        [SetUp]
        public void SetUp()
        {
            // 组合根会注入静态定位器；用例之间必须清干净（D7 的 fail-fast 也依赖这一点）
            Services.Reset();
            GameConfig.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            Services.Reset();
            GameConfig.Reset();
        }

        [UnityTest]
        public IEnumerator BootChainLoadsConfigInjectsServicesAndRunsLoop()
        {
            var go = new GameObject("BootRoot");
            var boot = go.AddComponent<GameBootstrap>();

            // Awake 建 UI；Start 走启动链 —— 等两帧确保都跑过
            yield return null;
            yield return null;

            Assert.IsNull(boot.LastError, "启动报错：" + boot.LastError);
            Assert.IsTrue(boot.Booted, "Boot 未完成");

            // ① 配置表
            Assert.IsTrue(GameConfig.IsLoaded, "配置表未载入");
            Assert.AreEqual(60, GameConfig.GetInt("network.tickRate", -1), "tickRate 必须来自配置表（V9 §13.4）");
            Assert.Greater(GameConfig.GetFloat("monsters.stitcher.speedMps", -1f), 0f, "配置表应能取到怪物速度");

            // ② 三接口注入（A7：组合根职责）
            Assert.IsTrue(Services.HasNet, "INetService 未注入");
            Assert.IsTrue(Services.HasVoice, "IVoiceService 未注入");
            Assert.IsTrue(Services.HasBackend, "IBackendService 未注入");
            Assert.IsFalse(Services.Backend.IsAvailable, "桩后端应处于无后端模式（V9 §15.2）");
            Assert.AreEqual(60, Services.Net.TickRate, "注入的 NetService 应使用配置表的 tickRate");

            // ③ 关卡
            Assert.IsNotNull(boot.Level, "关卡未加载");
            Assert.AreEqual("asylum_v1", boot.Level.LevelId);
            Assert.AreEqual(11, boot.Level.Rooms.Count, "布局重写后为 11 房间（10 任务房 + 太平间前室）");
            // pos 是**最小角点**（与灰盒 __m4.rect 一致）；灰盒是本项目唯一已验证行为的参照物
            var ward01 = boot.Level.Rooms.Find(r => r.Id == "ward_01");
            Assert.IsNotNull(ward01, "缺 ward_01");
            Assert.AreEqual(4f, ward01.MinX, 1e-3f, "pos.x 应为最小角点 x0=4");
            Assert.AreEqual(7f, ward01.MaxX, 1e-3f, "x1 = x0 + 宽 = 4 + 3");
            Assert.IsNotNull(boot.Level.Extraction, "缺撤离双点（V9 §7）");

            // ③.5 §13.4 只读状态面（①③④）：玩法层经接口即可读取，无需引用 Net 模块
            Assert.AreEqual(MatchPhase.Lobby, Services.Net.Phase, "初始阶段应为 Lobby");
            var snap0 = Services.Net.Snapshot;
            Assert.IsNotNull(snap0, "快照不可为空");
            Assert.IsNotNull(snap0.Players, "快照必须含玩家列表");
            Assert.IsNotNull(snap0.Props, "快照必须含道具/门列表");
            var phaseSeen = MatchPhase.Lobby;
            Services.Net.OnPhaseChanged += p => phaseSeen = p;
            ((Whisper.Net.LocalNetService)Services.Net).SetPhase(MatchPhase.Playing);
            Assert.AreEqual(MatchPhase.Playing, phaseSeen, "阶段变更事件未触发");
            Assert.AreEqual(MatchPhase.Playing, Services.Net.Phase, "阶段未生效");

            // ④ Play 循环：Tick 必须真的在涨
            var before = boot.Ticks;
            yield return null;
            yield return null;
            Assert.Greater(boot.Ticks, before, "Play 循环未推进（Ticks 未增长）");

            // HUD 由代码构建且内容可读（C2）
            var text = go.GetComponentInChildren<Text>();
            Assert.IsNotNull(text, "C2：UI 必须由代码构建");
            StringAssert.Contains("Project Whisper", text.text);

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BootReportsMissingConfigInsteadOfSilentFailure()
        {
            var go = new GameObject("BootRootBadConfig");
            var boot = go.AddComponent<GameBootstrap>();
            boot.ConfigResourcePath = "Data/__does_not_exist__";
            yield return null;
            yield return null;

            LogAssert.ignoreFailingMessages = true;
            Assert.IsFalse(boot.Booted, "缺配置表时不应报告启动成功");
            Assert.IsNotNull(boot.LastError, "缺配置表时必须给出确切原因，而不是静默失败");
            LogAssert.ignoreFailingMessages = false;

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BootReportsMissingLevelInsteadOfSilentFailure()
        {
            var go = new GameObject("BootRootBadLevel");
            var boot = go.AddComponent<GameBootstrap>();
            boot.LevelResourcePath = "Levels/__does_not_exist__";
            yield return null;
            yield return null;

            LogAssert.ignoreFailingMessages = true;
            Assert.IsFalse(boot.Booted, "缺关卡时不应报告启动成功");
            Assert.IsNotNull(boot.LastError, "缺关卡时必须给出确切原因");
            LogAssert.ignoreFailingMessages = false;

            Object.Destroy(go);
            yield return null;
        }
    }
}
