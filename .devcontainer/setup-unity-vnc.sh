#!/usr/bin/env bash
# 在免费云开发环境（GitHub Codespaces / Gitpod）里装一个**真的 Unity Editor**，
# 并用 noVNC 暴露成"手机浏览器能直接操作"的界面。
#
# ── 为什么需要这套东西（2026-10-05 实测）────────────────────────────────
# 手机上跑 Unity Editor 的两条路都被"文件拿不到"堵死了：
#   · `download.unity3d.com` 对手机解析到 **AliyunOSS 上海**（Unity 中国镜像）
#     → 那里**既没有 Linux 版、也没有 Windows-ARM64 版**（实测 404，HostId=download-unity-cn-sh）
#   · 而 GitHub 免费额度的 Actions artifact 只有 500 MB，传不了 3.9~4.5 GB 的安装包
# 云主机走**全球 CDN**能正常下载，且它是 **x86_64** —— Unity **原生运行、零指令翻译**。
# 手机只收视频流，**一个大文件都不用下到手机**。
#
# ── 保真度与代价（说清楚，别期望错）────────────────────────────────────
# ✅ 真 Editor：Play Mode / Scene View / Inspector / Profiler / 本地出包 全都有
# ⚠ 云主机**没有 GPU** → 走 Mesa 软件渲染（llvmpipe / lavapipe），Scene View 会卡；
#    编辑、改参数、跑 Play Mode 逻辑、出包都可用，只是画面刷新慢。
# ⚠ 免费额度按核时计：Codespaces 120 核时/月 → 4 核约 30 小时/月。
#
# ── 用法 ──────────────────────────────────────────────────────────────
#   bash .devcontainer/setup-unity-vnc.sh             # 安装（首次，约 10~20 分钟）
#   bash .devcontainer/setup-unity-vnc.sh start       # 启动 Editor + VNC
#   bash .devcontainer/setup-unity-vnc.sh stop        # 停
# 许可证：把 Unity_lic.ulf 放到仓库根目录，脚本会自动装；或用环境变量 UNITY_LICENSE_FILE 指定。
set -uo pipefail

UNITY_VER="6000.3.25f1"
UNITY_HASH="e1dba0a9aba4"
BASE="https://download.unity3d.com/download_unity/${UNITY_HASH}"
EDITOR_TAR="${BASE}/LinuxEditorInstaller/Unity-${UNITY_VER}.tar.xz"
EDITOR_SIZE=4536554312          # 官方体积，用来判"是不是真下全了"
OPT_DIR="/opt/unity"
WORK="$HOME/unity-dl"
VNC_PORT=6080
DISPLAY_NUM=":99"
LOG="$HOME/unity-editor.log"

say()  { printf '\n\033[1m== %s ==\033[0m\n' "$*"; }
warn() { printf '\033[33m   ! %s\033[0m\n' "$*"; }
die()  { printf '\033[31m   ✗ %s\033[0m\n' "$*" >&2; exit 1; }

# ─────────────────────────────────────────────────────────────────────
# start / stop
# ─────────────────────────────────────────────────────────────────────
if [ "${1:-install}" = "start" ]; then
  say "启动 Xvfb + Unity Editor + x11vnc + noVNC"
  pkill -f "Xvfb ${DISPLAY_NUM}" 2>/dev/null; pkill -f "x11vnc" 2>/dev/null; pkill -f websockify 2>/dev/null
  sleep 1
  # 软件 GL：云主机没有 GPU。llvmpipe 是 Mesa 的软件 OpenGL，lavapipe 是软件 Vulkan。
  export LIBGL_ALWAYS_SOFTWARE=1
  export GALLIUM_DRIVER=llvmpipe
  Xvfb "${DISPLAY_NUM}" -screen 0 1600x900x24 -ac +extension GLX +render -noreset > "$HOME/xvfb.log" 2>&1 &
  sleep 3
  x11vnc -display "${DISPLAY_NUM}" -forever -shared -nopw -rfbport 5900 > "$HOME/x11vnc.log" 2>&1 &
  sleep 1
  websockify --web=/usr/share/novnc/ "${VNC_PORT}" localhost:5900 > "$HOME/novnc.log" 2>&1 &
  sleep 1
  say "noVNC 已起 → 打开【端口 ${VNC_PORT}】就是 Unity Editor 的桌面"
  echo "   （Codespaces：Ports 面板里把 ${VNC_PORT} 设为 Public，再点开）"
  if [ -x "${OPT_DIR}/Editor/Unity" ]; then
    say "启动 Unity Editor（软件渲染，首次打开工程会慢）"
    cd "$HOME" && DISPLAY="${DISPLAY_NUM}" "${OPT_DIR}/Editor/Unity" -projectPath "${HOME}/whisper/unity" \
      > "$LOG" 2>&1 &
    echo "   Editor 日志：${LOG}（tail -f 看进度）"
  else
    warn "还没装 Unity（先跑不带参数的本脚本）"
  fi
  exit 0
