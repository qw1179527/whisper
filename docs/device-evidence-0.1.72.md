# 真机取证记录 · 0.1.72（2026-10-05 · 会话内第 5 轮）

> 目的：把「建房」这条路径的**真机事实**钉下来，避免下一轮从零开始。
> 设备 RMX5062 / Android 16；包 `com.whisper.projectwhisper`；APK `whisper-android-0.1.72-code72.apk`（36.1 MB）。

## 1. 出包与装机：✅ 成功

```
bash tools/build-android.sh 72
  → Unity rc=0
  → ProjectSettings 落盘校验：bundleVersion 磁盘='0.1.72' · versionCode 磁盘='72' → 一致
  → whisper-android-0.1.72-code72.apk · 36136733 字节（34 MB）
bash tools/device-verify.sh 72
  → 唤醒 mWakefulness=Awake → 安装 Success → 启动 pid=25675 → 截图 OK
```

## 2. 本轮改动在真机上**可见**（正面证据）

截图 `_evidence/build72-device/menu.png` 与 `board-mode.png`：

| 证据 | 内容 |
|---|---|
| 提示条文案已更新 | 屏上显示 **「按 空格 或 点击菜单板 进入操作界面　｜　点这里 = 输入房间码加入」**（第 4 轮的加入入口已上屏） |
| 房间码显示位已建 | HUD 诊断行出现 **`RoomCodeText[2440,24-3160,-24]`**（第 3 轮新增的 `_roomCodeText` 确实建出来了） |
| 板模式正常 | 按空格后进入操作视角，6 选项齐上板：`训练 / 多人联机 / 单人游戏` + `退出游戏 / 制作人员 / 选项` |

## 3. ⚠ 未通过的一项：点「多人联机」后**房间码没出现**

步骤与结果：
1. 点击屏幕 (1400, 455)（"多人联机"文字处）；
2. 前后截图 SHA256 **不同**（`20F7FD…` → `CE7BBF…`）→ **点击确实生效了**；画面里 Tick 从 1028 前进到 1316；
3. **但：房间码文字没出现，提示条也没变成建房状态文案。**

### 可能原因（按可能性排序，**下一轮先验证再改**）

1. **点击没被判定为板项命中**：`HandleBoardInput` 用"屏幕点 → 各 `BoardItem` 的局部坐标 → `rect.Contains`"判定，
   我的点击落点可能落在文字之外（板项矩形由 `PlaceAtScreen` 给定 470×100，但文字是居中的）。
   → 验证法：点"多人联机"文字**正中**再试；或直接看 HUD 里的 `_lastTouch`（应显示 `菜单板选项 1：多人联机`）。
   ⚠ `_lastTouch` 出现在 HUD 中段那一行，**截图取证时要裁切放大**才能读清。
2. **可能命中了但 `StartHostSession()` 早退**：`GetComponent<GameBootstrap>()` 返回 null → 会 `Note("组合根缺失：无法建房")`（弹窗），
   但截图上**没有弹窗**，故这条可能性较低。
3. **可能真的建了房但 `_roomCodeText` 没显示**：`ShowRoomCode` 里有 `if (_roomCodeText == null) return;` ——
   若 `BuildBoardUi` 早退过（画布未建）就会是 null。但 HUD 诊断里明明有 `RoomCodeText[...]`，故这条也较低。
4. **`LanAddress.TryGetLocal` 返回 false**（设备只有 loopback？移动网络下应该有全局 IPv6/私网 IPv4）→
   会走 `_roomCodeText.text = "建房未成功\n" + NetStatus`，文字**应该出现**（只是内容是失败）。截图上没有，故不成立。

### 下一轮的最小验证路径（不要跳）

```powershell
# 1) 点文字正中（板项矩形中心），并立刻截图
adb shell input tap 1400 458 ; adb shell screencap -p /sdcard/x.png ; adb pull /sdcard/x.png <ev>/tap2.png
# 2) 读 HUD 中段的 _lastTouch（裁切放大才能看清）：
#    期望出现「菜单板选项 1：多人联机」
# 3) 若 _lastTouch 正确但仍无房间码 → 在 MenuScene.StartHostSession 里
#    把 boot.NetStatus 直接写进 _boardHint 之外，再补一条 HUD 专行（便于截屏取证）
```

**取证纪律提醒（本轮踩过）**：`adb shell input tap` 的生效判据是**两次截图 SHA256 不同**，
但"画面变了"**不等于**"我的点击被业务逻辑接住了"（本次 Tick 前进也会让 SHA 变）——
要判定业务是否接住，必须读 `_lastTouch` 这类**业务侧回执**，而不是像素差。

## 4. 本轮门禁

| 门禁 | 结果 |
|---|---|
| `tools/unity-syntax-check.sh` | exit=0 |
| `tools/unity-tests.sh EditMode` | **total=62 passed=62 failed=0** |
| 出包 | `build-android.sh 72` → rc=0，APK 36.1 MB |
| 装机 | `device-verify.sh 72` → Success |
