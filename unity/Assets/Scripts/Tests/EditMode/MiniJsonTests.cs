using NUnit.Framework;
using Whisper.Gameplay.Level;

namespace Whisper.Tests.EditMode
{
    /// <summary>MiniJson 解析器测试（纯 C#，Edit Mode 可跑）。</summary>
    public class MiniJsonTests
    {
        [Test]
        public void ParsesObjectArrayAndScalars()
        {
            var root = MiniJson.AsMap(MiniJson.Parse("{\"a\":1,\"b\":\"x\",\"c\":true,\"d\":null,\"e\":[1,2.5,false]}"));
            Assert.AreEqual(1L, MiniJson.Get(root, "a"));
            Assert.AreEqual("x", MiniJson.Get(root, "b"));
            Assert.AreEqual(true, MiniJson.Get(root, "c"));
            Assert.IsNull(MiniJson.Get(root, "d"));
            var list = MiniJson.AsList(MiniJson.Get(root, "e"));
            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(2.5, (double)list[1], 1e-9);
        }

        [Test]
        public void ParsesEscapesAndUnicode()
        {
            var root = MiniJson.AsMap(MiniJson.Parse("{\"s\":\"a\\nb\\t\\\"c\\\"\",\"u\":\"\\u4f4e\\u8bed\"}"));
            Assert.AreEqual("a\nb\t\"c\"", MiniJson.Get(root, "s"));
            Assert.AreEqual("低语", MiniJson.Get(root, "u"));
        }

        [Test]
        public void ParsesNumbersAsLongWhenIntegral()
        {
            var root = MiniJson.AsMap(MiniJson.Parse("{\"i\":42,\"f\":3.5,\"neg\":-7,\"exp\":1e3}"));
            Assert.IsInstanceOf<long>(MiniJson.Get(root, "i"));
            Assert.IsInstanceOf<double>(MiniJson.Get(root, "f"));
            Assert.AreEqual(-7f, MiniJson.AsFloat(MiniJson.Get(root, "neg")));
            Assert.AreEqual(1000f, MiniJson.AsFloat(MiniJson.Get(root, "exp")));
        }

        [Test]
        public void RejectsTrailingContent()
        {
            Assert.Throws<System.FormatException>(() => MiniJson.Parse("{\"a\":1} extra"));
        }

        [Test]
        public void RejectsUnclosedString()
        {
            Assert.Throws<System.FormatException>(() => MiniJson.Parse("{\"a\":\"unterminated}"));
        }

        [Test]
        public void RejectsMissingColon()
        {
            Assert.Throws<System.FormatException>(() => MiniJson.Parse("{\"a\" 1}"));
        }
    }
}
