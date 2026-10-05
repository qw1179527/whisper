#!/usr/bin/env bash
# 点「开始调查（单人）」并取证（看按钮日志 + 对局是否真的开始）。
set -u
ADB="C:/Users/qing_/.dsh/tools/platform-tools/adb.exe"
PKG="com.whisper.projectwhisper"
N="${1:-23}"
EV="/d/DSH专用/_evidence/build${N}-device"

X="${2:-2400}"; Y="${3:-328}"
echo "── 点 ($X,$Y) ──"
"$ADB" logcat -c 2>/dev/null
"$ADB" shell input tap "$X" "$Y"
sleep 9

echo "── 按钮日志（有没有被点到） ──"
"$ADB" logcat -d 2>/dev/null | grep -a '主界面按钮' | head -4 | sed 's/^/  /'
echo "  （上面为空 = 点击根本没到达按钮 → EventSystem/输入模块问题）"

echo "── 对局是否开始 ──"
"$ADB" logcat -d 2>/dev/null | grep -aE '对局开始|玩家：|怪物 |身体：|模型池' | head -8 | sed 's/^/  /'

echo "── pid ──"
"$ADB" shell pidof "$PKG" 2>/dev/null | tr -d '\r' | sed 's/^/  /'
echo "── 截图 ──"
"$ADB" shell screencap -p //sdcard/t${N}.png 2>&1 | tail -1
"$ADB" pull //sdcard/t${N}.png "$EV/after-tap.png" 2>&1 | tail -1 | sed 's/^/  /'
