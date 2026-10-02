#!/data/user/0/app.dsh.mobile/files/engine/bin/bash
# fetch-android-tools.sh — 从 Termux 官方 arm64 源取 arm64 可用的 d8 / aapt2
#
# 为什么需要它：Google 只发布 x86-64 Linux 版 SDK build-tools，本机 arm64 执行即
# `Exec format error`。Termux 官方源提供 **aarch64 原生**的 d8 / aapt2（及 dx / r8 / kotlin），
# 于是"本机出 APK"的最后一块（dexer）可以补齐。
#
# 用法：bash fetch-android-tools.sh
set -euo pipefail
cd "$(dirname "$0")"
MIRROR="${TERMUX_MIRROR:-https://mirrors.tuna.tsinghua.edu.cn/termux/apt/termux-main}"
BASE="$MIRROR/dists/stable/main/binary-aarch64"
CACHE="$HOME/tmp/debs"; mkdir -p "$CACHE"

echo "[1/3] 拉 Packages 索引并定位 d8 / aapt2"
curl -sSL -o "$CACHE/Packages.gz" "$BASE/Packages.gz"
PKGS=$(python3 - "$CACHE/Packages.gz" "$MIRROR" <<'PY'
import gzip, sys, re
idx, mirror = sys.argv[1], sys.argv[2]
txt = gzip.open(idx, 'rt', encoding='utf8', errors='replace').read()
want = {'d8', 'aapt2', 'dx'}
for block in txt.split('\n\n'):
    m = {}
    for line in block.split('\n'):
        i = line.find(':')
        if i > 0: m[line[:i]] = line[i+1:].strip()
    if m.get('Package') in want and 'Filename' in m:
        print(f"{m['Package']}|{mirror}/{m['Filename'].lstrip('./')}")
PY
)
echo "$PKGS" | while IFS='|' read -r pkg url; do
  [ -n "$pkg" ] || continue
  echo "  下载 $pkg ← $url"
  curl -sSL -o "$CACHE/$pkg.deb" "$url"
done

echo "[2/3] 解包（ar + tar.xz，纯 Python 实现）"
python3 - "$CACHE" "$(pwd)/android-tools" <<'PY'
import io, lzma, gzip, os, sys, tarfile
cache, dest = sys.argv[1], sys.argv[2]

def ar_members(data):
    assert data[:8] == b'!<arch>\n', '不是 ar 归档'
    off = 8
    while off + 60 <= len(data):
        hdr = data[off:off+60]
        name = hdr[0:16].decode('utf8', 'replace').strip()
        try: size = int(hdr[48:58].decode().strip())
        except ValueError: return
        yield name, data[off+60:off+60+size]
        off += 60 + size + (size % 2)

for pkg in ('d8', 'aapt2', 'dx'):
    deb = os.path.join(cache, pkg + '.deb')
    if not os.path.exists(deb): continue
    for name, body in ar_members(open(deb, 'rb').read()):
        if not name.startswith('data.tar'): continue
        if name.endswith('.xz'): body = lzma.decompress(body)
        elif name.endswith('.gz'): body = gzip.decompress(body)
        with tarfile.open(fileobj=io.BytesIO(body)) as t:
            t.extractall(os.path.join(dest, pkg))
        print(f'  {pkg}: 解出 {name}')
        break
PY

echo "[3/3] 生成本地启动器"
D8JAR="$(pwd)/android-tools/d8/data/data/com.termux/files/usr/share/java/d8.jar"
AAPT2BIN="$(pwd)/android-tools/aapt2/data/data/com.termux/files/usr/bin/aapt2"
cat > android-tools/d8.sh <<EOF
#!$(command -v bash)
exec java -cp "$D8JAR" com.android.tools.r8.D8 "\$@"
EOF
cat > android-tools/aapt2.sh <<EOF
#!$(command -v bash)
exec "$AAPT2BIN" "\$@"
EOF
chmod +x android-tools/d8.sh android-tools/aapt2.sh android-tools/*/data/data/com.termux/files/usr/bin/* 2>/dev/null || true

echo "--- 自检 ---"
android-tools/d8.sh --version | head -1
android-tools/aapt2.sh version | head -1
echo "完成：d8 / aapt2 已落地在 native/android-tools/"
