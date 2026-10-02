using System;
using Whisper.Core.Contracts;

namespace Whisper.Backend
{
    /// <summary>
    /// IBackendService 的**本机桩实现**（零第三方依赖）。
    ///
    /// 纪律（V9 §13.2）：真 `FirebaseBackendService` 将来放同一目录；
    /// 本机桩不引 Firebase，故可在无 Unity 环境下编译装配。
    ///
    /// 关键语义（V9 §15.2）：Firebase 不可达时客户端进入**无后端模式** ——
    /// 联机与语音不受影响，只失去建房门禁与付费校验。本桩 `IsAvailable == false` 即该模式：
    /// 启动链必须能在"后端不可用"下照常完成，这正是本桩要持续验证的事。
    /// </summary>
    public sealed class LocalBackendService : IBackendService
    {
        public bool IsAvailable => false;   // 无后端模式（桩）

        public void IssueTokens(string appVersion, Action<TokenBundle> onOk, Action<string> onFail)
            => onFail?.Invoke("offline: 无后端模式（桩实现），令牌签发不可用");

        public void VerifyPurchase(string purchaseToken, string productId, Action<bool> onDone)
            => onDone?.Invoke(false);

        public void SubmitReport(string category, string matchId, Action<bool> onDone)
            => onDone?.Invoke(false);
    }
}
