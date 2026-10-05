using System.Collections.Generic;
using NUnit.Framework;
using Whisper.Gameplay.Level;

namespace Whisper.Tests.EditMode
{
    /// <summary>
    /// Level DSL 加载与校验测试（纯 C#，不依赖 UnityEngine，Edit Mode 可跑）。
    /// 规则必须与 tools/validate-levels.mjs 一致；两侧规则表同步改。
    ///
    /// 夹具修订记录（2026-10-04，本机首次真跑 Edit Mode 测试时发现）：
    ///   本夹具长期停留在旧 DSL，而加载器早已新增四类硬校验，导致两个用例**恒失败**——
    ///   "合法的关卡被判非法"。此前从未被发现，因为测试一直没在真 Unity 里跑过（CI 缺许可证）。
    ///   ① 房间缺 pos:[x,z]（房间重叠检查靠它定位）
    ///   ② 门缺 id，且用了旧字段名 offset（现为 offsetM/widthM）；走廊靠 doorA/doorB 引用具体门
    ///   ③ 事件缺 counterplay（V9 §30.2）
    ///   ④ 事件类型用了 "doorlock"，而配置真源是 "door_lock_shift"
    ///      （LevelLoader 注释里记过同一个坑：blackout/doorlock/static/… 是想当然的简名）
    /// </summary>
    public class LevelLoaderTests
    {
        /// <summary>合法事件段（2 个；V9 §19.2 要求每局 2~3 个）。单独抽出，便于用例精确替换。</summary>
        const string EventsValid = @"{ ""type"": ""blackout"", ""minute"": 6, ""durationSec"": 10, ""counterplay"": ""手电筒照走廊地面确认出口"" },
            { ""type"": ""door_lock_shift"", ""minute"": 9, ""durationSec"": 20, ""counterplay"": ""锁门期间走东西走廊绕行"" }";

        /// <summary>
        /// 合法关卡夹具。几何要点（走廊几何校验很严）：
        ///   r1 x∈[0,8] z∈[0,6]；r2 x∈[8,14] z∈[0,6] —— 仅贴面共墙，XZ 不重叠。
        ///   两门分处 east/west 墙、同一条 x=8 平面上、开口中心都在沿墙 1.3 处、法向相反。
        /// </summary>
        const string Good = @"{
          ""levelId"": ""t_v1"",
          ""_note"": ""注释键必须被忽略"",
          ""rooms"": [
            { ""id"": ""r1"", ""pos"": [0,0], ""size"": [8,3.5,6], ""rotY"": 0, ""floor"": 0, ""kit"": ""hospital_ward"",
              ""doors"": [{ ""id"": ""d_east"", ""wall"": ""east"", ""offsetM"": 0.3, ""widthM"": 2, ""locked"": false }],
              ""props"": [{ ""kit"": ""bed_b"", ""pos"": [1,0,-2], ""rot"": 90 }],
              ""evidencePoint"": true, ""lightZone"": ""pressure"" },
            { ""id"": ""r2"", ""pos"": [8,0], ""size"": [6,3,6], ""rotY"": 0, ""floor"": 0, ""kit"": ""morgue"",
              ""doors"": [{ ""id"": ""d_west"", ""wall"": ""west"", ""offsetM"": 0.3, ""widthM"": 2, ""locked"": false }],
              ""props"": [], ""evidencePoint"": false, ""lightZone"": ""high-risk"" }
          ],
          ""corridors"": [{ ""from"": ""r1"", ""to"": ""r2"", ""doorA"": ""r1/d_east"", ""doorB"": ""r2/d_west"", ""width"": 2.0 }],
          ""events"": [" + EventsValid + @"],
          ""extraction"": { ""standard"": ""r1"", ""deep"": ""r2"" }
        }";

        static readonly HashSet<string> Kits = new HashSet<string> { "hospital_ward", "morgue", "bed_b" };

        [Test]
        public void LoadsValidLevelAndIgnoresCommentKeys()
        {
            var level = LevelLoader.Load(Good, Kits);
            Assert.AreEqual("t_v1", level.LevelId);
            Assert.AreEqual(2, level.Rooms.Count);
            Assert.AreEqual(1, level.Corridors.Count);
            Assert.AreEqual(2, level.Events.Count);
            Assert.IsTrue(level.Rooms[0].EvidencePoint);
            Assert.AreEqual("pressure", level.Rooms[0].LightZone);
            Assert.AreEqual(1f, level.Rooms[0].Props[0].X, 1e-6);   // 夹具里 pos 是 [1,0,-2]
            Assert.AreEqual(-2f, level.Rooms[0].Props[0].Z, 1e-6);
            Assert.AreEqual("r1", level.Extraction.Standard);
            Assert.AreEqual("r2", level.Extraction.Deep);
        }

