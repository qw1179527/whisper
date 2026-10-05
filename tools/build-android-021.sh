#!/usr/bin/env bash
# 出 Android 包（本项目唯一合法的出包方式）。
# 用脚本文件而不是内联命令：内联时 PowerShell → bash → Unity 三层引号必然出错（实测多次）。
set -u
cd /d/DSH专用/whisper

LOG="/d/DSH专用/_evidence/android-build/build21.log"
mkdir -p "$(dirname "$LOG")"

export WHISPER_BUILD_NUMBER=21
UNITY="/d/Unity/6000.3.25f1/Editor/Unity.exe"
PROJ="D:/dsh-whisper-unity"          # 纯 ASCII junction（Android 构建要求路径无中文）
METHOD="Whisper.Editor.BuildScript.BuildAndroid"

echo "── 出包 0.1.21 · 工程 $PROJ ──"
bash tools/unity-lock.sh run android-build -- \
  "$UNITY" -quit -batchmode -projectPath "$PROJ" -executeMethod "$METHOD" -logFile "$LOG"
RC=$?
echo "Unity rc=$RC"
echo "── 日志尾部 ──"
tail -20 "$LOG" 2>/dev/null | sed 's/^/  /'
echo "── 产物 ──"
ls -la build/Android/*.apk 2>/dev/null | sed 's/^/  /'
exit $RC
