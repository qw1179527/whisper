# 真机取证记录 · 0.1.75（2026-10-05 · 第 9 轮）

> 设备 RMX5062 / Android 16。APK `whisper-android-0.1.75-code75.apk`（36.1 MB），安装 Success。
> 证据目录 `_evidence/build75-device/`。

## ✅ 通过：屏幕中间的大字已去掉（用户第 8 轮要求 ②）

`02-menu.png`（冷启动主界面）逐项核对：
| 原元素 | 现在 |
|---|---|
| `MenuTitle`「PROJECT WHISPER」（大号、锚正中） | **已消失** ✓ |
| `MenuSub`「多人联机恐怖调查 · 3 层疗养院 · 27 种鬼」 | **已消失** ✓ |
| `MenuFallbackHint`「（旧版提示已废弃 · 请用菜单板）」 | **已消失** ✓ |
| 中心区域现在只剩 | 提示条「按 空格 或 点击菜单板 进入操作界面　｜　点这里 = 输入房间码加入」 |

## 🎉 意外收获：帧率从 **30 fps → 59 fps**

`02-menu.png` / `03-board.png` 的 HUD 首行实测：
```
Tick 791  · 59 fps · tickRate=60
Tick 1349 · 59 fps · tickRate=60
```
而此前所有真机截图（0.1.71 / 0.1.72）都是 **30 fps**。本轮唯一与渲染相关的改动是
`Application.runInBackground = true` + `PlayerSettings.runInBackground = true`（第 7 轮）。
推测：失焦/未获焦状态下 Unity 会降频渲染（30fps），开着 runInBackground 后保持满帧。
**⚠ 这是相关性不是因果证明** —— 下一轮应在同一台设备上装回旧版对照，或读 HUD 的帧率构成再定论。

## ⚠ 未通过（且这是关键障碍）：`Space` 进不了板模式

现象：`adb shell input keyevent KEYCODE_SPACE` 后画面几乎不变（Tick 在涨、模拟在跑，但没切到操作视角）。

**根因（决定性证据）**：
```
adb shell dumpsys window | grep mCurrentFocus
→ mCurrentFocus=Window{73d1325 u0 NotificationShade}
```
**焦点在系统通知栏上，不在应用窗口** → 注入的按键与点击全被通知栏消费，应用根本没收到。
这既解释了我这边测不出板模式，也**可能就是用户报「点了没反应」的同一类原因**（应用失焦期间输入无人接收）。

**下一轮取证前必须先做**：
```bash
adb shell input keyevent KEYCODE_BACK     # 关掉通知栏
# 或 adb shell cmd statusbar collapse
adb shell dumpsys window | grep mCurrentFocus   # 必须显示 com.whisper.projectwhisper 才继续
```

## 📌 待办：中心那块旧 UI 面板还在

裁切放大（`center-panel.png`）读到的内容是**旧按钮条**：
`「加入房间 | 退出 | …IPv6 直连」`（暗底、居中偏上）。
即：中间大字已去，但旧 UI 还有这一块占据屏幕中心区域 —— 与用户「屏幕中间的字去掉」的意图相关，
应在**主界面 UI 收尾**（用户约束里的下一步）中随旧 UI 一起废弃，而不是零敲碎打。

## 本轮门禁
| 门禁 | 结果 |
|---|---|
| `tools/unity-syntax-check.sh` | exit=0 |
| `tools/unity-tests.sh EditMode` | **total=62 passed=62 failed=0** |
| 出包 0.1.75 | rc=0 · 36135797 字节 |
| 装机 | Success |
