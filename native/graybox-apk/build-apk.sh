#!/data/user/0/app.dsh.mobile/files/engine/bin/bash
# build-apk.sh — 灰盒 WebView APK 一条命令构建（含严格门禁）
#
# 与 native/micprobe/build-apk.sh 的区别：那是**契约镜像探针**（Java 侧验证契约），
# 这是**真正能玩的灰盒**：WebView 里跑 build/game.js。
#
# 做法：以 0.6.0 基线 APK 的二进制清单/资源/dex 为模板（R 表与清单已编译，本机无 aapt2 全链编译能力），
# 只替换 assets/web/* 与内嵌数据，再重新签名 —— 这样 APK 结构与基线逐项一致，差异只在资产。
#
# 注入的版本水印：游戏画面右上角显示「版本 + 构建标签 + game.js 字节数与哈希前 8 位」，
# 用于**确证装的是哪一版**（模糊的"我更新了"没有说服力）。
#
# 用法：bash native/graybox-apk/build-apk.sh [输出文件名] [构建标签]
set -euo pipefail
cd "$(dirname "$0")/../.."
ROOT="$(pwd)"
TPL="$ROOT/native/graybox-apk/template"
OUT="$ROOT/native/graybox-apk/out"
NAME="${1:-whisper-graybox-0.7.0-dev.apk}"
TAG="${2:-$(date +%y%m%d-%H%M)}"
EXT=/data/user/0/app.dsh.mobile/files/engine/extensions/android-buildtools
BUNDLE="$ROOT/build/game.js"
[ -f "$BUNDLE" ] || { echo "缺 build/game.js —— 先跑 bash build.sh"; exit 1; }

rm -rf "$OUT"; mkdir -p "$OUT/apk"
echo "[1/6] 复制模板结构（清单/资源/dex 沿用基线，R 表已编译）"
cp -r "$TPL/." "$OUT/apk/"

echo "[2/6] 替换 WebView 资产"
cp "$BUNDLE" "$OUT/apk/assets/web/game.js"
cp "$ROOT/baseline/index-0.6.0.html" "$OUT/apk/assets/web/index.html"
cp "$ROOT/baseline/whisper-kits.glb" "$OUT/apk/assets/web/assets/whisper-kits.glb" 2>/dev/null || \
  cp "$TPL/assets/web/assets/whisper-kits.glb" "$OUT/apk/assets/web/assets/whisper-kits.glb"

echo "[3/6] 注入版本水印（可确证版本，而非只看文件名）"
python3 - "$OUT/apk/assets/web/index.html" "$BUNDLE" "$TAG" <<'PY'
import sys, pathlib, hashlib
html_path, bundle_path, tag = sys.argv[1], sys.argv[2], sys.argv[3]
b = pathlib.Path(bundle_path).read_bytes()
ver = "0.7.0-dev"
try:
    import json
    v = json.loads(pathlib.Path("data/version.json").read_text(encoding="utf8"))
    ver = v.get("version", ver) + "-dev"
except Exception:
    pass
info = f"{ver} · build {tag} · game.js {len(b)}B · {hashlib.sha256(b).hexdigest()[:8]}"
h = pathlib.Path(html_path).read_text(encoding="utf8")
snippet = (
 '<div id="__buildmark" style="position:fixed;right:8px;top:6px;z-index:2147483647;'
 'font:12px/1.4 monospace;color:#F0E6D2;background:rgba(14,13,12,.72);'
 'padding:3px 8px;border-radius:4px;pointer-events:none">' + info + '</div>\n'
)
if "</body>" not in h:
    raise SystemExit("index.html 缺 </body>，无法注入版本水印")
h = h.replace("</body>", snippet + "</body>", 1)
pathlib.Path(html_path).write_text(h, encoding="utf8")
print(f"      水印：{info}")
PY

