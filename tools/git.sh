#!/data/user/0/app.dsh.mobile/files/engine/bin/bash
# git.sh — 本机 git 的 HTTPS 包装
#
# 为什么需要它：本机 git 的两处路径指向 Termux 布局，而实际运行在 DSH 环境里 ——
#   ① --exec-path 指向不存在的 /data/data/com.termux/files/usr/libexec/git-core
#      → 报 "git: 'remote-https' is not a git command"（所有 HTTPS 传输不可用）
#   ② TLS 信任锚指向不存在的 .../com.termux/files/usr/etc/tls/cert.pem
#      → 报 "error adding trust anchors from file"
# 真正的文件都在 git 扩展目录下。本脚本把两处指对，其余参数原样透传。
#
# 用法：tools/git.sh ls-remote <url>
#       tools/git.sh push origin main
set -euo pipefail
GITEXT=/data/user/0/app.dsh.mobile/files/engine/extensions/git
export GIT_EXEC_PATH="$GITEXT/libexec/git-core"
export GIT_SSL_CAINFO="$GITEXT/etc/tls/cert.pem"
exec git "$@"
