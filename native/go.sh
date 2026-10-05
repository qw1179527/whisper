#!/data/user/0/app.dsh.mobile/files/engine/bin/bash
# go.sh — Go 构建 wrapper（GOOS/GOARCH 已按本机 android/arm64 设定，产物可本机直跑）
set -euo pipefail
export TMPDIR="${TMPDIR:-$HOME/tmp}"; mkdir -p "$TMPDIR"
export GOOS="${GOOS:-android}" GOARCH="${GOARCH:-arm64}" GOCACHE="${GOCACHE:-$HOME/.gocache}"
exec go "$@"