echo "[4/6] 压缩为 APK（严格按 APK 规则）"
# ⚠ 关键（真实装机失败换来的教训）：
#   Android 11+（targetSdk/targeting R+）要求 **resources.arsc 必须未压缩（STORED）且 4 字节对齐**，
#   否则安装直接失败：-124: Failed parse during installPackageLI: Targeting R+ ... requires the
#   resources.arsc of installed APKs to be stored uncompressed and aligned on a 4-byte boundary。
#   我第一版用 `zip -r` 默认压缩了它 → 包在电脑上"构建成功"，在手机上装不上。
#   修法：先把 resources.arsc 以 -0（store）单独写入，再压其余条目（-n 后缀规则对 arsc 不可靠）。
(cd "$OUT/apk" && rm -f "$OUT/base.apk" \
  && zip -q -X -0 "$OUT/base.apk" resources.arsc \
  && zip -q -X -r "$OUT/base.apk" . -x 'META-INF/*' 'resources.arsc')
[ -f "$OUT/base.apk" ] || { echo "✗ 打包失败"; exit 1; }
echo "      resources.arsc 以 STORED 写入（未压缩）"

echo "[5/6] zipalign -p 4 与签名"
KS="$ROOT/native/micprobe/out/debug.keystore"
[ -f "$KS" ] || { echo "缺调试密钥库（先跑 native/micprobe/build-apk.sh 生成）"; exit 1; }
"$EXT/bin/zipalign" -f -p 4 "$OUT/base.apk" "$OUT/aligned.apk"
"$EXT/bin/apksigner" sign --ks "$KS" --ks-pass pass:android --key-pass pass:android \
  --ks-key-alias whisper --v1-signing-enabled true --v2-signing-enabled true \
  --out "$OUT/$NAME" "$OUT/aligned.apk"

echo "[6/6] 门禁：签名 / 对齐 / 结构与基线一致 / 关键资产非空"
"$EXT/bin/apksigner" verify "$OUT/$NAME" >/dev/null && echo "      ✓ 签名有效"
"$EXT/bin/zipalign" -c -p -v 4 "$OUT/$NAME" >/dev/null && echo "      ✓ 4 字节对齐（含未压缩条目的页对齐）"
python3 - "$OUT/$NAME" "$TPL" "$BUNDLE" <<'PY'
import sys, zipfile, pathlib, hashlib
apk, tpl, bundle = sys.argv[1], sys.argv[2], sys.argv[3]
z = zipfile.ZipFile(apk)
names = set(z.namelist())
# ① 结构与基线一致（基线条目必须都在，除签名文件）
tpl_names = {str(p.relative_to(tpl)) for p in pathlib.Path(tpl).rglob('*') if p.is_file()}
missing = [n for n in tpl_names if n not in names and not n.startswith('META-INF/')]
if missing: raise SystemExit(f"✗ 缺基线条目：{missing}")
# ② 关键资产非空且 game.js 与产物一致
gj = z.read('assets/web/game.js')
if gj != pathlib.Path(bundle).read_bytes(): raise SystemExit("✗ APK 内 game.js 与 build/game.js 不一致")
if len(gj) < 1000: raise SystemExit("✗ game.js 异常小")
html = z.read('assets/web/index.html').decode('utf8')
if '__buildmark' not in html: raise SystemExit("✗ index.html 未注入版本水印")
# ③ 硬门禁：resources.arsc 必须 STORED 且 4 字节对齐（Android 11+ 的装机硬要求）
import struct
arsc = z.getinfo('resources.arsc')
if arsc.compress_type != 0:
    raise SystemExit(f"✗ resources.arsc 被压缩了（compress_type={arsc.compress_type}）——Android 11+ 会拒装（错误码 -124）")
raw = pathlib.Path(apk).read_bytes()
off = arsc.header_offset
sig, ver, flag, m, mt, md, crc, csize, usize, fnlen, extlen = struct.unpack_from('<IHHHHHIIIHH', raw, off)
data_off = off + 30 + fnlen + extlen
if data_off % 4 != 0:
    raise SystemExit(f"✗ resources.arsc 数据偏移 {data_off} 未 4 字节对齐——Android 11+ 会拒装")
print(f"      ✓ resources.arsc 未压缩且 4 字节对齐（偏移 {data_off}）")
klass = z.read('classes.dex')
if len(klass) < 1000: raise SystemExit("✗ classes.dex 异常小")
print(f"      ✓ 结构含基线全部条目 · game.js {len(gj)}B（与产物逐字节一致）· dex {len(klass)}B · 水印已注入")
print(f"      sha256 {hashlib.sha256(pathlib.Path(apk).read_bytes()).hexdigest()[:16]}")
PY
ls -la "$OUT/$NAME"
echo "完成 → $OUT/$NAME"
