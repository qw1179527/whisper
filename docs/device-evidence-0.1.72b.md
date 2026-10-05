# 真机取证续记 · 0.1.72（第 6 轮补充）

> 承接 `device-evidence-0.1.72.md`。本文件只记**新增的确定性事实**与**下一步精确操作**。

## 1. 新增事实：主界面有「兜底开局」，会吞掉点偏的点击

`MenuScene.cs:1298-1300`：
```csharp
_lastTouch = "兜底开局 @" + sp.x + "," + (Screen.height - sp.y);
Debug.Log("[Whisper] 主界面兜底开局：" + _lastTouch);
OnOption(0, "开始调查（单人）");     // ← 点在非板项处 = 直接开单人对局
```
**后果**：我上一轮点 (1400, 455) 大概率落到兜底分支 → 直接进了单人对局 → 房间里当然没有房间码。
这也解释了"截图 SHA 变了但没有建房回执"。

## 2. 新增事实：板项有**精确屏幕矩形**，可直接算落点（不必猜坐标）

HUD 诊断行里有 `_btnRects`（`MenuScene.cs:1410-1411` 输出到 `_hint`）：
```
BoardItem2[-360,1314-360,1246] BoardItem3[-360,1314-360,1246] BoardItem4[360,1314-360,1246]
BoardItem5[360,1314-360,1246] ShopScreenText[-360,1298-360,1262] IdCardText[360,1294-360,1266]
RoomCodeText[2440,24-3160,-24] BoardHint[1040,899,-1760,851]
```
格式是 `[anchorPxX, anchorPxY, sizeDeltaX, sizeDeltaY]`（画布单位）。
**板项落点算法**（与代码同口径）：
- `BoardItem1[360,1314-360,1246]` 对应板上「多人联机」（标题序数组 `BoardItemTitles[1]`）；
- 屏幕坐标 = `anchorPx * scaleFactor`（本项目 `scaleFactor ≈ 1.458`，真机 2800×1280 / 参考 1920×1080）；
- 故 **多人联机**纸片中心 ≈ `x = 360*1.458 ≈ 525`？ —— ⚠ 注意矩形记的是**锚点像素**，`x=360` 出现在多个板项上，
  说明它是**画布锚点**而非最终屏幕位置；真正的落点应以 `_hint` 里给出的整串为准，
  或**直接读 `_lastTouch` 确认点击落在哪一项**（见下）。

## 3. 下一步精确操作（按顺序，不要跳）

```powershell
# ① 先回到主界面（当前在板模式）：按 Esc 或点板外
adb shell input keyevent KEYCODE_ESCAPE

# ② 空格进入操作视角（板模式），截图留底
adb shell input keyevent KEYCODE_SPACE ; sleep 2
adb shell screencap -p /sdcard/s0.png ; adb pull /sdcard/s0.png <ev>/s0.png

# ③ 点一处**确定在纸片上**的位置：用 s0.png 里"多人联机"文字的像素中心
#    （2800x1280 下该文字中心约 x≈950*2800/1918≈1387, y≈315*1280/877≈460 —— 我上一轮就是点的这里，
#     若仍走兜底，说明射线没命中板拾取面，需改用"点板面中心再靠近纸片"策略）
adb shell input tap 1387 460 ; sleep 2
adb shell screencap -p /sdcard/s1.png ; adb pull /sdcard/s1.png <ev>/s1.png

# ④ 读业务回执：com.whisper.projectwhisper 的 logcat（release 下 Debug.Log 时有时无）
adb logcat -d -s Unity | Select-String "Whisper" | Select-Object -Last 20
#    期望看到：「主界面兜底开局」= 没命中板项（需换落点）；
#             或「菜单板选项 1：多人联机」= 命中（则房间码应出现，若仍无则是 ShowRoomCode 的问题）

# ⑤ 读 HUD 的触摸行（像素级取证，工具已备）
python tools/crop_hud.py <ev>/s1.png <ev>/touch.png 830 292 1600 320 4   # 再缩半以避开 8192px 上限
```

## 4. 本轮新增工具

`tools/crop_hud.py` —— 裁切 + 放大真机截图的指定像素区，用于读 HUD 小字回执。
（**为什么必须要它**：本项目纪律要求判定"点击是否被业务接住"时不能看像素差，必须读业务回执文本；
而回执是 2800×1280 图里的 24px 小字，直接 `read_image` 读不清。用法见文件头。）

⚠ 踩坑：`read_image` 对**任何一边 >8192px** 的图直接拒绝 → 放大倍数 × 裁切宽度必须 ≤8192
（本次 2800×3=8400 被拒，改 2 倍或先缩小）。
