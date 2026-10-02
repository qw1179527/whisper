# Vivox 16.x API 摘要（喂入包 · 状态：**待填充**）

> 铁律：本文件未填充前，**AI 不得书写任何 Vivox API 调用**（V9 §27.1）。

## 需要填充的内容清单
- [ ] 登录：Token 签发与 `Login` 流程（令牌由后端 `issueTokens` 提供，V9 §15.1）
- [ ] 频道：加入/离开、3D 位置频道参数（频道名 = 房间码 + 局序号）
- [ ] **能量回调**：本地参与者音频能量 / 电平回调（V9 §13.5 采集源优先级 ①，首选，零额外采集）
- [ ] **external audio input**：由 Unity 侧单一 `AudioRecord` 供流（优先级 ②）
- [ ] `LocalMute`：对局内长按头像屏蔽（V9 §16 合规第 3 项）
- [ ] 区域故障降级：退化为「仅轮盘 + 文字」，游戏不中断
- [ ] 16KB 页对齐要求 ≥ 16.6.2（V9 §12）

## 出处
- Unity Vivox 官方文档（版本见 `unity/dependency-lock.json` → `thirdParty.unityVivox`）
- 关键风险记录：Android 上带 AEC 的 VOIP 采集会独占 `AudioRecord`，**必须真机验证**三方案 ①/②/③（V9 §13.5 / V8 N1；验证件见仓库 `native/micprobe/`）
