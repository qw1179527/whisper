# Fusion 2 API 摘要（喂入包 · 状态：**待填充**）

> 铁律：本文件未填充前，**AI 不得书写任何 Fusion API 调用**——找不到出处就提问，不许编造（V9 §27.1）。

## 需要填充的内容清单
- [ ] `NetworkBehaviour` 生命周期（`Spawned` / `Despawned` / `FixedUpdateNetwork`）
- [ ] `[Networked]` 属性写法与 `ChangeDetector` 用法
- [ ] `Rpc` 属性族（`RpcTargets` / `RpcSources`）与瞬时事件写法
- [ ] `Runner` 启动参数（Shared Mode / 房间码加入 / Tick 配置）
- [ ] Host 迁移回调与迁移期遮罩挂钩点
- [ ] 与 V9 §13.4 对应：固定 60 Tick、每 3 Tick（50ms）一批发送、带宽预算下行 ≤12KB/s / 上行 ≤6KB/s

## 出处
- Photon Fusion 2 官方文档（**锁定版本**对应的那一版；版本号见 `unity/dependency-lock.json` → `thirdParty.photonFusion2`）
- 本文件只在"每月偿债日"更新（V9 §27.1），改动需记 changelog 摘要
