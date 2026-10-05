# 0.1.79 真机取证与两个待定问题（2026-10-05 · 第 19 轮）

> 本轮在真机上验 P0 接线时撞到两个现象，**证据都不足以定论**，如实记录，不做结论。

## 一、已确证：冷启动有一段**长黑屏**（约 40 秒）

时间线（`_evidence/build79-session/`）：

| 时刻 | 动作 | 结果 |
|---|---|---|
| T+0 | `am start` | 焦点 = 应用 ✓ |
| T+14s | 截图 `02-hall.png` | **纯黑**（20,237 字节） |
| T+17s | 点击 | 截图 `03-after-tap.png` | **近黑**（17,032 字节，SHA 与 02 不同） |
| T+29s | 等待 | 截图 `04-later.png` | **不是应用**（3,042,533 字节，是桌面！） |

**确证的部分**：`02`/`03` 两帧都是"近全黑 + 字节数极小"，且两张 SHA 不同 → **不是渲染停止**，
而是"画面本身几乎全黑"。这与本项目既有事故描述一致（黑屏加载期），但**本轮没有测到它变亮的时刻**。

**未确证**：黑屏到底持续多久、之后是否正常渲染 —— 因为 `04` 拿到的不是应用画面。

## 二、未确证：`dumpsys` 焦点与 `screencap` 内容**不自洽**

现象：命令报告
```
mCurrentFocus=Window{b88a359 u0 com.whisper.projectwhisper/...UnityPlayerGameActivity}
pidof com.whisper.projectwhisper → 27318
```
但同一时刻的 `screencap` 内容**是系统桌面**。

这与第 10 轮记录的设备陷阱同族（`mWakefulness=Dozing` + `NotificationShade` 抢焦点
→ 截图与"以为的状态"不一致）。本轮证据不足以区分：
① 应用真被切到后台而 `dumpsys` 缓存了旧焦点；
② `screencap` 抓到了 compositor 的过期帧；
③ ColorOS 的某个浮层盖住了应用窗口。

**下轮必须先做**（否则任何真机结论都不可靠）：
```bash
adb shell dumpsys window | grep -E "mCurrentFocus|mFocusedApp"   # 两个都看，互相印证
adb shell dumpsys activity activities | grep -E "ResumedActivity|topResumedActivity"
adb shell input keyevent KEYCODE_WAKEUP && adb shell cmd statusbar collapse
adb shell screencap -p /sdcard/x.png && adb pull /sdcard/x.png x.png
# 关键：**连续两张截图**，若内容与焦点声明矛盾，先解决环境问题再谈功能
```

## 三、本轮代码进展（门禁级已验证）

| 项 | 内容 |
|---|---|
| 修掉自造冲突 | 上一轮我用 `AppendStatus(SessionStatus)` 每帧写 `_status`，会**覆盖既有 HUD 诊断行**（接口/关卡/玩家/交互次数那套取证通道）。改为在 HUD `string.Format` 里**追加**一条（`{14}`），不覆盖任何既有行 |
| 门禁 | `unity-syntax-check` exit=0 · `unity-tests EditMode` **62/62** |
| 交付包 | `whisper-android-0.1.79-code79.apk`（36,270,453 字节）· 装机 Success |

**仍未验证**（本轮没拿到可用截图）：P0 接线在真机上是否真的在跑。
判据已设计好：**HUD 诊断末行 `SessionStatus` 里的"理智 %"随时间变化** ⇒ `GameSession.Tick` 真在被调用。

## 四、下轮建议顺序

1. **先解决真机取证环境**（焦点自洽性）—— 否则后面每一步都不可信
2. 再验 P0：`SessionStatus` 的理智数字是否在变
3. 然后做"逐项替换"（把 `GameBootstrap` 的简化逻辑换成 Session 的结论），每项一次门禁
4. 货车其余功能（车尾键盘控坡道、主门钥匙、监控电脑/地图面板交互、装备墙取用放回）
5. 货车视觉验证（至今只有门禁级验证，无渲染截图）

## 五、本轮教训

**"命令报的状态"与"实际看到的画面"可能矛盾** —— 本会话已因此浪费多轮。
纪律：**任何真机结论必须有两个相互独立的证据**（例如 `dumpsys` 焦点 + `dumpsys activity` 的 ResumedActivity，
再加连续两张截图）；单一证据不足以定论，更不能据此改代码。
