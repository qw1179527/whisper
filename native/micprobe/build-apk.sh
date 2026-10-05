#!/data/user/0/app.dsh.mobile/files/engine/bin/bash
# build-apk.sh — 本机零 gradle / 零网络 APK 构建链
#
# ⚠ 缺陷记录（独立验证轨第三轮抓出，我此前误记为"成功"）：
#   旧版顺序是 javac → aapt，导致 **R.java 永不生成** → ProbeActivity 编译失败
#   （`package R does not exist`），而错误被 `| grep … || true` 吞掉、又只做"至少有一个 .class"的
#   弱门禁 → 脚本 exit 0 报成功，但产出的 dex 里**没有 manifest 声明的 ProbeActivity**，装上也起不来。
#   即"出包是真、可跑是假"。
#   修法：① 先 aapt 生成 R.java 再 javac；② javac 退出码必须为 0；③ 门禁改为"必须存在 launcher 类"；
#         ④ 打包后校验 dex 里确实含 launcher 类。
#
# 工具：javac(OpenJDK21) · android.jar(android-36) · d8/aapt2(Termux arm64) · aapt/zipalign/apksigner(扩展 arm64)
set -euo pipefail
cd "$(dirname "$0")"

SDK="${ANDROID_HOME:-$HOME/androidsdk}"
PLATFORM="$SDK/platforms/android-36/android.jar"
EXT=/data/user/0/app.dsh.mobile/files/engine/extensions/android-buildtools
TOOLS="$(cd ../android-tools && pwd)"
OUT="$(pwd)/out"
export ANDROID_DATA="${ANDROID_DATA:-$HOME/.android-data}"; mkdir -p "$ANDROID_DATA/dalvik-cache"

LAUNCHER_CLASS="com/whisper/probe/ProbeActivity"   # 必须与 AndroidManifest 的 launcher activity 一致
LAUNCHER_DESC="L$LAUNCHER_CLASS;"                  # DEX 里类名是类型描述符 L…;（比较口径别搞错）
[ -f "$PLATFORM" ] || { echo "缺 android.jar: $PLATFORM"; exit 1; }
[ -x "$TOOLS/d8.sh" ] || { echo "缺 d8：先跑 native/fetch-android-tools.sh"; exit 1; }

rm -rf "$OUT"; mkdir -p "$OUT/classes" "$OUT/dex" "$OUT/gen"

echo "[1/6] aapt 打包资源与清单 + 生成 R.java（必须先于 javac）"
"$EXT/bin/aapt" package -f -M AndroidManifest.xml -S res -I "$PLATFORM" -J "$OUT/gen" -F "$OUT/base.apk" 2>&1 | grep -viE "warning|^note|ResourceType" || true
# aapt 的 -J 会把 R.java 放在该目录**根上**（包名取自 manifest 的 package）
[ -f "$OUT/gen/R.java" ] || { echo "✗ aapt 未产出 R.java（$OUT/gen/R.java）"; exit 1; }
echo "      R.java 已生成"

echo "[2/6] javac 编译（源码 + 生成的 R.java），退出码必须为 0"
find src "$OUT/gen" -name '*.java' > "$OUT/sources.txt"
echo "      源文件 $(wc -l < "$OUT/sources.txt") 个"
if ! javac --release 8 -encoding UTF-8 -classpath "$PLATFORM" -d "$OUT/classes" @"$OUT/sources.txt" 2> "$OUT/javac.err"; then
  echo "✗ javac 失败（错误被显式拦截，不再吞）:"
  grep -E "error:" "$OUT/javac.err" | head -10 | sed 's/^/      /'
  exit 1
fi
[ -f "$OUT/classes/$LAUNCHER_CLASS.class" ] || { echo "✗ 编译产物缺 launcher 类：$LAUNCHER_CLASS.class"; ls "$OUT/classes" | head; exit 1; }
echo "      launcher 类已编译：$LAUNCHER_CLASS.class（共 $(find "$OUT/classes" -name '*.class' | wc -l) 个 class）"

echo "[3/6] d8 转 dex"
"$TOOLS/d8.sh" --min-api 26 --lib "$PLATFORM" --output "$OUT/dex" $(find "$OUT/classes" -name '*.class')
[ -f "$OUT/dex/classes.dex" ] || { echo "✗ d8 未产出 classes.dex"; exit 1; }

echo "[4/6] 塞入 classes.dex + 校验 dex 内确实含 launcher 类"
cp "$OUT/base.apk" "$OUT/unsigned.apk"
(cd "$OUT/dex" && zip -q -X "$OUT/unsigned.apk" classes.dex)
python3 - "$OUT/dex/classes.dex" "$LAUNCHER_DESC" <<'PY'
import sys, struct
dex = open(sys.argv[1], 'rb').read()
want = sys.argv[2]
str_size, str_off = struct.unpack_from('<II', dex, 0x38)
found = False
for i in range(str_size):
    off = struct.unpack_from('<I', dex, str_off + i * 4)[0]
    p = off; n = 0; shift = 0
    while True:
        b = dex[p]; p += 1
        n |= (b & 0x7f) << shift
        if not (b & 0x80): break
        shift += 7
    if dex[p:p+n].decode('utf8', 'replace') == want:
        found = True; break
print(f"      dex 字符串数 {str_size}；含 launcher 类 {want}: {found}")
sys.exit(0 if found else 1)
PY

echo "[5/6] zipalign"
"$EXT/bin/zipalign" -f -p 4 "$OUT/unsigned.apk" "$OUT/aligned.apk"

echo "[6/6] apksigner 签名"
APK_NAME="${APK_NAME:-micprobe.apk}"
KS="$OUT/debug.keystore"
[ -f "$KS" ] || keytool -genkeypair -keystore "$KS" -storepass android -keypass android -alias whisper \
  -keyalg RSA -keysize 2048 -validity 10000 -dname "CN=Whisper Graybox, O=Project Whisper" >/dev/null 2>&1
"$EXT/bin/apksigner" sign --ks "$KS" --ks-pass pass:android --key-pass pass:android \
  --ks-key-alias whisper --v1-signing-enabled true --v2-signing-enabled true \
  --out "$OUT/$APK_NAME" "$OUT/aligned.apk"

echo "--- 交付校验 ---"
"$EXT/bin/apksigner" verify --print-certs "$OUT/$APK_NAME" | head -2
"$EXT/bin/aapt" dump badging "$OUT/$APK_NAME" 2>/dev/null | grep -E "^package|^launchable-activity|^uses-permission" | head -3
ls -la "$OUT/$APK_NAME"
echo "完成 → $OUT/$APK_NAME"
