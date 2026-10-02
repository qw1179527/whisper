#!/data/user/0/app.dsh.mobile/files/engine/bin/bash
# cgo-cc.sh — 给 Go cgo 用的 C 编译器包装（bionic + builtin 头，清 CPATH 截胡）
set -euo pipefail
EXT=/data/user/0/app.dsh.mobile/files/engine/extensions/clang
exec env -u CPATH -u C_INCLUDE_PATH -u CPLUS_INCLUDE_PATH -u OBJC_INCLUDE_PATH \
  clang -target aarch64-linux-android28 \
  -isystem "$EXT/include" -isystem "$EXT/include/aarch64-linux-android" \
  -isystem "$EXT/lib/clang/21/include" \
  "$@"
