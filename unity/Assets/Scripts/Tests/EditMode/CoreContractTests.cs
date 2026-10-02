using NUnit.Framework;
using Whisper.Core;

namespace Whisper.Tests.EditMode
{
    /// <summary>Core 契约测试：DesignTokens（C2 生成物）与服务定位器（V9 §13.2 / §15.2）。</summary>
    public class CoreContractTests
    {
        [TearDown]
        public void TearDown() => Services.Reset();

        [Test]
        public void DesignTokensMatchesProductSource()
        {
            // 口径：DesignTokens 由产品真源 data/design-tokens.json（= 0.6.0 产物内嵌 __TOK）生成。
            // 实测 V9 §11 表11-1 的 7 个色彩 token 与产品只重合 3 个同名同值（paper/ink/blood）；
            // 规范中的 faded/warning/dark 在产品侧不存在，ghost 与产品的 mold 同值异名。
            // 缺口记入机制清单，不在此改名对齐（否则"设计与代码一一对应"会变成"设计被代码改写"）。
            Assert.AreEqual("#F0E6D2", DesignTokens.ColorPaper);
            Assert.AreEqual("#1A1A1A", DesignTokens.ColorInk);
            Assert.AreEqual("#8B1E1E", DesignTokens.ColorBlood);
            Assert.AreEqual("#5C8C6E", DesignTokens.ColorMold);   // ≡ 规范 ghost 的值
            Assert.AreEqual("#D8CFBB", DesignTokens.ColorBone);
            Assert.AreEqual("#4E7A5A", DesignTokens.ColorSafe);
        }

        [Test]
        public void ServicesThrowBeforeInstall()
        {
            Services.Reset();
            Assert.IsFalse(Services.HasNet);
            Assert.Throws<System.InvalidOperationException>(() => { var _ = Services.Net; });
            Assert.Throws<System.InvalidOperationException>(() => { var _ = Services.Voice; });
            Assert.Throws<System.InvalidOperationException>(() => { var _ = Services.Backend; });
        }

        [Test]
        public void ServicesInstallAndExposeStubs()
        {
            Services.Install(new FakeNet());
            Assert.IsTrue(Services.HasNet);
            Assert.IsTrue(Services.Net.IsConnected);
            Assert.AreEqual(60, Services.Net.TickRate);

            // 「无后端模式」（V9 §15.2）：后端未注入不影响联机与语音
            Assert.IsFalse(Services.HasBackend);
            Services.Install(new FakeVoice());
            Assert.IsTrue(Services.HasVoice);
            Assert.AreEqual(0.25f, Services.Voice.LocalEnergy01, 1e-6f);
        }

        sealed class FakeNet : Whisper.Core.Contracts.INetService
        {
            public bool IsHost => true;
            public bool IsConnected => true;
            public int TickRate => 60;
            public void Connect(string roomCode, string authToken) { }
            public void Disconnect() { }
            public void SendVoiceStimulus(in Whisper.Core.Contracts.StimulusEvent stimulus) { }
            public event System.Action<string> OnRoomClosed;
            public event System.Action<bool> OnHostMigration;
            void Silence() { OnRoomClosed?.Invoke(null); OnHostMigration?.Invoke(false); }
        }

        sealed class FakeVoice : Whisper.Core.Contracts.IVoiceService
        {
            public bool IsMuted => false;
            public float LocalEnergy01 => 0.25f;
            public void JoinChannel(string channelName) { }
            public void LeaveChannel() { }
            public void SetLocalMute(string participantId, bool muted) { }
            public event System.Action<string, float> OnParticipantEnergy;
            void Silence() { OnParticipantEnergy?.Invoke(null, 0f); }
        }
    }
}
