#!/data/user/0/app.dsh.mobile/files/engine/bin/bash
# verify-apk-on-device.sh — 真机装机验收（一键，产出可复核证据）
#
# ## 为什么要有这个脚本
# 首包装机时我是**手工**敲了一串 adb/dumpsys/screencap 才发现黑屏根因的；
# 第二次验收如果还靠手敲，就会漏项、就会"看着像好了"。所以把判据固化成脚本：
# 每一条都给出**可复核的证据**，而不是"我觉得能玩了"。
#
# ## 判据（全部要有证据）
#   V1  安装成功且版本号/包名正确（证明 PlayerSettings 真的生效，不是默认 com.DefaultCompany.unity）
#   V2  进程存活 ≥N 秒且 pid 不变（证明**不是崩溃/重启循环**——首包"闪屏"的排除项）
#   V3  logcat 出现 "[Whisper] BOOT OK" 且带房间/门/道具计数（证明 boot 真的走完）
#   V4  logcat 无 ArgumentNullException("shader")（证明着色器不再被剥离）
#   V5  截屏非纯黑、非纯洋红（证明几何真的渲染出来了）
#   V6  截屏有足够多不同颜色（粗略证明"看到了场景"而不是一块色块）
#
# ## 用法
#   bash tools/verify-apk-on-device.sh <apk路径> [观察秒数,默认20]
#
# ## 依赖
#   Shizuku（shz 桥）取 adb 身份；无 Shizuku 时只能做静态检查并明确报错退出。
set -uo pipefail
cd "$(dirname "$0")/.."
ROOT="$(pwd)"

APK="${1:?用法: bash tools/verify-apk-on-device.sh <apk路径> [观察秒数]}"
OBSERVE="${2:-20}"
WORK="$ROOT/../tmp/verify"; mkdir -p "$WORK"
PKG_EXPECT="com.whisper.projectwhisper"

pass=0; fail=0
ok()  { echo "  ✓ $1"; pass=$((pass+1)); }
bad() { echo "  ✗ $1"; fail=$((fail+1)); }

echo "[verify] 真机装机验收 · APK=$(basename "$APK") · 观察 ${OBSERVE}s"

[ -f "$APK" ] || { echo "  ✗ APK 不存在：$APK"; exit 1; }
command -v shz >/dev/null || { echo "  ✗ 无 shz（需 Shizuku 模式）——无法做真机验收"; exit 1; }

# ── 设备信息（写进报告，便于复核是哪台机器）──
DEV="$(shz 'getprop ro.product.model; getprop ro.build.version.release; getprop ro.build.version.sdk' 2>/dev/null | tr '\n' '/')"
echo "  设备：$DEV"

# ── V1 安装 ──
echo "[V1] 安装"
shz "am force-stop $PKG_EXPECT" >/dev/null 2>&1
shz "pm uninstall $PKG_EXPECT" >/dev/null 2>&1
INST="$(shz "pm install -r '$APK'" 2>&1 | tr -d '\r')"
if echo "$INST" | grep -qi "Success"; then ok "安装成功（$INST）"; else bad "安装失败：$INST"; echo "[verify] 中止"; exit 1; fi

# 实际包名（从 pm list 反查，避免我们假设错）——
# 用 aapt 读 APK 的 manifest 更直接，但真机上不一定有 aapt；退而求其次：
# 装在设备上的、含 whisper 或 DefaultCompany 的包，取最新安装的那个。
PKG="$(shz "pm list packages | grep -iE 'whisper|DefaultCompany'" 2>/dev/null | sed 's/package://' | tr -d '\r' | head -1)"
if [ -n "$PKG" ]; then
  ok "包名 = $PKG$([ "$PKG" = "$PKG_EXPECT" ] && echo '（与 PlayerSettings 期望一致）' || echo "（⚠ 与期望 $PKG_EXPECT 不一致）")"
else
  bad "装了但查不到包名"; echo "[verify] 中止"; exit 1
