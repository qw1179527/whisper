#!/data/user/0/app.dsh.mobile/files/engine/bin/bash
# dotnet.sh — 本机 .NET 8 SDK wrapper（Termux arm64/bionic 版）
#
# 缺陷记录（独立验证轨指出后修正）：旧版把 DOTNET_ROOT 写成 "$HOME/whisper/native/dotnet/root"，
# 于是把仓库复制到别处后，脚本仍连回**原仓库**的 dotnet —— 副本里会给出假绿。
# 现改为按脚本自身位置解析，仓库改名/换目录/复制都能正确工作。
set -euo pipefail
SELF_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export DOTNET_ROOT="$SELF_DIR/dotnet/root"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export TMPDIR="${TMPDIR:-$HOME/tmp}"; mkdir -p "$TMPDIR"
exec "$DOTNET_ROOT/dotnet" "$@"
