#!/usr/bin/env bash
# 出 Android 包（通用）。用法：bash tools/build-android.sh <buildNumber>
set -u
cd /d/DSH专用/whisper

N="${1:-22}"
LOG="/d/DSH专用/_evidence/android-build/build${N}.log"
mkdir -p "$(dirname "$LOG")"

export WHISPER_BUILD_NUMBER="$N"
UNITY="/d/Unity/6000.3.25f1/Editor/Unity.exe"
PROJ="D:/dsh-whisper-unity"          # 纯 ASCII junction（Android 构建要求路径无中文）
METHOD="Whisper.Editor.BuildScript.BuildAndroid"

echo "── 出包 0.1.${N} · 工程 $PROJ ──"
"$UNITY" -quit -batchmode -projectPath "$PROJ" -executeMethod "$METHOD" -logFile "$LOG"
RC=$?
echo "Unity rc=$RC"

echo "── 结果行 ──"
grep -a '\[Whisper\]' "$LOG" 2>/dev/null | tail -5 | sed 's/^/  /'
echo "── 产物 ──"
ls -la unity/build/Android/*.apk 2>/dev/null | tail -3 | sed 's/^/  /'
exit $RC