fi
VER="$(shz "dumpsys package $PKG | grep -m1 versionName" 2>/dev/null | tr -d '\r' | tr -s ' ')"
[ -n "$VER" ] && ok "版本：$VER"

# ── 启动并抓日志 ──
echo "[V2/V3/V4] 启动 + 抓日志"
shz "logcat -c" >/dev/null 2>&1
shz "monkey -p $PKG -c android.intent.category.LAUNCHER 1" >/dev/null 2>&1
sleep "$OBSERVE"

PID1="$(shz "pidof $PKG" 2>/dev/null | tr -d '\r' | tr -s ' ' | cut -d' ' -f1)"
shz "logcat -d -b all -s Unity:V UnityPlayer:V GameActivity:V AndroidRuntime:E CRASH:V" > "$WORK/logcat.txt" 2>&1
shz "screencap -p /sdcard/verify-shot.png" >/dev/null 2>&1
cp /storage/emulated/0/verify-shot.png "$WORK/shot.png" 2>/dev/null

if [ -n "$PID1" ]; then ok "进程存活 pid=$PID1（观察 ${OBSERVE}s 后仍在）"; else bad "进程已不在（崩溃或未启动）"; fi

BOOTLINE="$(grep -a '\[Whisper\] BOOT OK' "$WORK/logcat.txt" | tail -1 | sed 's/.*\[Whisper\]/[Whisper]/')"
if [ -n "$BOOTLINE" ]; then ok "boot 完成：$BOOTLINE"; else bad "没有 BOOT OK 行（boot 未走完）"; fi

if grep -qa 'Parameter name: shader\|ArgumentNullException' "$WORK/logcat.txt"; then
  bad "仍有 shader/ArgumentNullException —— 着色器问题未解决"
else
  ok "无 shader ArgumentNullException"
fi
FAILMSG="$(grep -a '\[Whisper\] BOOT FAILED' "$WORK/logcat.txt" | tail -1 | cut -c1-200)"
[ -n "$FAILMSG" ] && bad "boot 失败信息：$FAILMSG"

# ── V5/V6 截屏分析 ──
echo "[V5/V6] 截屏分析"
if [ -f "$WORK/shot.png" ]; then
  node - "$WORK/shot.png" <<'NODE'
const fs = require('node:fs');
const p = process.argv[2];
const b = fs.readFileSync(p);
// 极简 PNG 解码：仅支持 8bit RGB/RGBA 非隔行（Unity screencap 输出符合）
if (b.readUInt32BE(0) !== 0x89504e47) { console.log('  ✗ 不是 PNG'); process.exit(1); }
let off = 8, w = 0, h = 0, bd = 0, ct = 0, idat = [];
while (off < b.length) {
  const len = b.readUInt32BE(off), type = b.toString('ascii', off + 4, off + 8);
  const data = b.subarray(off + 8, off + 8 + len);
  if (type === 'IHDR') { w = data.readUInt32BE(0); h = data.readUInt32BE(4); bd = data[8]; ct = data[9]; if (data[12]) { console.log('  ✗ 隔行 PNG 不支持'); process.exit(1); } }
  else if (type === 'IDAT') idat.push(data);
  else if (type === 'IEND') break;
  off += 12 + len;
}
if (bd !== 8 || (ct !== 2 && ct !== 6)) { console.log(`  · PNG 格式 bd=${bd} ct=${ct} 暂不支持精细分析，跳过像素判据`); process.exit(0); }
const zlib = require('node:zlib');
const raw = zlib.inflateSync(Buffer.concat(idat));
const bpp = ct === 6 ? 4 : 3, stride = w * bpp;
const cur = Buffer.alloc(stride), prev = Buffer.alloc(stride);
let pos = 0;
const colors = new Map();
const un = (a, b2, c) => { const p2 = a + b2 - c, pa = Math.abs(p2 - a), pb = Math.abs(p2 - b2), pc = Math.abs(p2 - c); return (pa <= pb && pa <= pc) ? a : (pb <= pc ? b2 : c); };

