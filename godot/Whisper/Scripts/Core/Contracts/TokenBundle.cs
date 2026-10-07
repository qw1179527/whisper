namespace Whisper.Core.Contracts
{
    /// <summary>双令牌 + 服务端时间（V9 §29 P-B1：令牌 TTL 90 分钟，签发幂等）。</summary>
    public readonly struct TokenBundle
    {
        public readonly string PhotonToken;
        public readonly string VivoxToken;
        public readonly long ServerTimeUnixMs;

        public TokenBundle(string photonToken, string vivoxToken, long serverTimeUnixMs)
        {
            PhotonToken = photonToken;
            VivoxToken = vivoxToken;
            ServerTimeUnixMs = serverTimeUnixMs;
        }
    }
}
