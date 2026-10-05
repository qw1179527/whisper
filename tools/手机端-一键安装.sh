#!/data/data/com.termux/files/usr/bin/sh
# ============================================================================
# 手机端一键安装（Termux / Android sh）
#
# ## 用法（在 Termux 里）
#   sh 手机端-一键安装.sh
#
# ## 做什么
#   ① 装基础环境（nodejs / python / git / unzip）
#   ② 让 Termux 能访问共享存储（/sdcard）
#   ③ 解压三个包：whisper-full.zip（工程）/ dsh-mcp-tools.zip（工具）/ 建模知识.zip
#   ④ 自检：跑本项目 Node 门禁 + 启动手机版 MCP 服务自检
#
# ## 重要：本脚本**不装 Unity**
#   Unity 官方明确「Editor requires Windows, macOS, or Linux」且「will not run on a
#   Chromebook or tablet」—— 所以**出 APK 无法在手机上做**。手机端定位是：
#   **建模（Blender）+ 门禁（Node）+ MCP 服务** 的生产端。
#   依据：https://learn.unity.com/pathway/unity-essentials/unit/editor-essentials/tutorial/unity-essentials-install-unity
#
# ## 行尾说明
#   本文件必须是 **LF** 行尾（Android 的 sh 不认 CRLF；CRLF 会报 "not found"）。
# ============================================================================
set -e

BASE="/sdcard/DSH专用/电脑上"
WORK="$HOME/dsh-work"

say() { printf '\n\033[1;36m== %s ==\033[0m\n' "$1"; }
ok()  { printf '  \033[1;32m✓\033[0m %s\n' "$1"; }
warn(){ printf '  \033[1;33m!\033[0m %s\n' "$1"; }

say "① 检查基础环境"
for c in node python git unzip; do
  if command -v "$c" >/dev/null 2>&1; then ok "$c 已装（$(command -v $c)）"
  else warn "$c 未装 → 执行 pkg install -y $c"; pkg install -y "$c" || warn "$c 安装失败，稍后手动装"; fi
done
node -v 2>/dev/null && ok "node $(node -v)" || warn "node 不可用"

say "② 共享存储访问"
if [ -d /sdcard ]; then ok "/sdcard 可访问"
else warn "不可访问 → 运行 termux-setup-storage 并授权"; termux-setup-storage || true; fi

say "③ 检查包是否就位"
for f in whisper-full.zip dsh-mcp-tools.zip 建模知识.zip; do
  if [ -f "$BASE/$f" ]; then
    sz=$(du -h "$BASE/$f" | cut -f1)
    ok "$f（$sz）"
  else
    warn "$f 不存在于 $BASE（若已解压可忽略）"
  fi
done

say "④ 解压到工作目录 $WORK"
mkdir -p "$WORK"
cd "$WORK"
[ -f "$BASE/whisper-full.zip" ] && { unzip -q -o "$BASE/whisper-full.zip" -d "$WORK"; ok "whisper 已解压"; } || warn "跳过 whisper"
[ -f "$BASE/dsh-mcp-tools.zip" ] && { unzip -q -o "$BASE/dsh-mcp-tools.zip" -d "$WORK"; ok "MCP 工具已解压"; } || warn "跳过 MCP 工具"
[ -f "$BASE/建模知识.zip" ] && { unzip -q -o "$BASE/建模知识.zip" -d "$WORK"; ok "建模知识已解压"; } || warn "跳过建模知识"
ls -la "$WORK" | head -20

say "⑤ 工程自检（Node 门禁）"
if [ -d "$WORK/whisper" ]; then
  cd "$WORK/whisper"
  for t in gate-model validate-levels data-mirror; do
    if [ -f "tools/$t.mjs" ]; then
      printf '  → node tools/%s.mjs\n' "$t"
      node "tools/$t.mjs" >/tmp/$t.log 2>&1 && ok "$t 通过（末尾：$(tail -1 /tmp/$t.log | cut -c1-70)）" \
        || warn "$t 失败，见 /tmp/$t.log"
    fi
  done
else
  warn "未找到 $WORK/whisper"
fi

say "⑥ 手机版 MCP 服务自检"
MCP="$WORK/whisper/tools/phone-mcp-server.mjs"
if [ -f "$MCP" ]; then
  node "$MCP" --selftest 2>&1 | head -12
  ok "MCP 服务可用（stdio 模式：node $MCP）"
else
  warn "未找到 phone-mcp-server.mjs（可能在 dsh-mcp-tools.zip 里）"
fi

say "完成"
cat <<'TIP'
后续用法：
  · 跑门禁：      cd ~/dsh-work/whisper && node tools/gate-model.mjs
  · 起 MCP 服务： node tools/phone-mcp-server.mjs        # stdio，供 MCP 客户端连接
  · 建模（Blender，需 proot）：
        pkg install -y proot-distro
        proot-distro install ubuntu
        proot-distro login ubuntu
        # 容器内： apt update && apt install -y blender python3-pip
  · 出 APK：**手机做不了**（Unity Editor 不支持 Android）→ 电脑或云端 CI
TIP
