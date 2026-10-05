#!/usr/bin/env bash
set -u
ADB="C:/Users/qing_/.dsh/tools/platform-tools/adb.exe"
PKG="com.whisper.projectwhisper"
EV="/d/DSH专用/_evidence/build21-device"

echo "── 解析启动 activity ──"
"$ADB" shell cmd package resolve-activity --brief "$PKG" 2>&1 | tail -3 | sed 's/^/  /'

ACT="$("$ADB" shell cmd package resolve-activity --brief "$PKG" 2>/dev/null | tail -1 | tr -d '\r')"
echo "  → ACT=$ACT"
[ -z "$ACT" ] && { echo "  解析失败，退出"; exit 2; }

"$ADB" shell am force-stop "$PKG" 2>/dev/null
"$ADB" logcat -c 2>/dev/null
"$ADB" shell am start -n "$ACT" 2>&1 | tail -2 | sed 's/^/  /'
sleep 16

PID="$("$ADB" shell pidof "$PKG" 2>/dev/null | tr -d '\r')"
echo "── pid=${PID:-（空）} ──"
echo "── 致命 ──"
"$ADB" logcat -d 2>/dev/null | grep -aE 'FATAL EXCEPTION|force finishing|ANR in|has died' | grep -a whisper | head -6 | sed 's/^/    /'
echo "── Unity 日志 ──"
"$ADB" logcat -d -s Unity:V 2>/dev/null | tail -26 | sed 's/^/    /'
echo "── 截图 ──"
"$ADB" shell screencap -p //sdcard/s21b.png 2>&1 | tail -1
"$ADB" pull //sdcard/s21b.png "$EV/screen-02.png" 2>&1 | tail -1 | sed 's/^/  /'
