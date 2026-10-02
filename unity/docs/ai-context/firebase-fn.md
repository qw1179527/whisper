# Firebase Functions + Play Developer API + RTDN 摘要（喂入包 · 状态：**待填充**）

> 铁律：本文件未填充前，**AI 不得书写任何 Firebase / Play Developer API 调用**（V9 §27.1）。

## 需要填充的内容清单
- [ ] `issueTokens`：入参 `{uid, appVersion}` → 校验 `minVersion`（Remote Config）→ 查 `bans`（10 分钟 LRU 缓存）→ 签发 Photon Custom Auth 令牌（HMAC）+ Vivox 令牌 → 返回双令牌 + `serverTime`；TTL 90 分钟、幂等、拒签带原因码（V9 §29 P-B1）
- [ ] `verifyPurchase`：票据幂等（`entitlements/{purchaseToken}`）→ Play Developer API 校验 `purchaseState===0` → 归属校验（`obfuscatedAccountId === HMAC(uid)`）→ 事务写票据与权益 → 服务端 acknowledge（3 天内）（V9 §15.1 / P-B3）
- [ ] 举报与封禁：`reports` 写入 → 人工处置三选一（驳回 / 警告 -15 信誉 / 封禁 7~30 天写 `bans`）→ 封禁在下次 `issueTokens` 拦截；SLO 48 小时；**永不自动封禁**、证据不含语音本体（V9 §29 P-B5）
- [ ] RTDN：`ONE_TIME_PRODUCT_VOIDED` 回滚权益，`messageId` 幂等；每日 `voidedpurchases` 对账兜底
- [ ] 六集合数据模型与安全规则（`players` / `entitlements` / `bans` / `reports` / `room_usage` / `room_counters`）（V9 §15.2）
- [ ] 配额预算：DAU 2,000 时合计 ≈3,000 读 / ≈1,190 写（Spark 日配额 5 万读 / 2 万写，占用 6%/6%）

## 出处
- Firebase 官方文档（Functions Node 20 / Firestore / Auth / Remote Config）+ Google Play Developer API 与 RTDN 文档
- 版本下限：Firebase Unity SDK ≥ 12.10.0（16KB 页对齐，V9 §12）
