# -*- coding: utf-8 -*-
"""裁切放大真机截图的 HUD 中段，用于读「业务侧回执」（_lastTouch 等）。

为什么要这个工具：本项目纪律要求判定"点击是否被业务接住"时**不能看像素差**
（Tick 在跑，任何两次截图 SHA 都不同），必须读业务回执文本。
而回执出现在 2800x1280 截图的中段小字里，直接看整图读不清 → 裁切 + 放大。

用法：
  python crop_hud.py <in.png> <out.png> [x0 y0 x1 y1] [scale]
默认裁 中段横带 (0, 300, 2800, 360) 并放大 3 倍。
"""
import sys
from PIL import Image

if len(sys.argv) < 3:
    print(__doc__)
    sys.exit(1)

src, dst = sys.argv[1], sys.argv[2]
box = (0, 300, 2800, 360)
scale = 3
if len(sys.argv) >= 7:
    box = tuple(int(v) for v in sys.argv[3:7])
if len(sys.argv) >= 8:
    scale = int(sys.argv[7])

im = Image.open(src).convert("RGB")
x0, y0, x1, y1 = box
x0 = max(0, min(x0, im.width - 1)); x1 = max(x0 + 1, min(x1, im.width))
y0 = max(0, min(y0, im.height - 1)); y1 = max(y0 + 1, min(y1, im.height))
crop = im.crop((x0, y0, x1, y1))
crop = crop.resize((crop.width * scale, crop.height * scale), Image.LANCZOS)
crop.save(dst)
print(f"OK {dst} · 源 {im.size} · 裁 ({x0},{y0})-({x1},{y1}) · 放大 {scale}x → {crop.size}")
