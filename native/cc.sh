#!/data/user/0/app.dsh.mobile/files/engine/bin/bash
# cc.sh — 本机原生编译 wrapper（修复 clang 扩展的 C++ 头搜索顺序）
#
# 已查明的根因（不是缺文件）：
#   1) 引擎把 20+ 个扩展 include 目录注入 CPATH —— `-nostdinc` 清不掉环境注入，会截胡 libc++ 的 include_next
#   2) clang 自身把 builtin 头永远排在搜索列表最末，bionic 头在列表中段 —— libc++ 的 `#include_next <stdio.h>` 跳不到 bionic
#   3) Windows 式 `-isystem` 会被排到 builtin 之前/之后不可控
# 修法：清 CPATH 类变量 + `-nostdinc` + 显式三段排序（L=libc++ → B=bionic → builtins）
#
# 用法：
#   ./cc.sh -O2 -o out prog.cpp          # C++17
#   ./cc.sh -O2 -o out prog.c            # C（自动识别扩展名）
#   CC_EXTRA_FLAGS="-D__ANDROID_API__=28 -laaudio -L/system/lib64" ./cc.sh ...
set -euo pipefail

CLANG_EXT=/data/user/0/app.dsh.mobile/files/engine/extensions/clang
SRCS=(); FLAGS=()
for a in "$@"; do
  case "$a" in
    *.c|*.cc|*.cpp|*.cxx|*.h|*.hpp) SRCS+=("$a") ;;
    *) FLAGS+=("$a") ;;
  esac
done
is_cxx=0
for f in "${SRCS[@]:-}"; do case "$f" in *.cc|*.cpp|*.cxx|*.hpp) is_cxx=1 ;; esac; done

COMMON=(-nostdinc
  -idirafter "$CLANG_EXT/include"
  -idirafter "$CLANG_EXT/include/aarch64-linux-android"
  -idirafter "$CLANG_EXT/lib/clang/21/include")

if [ "$is_cxx" = "1" ]; then
  exec env -u C_INCLUDE_PATH -u CPLUS_INCLUDE_PATH -u CPATH -u OBJC_INCLUDE_PATH \
    clang++ -std=c++17 \
    -isystem "$CLANG_EXT/include/c++/v1" "${COMMON[@]}" \
    -Wl,-rpath,"$CLANG_EXT/lib" -L"$CLANG_EXT/lib" -lc++_shared \
    ${CC_EXTRA_FLAGS:-} "${FLAGS[@]}" "${SRCS[@]}"
else
  exec env -u C_INCLUDE_PATH -u CPLUS_INCLUDE_PATH -u CPATH -u OBJC_INCLUDE_PATH \
    clang "${COMMON[@]}" -Wno-nullability-completeness \
    ${CC_EXTRA_FLAGS:-} "${FLAGS[@]}" "${SRCS[@]}"
fi
