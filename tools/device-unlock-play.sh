#!/usr/bin/env bash
# 解锁 → 启动 → 点「开始调查」→ 取证（一路都留数字，不靠猜）。
set -u
ADB="C:/Users/qing_/.dsh/tools/platform-tools/adb.exe"
PKG="com.whisper.projectwhisper"
N="${1:-26}"
EV="/d/DSH专用/_evidence/build${N}-device"
mkdir -p "$EV"

echo "── 0. 当前屏幕状态 ──"
"$ADB" shell dumpsys window 2>/dev/null | grep -aE 'mDreamingLockscreen|mAwake=' | head -2 | sed 's/^/  /'

echo "── 1. 唤醒 + 解锁（用户给的锁屏密码） ──"
"$ADB" shell input keyevent KEYCODE_WAKEUP
sleep 1
"$ADB" shell input swipe 640 2200 640 700 250
sleep 2
"$ADB" shell input text "114514"
sleep 1
"$ADB" shell input keyevent KEYCODE_ENTER
sleep 3
"$ADB" shell dumpsys window 2>/dev/null | grep -aE 'mDreamingLockscreen' | head -1 | sed 's/^/  /'

echo "── 2. 启动 0.1.${N} ──"
ACT="$("$ADB" shell cmd package resolve-activity --brief "$PKG" 2>/dev/null | tail -1 | tr -d '\r')"
"$ADB" shell am force-stop "$PKG" 2>/dev/null
"$ADB" logcat -c 2>/dev/null
"$ADB" shell am start -n "$ACT" 2>&1 | tail -1 | sed 's/^/  /'
sleep 15
PID="$("$ADB" shell pidof "$PKG" 2>/dev/null | tr -d '\r')"
echo "  pid=${PID:-（空 = 未运行）}"
"$ADB" shell wm size 2>/dev/null | sed 's/^/  屏幕: /'

echo "── 3. 截图（主界面） ──"
"$ADB" shell screencap -p //sdcard/a${N}.png 2>&1 | tail -1
"$ADB" pull //sdcard/a${N}.png "$EV/01-menu.png" 2>&1 | tail -1 | sed 's/^/  /'

echo "── 4. 点「开始调查」中心（由上一版 HUD 量出的矩形推算：屏幕 2800x1280 → 中心约 2408,794） ──"
# 用当前真实分辨率换算：横屏 2800x1280 时按钮中心 x≈2408 y≈794
"$ADB" shell input tap 2408 794
sleep 6

echo "── 5. 截图（点击后） ──"
"$ADB" shell screencap -p //sdcard/b${N}.png 2>&1 | tail -1
"$ADB" pull //sdcard/b${N}.png "$EV/02-after-tap.png" 2>&1 | tail -1 | sed 's/^/  /'
echo "  对局日志:"
"$ADB" logcat -d 2>/dev/null | grep -aE '对局开始|玩家：|怪物 ' | head -5 | sed 's/^/    /'
ls -la "$EV" | sed 's/^/  /'