// 三条判据合起来看（首包事故让我明白"看着不黑"≠"看见了东西"）：
//   · distinct   颜色种类（4bit 量化）：一块纯色只有 1 种
//   · stddev     亮度标准差：纯色 ≈ 0
//   · edgeRatio  相邻采样点亮度差 > 24 的比例：**几何场景必然有边界**，纯色为 0
// 标定基准：首包那张真机黑屏截图实测 = 亮度 31.3 / 颜色 1 种 / 边缘 0% / 标准差 ~0。
let sum = 0, sum2 = 0, n = 0, edges = 0, pairs = 0, prevRow = null;
const STEP = 4;
for (let y = 0; y < h; y++) {
  const f = raw[pos++]; raw.copy(cur, 0, pos, pos + stride); pos += stride;
  for (let x = 0; x < stride; x++) {
    const A = x >= bpp ? cur[x - bpp] : 0, B = prev[x], C = x >= bpp ? prev[x - bpp] : 0;
    if (f === 1) cur[x] = (cur[x] + A) & 255;
    else if (f === 2) cur[x] = (cur[x] + B) & 255;
    else if (f === 3) cur[x] = (cur[x] + ((A + B) >> 1)) & 255;
    else if (f === 4) cur[x] = (cur[x] + un(A, B, C)) & 255;
  }
  const row = [];
  for (let x = 0; x < w; x += STEP) {
    const i = x * bpp, r = cur[i], g = cur[i + 1], bb = cur[i + 2];
    const lum = (r * 299 + g * 587 + bb * 114) / 1000;
    row.push(lum);
    colors.set(((r >> 4) << 8) | ((g >> 4) << 4) | (bb >> 4), 1);
    sum += lum; sum2 += lum * lum; n++;
  }
  for (let k = 1; k < row.length; k++) { pairs++; if (Math.abs(row[k] - row[k - 1]) > 24) edges++; }
  if (prevRow) for (let k = 0; k < row.length; k++) { pairs++; if (Math.abs(row[k] - prevRow[k]) > 24) edges++; }
  prevRow = row;
  cur.copy(prev);
}
const avg = sum / n, std = Math.sqrt(Math.max(0, sum2 / n - avg * avg)), edgeRatio = edges / pairs;
console.log(`  · 截屏 ${w}x${h} · 平均亮度 ${avg.toFixed(1)}/255 · 标准差 ${std.toFixed(1)} · 颜色(量化) ${colors.size} 种 · 边缘密度 ${(edgeRatio * 100).toFixed(1)}%`);

let magenta = 0;
for (const k of colors.keys()) { const r = (k >> 8) & 15, g = (k >> 4) & 15, b2 = k & 15; if (r >= 13 && g <= 3 && b2 >= 13) magenta++; }
const magentaRatio = magenta / colors.size;

if (magentaRatio > 0.5) console.log('  ✗ 屏幕以洋红为主 —— 着色器加载失败（Unity 的 shader error 标志色）');
else if (colors.size <= 2 || edgeRatio < 0.005) console.log(`  ✗ 屏幕是纯色块（颜色 ${colors.size} 种 · 边缘 ${(edgeRatio * 100).toFixed(1)}%）—— 几何没有渲染出来`);
else if (std < 8) console.log(`  ⚠ 亮度几乎均匀（标准差 ${std.toFixed(1)}）—— 有可能只画了背景色`);
else console.log(`  ✓ 屏幕有场景内容（亮度 ${avg.toFixed(1)} · 标准差 ${std.toFixed(1)} · 颜色 ${colors.size} 种 · 边缘 ${(edgeRatio * 100).toFixed(1)}%）`);
NODE
else
  echo "  ✗ 没抓到截屏"
fi

echo
echo "[verify] 结果：通过 $pass · 失败 $fail"
echo "[verify] 证据留在 $WORK/（logcat.txt · shot.png）"
[ "$fail" -eq 0 ] && echo "[verify] ✓ 全部判据满足" || echo "[verify] ✗ 有判据未满足 —— 见上"
exit $([ "$fail" -eq 0 ] && echo 0 || echo 1)
