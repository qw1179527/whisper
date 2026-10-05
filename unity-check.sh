#!/data/user/0/app.dsh.mobile/files/engine/bin/bash
# unity-check.sh — Unity 侧骨架的本机一键检查（不需要 Unity 授权）
#
# ## 为什么**不用** `set -e`（2026-10-05 修）
# 原来第一行是 `set -euo pipefail`，于是链在**第一道失败的门禁就中止**，
# 把后面所有门禁的失败**全部遮住**。实测代价：`gate-physics` 一红，
# `gate-code` / `gate-test` / `gate-asset-bbox` 三道**同时红着也没人知道**——
# 其中 `gate-test` 是"156 条断言完全编译不过"，被遮了整整一轮才发现。
#
# 更关键的是：这**违背本仓库自己写下的纪律**——
# `TRUE-SOURCE.md`：「门禁链 `bash unity-check.sh` **必须全步骤跑完**（不只看"没报错"）」。
# 所以现在改为：**每一步都跑**，各自的退出码收集起来，**末尾出一张汇总表**，
# 有任一失败则整链 exit 1。这样"红"能被完整看见，而不是看见第一个就停。
set -uo pipefail
cd "$(dirname "$0")"

STEP_NO=0
declare -a R_NAMES=() R_CODES=()
CUR=""
TOTAL=22

begin() {
  CUR="$1"; STEP_NO=$((STEP_NO + 1))
  printf '\n\033[1m[%s/%s] %s\033[0m\n' "$STEP_NO" "$TOTAL" "$CUR"
}
end() { R_NAMES+=("$CUR"); R_CODES+=("${1:-0}"); }

begin "建模门禁（几何 + 资产 · 11 项）"
node tools/gate-model.mjs; end $?

begin "物理规则门禁（数值真源/确定性/符号量纲/单调性）"
node tools/gate-physics.mjs; end $?

begin "代码质量门禁（作用域/禁用 API/静默吞异常/规模/接口/注释）"
node tools/gate-code.mjs; end $?

begin "资产几何真源门禁（footprint ↔ GLB 实际网格）"
node tools/gate-asset-bbox.mjs; end $?

begin "功能测试门禁（真编译真跑断言 · 分类计数 · 断言数不得下降）"
node tools/gate-test.mjs; end $?

begin "asmdef 与 V9 §13.1 规则表一致"
node tools/gen-asmdef.mjs --check; end $?

begin "Editor 代码 Unity API 出处（防臆造 API · CI #18 教训）"
node tools/gate-editor-api.mjs; end $?

begin "Packages/manifest.json 与 dependency-lock.json 一致"
node tools/gen-manifest.mjs --check; end $?

begin "DesignTokens.cs 与 data/design-tokens.json 一致"
node tools/gen-design-tokens.mjs --check; end $?

begin "架构守护（§13.1/§13.2/§19.1）"
node tools/arch-guard.mjs; end $?

begin "真源镜像一致性（data/ ↔ Assets/Data ↔ Resources）"
node tools/data-mirror.mjs; end $?

begin "关卡 DSL 校验 + Resources 镜像"
node tools/validate-levels.mjs; end $?

begin "资产清单准入（C3：产物落地 + 引用/kind 匹配 + 记录齐备）"
rc=0
node tools/validate-assets.mjs || rc=$?
node tools/gen-kits.mjs --check || rc=$?
end $rc

begin "C# 静态一致性检查"
node tools/cs-lint.mjs; end $?

begin "V9 §11 色彩 Token 对账双射"
node tools/tokens-map-check.mjs; end $?

begin "声纹移植等价性（灰盒 JS ↔ C# 移植）"
node tools/voice-port-vectors.mjs; end $?

begin "听觉索敌移植等价性（灰盒 ↔ C#）"
node tools/hearing-port-vectors.mjs; end $?

begin "三怪状态机移植等价性（灰盒 ↔ C#）"
node tools/monster-port-vectors.mjs; end $?

begin "配置表符号约定与量纲门禁"
node tools/config-lint.mjs; end $?

begin "Unity 依赖文件的语法+语义检查（Roslyn + Unity 桩）"
bash tools/unity-syntax-check.sh; end $?

begin "C# 真编译真跑（本机 .NET 8）"
rc=0
cp unity/Assets/Levels/*.json native/csharp-verify/ 2>/dev/null || true
(cd native/csharp-verify && ../../native/dotnet.sh run --nologo) || rc=$?
end $rc

# ── 汇总：红在哪几条，一眼看完（而不是只知道第一条红的）──
printf '\n\033[1m════════ 门禁链汇总（%s 步全跑完）════════\033[0m\n' "$STEP_NO"
fail=0
for i in "${!R_NAMES[@]}"; do
  if [ "${R_CODES[$i]}" = "0" ]; then
    printf '  \033[32m✓\033[0m %s\n' "${R_NAMES[$i]}"
  else
    printf '  \033[31m✗\033[0m %s  \033[31m(exit %s)\033[0m\n' "${R_NAMES[$i]}" "${R_CODES[$i]}"
    fail=$((fail + 1))
  fi
done
if [ "$fail" = 0 ]; then
  printf '\n\033[1;32mUnity 骨架检查完成：%s 步全绿\033[0m\n' "$STEP_NO"
else
  printf '\n\033[1;31mUnity 骨架检查：通过 %s · 失败 %s（详见上方各步输出）\033[0m\n' "$((STEP_NO - fail))" "$fail"
  exit 1
fi