fi

if [ "${1:-install}" = "stop" ]; then
  pkill -f "${OPT_DIR}/Editor/Unity" 2>/dev/null
  pkill -f "Xvfb ${DISPLAY_NUM}" 2>/dev/null; pkill -f x11vnc 2>/dev/null; pkill -f websockify 2>/dev/null
  echo "已停"; exit 0
fi

# ─────────────────────────────────────────────────────────────────────
# install
# ─────────────────────────────────────────────────────────────────────
say "① 磁盘检查（Unity + Android 模块解压后约 12 GB）"
AVAIL_GB=$(df -BG --output=avail "$HOME" | tail -1 | tr -dc '0-9')
echo "   可用 ${AVAIL_GB} GB"
[ "${AVAIL_GB:-0}" -lt 20 ] && die "可用空间不足 20 GB（当前 ${AVAIL_GB} GB）—— 云机型要选 32 GB 盘"

say "② 装显示与依赖（Xvfb / x11vnc / noVNC / Mesa 软件渲染 / Unity 的 Linux 运行库）"
export DEBIAN_FRONTEND=noninteractive
sudo apt-get update -qq
sudo apt-get install -y -qq --no-install-recommends \
  xvfb x11vnc novnc websockify \
  mesa-utils libgl1-mesa-dri libglu1-mesa libvulkan1 mesa-vulkan-drivers \
  libgtk-3-0t64 libgconf-2-4 libnss3 libasound2t64 libgbm1 libxtst6 libxss1 \
  libnotify4 libxshmfence1 ca-certificates curl xz-utils \
  || warn "部分包名在该发行版可能不同，继续（Unity 自己会报缺哪个）"

say "③ 下载 Unity Linux 编辑器（约 4.2 GB —— 云主机走全球 CDN，这一步在手机上是做不到的）"
mkdir -p "$WORK"
if [ -f "$WORK/editor.tar.xz" ] && [ "$(stat -c%s "$WORK/editor.tar.xz")" = "$EDITOR_SIZE" ]; then
  echo "   已下好（体积相符，跳过）"
else
  # -C - 断点续传；--retry 抗抖
  curl -fL -C - --retry 5 --retry-delay 3 --retry-all-errors -o "$WORK/editor.tar.xz" "$EDITOR_TAR" \
    || die "下载失败：$EDITOR_TAR"
  S=$(stat -c%s "$WORK/editor.tar.xz")
  [ "$S" = "$EDITOR_SIZE" ] || die "体积不符：$S ≠ $EDITOR_SIZE（下载不完整，重跑本脚本会续传）"
  echo "   ✓ 4.2 GB 完整"
fi

say "④ 解压到 ${OPT_DIR}"
if [ -x "${OPT_DIR}/Editor/Unity" ]; then
  echo "   已解压（跳过）"
else
  sudo mkdir -p "$OPT_DIR"
  sudo tar -xJf "$WORK/editor.tar.xz" -C "$OPT_DIR" || die "解压失败"
  echo "   ✓ $(du -sh "$OPT_DIR" 2>/dev/null | cut -f1)"
