#!/usr/bin/env bash
# 用 HUD 量出的**真实按钮坐标**点击，并把点击后的 HUD 读回来。
#
# 坐标来源：0.1.25 HUD 打印的按钮矩形（Unity 屏幕坐标，左下原点、2800x1280）：
#   MenuBtn0 开始调查(单人)  [2258,761 - 2558,827] → 中心 (2408, 794)
# 但 0.1.26 实测点 y=794 命中的是 MenuBtn2 → 说明 HUD 那串数字与我以为的坐标系不一致。
# 正解：**先点一次看 HUD 回显的真实命中**，再据此修正 —— 不再靠推算。
set -u
ADB="C:/Users/qing_/.dsh/tools/platform-tools/adb.exe"
PKG="com.whisper.projectwhisper"
N="${1:-26}"
EV="/d/DSH专用/_evidence/build${N}-device"

probe() {
  local X="$1" Y="$2" TAG="$3"
  "$ADB" shell input tap "$X" "$Y"
  sleep 4
  "$ADB" shell screencap -p //sdcard/p.png >/dev/null 2>&1
  "$ADB" pull //sdcard/p.png "$EV/probe-${TAG}.png" >/dev/null 2>&1
  echo "  点到 ($X,$Y) → $EV/probe-${TAG}.png"
}

echo "── 按 MenuBtn0 矩形区间逐个试探（HUD 会回显真实命中） ──"
probe 2408 790 A
probe 2408 300 B
probe 2408 540 C
echo "── 全部探测图 ──"
ls -la "$EV"/probe-*.png 2>/dev/null | sed 's/^/  /'
