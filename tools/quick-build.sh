#!/usr/bin/env bash
# 一键：语法门禁 → EditMode 真编译 → 出包。用法：bash tools/quick-build.sh <N>
# 纯 ASCII 脚本：调用方常带不走中文路径，所以仓库路径在脚本内写死。
set -u
cd /d/DSH专用/whisper

N="${1:-35}"
echo "── 语法门禁 ──"
bash tools/unity-syntax-check.sh 2>&1 | tail -2
echo "── EditMode（真 Unity 编译） ──"
bash tools/unity-tests.sh EditMode 2>&1 | tail -2
echo "── 出包 0.1.${N} ──"
bash tools/build-android.sh "$N" 2>&1 | grep -a '构建成功\|构建失败\|error CS' | head -3
