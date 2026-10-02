#!/usr/bin/env bash
# build.sh — 灰盒 WebView 端一条命令全链构建（V9 §19.5「改数值不碰代码」的可执行入口）
#
# 链条（每步失败即中止，绝不带病往下走）：
#   1 抽取模块清单   tools/extract-modules.mjs   → data/module-manifest.json
#   2 切分源树       tools/split-modules.mjs     → src/modules/*.js · src/entry.js
#   3 分离配置表     tools/extract-config.mjs    → data/config.json（唯一数值真相源，逐键核对）
#   4 源树门控       tools/verify-sourcetree.mjs → G1/G1.5/G2/G3 全绿
#   5 打包单文件     tools/bundle-web.mjs        → build/game.js（S1/S2/S3 自检）
#   6 产物门控       tools/verify-bundle.mjs     → 一致性/等价/稳定性
#
# 用法：bash build.sh             # 全链
#       bash build.sh --quick     # 跳过清单重抽（源树已是最新时更快）
set -euo pipefail
cd "$(dirname "$0")"

QUICK=0
[ "${1:-}" = "--quick" ] && QUICK=1

step() { printf '\n\033[1m[%s/6] %s\033[0m\n' "$1" "$2"; }

if [ "$QUICK" = "0" ]; then
  step 1 "抽取模块清单"
  node tools/extract-modules.mjs
else
  step 1 "抽取模块清单（--quick 跳过）"
fi

step 2 "切分源树"
node tools/split-modules.mjs

step 3 "分离配置表并逐键核对"
node tools/extract-config.mjs

step 4 "源树门控"
node tools/verify-sourcetree.mjs

step 5 "打包单文件产物"
node tools/bundle-web.mjs

step 6 "产物门控"
node tools/verify-bundle.mjs

printf '\n\033[1;32m构建完成\033[0m → build/game.js\n'
node tools/version.mjs | sed 's/^/  /'