fi

say "⑤ Android Build Support（出包必需）"
# Linux 模块的命名有几种历史形态，逐个试 —— 猜一个不如都试一遍
AND_OK=0
for cand in \
  "${BASE}/UnitySetup-Android-Support-for-Editor-${UNITY_VER}.tar.xz" \
  "${BASE}/LinuxEditorTargetInstaller/UnitySetup-Android-Support-for-Editor-${UNITY_VER}.tar.xz" \
  "${BASE}/TargetSupportInstaller/UnitySetup-Android-Support-for-Editor-${UNITY_VER}.tar.xz" ; do
  echo "   试：$(basename "$cand")"
  if curl -fL -sS -o "$WORK/android.tar.xz" "$cand" 2>/dev/null; then
    SZ=$(stat -c%s "$WORK/android.tar.xz" 2>/dev/null || echo 0)
    if [ "$SZ" -gt 10000000 ]; then
      sudo tar -xJf "$WORK/android.tar.xz" -C "$OPT_DIR" && { echo "   ✓ Android 模块已装（$(du -sh "$OPT_DIR/PlaybackEngines" 2>/dev/null | cut -f1)）"; AND_OK=1; break; }
    fi
  fi
done
[ "$AND_OK" = 1 ] || warn "Android 模块没装上 —— 装不成也能用 Editor 编辑/Play，只是不能出包。可在 Unity Hub 的 Add modules 里补。"

say "⑥ 装 Unity 许可证"
# 三种来源，按"最不容易误提交"排序：
#   ① Codespaces secret `UNITY_LICENSE`（推荐：内容随环境注入，**永不进仓库**）
#   ② 环境变量 UNITY_LICENSE_FILE 指向的文件
#   ③ 仓库根目录/工程目录下的 Unity_lic.ulf（本机自用；已在 .gitignore 里，不会被提交）
LICDIR="$HOME/.local/share/unity3d/Unity"
mkdir -p "$LICDIR"
if [ -n "${UNITY_LICENSE:-}" ]; then
  printf '%s' "$UNITY_LICENSE" > "$LICDIR/Unity_lic.ulf"
  echo "   ✓ 已写入（来自 Codespaces secret UNITY_LICENSE，${#UNITY_LICENSE} 字节）"
else
  LIC="${UNITY_LICENSE_FILE:-$PWD/Unity_lic.ulf}"
  [ -f "$LIC" ] || LIC="$PWD/Unity_lic.ulf"
  [ -f "$LIC" ] || LIC="$HOME/whisper/Unity_lic.ulf"
  if [ -f "$LIC" ]; then
    cp "$LIC" "$LICDIR/Unity_lic.ulf"
    echo "   ✓ 已装 $(basename "$LIC") → $LICDIR"
  else
    warn "没找到许可证。三种补法（推荐第 1 种）："
    echo "      1) 在 GitHub → Settings → Codespaces → Secrets 里加 UNITY_LICENSE，"
    echo "         内容 = 电脑上 Unity_lic.ulf 的全部文本，然后重建/重跑本脚本"
    echo "      2) 把 Unity_lic.ulf 放到工程根目录（已在 .gitignore，不会被提交），重跑"
    echo "      3) 走 noVNC 界面在 Editor 里用 Unity 账号登录激活"
    echo "      （许可证文件现在手机上就有：/storage/emulated/0/DSH专用/Unity_lic.ulf）"
  fi
fi

say "完成"
cat <<EOF
接着做：
  1. bash .devcontainer/setup-unity-vnc.sh start
  2. 打开【端口 ${VNC_PORT}】（Codespaces 的 Ports 面板里设为 Public）→ 就是 Editor 桌面
  3. 停：bash .devcontainer/setup-unity-vnc.sh stop

软件渲染较慢，可以调：
  · 想更快：在 Editor 里把 Scene View 关掉、只用 Inspector 与 Play Mode
  · 出包：File → Build Settings → Android → Build（在云上跑，速度接近真 PC）
EOF
