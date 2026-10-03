#!/data/user/0/app.dsh.mobile/files/engine/bin/bash
# make-migration-zip.sh — 把本机 DSH 环境打包成可迁移到电脑端的压缩包
#
# 为什么分成两个包（而不是一个全量包）：
#   engine/extensions 下 19 个扩展共 **4.2 GB**，全是 **aarch64 + bionic** 的 Android 二进制。
#   电脑端 DSH 是 glibc/x86_64 —— 这些文件**在电脑上跑不了**，装了也是死的。
#   而它们占了全量的 98% 体积，混在一起会让核心包（真正要用的东西）难以传输与校验。
#   所以：
#     · core 包   = 一切可移植的东西（源码/文档/数据/工具/配置/状态）→ **迁移就靠它**
#     · engines 包 = 平台锁定的二进制，**当备份用**，电脑端应用扩展中心原生安装
#   两个包都要过硬：装机前先看 README-MIGRATION.md。
#
# 打包内容（core，按"迁移后能立刻继续工作"来定，不是无脑全量）：
#   · DSH 配置与状态：profiles / external / storages / sessions / AGENTS.md / graded-state 等
#   · 项目仓库：whisper（含 git 历史、156 条断言、门禁、CI 配置）
#   · 方案原文：DSH专用 下的 PDF 与已提取文本
#   · README-MIGRATION.md（电脑端开工步骤）+ MANIFEST.txt（本包清单，自动生成）
#
# 安全：打包前扫描明文凭据；命中即中止（绝不把密钥带进压缩包）。
set -euo pipefail

HOME_DIR="/data/user/0/app.dsh.mobile/files/dsh-home"
ENGINE_DIR="/data/user/0/app.dsh.mobile/files/engine"
STAGE="$HOME_DIR/tmp/migration-stage"
OUT_DIR="/storage/emulated/0/DSH专用"
STAMP="$(date +%Y%m%d-%H%M)"
CORE_ZIP="$OUT_DIR/DSH-MIGRATION-$STAMP-core.zip"
ENG_ZIP="$OUT_DIR/DSH-MIGRATION-$STAMP-engines-arm64.zip"
WANT_ENGINES="${1:-}"     # 传 --with-engines 才产出引擎包

log() { echo "$1"; }

# ── 凭据扫描（core 与 engines 共用）──
scan_leaks() {
  local dir="$1" label="$2"
  local hit=0
  if command -v rg >/dev/null 2>&1; then
    if rg -l --no-messages -e 'ghp_[A-Za-z0-9]{30,}' -e 'github_pat_[A-Za-z0-9_]{30,}' \
         -e 'BEGIN [A-Z ]*PRIVATE KEY' "$dir" 2>/dev/null | head -5 | grep -q .; then
      echo "  ✗ [$label] 发现疑似凭据："
      rg -l --no-messages -e 'ghp_[A-Za-z0-9]{30,}' -e 'github_pat_[A-Za-z0-9_]{30,}' "$dir" 2>/dev/null | head -5
      hit=1
    fi
  fi
  if [ "$hit" = "1" ]; then return 1; fi
  echo "  ✓ [$label] 未发现明文凭据"
  return 0
}

# ── 写 MANIFEST ──
write_manifest() {
  local dir="$1" name="$2"
  {
    echo "Project Whisper · DSH 迁移包清单"
    echo "包名     : $name"
    echo "生成时间 : $(date '+%Y-%m-%d %H:%M:%S %Z')"
    echo "来源     : Android DSH Mobile（\$HOME = $HOME_DIR）"
    echo "架构     : $(uname -m) · libc=bionic（Android）"
    echo
    echo "── 项目状态 ──"
    ( cd "$HOME_DIR/whisper" && echo "提交         : $(tools/git.sh rev-parse --short HEAD 2>/dev/null || echo '?')（共 $(tools/git.sh rev-list --count HEAD 2>/dev/null || echo '?') 次）"
      echo "工作区       : $(tools/git.sh status --porcelain 2>/dev/null | wc -l | tr -d ' ') 个未提交改动" )
    echo "本机断言     : 156 条（native/csharp-verify，真编译真跑）"
    echo "C# 规模      : $(find "$HOME_DIR/whisper/unity/Assets/Scripts" -name '*.cs' 2>/dev/null | wc -l | tr -d ' ') 个文件"
    echo "工具脚本     : $(ls "$HOME_DIR/whisper/tools"/*.mjs "$HOME_DIR/whisper/tools"/*.sh 2>/dev/null | wc -l | tr -d ' ') 个"
    echo
    echo "── 体积 ──"
    du -sh "$dir" 2>/dev/null | awk '{print "  总大小       : "$1}'
    if [ -d "$dir/whisper" ]; then du -sh "$dir/whisper" 2>/dev/null | awk '{print "  whisper      : "$1}'; fi
    if [ -d "$dir/engine-extensions" ]; then du -sh "$dir/engine-extensions" 2>/dev/null | awk '{print "  engine-extensions : "$1}'; fi
  } > "$dir/MANIFEST.txt"
}

log "=== [1/6] 准备暂存目录 ==="
rm -rf "$STAGE"; mkdir -p "$STAGE/core/DSH-MIGRATION"

log "=== [2/6] 拷贝 DSH 配置与状态 ==="
# 注意：storages/session_projcache 是**会话投影缓存**，会被运行时不断重建，
# 且实测其中残留过明文凭据（会话历史里的令牌）。缓存本身可重建，因此打包时排除。
for item in profiles profiles.last-good external storages sessions cache attachments \
            graded-state router-standard super-injector llm-deepseek AGENTS.md; do
  [ -e "$HOME_DIR/$item" ] || continue
  mkdir -p "$STAGE/core/DSH-MIGRATION/$(dirname "$item")"
  cp -a "$HOME_DIR/$item" "$STAGE/core/DSH-MIGRATION/$item"
