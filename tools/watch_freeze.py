# -*- coding: utf-8 -*-
"""复现「画面死住」并定位起点：按固定间隔截图，报告每帧 SHA 是否变化。

为什么用 SHA 而不是看画面：本项目已实测过"画面变了不等于业务接住了"，
反过来也一样——"看起来静止"可能只是场景本身没有动画。
**逐帧 SHA 完全相同**才是"渲染/主循环停了"的硬证据（大厅有灵球漂浮与灯闪，不可能逐字节一样）。

用法：
  python watch_freeze.py <out_dir> [shots] [interval_sec]
默认 8 张、间隔 3 秒；同时把每次的 Tick 行裁切出来便于读数。
"""
import hashlib
import os
import subprocess
import sys
import time

ADB = r"C:/Users/qing_/.dsh/tools/platform-tools/adb.exe"
PKG = "com.whisper.projectwhisper"
ACT = PKG + "/com.unity3d.player.UnityPlayerGameActivity"

out_dir = sys.argv[1] if len(sys.argv) > 1 else r"D:/DSH专用/_evidence/freeze-probe"
shots = int(sys.argv[2]) if len(sys.argv) > 2 else 8
interval = float(sys.argv[3]) if len(sys.argv) > 3 else 3.0
os.makedirs(out_dir, exist_ok=True)


def sh(*args):
    return subprocess.run([ADB] + list(args), capture_output=True, text=True).stdout


print("== 唤醒 + 清日志 + 冷启动 ==")
sh("shell", "svc", "power", "stayon", "true")
sh("shell", "input", "keyevent", "KEYCODE_WAKEUP")
sh("logcat", "-c")
sh("shell", "am", "force-stop", PKG)
time.sleep(1)
sh("shell", "am", "start", "-n", ACT)
time.sleep(10)  # 等首帧

prev = None
rows = []
for i in range(shots):
    remote = f"/sdcard/fz{i}.png"
    sh("shell", "screencap", "-p", remote)
    local = os.path.join(out_dir, f"fz{i}.png")
    subprocess.run([ADB, "pull", remote, local], capture_output=True, text=True)
    h = hashlib.sha256(open(local, "rb").read()).hexdigest()[:16]
    same = "SAME" if h == prev else "DIFF"
    rows.append((i, h, same))
    print(f"  #{i}  sha={h}  {same}")
    prev = h
    time.sleep(interval)

print("\n== 判定 ==")
tail = [r[2] for r in rows[-4:]]
if tail.count("SAME") >= 3:
    print("  ✗ 末段连续 SAME → 画面已死住（渲染/主循环停止）")
else:
    print("  · 末段仍有变化 → 本次未复现死住")

log = sh("logcat", "-d")
keys = ("FATAL", "AndroidRuntime", "Exception", "ANR", "Unity", "Whisper", "libil2cpp")
hits = [ln for ln in log.splitlines() if any(k in ln for k in keys)]
print(f"\n== 日志命中 {len(hits)} 行（末 40 行）==")
for ln in hits[-40:]:
    print("  " + ln[:200])
with open(os.path.join(out_dir, "logcat-filtered.txt"), "w", encoding="utf-8") as f:
    f.write("\n".join(hits))