        [Test]
        public void RejectsUnknownKitAgainstManifest()
        {
            var bad = Good.Replace("\"hospital_ward\"", "\"not_a_kit\"");
            var ex = Assert.Throws<LevelLoader.LevelValidationException>(() => LevelLoader.Load(bad, Kits));
            Assert.IsTrue(ex.Message.Contains("not_a_kit"), ex.Message);
        }

        [Test]
        public void RejectsInvalidLightZoneAndWallAndOffset()
        {
            var ex1 = Assert.Throws<LevelLoader.LevelValidationException>(() =>
                LevelLoader.Load(Good.Replace("\"pressure\"", "\"spooky\""), Kits));
            Assert.IsTrue(ex1.Message.Contains("lightZone"), ex1.Message);

            // 门墙：夹具里的合法值是 east，换成 "up"
            var ex2 = Assert.Throws<LevelLoader.LevelValidationException>(() =>
                LevelLoader.Load(Good.Replace("\"wall\": \"east\"", "\"wall\": \"up\""), Kits));
            Assert.IsTrue(ex2.Message.Contains("wall"), ex2.Message);

            // 门洞越界：offsetM 5.0 + widthM 2 = 7 > 墙长 6
            var ex3 = Assert.Throws<LevelLoader.LevelValidationException>(() =>
                LevelLoader.Load(Good.Replace("\"offsetM\": 0.3", "\"offsetM\": 5.0"), Kits));
            Assert.IsTrue(ex3.Message.Contains("offsetM"), ex3.Message);
        }

        [Test]
        public void RejectsDanglingCorridorAndSameExtractionPoint()
        {
            var ex1 = Assert.Throws<LevelLoader.LevelValidationException>(() =>
                LevelLoader.Load(Good.Replace("\"to\": \"r2\"", "\"to\": \"nowhere\""), Kits));
            Assert.IsTrue(ex1.Message.Contains("走廊终点"), ex1.Message);

            var ex2 = Assert.Throws<LevelLoader.LevelValidationException>(() =>
                LevelLoader.Load(Good.Replace("\"deep\": \"r2\"", "\"deep\": \"r1\""), Kits));
            Assert.IsTrue(ex2.Message.Contains("同一房间"), ex2.Message);
        }

        [Test]
        public void RejectsWrongEventCountAndUnknownEventType()
        {
            // 必须真的把事件减到 1 个：早先的写法只是换个类型，事件数仍是 2（合法）→ 用例永不触发
            var one = Good.Replace(EventsValid, @"{ ""type"": ""blackout"", ""minute"": 6, ""durationSec"": 10, ""counterplay"": ""手电筒"" }");
            var ex1 = Assert.Throws<LevelLoader.LevelValidationException>(() => LevelLoader.Load(one, Kits));
            Assert.IsTrue(ex1.Message.Contains("2~3"), ex1.Message);

            var bad = Good.Replace("\"blackout\"", "\"poltergeist\"");
            var ex2 = Assert.Throws<LevelLoader.LevelValidationException>(() => LevelLoader.Load(bad, Kits));
            Assert.IsTrue(ex2.Message.Contains("事件类型"), ex2.Message);
        }

        [Test]
        public void ReportsAllProblemsAtOnce()
        {
            // 同时注入 4 个互不相关的错误，断言**全部**被一次报出（而不是只报第一条）
            var broken = Good
                .Replace("\"pressure\"", "\"spooky\"")
                .Replace("\"wall\": \"east\"", "\"wall\": \"up\"")
                .Replace(EventsValid, @"{ ""type"": ""poltergeist"", ""minute"": 6, ""durationSec"": -1 }");
            var ex = Assert.Throws<LevelLoader.LevelValidationException>(() => LevelLoader.Load(broken, Kits));
            Assert.GreaterOrEqual(ex.Problems.Count, 4, string.Join(" | ", ex.Problems));
            Assert.IsTrue(ex.Message.Contains("校验失败"), ex.Message);
            Assert.IsTrue(ex.Message.Contains("lightZone"), ex.Message);
            Assert.IsTrue(ex.Message.Contains("wall"), ex.Message);
            Assert.IsTrue(ex.Message.Contains("事件类型"), ex.Message);
            Assert.IsTrue(ex.Message.Contains("counterplay"), ex.Message);
        }
    }
}