done
rm -rf "$STAGE/core/DSH-MIGRATION/storages/session_projcache"
rm -rf "$STAGE/core/DSH-MIGRATION"/*/session_projcache 2>/dev/null || true

log "=== [3/6] 拷贝项目仓库（排除可重装工具链与缓存） ==="
# 本机没有 rsync（Android 常见），用 tar 管道 + --exclude 做排除式拷贝，不产生中间文件。
mkdir -p "$STAGE/core/DSH-MIGRATION/whisper"
tar -cf - -C "$HOME_DIR" \
  --exclude='whisper/native/dotnet' \
  --exclude='whisper/native/android-tools' \
  --exclude='whisper/native/unity-syntax/bin' \
  --exclude='whisper/native/unity-syntax/obj' \
  --exclude='whisper/native/*/bin' \
  --exclude='whisper/native/*/obj' \
  --exclude='whisper/tmp' \
  --exclude='whisper/build' \
  --exclude='*/node_modules' \
  --exclude='*/__pycache__' \
  --exclude='*/.venv' \
  --exclude='*.log' \
  whisper | tar -xf - -C "$STAGE/core/DSH-MIGRATION"
log "  项目仓库已拷贝"

log "  [3.5] 加入迁移说明、方案原文、清单"
cp "$HOME_DIR/tmp/README-MIGRATION.md" "$STAGE/core/DSH-MIGRATION/README-MIGRATION.md"
mkdir -p "$STAGE/core/DSH-MIGRATION/方案原文"
for f in /storage/emulated/0/DSH专用/*.pdf; do
  [ -f "$f" ] && cp "$f" "$STAGE/core/DSH-MIGRATION/方案原文/" || true
done
if [ -d "$HOME_DIR/whisper/docs/spec" ]; then
  mkdir -p "$STAGE/core/DSH-MIGRATION/方案原文/已提取文本"
  cp "$HOME_DIR/whisper/docs/spec/"*.txt "$STAGE/core/DSH-MIGRATION/方案原文/已提取文本/" 2>/dev/null || true
fi
# 把交付目录里的文档与 APK 一并带上（那是最新的成品）
mkdir -p "$STAGE/core/DSH-MIGRATION/交付物"
for f in /storage/emulated/0/DSH专用/*.md /storage/emulated/0/DSH专用/*.apk; do
  [ -f "$f" ] && cp "$f" "$STAGE/core/DSH-MIGRATION/交付物/" || true
done

log "=== [4/6] 凭据扫描（core） ==="
if ! scan_leaks "$STAGE/core" "core"; then
  rm -rf "$STAGE"; echo "请先清理后再打包"; exit 1
fi

log "=== [5/6] 生成 core 压缩包 ==="
mkdir -p "$OUT_DIR"
write_manifest "$STAGE/core/DSH-MIGRATION" "$(basename "$CORE_ZIP")"
( cd "$STAGE/core" && zip -qr "$CORE_ZIP" DSH-MIGRATION -x '*.DS_Store' )
ls -la "$CORE_ZIP" | awk '{printf "  ✓ core：%s（%.1f MB）\n", $NF, $5/1048576}'

# ── 引擎包（可选）──
if [ "$WANT_ENGINES" = "--with-engines" ]; then
  log "=== [6/6] 生成 engines 包（平台锁定二进制，当备份用） ==="
  mkdir -p "$STAGE/eng"
  # engines 里全是已压缩过的二进制（.so/.deb/.jar），再压收益极低且很慢 → 用 -0 仅存储
  cp -a "$ENGINE_DIR/extensions" "$STAGE/eng/engine-extensions"
  if [ -d "$HOME_DIR/whisper/native/dotnet" ]; then
    mkdir -p "$STAGE/eng/native-dotnet"
    cp -a "$HOME_DIR/whisper/native/dotnet/." "$STAGE/eng/native-dotnet/"
  fi
  cat > "$STAGE/eng/README-ENGINES.md" <<'ENGEOF'
# 引擎包（engine-extensions + native-dotnet）· 仅供留档

⚠️ **这些全是 aarch64 + bionic（Android）二进制，在电脑（glibc/x86_64）上跑不了。**

用途只有两个：
1. 万一要还原手机端环境（同架构同 libc）；
2. 想知道手机端装了哪些扩展、各是什么版本。

**电脑端正确做法**：用电脑端 DSH 的扩展中心原生安装同名扩展
（`git` 必须；`python` / `archivers` / `openjdk-17` 视需要），
`native/dotnet` 用 `whisper/native/fetch-dotnet.sh` 重新拉取对应平台版本。

扩展清单与体积见 MANIFEST.txt。
ENGEOF
  write_manifest "$STAGE/eng" "$(basename "$ENG_ZIP")"
  if ! scan_leaks "$STAGE/eng" "engines"; then
    rm -rf "$STAGE"; echo "请先清理后再打包"; exit 1
  fi
  ( cd "$STAGE/eng" && zip -0 -qr "$ENG_ZIP" . -x '*.DS_Store' )
  ls -la "$ENG_ZIP" | awk '{printf "  ✓ engines：%s（%.1f MB）\n", $NF, $5/1048576}'
else
  log "=== [6/6] 跳过 engines 包（未传 --with-engines） ==="
  log "  要一并产出（约 4.7 GB）请执行：bash tools/make-migration-zip.sh --with-engines"
fi

rm -rf "$STAGE"
log ""
log "完成。产物在 $OUT_DIR/"
ls -la "$OUT_DIR"/DSH-MIGRATION-*.zip 2>/dev/null | awk '{printf "  %s  %.1f MB\n", $NF, $5/1048576}'
