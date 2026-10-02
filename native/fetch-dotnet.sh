#!/data/user/0/app.dsh.mobile/files/engine/bin/bash
# fetch-dotnet.sh — 获取本机可用的 .NET 8（Termux aarch64/bionic 版）
#
# 为什么需要：本机是 Android bionic arm64，官方 dotnet-install 脚本给的是 glibc 构建，跑不起来；
# Termux 官方源提供 **linux-bionic-arm64** 版运行时/SDK，实测可直接运行（RID: linux-bionic-arm64）。
#
# 装法要点（两个坑，已实测）：
#   1. 包内 bin/dotnet 是指向 /data/data/com.termux 的 sh 包装脚本 → 必须直连 lib/dotnet/dotnet；
#   2. 运行时包里的 libhostfxr.so 是指回 host/fxr/<ver>/ 的**循环符号链接**，
#      真身在 dotnet-hostfxr-8.0 包里（999,568 字节），必须单独补入 host/fxr/8.0.31/。
#
# 用法：bash fetch-dotnet.sh
set -euo pipefail
cd "$(dirname "$0")"

MIRROR="${TERMUX_MIRROR:-https://mirrors.tuna.tsinghua.edu.cn/termux/apt/termux-main}"
BASE="$MIRROR/dists/stable/main/binary-aarch64"
CACHE="$HOME/tmp/debs"; mkdir -p "$CACHE"
DEST="$(pwd)/dotnet"; ROOT="$DEST/root"
PKGS="dotnet-host-8.0 dotnet-hostfxr-8.0 dotnet-runtime-8.0 dotnet-sdk-8.0"

echo "[1/3] 拉索引并确认包存在"
curl -sSL -o "$CACHE/Packages.gz" "$BASE/Packages.gz"
python3 - "$CACHE/Packages.gz" "$MIRROR" "$PKGS" > "$CACHE/dotnet-urls.txt" <<'PY'
import gzip, sys
idx, mirror, pkgs = sys.argv[1], sys.argv[2], set(sys.argv[3].split())
txt = gzip.open(idx, 'rt', encoding='utf8', errors='replace').read()
for block in txt.split('\n\n'):
    m = {}
    for line in block.split('\n'):
        i = line.find(':')
        if i > 0: m[line[:i]] = line[i+1:].strip()
    if m.get('Package') in pkgs and 'Filename' in m:
        print(f"{m['Package']}|{mirror}/{m['Filename'].lstrip('./')}")
PY
while IFS='|' read -r pkg url; do
  [ -n "$pkg" ] || continue
  echo "  下载 $pkg"
  curl -sSL -o "$CACHE/$pkg.deb" "$url"
done < "$CACHE/dotnet-urls.txt"

echo "[2/3] 解包（ar + tar.xz）"
python3 - "$CACHE" "$DEST" "$PKGS" <<'PY'
import io, lzma, os, sys, tarfile
cache, dest, pkgs = sys.argv[1], sys.argv[2], sys.argv[3].split()

def ar_members(data):
    assert data[:8] == b'!<arch>\n'
    off = 8
    while off + 60 <= len(data):
        hdr = data[off:off+60]
        name = hdr[0:16].decode('utf8', 'replace').strip()
        try: size = int(hdr[48:58].decode().strip())
        except ValueError: return
        yield name, data[off+60:off+60+size]
        off += 60 + size + (size % 2)

for pkg in pkgs:
    deb = os.path.join(cache, pkg + '.deb')
    if not os.path.exists(deb): continue
    for name, body in ar_members(open(deb, 'rb').read()):
        if not name.startswith('data.tar'): continue
        if name.endswith('.xz'): body = lzma.decompress(body)
        with tarfile.open(fileobj=io.BytesIO(body)) as t:
            t.extractall(os.path.join(dest, pkg))
        print(f"  {pkg}: 解出 {name}")
        break
PY

echo "[3/3] 合并成可用布局并补 hostfxr 真身"
PREFIX_REL="data/data/com.termux/files/usr/lib/dotnet"
rm -rf "$ROOT"; mkdir -p "$ROOT"
for pkg in $PKGS; do
  S="$DEST/$pkg/$PREFIX_REL"
  [ -d "$S" ] && cp -r "$S"/. "$ROOT"/ 2>/dev/null || true
done
FXR_REAL="$DEST/dotnet-hostfxr-8.0/$PREFIX_REL/host/fxr/8.0.31/libhostfxr.so"
if [ -f "$FXR_REAL" ]; then
  mkdir -p "$ROOT/host/fxr/8.0.31"
  rm -f "$ROOT/host/fxr/8.0.31/libhostfxr.so"
  cp "$FXR_REAL" "$ROOT/host/fxr/8.0.31/libhostfxr.so"
  echo "  hostfxr 真身已补入（$(stat -c%s "$ROOT/host/fxr/8.0.31/libhostfxr.so") 字节）"
else
  echo "  ✗ 未找到 hostfxr 真身，dotnet 将无法启动"; exit 1
fi
chmod +x "$ROOT/dotnet"
echo "--- 自检 ---"
DOTNET_ROOT="$ROOT" "$ROOT/dotnet" --version
echo "完成：用 native/dotnet.sh 调用"
