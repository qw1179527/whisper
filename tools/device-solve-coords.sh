#!/usr/bin/env bash
# 解出 adb 坐标 → Unity 屏幕坐标的线性映射，然后精确点到 MenuBtn0。
#
# 已知三个观测（窗口 2800x1280）：
#   点 y=300 → Unity y≈486 ; 点 y=540 → 490 ; 点 y=790 → 794(?)
# 说明中间一段被压在 MenuBtn2(492-558) 内 → 是**仿射**关系，取两端点解斜率。
# 做法：先点两个远离的 y，读回 Unity y，再解出 y_unity = a*y_adb + b，反推 MenuBtn0 的中心。
set -u
ADB="C:/Users/qing_/.dsh/tools/platform-tools/adb.exe"
PKG="com.whisper.projectwhisper"
N="${1:-26}"
EV="/d/DSH专用/_evidence/build${N}-device"

# 从截图底部那一行 HUD 里读 "命中 XXX @x,y" 或 "未命中 @x,y" 需要 OCR，这里改用**读像素**太脆；
# 改为：探测多个 y，把截图存下来，由调用方用视觉判断。但更快的办法是——直接用 Unity 的坐标语义：
# `Input.mousePosition` 是**左下原点**，而 `adb tap` 是**左上原点** → 正确的换算就是 y_unity = H - y_adb。
# 我先前用 §推算§ 没做这个翻转。下面两个探测就是这个换算的验证：
#   MenuBtn0 的 Unity 矩形 y∈[761,827] → 若未翻转，adb y 应为 1280-794=486（命中 MenuBtn2 正好说明**需要翻转**）
#   → 所以 adb y 应取 1280-794=486 命中 MenuBtn2，而 MenuBtn0 对应 adb y = 1280-827..1280-761 = 453..519 之外
# 真正的解：MenuBtn0 在 Unity y=761..827 → adb y = 1280-827 .. 1280-761 = 453..519 → 中心 486？？ 与观测矛盾。
# ⟹ 结论：HUD 打印的 corners 是**世界坐标**，不是屏幕像素。必须按 HUD 回显逐个逼近。
echo "── 逐个 y 逼近 MenuBtn0（每次读 HUD 回显） ──"
for Y in 486 700 850 950 1020; do
  "$ADB" shell input tap 2408 "$Y"
  sleep 3
  "$ADB" shell screencap -p //sdcard/q.png >/dev/null 2>&1
  "$ADB" pull //sdcard/q.png "$EV/scan-y${Y}.png" >/dev/null 2>&1
  echo "  tap y=$Y → $EV/scan-y${Y}.png"
done
ls -la "$EV"/scan-*.png 2>/dev/null | sed 's/^/  /'
