#!/bin/bash
# ============================================================================
# ② 容器内（proot Ubuntu）：装 Blender + Python 依赖 + 验证本项目生成器
#
# ## 用法（**先 `proot-distro login ubuntu` 进容器**，再执行）
#   bash ~/dsh-work/手机端-02-容器内装Blender.sh
#
# ## 为什么 Blender 要在容器里装
#   Termux 本体（Android 用户态）装不了 Blender（缺 glibc 与大量系统库）；
#   proot Ubuntu 提供完整 glibc 环境 —— 这是社区成熟做法。
#
# ## 本脚本会验证的关键一条
#   本项目 `tools/gen-kits.mjs` **调用 Blender 生成套件 GLB**，
#   所以"手机能建模"的实证是：**容器内 Blender 能跑通我们的生成脚本**。
# ============================================================================
set -e

R='\033[1;31m'; G='\033[1;32m'; C='\033[1;36m'; Y='\033[1;33m'; N='\033[0m'
say() { printf "\n${C}== %s ==${N}\n" "$1"; }
ok()  { printf "  ${G}✓${N} %s\n" "$1"; }
warn(){ printf "  ${Y}!${N} %s\n" "$1"; }

say "① 换源 + 更新（国内网络建议已换源）"
export DEBIAN_FRONTEND=noninteractive
apt-get update -y || warn "apt update 有警告，继续"

say "② 装 Blender 与 Python"
apt-get install -y --no-install-recommends \
  blender python3 python3-pip ca-certificates wget file || {
    warn "blender 安装失败。若软件源不含 blender，改用下列任一方案："
    echo "    A) apt install -y snapd && snap install blender --classic   （proot 内 snap 常不可用）"
    echo "    B) 下载官方 tar.xz 手动解压："
    echo "       wget https://download.blender.org/release/Blender4.2/blender-4.2.0-linux-x64.tar.xz"
    echo "       tar xf blender-*.tar.xz -C /opt && ln -s /opt/blender-*/blender /usr/local/bin/blender"
  }

say "③ Blender 版本"
if command -v blender >/dev/null 2>&1; then
  blender --background --version 2>/dev/null | head -3
  ok "Blender 可用：$(command -v blender)"
else
  warn "Blender 未安装成功（见 ② 的备选方案）"
fi

say "④ Node（容器内也需要，用于跑 gen-kits/gen-map）"
if command -v node >/dev/null 2>&1; then ok "node 已存在：$(node -v)"
else
  warn "容器内无 node → 装一个："
  echo "    apt-get install -y nodejs npm"
  apt-get install -y nodejs npm || warn "nodejs 安装失败"
  command -v node >/dev/null 2>&1 && ok "node $(node -v)" || warn "node 仍不可用"
fi

say "⑤ 工程可见性（容器内能看到手机上的工程）"
W="$HOME/dsh-work/whisper"
if [ -d "$W" ]; then
  ok "工程就位：$W"
else
  warn "容器内看不到 $W。proot 的 \$HOME 与 Termux 的 \$HOME 通常共享同一路径，"
  echo "    若不一致，用绝对路径指向 /sdcard/DSH专用/电脑上/whisper 或已解压目录。"
  ls -d /sdcard/DSH专用/电脑上 2>/dev/null && echo "    （/sdcard 在容器内可见）"
fi

say "⑥ **关键实证**：用本项目生成器跑一次真建模"
if [ -d "$W" ] && [ -f "$W/tools/gen-kits.mjs" ]; then
  cd "$W"
  echo "  → node tools/gen-kits.mjs --lint-only    （先跑几何自检，不调 Blender）"
  node tools/gen-kits.mjs --lint-only 2>&1 | tail -6 || warn "lint 失败"
  echo "  → node tools/gen-kits.mjs --emit /tmp/kits-out   （**真调 Blender 生成 GLB**）"
  if node tools/gen-kits.mjs --emit /tmp/kits-out 2>&1 | tail -8; then
    ok "Blender 生成链路打通"
    ls -la /tmp/kits-out 2>/dev/null | head -8
  else
    warn "生成失败（多为 Blender 未找到或内存不足；见输出）"
  fi
else
  warn "未找到工程或 gen-kits.mjs，跳过实证"
fi

say "完成"
cat <<'TIP'
  容器内验证 Blender：
      blender --background --version
  回 Termux：exit

  注意（实机限制，非本脚本问题）：
    · 手机内存与散热决定能跑多复杂的模型；本项目套件属轻量（单套件 ~1k 面），可行
    · 大场景渲染会很慢 → 建议手机只做**建模与导出 GLB**，渲染预览降分辨率
TIP
