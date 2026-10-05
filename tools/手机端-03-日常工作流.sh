#!/data/data/com.termux/files/usr/bin/sh
# ============================================================================
# ③ B 方案日常工作流：手机建模 → 门禁 → 提交（电脑只在出包时开机）
#
# ## 用法
#   sh 手机端-03-日常工作流.sh            # 全流程：门禁 + 生成 + 状态
#   sh 手机端-03-日常工作流.sh gate       # 只跑门禁
#   sh 手机端-03-日常工作流.sh gen        # 只跑生成器（含 Blender 链路）
#   sh 手机端-03-日常工作流.sh status     # 只看状态与 git 摘要
#
# ## 这套流程存在的意义
#   官方 Unity 明确 Editor 不能在 Android 跑（出 APK 只能在电脑/CI），
#   所以手机端定位是**建模 + 门禁**的生产端：
#     · 手机上改布局表 / 材质 / 套件 → 立刻跑门禁验证
#     · 验证通过 → git commit + push
#     · 电脑（或你已实现的 CI）只在需要 APK 时拉取并出包
#
# ## 行尾：必须 LF
# ============================================================================
set -e
WORK="${DSH_WORK:-$HOME/dsh-work/whisper}"
R='\033[1;31m'; G='\033[1;32m'; C='\033[1;36m'; Y='\033[1;33m'; N='\033[0m'
say() { printf "\n${C}== %s ==${N}\n" "$1"; }
ok()  { printf "  ${G}✓${N} %s\n" "$1"; }
bad() { printf "  ${R}✗${N} %s\n" "$1"; }
warn(){ printf "  ${Y}!${N} %s\n" "$1"; }

CMD="${1:-all}"
cd "$WORK" 2>/dev/null || { bad "工作目录不存在：$WORK（设 DSH_WORK 或先解压）"; exit 1; }
say "工程：$(pwd)"

run_gate() {
  say "门禁（与电脑上同一套脚本）"
  fail=0
  for t in gate-model validate-levels data-mirror; do
    if [ -f "tools/$t.mjs" ]; then
      if node "tools/$t.mjs" >/tmp/$t.log 2>&1; then
        ok "$t 通过 · $(tail -1 /tmp/$t.log | cut -c1-64)"
      else
        bad "$t **失败** → /tmp/$t.log"; tail -6 /tmp/$t.log; fail=1
      fi
    else warn "缺 tools/$t.mjs"; fi
  done
  if [ -f "tools/unity-syntax-check.sh" ]; then
    if sh tools/unity-syntax-check.sh >/tmp/syn.log 2>&1; then ok "unity-syntax-check 通过"
    else bad "unity-syntax-check 失败 → /tmp/syn.log"; fail=1; fi
  fi
  [ "$fail" -eq 0 ] && ok "门禁全部通过" || bad "有门禁未通过（**不要提交**）"
  return $fail
}

run_gen() {
  say "生成器（先自检，再真生成）"
  [ -f tools/gen-map.mjs ] && { node tools/gen-map.mjs --layout tanglewood_v1 --out /tmp/t.json \
    && ok "gen-map 通过（tanglewood_v1）" || bad "gen-map 失败"; }
  [ -f tools/gen-asylum-v1.mjs ] && { node tools/gen-asylum-v1.mjs >/tmp/ga.log 2>&1 \
    && ok "gen-asylum 通过" || bad "gen-asylum 失败 → /tmp/ga.log"; }
  # Blender 链路（GlbReader / gen-kits）——需容器内 Blender
  if command -v blender >/dev/null 2>&1; then ok "Blender 可用：$(command -v blender)"
  elif command -v proot-distro >/dev/null 2>&1; then
    warn "Termux 本体无 Blender（正常）→ 用容器跑："
    echo "      proot-distro login ubuntu -- bash -lc 'cd $WORK && node tools/gen-kits.mjs --emit /tmp/kits-out'"
  else warn "无 Blender 也无 proot-distro（先跑 手机端-01）"; fi
}

show_status() {
  say "状态"
  du -sh "$WORK" 2>/dev/null || true
  [ -f unity/ProjectSettings/ProjectVersion.txt ] && ok "Unity 版本：$(grep m_EditorVersion: unity/ProjectSettings/ProjectVersion.txt | head -1)"
  [ -f data/config.json ] && ok "config 存在（$(wc -c < data/config.json) 字节）"
  if [ -d .git ]; then
    say "git"
    git log --oneline -3 2>/dev/null || warn "无提交历史"
    git status --short 2>/dev/null | head -10 || true
  else warn "无 .git（whisper-full.zip 里应包含）"; fi
  say "提醒：出 APK 不在手机（Unity Editor 不支持 Android）→ 走电脑或你已实现的 CI"
}

case "$CMD" in
  gate)   run_gate ;;
  gen)    run_gen ;;
  status) show_status ;;
  all)    run_gate && run_gen; show_status ;;
  *) echo "未知参数：$CMD（可用 gate|gen|status|all）"; exit 2 ;;
esac
