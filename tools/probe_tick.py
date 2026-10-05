# -*- coding: utf-8 -*-
"""抽帧读 Tick：判断"画面死住"到底是主循环停了，还是只是不再合成画面。

判据（两种故障的修法完全不同）：
  · Tick 仍在涨 + 画面逐字节不变 → **渲染/合成停了**（Surface 丢失、相机无 RenderTarget…）
  · Tick 也停了                  → **主循环停了**（异常打死 Update、死锁、被系统冻结）

做法：冷启动 → 每 N 秒截图 → 裁左上角 Tick 行 → 保存每帧裁剪并打印 SHA。
用法：python probe_tick.py <out_dir> [shots] [interval]
"""
import hashlib
import os
import subprocess
import sys
import time
from PIL import Image

ADB = r"C:/Users/qing_/.dsh/tools/platform-tools/adb.exe"
PKG = "com.whisper.projectwhisper"
ACT = PKG + "/com.unity3d.player.UnityPlayerGameActivity"

out_dir = sys.argv[1] if len(sys.argv) > 1 else r"D:/DSH专用/_evidence/tick-probe"
shots = int(sys.argv[2]) if len(sys.argv) > 2 else 8
interval = float(sys.argv[3]) if len(sys.argv) > 3 else 4.0
os.makedirs(out_dir, exist_ok=True)


def sh(*a):
    return subprocess.run([ADB] + list(a), capture_output=True, text=True).stdout


print("== 冷启动（清日志）==")
sh("shell", "svc", "power", "stayon", "true")
sh("shell", "input", "keyevent", "KEYCODE_WAKEUP")
sh("logcat", "-c")
sh("shell", "am", "force-stop", PKG)
time.sleep(1)
sh("shell", "am", "start", "-n", ACT)
time.sleep(10)

print(f"== 每 {interval:g}s 抽一帧，共 {shots} 帧 ==")
prev_full = prev_tick = None
alive_flags = []
for i in range(shots):
    r = f"/sdcard/tk{i}.png"
    sh("shell", "screencap", "-p", r)
    full = os.path.join(out_dir, f"tk{i}.png")
    subprocess.run([ADB, "pull", r, full], capture_output=True, text=True)
    h_full = hashlib.sha256(open(full, "rb").read()).hexdigest()[:12]

    im = Image.open(full).convert("RGB")
    # Tick 行在左上角第二行：按 2800x1280 估 (40,85)-(900,125)
    tick = im.crop((30, 80, 900, 130))
    tick_path = os.path.join(out_dir, f"tick{i}.png")
    tick = tick.resize((tick.width * 2, tick.height * 2), Image.LANCZOS)
    tick.save(tick_path)
    h_tick = hashlib.sha256(open(tick_path, "rb").read()).hexdigest()[:12]

    print(f"  #{i}  full={h_full} ({'SAME' if h_full == prev_full else 'DIFF'})"
          f"  tick={h_tick} ({'SAME' if h_tick == prev_tick else 'DIFF'})  → {tick_path}")
    alive_flags.append(h_tick != prev_tick)
    prev_full, prev_tick = h_full, h_tick
    time.sleep(interval)

print("\n== 判定 ==")
tail = alive_flags[-4:]
if all(not x for x in tail):
    print("  ✗ 末 4 帧 Tick 文本完全相同 → **主循环也停了**（异常/死锁/被冻结）")
elif sum(1 for x in tail if x) >= 1 and not any(
        hashlib.sha256(open(os.path.join(out_dir, f"tk{i}.png"), "rb").read()).hexdigest()
        != hashlib.sha256(open(os.path.join(out_dir, f"tk{i-1}.png"), "rb").read()).hexdigest()
        for i in range(shots - 3, shots)):
    print("  · Tick 在变但整帧不变 → 渲染/合成停了")
else:
    print("  · 仍在刷新（未复现）")
print("\n请用 read_image 逐张看 tick*.png 读出真实 Tick 数值（文本判据比哈希更硬）")
