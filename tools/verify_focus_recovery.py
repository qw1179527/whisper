# -*- coding: utf-8 -*-
"""验收「失焦→回前台是否恢复」：本轮 runInBackground 修复的直接判据。

背景（真机日志证据）：
    [Unity] HasWindow = 1, HasFocus = 0 → APP_CMD_TERM_WINDOW → WindowManager destroySurface
即 Android 失焦时 Unity 默认停渲染并释放 Surface；本工程相机/UI 全是代码建的，不会自建回来
→ 「画面死住」+「点了没反应」。修复 = PlayerSettings/Application.runInBackground = true。

判据（三步，每步都记 SHA）：
  A. 冷启动稳定后取一帧（基准）
  B. `input keyevent KEYCODE_HOME` 回桌面（触发失焦）→ 等 3s → 再切回应用 → 等 5s
  C. 取一帧：**必须与 A 不同**（说明仍在渲染），且与 D 收敛后的连续两帧应仍在变化
  D. 再等 4s 取一帧：与 C 比较，验证是否持续刷新（不是只重画了一帧）

用法：python verify_focus_recovery.py <out_dir> [package] [activity]
"""
import hashlib
import os
import subprocess
import sys
import time

ADB = r"C:/Users/qing_/.dsh/tools/platform-tools/adb.exe"
out_dir = sys.argv[1] if len(sys.argv) > 1 else r"D:/DSH专用/_evidence/focus-recovery"
PKG = sys.argv[2] if len(sys.argv) > 2 else "com.whisper.projectwhisper"
ACT = sys.argv[3] if len(sys.argv) > 3 else (PKG + "/com.unity3d.player.UnityPlayerGameActivity")
os.makedirs(out_dir, exist_ok=True)


def sh(*a):
    return subprocess.run([ADB] + list(a), capture_output=True, text=True).stdout


def shot(tag):
    r = f"/sdcard/{tag}.png"
    sh("shell", "screencap", "-p", r)
    local = os.path.join(out_dir, f"{tag}.png")
    subprocess.run([ADB, "pull", r, local], capture_output=True, text=True)
    h = hashlib.sha256(open(local, "rb").read()).hexdigest()[:16]
    print(f"  {tag}: sha={h}  → {local}")
    return h


print("== A. 冷启动 + 稳定 ==")
sh("shell", "svc", "power", "stayon", "true")
sh("shell", "input", "keyevent", "KEYCODE_WAKEUP")
sh("shell", "am", "force-stop", PKG)
time.sleep(1)
sh("shell", "am", "start", "-n", ACT)
time.sleep(12)
a = shot("A_baseline")

print("== B. 回桌面（失焦）→ 切回前台 ==")
sh("shell", "input", "keyevent", "KEYCODE_HOME")
time.sleep(3)
print("  已回桌面；当前 focus:",
      [l.strip() for l in sh("shell", "dumpsys", "window").splitlines() if "mCurrentFocus" in l][:1])
sh("shell", "input", "keyevent", "KEYCODE_WAKEUP")
sh("shell", "am", "start", "-n", ACT)
time.sleep(6)
c = shot("C_after_resume")

print("== D. 再等 4s（验证持续刷新，而非只重画一帧）==")
time.sleep(4)
d = shot("D_settled")

print("\n== 判定 ==")
print(f"  A→C 有变化（回前台后确实重绘）: {a != c}")
print(f"  C→D 有变化（持续刷新、没再次冻住）: {c != d}")
if a != c and c != d:
    print("  ✅ 通过：回前台恢复渲染且持续刷新 → runInBackground 修复生效")
elif a != c:
    print("  ⚠ 半通过：回前台重绘了一帧但随后又停 → 仍有失焦停渲染问题")
else:
    print("  ✗ 未通过：回前台没有重绘 → 修复未生效（或应用已退出）")
print("\n另：本轮同时加了键盘主控，可用下面两条命令确定性验证建房（绕开点击不确定性）")
print("  adb shell input keyevent KEYCODE_SPACE   # 进操作视角")
print("  adb shell input keyevent 36              # H = 建房")
