#!/data/user/0/app.dsh.mobile/files/engine/bin/bash
# rust.sh — Rust 构建 wrapper（TMPDIR 修复：默认指向 /dev 会导致 couldn't create a temp dir）
set -euo pipefail
export TMPDIR="${TMPDIR:-$HOME/tmp}"; mkdir -p "$TMPDIR"
exec rustc "$@"
