#!/usr/bin/env bash
# 装并取证：用法 bash tools/device-verify.sh <buildNumber> [x y]
# 第 2/3 个参数给出时，安装启动后会额外注入一次点击（用于"按钮是否响应"的取证）。
set -u
ADB="C:/Users/qing_/.dsh/tools/platform-tools/adb.exe"
N="${1:-22}"
APK="D:/dsh-whisper-unity/build/Android/whisper-android-0.1.${N}-code${N}.apk"
PKG="com.whisper.projectwhisper"
EV="/d/DSH专用/_evidence/build${N}-device"
mkdir -p "$EV"

echo "── 唤醒 + 解锁（设备会被自动息屏，不唤醒截图就是黑的） ──"
"$ADB" shell svc power stayon true 2>/dev/null
"$ADB" shell input keyevent KEYCODE_WAKEUP 2>/dev/null
sleep 1
if "$ADB" shell dumpsys window 2>/dev/null | grep -qa 'mDreamingLockscreen=true'; then
  "$ADB" shell input swipe 640 2200 640 700 250 2>/dev/null
  sleep 2
  "$ADB" shell input text "114514" 2>/dev/null
  "$ADB" shell input keyevent KEYCODE_ENTER 2>/dev/null
  sleep 3
fi
echo "  屏幕: $("$ADB" shell dumpsys power 2>/dev/null | grep -ao 'mWakefulness=[A-Za-z]*' | head -1)"

echo "── 安装 0.1.${N} ──"
"$ADB" install -r "$APK" 2>&1 | tail -2 | sed 's/^/  /'

ACT="$("$ADB" shell cmd package resolve-activity --brief "$PKG" 2>/dev/null | tail -1 | tr -d '\r')"
echo "── 启动 $ACT ──"
"$ADB" shell am force-stop "$PKG" 2>/dev/null
"$ADB" logcat -c 2>/dev/null
"$ADB" shell am start -n "$ACT" 2>&1 | tail -1 | sed 's/^/  /'
sleep 15

PID="$("$ADB" shell pidof "$PKG" 2>/dev/null | tr -d '\r')"
echo "── pid=${PID:-（空 = 未运行）} ──"
echo "── 致命 ──"
"$ADB" logcat -d 2>/dev/null | grep -aE 'FATAL EXCEPTION|force finishing|ANR in' | head -5 | sed 's/^/  /'
echo "── 点击（给了坐标才做） ──"
if [ "$#" -ge 3 ]; then
  "$ADB" shell input tap "$2" "$3"
  sleep 5
  echo "  已点 ($2,$3)"
  echo "  按钮日志:"
  "$ADB" logcat -d 2>/dev/null | grep -a '被点击\|兜底开局\|未命中' | tail -4 | sed 's/^/    /'
  echo "  对局日志:"
  "$ADB" logcat -d 2>/dev/null | grep -aE '对局开始|玩家：' | tail -4 | sed 's/^/    /'
fi
echo "── 截图 ──"
"$ADB" shell screencap -p //sdcard/v${N}.png 2>&1 | tail -1
"$ADB" pull //sdcard/v${N}.png "$EV/menu.png" 2>&1 | tail -1 | sed 's/^/  /'
ls -la "$EV/menu.png" 2>/dev/null | sed 's/^/  /'
