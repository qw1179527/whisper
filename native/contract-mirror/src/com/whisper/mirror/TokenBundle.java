package com.whisper.mirror;

/** 契约镜像：对应 TokenBundle.cs（双令牌 + 服务端时间，TTL 90 分钟，V9 §29 P-B1）。 */
public final class TokenBundle {
    public final String photonToken;
    public final String vivoxToken;
    public final long serverTimeUnixMs;

    public TokenBundle(String photonToken, String vivoxToken, long serverTimeUnixMs) {
        this.photonToken = photonToken; this.vivoxToken = vivoxToken; this.serverTimeUnixMs = serverTimeUnixMs;
    }
}
