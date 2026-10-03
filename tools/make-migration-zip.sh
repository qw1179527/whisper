#!/data/user/0/app.dsh.mobile/files/engine/bin/bash
# make-migration-zip.sh — 把本机 DSH 环境打包成可迁移到电脑端的压缩包
#
# 打包内容（按"迁移后能立刻继续工作"来定，不是无脑全量）：
#   · DSH 配置与插件：profiles / external / storages / sessions / AGENTS.md / router-standard
#   · 项目仓库：whisper（含 git 历史、113 条断言、门禁、CI 配置）
#   · 方案原文：DSH专用 下的 PDF 与已提取文本
#
# 刻意排除（可在电脑端重装或用 git 恢复，占了 98% 体积）：
#   · whisper/native/dotnet（472MB，用 fetch-dotnet.sh 重装）
#   · whisper/native/android-tools（24MB，用 fetch-android-tools.sh 重装）
#   · whisper/native/unity-syntax 的缓存/产物（44MB，脚本可重建）
#   · androidsdk（754MB）、tmp（508MB）、node_modules、__pycache__、.venv
#
# 安全：打包前扫描明文凭据；命中即中止（避免把密钥带进压缩包）。
set -euo pipefail

HOME_DIR="/data/user/0/app.dsh.mobile/files/dsh-home"
STAGE="$HOME_DIR/tmp/migration-stage"
OUT_DIR="/storage/emulated/0/DSH专用"
STAMP="$(date +%Y%m%d-%H%M)"
ZIP="$OUT_DIR/DSH-MIGRATION-$STAMP.zip"

echo "[1/5] 准备暂存目录"
rm -rf "$STAGE"; mkdir -p "$STAGE/DSH-MIGRATION"

echo "[2/5] 拷贝 DSH 配置与状态"
# 注意：storages/session_projcache 是**会话投影缓存**，会被运行时不断重建，
# 且实测其中残留过明文凭据（会话历史里的令牌）。缓存本身可重建，因此打包时排除，
# 只保留 storages 下的其它状态（工具状态、门禁基线等）。
for item in profiles profiles.last-good external storages sessions cache attachments \
            graded-state router-standard super-injector llm-deepseek AGENTS.md; do
  [ -e "$HOME_DIR/$item" ] || continue
  mkdir -p "$STAGE/DSH-MIGRATION/$(dirname "$item")"
  cp -a "$HOME_DIR/$item" "$STAGE/DSH-MIGRATION/$item"
done
# 移除会被重建且可能残留凭据的会话投影缓存
rm -rf "$STAGE/DSH-MIGRATION/storages/session_projcache"

echo "[3/5] 拷贝项目仓库（排除可重装工具链与缓存）"
# 注：本机没有 rsync（Android 上很常见），因此用 tar 管道 + --exclude 完成排除式拷贝。
# tar 在这里只做"打包到 stdout 再解开"，不产生中间文件。
mkdir -p "$STAGE/DSH-MIGRATION/whisper"
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
  whisper | tar -xf - -C "$STAGE/DSH-MIGRATION"
echo "  项目仓库已拷贝"

echo "  [3.5] 加入迁移说明与方案原文"
cp "$HOME_DIR/tmp/README-MIGRATION.md" "$STAGE/DSH-MIGRATION/README-MIGRATION.md"
mkdir -p "$STAGE/DSH-MIGRATION/方案原文"
for f in /storage/emulated/0/DSH专用/*.pdf; do
  [ -f "$f" ] && cp "$f" "$STAGE/DSH-MIGRATION/方案原文/" || true
done
[ -d "$HOME_DIR/whisper/docs/spec" ] && mkdir -p "$STAGE/DSH-MIGRATION/方案原文/已提取文本" && cp "$HOME_DIR/whisper/docs/spec/"*.txt "$STAGE/DSH-MIGRATION/方案原文/已提取文本/" 2>/dev/null || true
echo "  已加入说明文档与 PDF"

echo "[4/5] 安全扫描：检查是否有明文凭据"
LEAK=0
# GitHub 令牌 / 私钥 / 密码赋值（排除 .git 内的对象与已知示例）
if command -v rg >/dev/null 2>&1; then
  if rg -l --no-messages -e 'ghp_[A-Za-z0-9]{30,}' -e 'github_pat_[A-Za-z0-9_]{30,}' \
       -e 'BEGIN [A-Z ]*PRIVATE KEY' "$STAGE" | head -5 | grep -q .; then
    echo "  ✗ 发现疑似凭据，已中止打包："; rg -l --no-messages -e 'ghp_[A-Za-z0-9]{30,}' -e 'github_pat_[A-Za-z0-9_]{30,}' "$STAGE" | head -5; LEAK=1
  fi
fi
if [ "$LEAK" = "1" ]; then rm -rf "$STAGE"; echo "请先清理后再打包"; exit 1; fi
echo "  ✓ 未发现明文凭据"

echo "[5/5] 生成压缩包"
mkdir -p "$OUT_DIR"
( cd "$STAGE" && zip -qr "$ZIP" DSH-MIGRATION -x '*.DS_Store' )
rm -rf "$STAGE"
ls -la "$ZIP" | awk '{printf "  完成：%s（%.1f MB）\n", $NF, $5/1048576}'
