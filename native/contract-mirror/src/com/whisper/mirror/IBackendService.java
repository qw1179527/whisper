package com.whisper.mirror;

/** 契约镜像：对应 IBackendService.cs（V9 §13.2 / §15）。 */
public interface IBackendService {
    boolean isAvailable();                             // false = 无后端模式（§15.2）
    void issueTokens(String appVersion, TokenCallback cb);
    void verifyPurchase(String purchaseToken, String productId, PurchaseCallback cb);
    void submitReport(String category, String matchId, ReportCallback cb);

    interface TokenCallback { void onOk(TokenBundle bundle); void onFail(String reason); }
    interface PurchaseCallback { void onDone(boolean granted); }
    interface ReportCallback { void onDone(boolean accepted); }
}
