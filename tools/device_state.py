# -*- coding: utf-8 -*-
"""device_state.py — 真机取证的**焦点自洽性**诊断（两个独立证据 + 连续截图）

## 为什么需要它（第 19 轮踩的坑）
第 19 轮出现"命令报焦点在应用、`screencap` 却是桌面"的矛盾，导致整轮真机结论作废。
本会话已多次因"命令报的状态 ≠ 实际画面"白跑（第 10 轮 `Dozing` + 通知栏抢焦点也是同族）。
**纪律**：任何真机结论必须有两个**相互独立**的证据。本工具把这件事固化成一条命令。

## 输出
1. `mCurrentFocus`（窗口系统视角：谁有输入焦点）
2. `ResumedActivity`（ActivityManager 视角：谁在 resumed 状态）
3. `mWakefulness`（电源视角：是否在打盹 —— 打盹时输入与截图都不可信）
4. 连续两张截图的 SHA 与字节数（画面视角）
5. **自洽性判定**：三个视角是否指向同一个包；不一致就明确报"环境不可信，先修环境"

用法：
  python tools/device_state.py [--pkg com.whisper.projectwhisper] [--shots 2] [--out DIR]
"""
import argparse
import hashlib
import os
import re
import subprocess
import time

ADB = r"C:/Users/qing_/.dsh/tools/platform-tools/adb.exe"
APK_PKG = "com.whisper.projectwhisper"


def sh(*args):
    r = subprocess.run([ADB] + list(args), capture_output=True, text=True)
    return (r.stdout or "") + (r.stderr or "")


def grep_first(text, pattern):
    m = re.search(pattern, text, re.M)
    return m.group(1).strip() if m else None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pkg", default=APK_PKG)
    ap.add_argument("--shots", type=int, default=2)
    ap.add_argument("--interval", type=float, default=2.0)
    ap.add_argument("--out", default=None)
    a = ap.parse_args()

    out = a.out
    if out:
        os.makedirs(out, exist_ok=True)

    print("== ① 电源 ==")
    power = sh("shell", "dumpsys", "power")
    wake = grep_first(power, r"mWakefulness=(\w+)")
    print(f"  mWakefulness = {wake}")
    dozing = wake not in ("Awake",)
    if dozing:
        print("  ⚠ 设备不在 Awake —— **输入与截图都不可信**，先唤醒/解锁再谈功能")

    print("== ② 窗口焦点（WindowManager）==")
    win = sh("shell", "dumpsys", "window")
    focus = grep_first(win, r"mCurrentFocus=(\S+)")
    print(f"  mCurrentFocus = {focus}")

    print("== ③ 前台 Activity（ActivityManager，**独立证据**）==")
    acts = sh("shell", "dumpsys", "activity", "activities")
    resumed = grep_first(acts, r"ResumedActivity:\s*(\S+)")
    top = grep_first(acts, r"topResumedActivity=\S*\s*(\S+)")
    print(f"  ResumedActivity    = {resumed}")
    print(f"  topResumedActivity = {top}")

    print("== ④ 进程 ==")
    pid = sh("shell", "pidof", a.pkg).strip()
    print(f"  pid = {pid or '(未运行)'}")

    print(f"== ⑤ 连续 {a.shots} 张截图 ==")
    hashes = []
    for i in range(a.shots):
        r = f"/sdcard/_ds{i}.png"
        sh("shell", "screencap", "-p", r)
        if out:
            local = os.path.join(out, f"ds{i}.png")
            subprocess.run([ADB, "pull", r, local], capture_output=True, text=True)
            data = open(local, "rb").read()
            h = hashlib.sha256(data).hexdigest()[:16]
            print(f"  #{i} sha={h} · {len(data)} 字节 · {local}")
        else:
            h = hashlib.sha256(sh("exec-out", "cat", r).encode("utf-8", "ignore")).hexdigest()[:16]
            print(f"  #{i} sha(approx)={h}")
        hashes.append(h)
        time.sleep(a.interval)

    print("\n== 判定 ==")
    in_focus = bool(focus and a.pkg in focus)
    in_resumed = bool((resumed and a.pkg in resumed) or (top and a.pkg in top))
    consistent = in_focus and in_resumed and not dozing
    print(f"  焦点在应用: {in_focus} · 前台 Activity 是应用: {in_resumed} · 未打盹: {not dozing}")
    if consistent:
        print("  ✅ 环境自洽 —— 可以做功能结论")
    else:
        print("  ✗ **环境不可信**：三个视角不一致 → 先解决环境（唤醒/解锁/收通知栏/重拉应用），"
              "否则任何功能结论都会是假故障（第 19 轮已因此作废一整轮）")
    if len(set(hashes)) == 1 and a.shots > 1:
        print("  · 连续截图 SHA 相同 —— 画面可能静止（大厅有灯闪，正常情况下应当变化）")


if __name__ == "__main__":
    main()
