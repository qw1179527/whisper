using System.Collections.Generic;
using NUnit.Framework;
using Whisper.Gameplay.Level;

namespace Whisper.Tests.EditMode
{
    /// <summary>
    /// Level DSL 加载与校验测试（纯 C#，不依赖 UnityEngine，Edit Mode 可跑）。
    /// 规则必须与 tools/validate-levels.mjs 一致；两侧规则表同步改。
    /// </summary>
    public class LevelLoaderTests
    {
        const string Good = @"{
          ""levelId"": ""t_v1"",
          ""_note"": ""注释键必须被忽略"",
          ""rooms"": [
            { ""id"": ""r1"", ""size"": [8,3.5,6], ""kit"": ""hospital_ward"",
              ""doors"": [{ ""wall"": ""north"", ""offset"": 0.3, ""locked"": false }],
              ""props"": [{ ""kit"": ""bed_b"", ""pos"": [1,0,-2], ""rot"": 90 }],
              ""evidencePoint"": true, ""lightZone"": ""pressure"" },
            { ""id"": ""r2"", ""size"": [6,3,6], ""kit"": ""morgue"",
              ""doors"": [], ""props"": [], ""evidencePoint"": false, ""lightZone"": ""high-risk"" }
          ],
          ""corridors"": [{ ""from"": ""r1"", ""to"": ""r2"", ""width"": 2.0 }],
          ""events"": [
            { ""type"": ""blackout"", ""minute"": 6, ""durationSec"": 10 },
            { ""type"": ""doorlock"", ""minute"": 9, ""durationSec"": 20 }
          ],
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

            var ex2 = Assert.Throws<LevelLoader.LevelValidationException>(() =>
                LevelLoader.Load(Good.Replace("\"north\"", "\"up\""), Kits));
            Assert.IsTrue(ex2.Message.Contains("wall"), ex2.Message);

            var ex3 = Assert.Throws<LevelLoader.LevelValidationException>(() =>
                LevelLoader.Load(Good.Replace("\"offset\": 0.3", "\"offset\": 1.7"), Kits));
            Assert.IsTrue(ex3.Message.Contains("offset"), ex3.Message);
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
            // 必须真的把事件减到 1 个：早先的写法只是把 doorlock 换成 blackout，事件数仍是 2（合法）→ 用例永不触发
            var one = Good.Replace(@"{ ""type"": ""doorlock"", ""minute"": 9, ""durationSec"": 20 }", @"{ ""type"": ""blackout"", ""minute"": 9, ""durationSec"": 20 }")
                           .Replace(@"{ ""type"": ""blackout"", ""minute"": 9, ""durationSec"": 20 }", @"{ ""type"": ""static"", ""minute"": 9, ""durationSec"": 20 }");
            one = System.Text.RegularExpressions.Regex.Replace(one, @",\s*\{\s*""type"":\s*""static"".*?\}", "", System.Text.RegularExpressions.RegexOptions.Singleline);
            var ex1 = Assert.Throws<LevelLoader.LevelValidationException>(() => LevelLoader.Load(one, Kits));
            Assert.IsTrue(ex1.Message.Contains("2~3"), ex1.Message);

            var bad = Good.Replace("\"blackout\"", "\"poltergeist\"");
            var ex2 = Assert.Throws<LevelLoader.LevelValidationException>(() => LevelLoader.Load(bad, Kits));
            Assert.IsTrue(ex2.Message.Contains("事件类型"), ex2.Message);
        }

        [Test]
        public void ReportsAllProblemsAtOnce()
        {
            var broken = Good.Replace("\"spooky_placeholder\"", "\"x\"").Replace("\"pressure\"", "\"spooky\"").Replace("\"up\"", "\"north\"");
            var ex = Assert.Throws<LevelLoader.LevelValidationException>(() => LevelLoader.Load(broken, Kits));
            Assert.GreaterOrEqual(ex.Problems.Count, 1);
            Assert.IsTrue(ex.Message.Contains("校验失败"), ex.Message);
        }
    }
}
