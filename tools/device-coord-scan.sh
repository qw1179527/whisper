#!/usr/bin/env bash
# 验证坐标口径：HUD 打印的矩形 Y 到底自上还是自下。
# 由两次观测反解：adb y=790 → HUD y≈490 ; adb y=540 → HUD y≈490?? 以 790→490 为准取斜率 -1：
#   y_hud = 1280 - y_adb  ⇒  MenuBtn0(y_hud 761..827) → adb y = 453..519（中心 486）
# 但 adb y=486 在上一轮扫过（命中 MenuBtn2 490）→ 说明 MenuBtn0 在**另一个口径**。
# 剩下唯一自洽的解读：HUD 的 y 就是 **adb 口径**（自上而下）⇒ MenuBtn0 的 adb y = 761..827。
# 这一轮直接点 2408,794（落在 761..827 内）并读回 HUD —— 这正是脚本 device-tap-start 做过的那次，
# 那次 HUD 回显 486 而不是 794，说明屏幕实际高度不是 1280（窗口坐标 ≠ 屏坐标）。
# 收尾做法：直接扫 y ∈ {200,210,450,470,760,800} 各一次，取 HUD 回显里出现「(单人)」的那次。
set -u
ADB="C:/Users/qing_/.dsh/tools/platform-tools/adb.exe"
N="${1:-26}"
EV="/d/DSH专用/_evidence/build${N}-device"
for Y in 200 210 450 470 760 800; do
  "$ADB" shell input tap 2408 "$Y"
  sleep 3
  "$ADB" shell screencap -p //sdcard/r.png >/dev/null 2>&1
  "$ADB" pull //sdcard/r.png "$EV/y${Y}.png" >/dev/null 2>&1
  echo "  tap 2408,$Y → y${Y}.png"
done
