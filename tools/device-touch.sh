#!/usr/bin/env bash
# 用 `input motionevent` 注入**真实触摸序列**（DOWN → MOVE → UP，source=touchscreen）。
#
# 背景：`input tap` 在 Android 16 上只送 DOWN+UP，且默认不显式声明 touchscreen 源 ——
# 实测游戏收到的 `Input.touches` 恒为 0（HUD 显示 `touches 0 mp 0,0`）。
# 而 `PlayerController` 依赖 `Input.touches`，所以这条路必须打通，否则**任何**触摸都进不来。
#
# 用法：bash tools/device-touch.sh <x> <y>
set -u
ADB="C:/Users/qing_/.dsh/tools/platform-tools/adb.exe"
X="${1:-2408}"; Y="${2:-486}"

echo "── touchscreen DOWN/MOVE/UP @ ($X,$Y) ──"
"$ADB" shell input touchscreen motionevent DOWN "$X" "$Y"
sleep 0.15
"$ADB" shell input touchscreen motionevent MOVE "$((X + 1))" "$((Y + 1))"
sleep 0.15
"$ADB" shell input touchscreen motionevent UP "$((X + 1))" "$((Y + 1))"
echo "  已注入"
