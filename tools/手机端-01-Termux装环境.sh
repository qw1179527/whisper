#!/data/data/com.termux/files/usr/bin/sh
# ============================================================================
# ① Termux 侧：装 proot Ubuntu + 准备 Blender 环境
#
# ## 为什么分两段脚本
#   `proot-distro login` 会**切换进容器**，容器里的命令必须**在容器内**执行。
#   所以：本脚本（Termux 侧）负责装容器与写容器脚本；`手机端-02-容器内装Blender.sh`
#   由本脚本**拷进容器**并提示你在容器内执行。**不要试图在一个脚本里跨容器做完。**
#
# ## 用法
#   sh 手机端-01-Termux装环境.sh
#
# ## 行尾：必须 LF（Android sh 不认 CRLF）
# ============================================================================
set -e

say() { printf '\n\033[1;36m== %s ==\033[0m\n' "$1"; }
ok()  { printf '  \033[1;32m✓\033[0m %s\n' "$1"; }
warn(){ printf '  \033[1;33m!\033[0m %s\n' "$1"; }

say "① 基础包"
pkg update -y || warn "pkg update 有警告，继续"
for p in proot-distro nodejs-lts python git unzip wget; do
  if pkg list-installed 2>/dev/null | grep -q "^$p/"; then ok "$p 已装"
  else pkg install -y "$p" && ok "$p 安装完成" || warn "$p 安装失败"; fi
done

say "② 共享存储"
[ -d /sdcard ] && ok "/sdcard 可访问" || { termux-setup-storage; warn "请授权存储权限后重跑"; }

say "③ 安装 proot Ubuntu 容器"
if proot-distro list 2>/dev/null | grep -q "ubuntu.*installed"; then
  ok "Ubuntu 容器已存在"
else
  proot-distro install ubuntu && ok "Ubuntu 安装完成" || warn "安装失败（检查网络）"
fi

say "④ 把容器内脚本放进 Termux 家目录（便于容器内读取）"
BASE="/sdcard/DSH专用/电脑上"
C02="$BASE/手机端/手机端-02-容器内装Blender.sh"
if [ -f "$C02" ]; then
  mkdir -p "$HOME/dsh-work"
  cp "$C02" "$HOME/dsh-work/" && ok "已拷贝到 ~/dsh-work/"
else
  warn "未找到 $C02（先解压『手机端工具包.zip』）"
fi

say "⑤ 工程是否已解压"
if [ -d "$HOME/dsh-work/whisper/unity" ]; then
  ok "工程就位：$HOME/dsh-work/whisper"
  du -sh "$HOME/dsh-work/whisper" 2>/dev/null || true
else
  warn "工程未解压 → 先跑 手机端-一键安装.sh，或手动 unzip whisper-full.zip"
fi

say "⑥ Node 门禁自检（手机端可编译的实证）"
if [ -d "$HOME/dsh-work/whisper" ]; then
  cd "$HOME/dsh-work/whisper"
  for t in gate-model validate-levels; do
    [ -f "tools/$t.mjs" ] && { node "tools/$t.mjs" >/tmp/$t.log 2>&1 \
      && ok "$t 通过" || warn "$t 失败（见 /tmp/$t.log）"; }
  done
fi

say "下一步（重要）"
cat <<'TIP'
  进入容器装 Blender：
      proot-distro login ubuntu
      # 进去后执行：
      bash ~/dsh-work/手机端-02-容器内装Blender.sh
      exit

  然后手机上就能建模：
      proot-distro login ubuntu -- bash -lc 'blender --background --version'
TIP
