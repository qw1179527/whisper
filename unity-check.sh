#!/data/user/0/app.dsh.mobile/files/engine/bin/bash
# unity-check.sh — Unity 侧骨架的本机一键检查（不需要 Unity 授权）
#
# 链条（16 步）：asmdef 一致 → manifest 一致 → DesignTokens 一致 → 架构守护 → 镜像一致 →
#   关卡 DSL 校验 → C# 静态检查 → Token 产物自检 → 色彩双射 → 三大机制移植等价性（声纹/听觉/状态机）→
#   配置符号门禁 → Unity 语法语义检查 → 本机真编译真跑断言
# 说明：最后一步用 .NET 8 SDK（native/dotnet.sh，Termux arm64/bionic 版）**真编译真运行** Core 契约与 Gameplay 的 Level 层；
#       依赖 UnityEngine 的 Boot/Bootstrap 由 Unity Test Framework（PlayMode，CI 跑）覆盖。
set -euo pipefail
cd "$(dirname "$0")"

step() { printf '\n\033[1m[%s/16] %s\033[0m\n' "$1" "$2"; }

step 1 "asmdef 与 V9 §13.1 规则表一致"
node tools/gen-asmdef.mjs --check

step 2 "Packages/manifest.json 与 dependency-lock.json 一致"
node tools/gen-manifest.mjs --check

step 3 "DesignTokens.cs 与 data/design-tokens.json 一致"
node tools/gen-design-tokens.mjs --check

step 4 "架构守护（§13.1/§13.2/§19.1）"
node tools/arch-guard.mjs

step 5 "真源镜像一致性（data/ ↔ Assets/Data ↔ Resources）"
node tools/data-mirror.mjs

step 6 "关卡 DSL 校验 + Resources 镜像"
node tools/validate-levels.mjs

step 7 "资产清单准入（C3：产物落地 + 引用/kind 匹配 + 记录齐备）"
node tools/validate-assets.mjs
node tools/gen-kits.mjs --check

step 8 "C# 静态一致性检查"
node tools/cs-lint.mjs

step 9 "设计 Token 生成物 + 产物自检"
node tools/gen-design-tokens.mjs --check

step 10 "V9 §11 色彩 Token 对账双射"
node tools/tokens-map-check.mjs

step 11 "声纹移植等价性（灰盒 JS ↔ C# 移植，1140 帧）"
node tools/voice-port-vectors.mjs

step 12 "听觉索敌移植等价性（灰盒 ↔ C#，1716 例）"
node tools/hearing-port-vectors.mjs

step 13 "三怪状态机移植等价性（灰盒 ↔ C#，21 例）"
node tools/monster-port-vectors.mjs

step 14 "配置表符号约定与量纲门禁"
node tools/config-lint.mjs

step 15 "Unity 依赖文件的语法+语义检查（Roslyn + 最小 Unity 桩）"
bash tools/unity-syntax-check.sh

step 16 "C# 真编译真跑（本机 .NET 8）"
cp unity/Assets/Levels/*.json native/csharp-verify/ 2>/dev/null || true
(cd native/csharp-verify && ../../native/dotnet.sh run --nologo)

printf '\n\033[1;32mUnity 骨架检查完成\033[0m\n'
