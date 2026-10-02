using System;

namespace Whisper.Core.Contracts
{
    /// <summary>
    /// 后端服务契约（V9 §13.2 / §15）。对局进行中客户端与 Firebase 零交互，
    /// 只在「登录、建房前校验、购买、举报、会话结束」五个时刻被触碰。
    /// Firebase 的实现类是唯一允许 import 第三方命名空间的位置（Assets/Scripts/Backend/）。
    /// </summary>
    public interface IBackendService
    {
        /// <summary>Firebase 整体不可达 → 客户端进入「无后端模式」：联机与语音不受影响，仅失去建房门禁（V9 §15.2）。</summary>
        bool IsAvailable { get; }

        /// <summary>V9 §29 P-B1：校验 minVersion → 查 bans → 签发 Photon 与 Vivox 双令牌（TTL 90 分钟）。</summary>
        void IssueTokens(string appVersion, Action<TokenBundle> onOk, Action<string> onFail);

        /// <summary>V9 §29 P-B3：客户端零授权逻辑；票据幂等 + 归属校验 + 服务端 acknowledge。</summary>
        void VerifyPurchase(string purchaseToken, string productId, Action<bool> onDone);

        /// <summary>V9 §29 P-B5：举报落库留痕（合规红线：证据不含语音本体）。</summary>
        void SubmitReport(string category, string matchId, Action<bool> onDone);
    }
}
