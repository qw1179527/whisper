#!/usr/bin/env bash
# render-evidence.sh — 一条命令复现渲染路径的**像素证据**
#
# ## 这条命令要证明什么（判据不是"我说灯生效了"，而是像素）
#   ① 光照真的生效：同一场景、同一相机、唯一变量是灯 → 开灯 vs 关灯两张图必须**可分辨地不同**
#      （旧版自研 Unlit 的 fragment 是 `return i.color;`，实测 4 盏定向光下 `_lit` 与 `_dark` 逐像素相同、平均差 0.00）
#   ② 雾可开关且**不洗白**：默认雾很淡（暗色，只压暗远处）；关雾 / 默认 / 调浓三档必须可分辨，
#      且"开雾后的平均亮度不得高于关雾"（2026-10-03 真机事故：雾把整个 3D 视图洗成 #D8CFBB，
#      亮度 207/255 · 标准差 2.1 · 边缘密度 0.0%——那种形态由 RenderEvidenceCapture 内部直接判红）
#
# ## 判据分两层（缺一不可）
#   第一层 = 真 Unity 进程的退出码 **且** 日志里必须出现 `RENDER_EVIDENCE OK`
#            （只看退出码不够：executeMethod 名字写错会"静默成功"，本项目已有先例）
#   第二层 = 用**现成的独立工具** `tools/pixel-diff-pair.mjs` 逐像素比对两张 PNG，
#            输出"变化像素占比"，并按阈值判红/判绿
#
# ## 用法
#   bash tools/render-evidence.sh
#   可用环境变量覆盖：UNITY_EXE / UNITY_PROJECT / OUT_DIR
#   证据目录默认 $ROOT/../_evidence/render-v2（即 _evidence/render-v2/）
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY_EXE="${UNITY_EXE:-D:/Unity/6000.3.25f1/Editor/Unity.exe}"
UNITY_PROJECT="${UNITY_PROJECT:-D:/dsh-whisper-unity}"
OUT_DIR="${OUT_DIR:-$ROOT/../_evidence/render-v2}"
OUT_DIR="$(cd "$(dirname "$OUT_DIR")" 2>/dev/null && pwd)/$(basename "$OUT_DIR")"

SHADER_REL="Assets/Resources/Shaders/WhisperUnlitColor.shader"
METHOD="Whisper.Editor.RenderEvidenceCapture.Run"

# 光照判据：像素变化占比下限（= pixel-diff-pair 的 "有变化 ★" 阈值）
LIGHT_MIN_PCT="${LIGHT_MIN_PCT:-1.0}"
# 雾判据（默认雾很淡，故下限低；"调浓"另用更高阈值，见下）
FOG_MIN_PCT="${FOG_MIN_PCT:-0.05}"
FOG_STRONG_MIN_PCT="${FOG_STRONG_MIN_PCT:-0.5}"
# 手电判据：主光关着，手电开/关的画面必须可分辨（ForwardAdd pass 的判据）
FLASHLIGHT_MIN_PCT="${FLASHLIGHT_MIN_PCT:-1.0}"

echo "[render-evidence] 渲染路径像素取证"
[ -f "$UNITY_EXE" ] || { echo "  ✗ 找不到 Unity：$UNITY_EXE（可用 UNITY_EXE 覆盖）"; exit 2; }
[ -d "$UNITY_PROJECT" ] || { echo "  ✗ 找不到 ASCII 工程：$UNITY_PROJECT（见 docs/HANDOFF.md 第四节·五）"; exit 2; }
# junction 必须真的指向本仓库的 unity/ —— 指错工程会让"证据"与"真源"脱钩（静默型错误）
[ -f "$UNITY_PROJECT/$SHADER_REL" ] || { echo "  ✗ $UNITY_PROJECT 下没有 $SHADER_REL —— junction 指错工程了？"; exit 2; }
mkdir -p "$OUT_DIR"
log="$OUT_DIR/render-evidence-unity.log"
rm -f "$log"

# ⚠ -projectPath 必须带引号（不带会静默回落到"上次打开的工程"）；⚠ 不要加 -nographics
#   （Camera.Render() 在无图形设备下会崩 0xC0000005）
#
# ── 共享工程锁：**同一个 Unity 工程只能有一个实例**（实测：并发启动会
#    `Aborting batchmode due to fatal error: It looks like another Unity instance is running`）──
# 本项目有多个智能体/流水线会跑真 Unity（套件取证、测试回归、本条取证），所以这里**按原因重试**：
# 只在日志里真的出现"另一实例占用"时等待重试，超时则明确报错 —— 不用 lockfile 存在与否判断，
# 因为崩溃残留的 lockfile 会让判据假阳性。
LOCK_NOTE="$UNITY_PROJECT/Temp/UnityLockfile"
# 共享工程锁：与 lead 的链 / unity-tests.sh / kit-bytes-selftest.sh 用**同一把锁**（tools/unity-lock.sh）。
# 拿不到就明确失败（exit 3 = 环境忙，不是判据不通过）。下面"看进程/lockfile + 按日志原因重试"是兜底。
if [ -f "$ROOT/tools/unity-lock.sh" ]; then
  # shellcheck disable=SC1091
  source "$ROOT/tools/unity-lock.sh"
  if declare -F unity_lock_acquire >/dev/null 2>&1; then
    unity_lock_acquire "render-evidence" || { echo "  ✗ 拿不到工程锁（别的 Unity 消费者在跑）"; exit 3; }
    trap 'declare -F unity_lock_release >/dev/null 2>&1 && unity_lock_release' EXIT
  fi
fi
# 共享工程锁（团队约定）：跑之前先等锁释放 —— 判据 = 没有 Unity 进程 **且** 没有 lockfile。
# 等待是**有界**的：崩溃残留的 lockfile 会让"只看 lockfile"的判据假阳性，
# 所以超时后带警告继续跑，真正的并发冲突由下面"按日志原因重试"兜住（两道防线，各管一种情况）。
unity_running() {
  command -v tasklist.exe >/dev/null 2>&1 || return 1
  tasklist.exe /FI "IMAGENAME eq Unity.exe" 2>/dev/null | grep -qi '^Unity\.exe'
}
waited=0
while { [ -f "$LOCK_NOTE" ] || unity_running; } && [ "$waited" -lt "${LOCK_WAIT_S:-120}" ]; do
  [ "$waited" = 0 ] && echo "  · 工程被另一个 Unity 实例占用（lockfile 或 Unity.exe 存在），等待释放…"
  sleep 10
  waited=$((waited + 10))
done
[ "$waited" -gt 0 ] && echo "  · 已等待 ${waited}s"
echo "  · 真 Unity 离屏渲染 → $OUT_DIR"

attempt=0
max_attempts="${LOCK_MAX_ATTEMPTS:-40}"
while :; do
  attempt=$((attempt + 1))
  rm -f "$log"
  "$UNITY_EXE" -quit -batchmode \
      -projectPath "$UNITY_PROJECT" \
      -executeMethod "$METHOD" \
      -captureDir "$OUT_DIR" \
      -logFile "$log"
  rc=$?
  if grep -q 'another Unity instance is running' "$log" 2>/dev/null; then
    if [ "$attempt" -lt "$max_attempts" ]; then
      echo "  · 第 $attempt 次：工程被另一个 Unity 实例占用，${LOCK_RETRY_S:-15}s 后重试…"
      sleep "${LOCK_RETRY_S:-15}"
      continue
    fi
    echo "  ✗ 重试 $max_attempts 次后工程仍被占用 —— 请与另一个 Unity 任务错开时间（并发会 fatal abort）"
    exit 3
  fi
  break
done

echo "  Unity rc=$rc · 日志：$log"
grep -E '\[RENDER\]' "$log" 2>/dev/null | sed 's/^/  /' || true

if [ "$rc" != "0" ]; then
  echo "  ✗ 取证脚本判红（Unity rc=$rc）—— 逐条理由见上方 [RENDER] ✗ 行"
  exit 1
fi
if [ "$(grep -c 'RENDER_EVIDENCE OK' "$log" 2>/dev/null || echo 0)" -lt 1 ]; then
  echo "  ✗ Unity 退出码为 0，但日志里没有 RENDER_EVIDENCE OK ——"
  echo "     很可能是取证脚本根本没跑到（executeMethod 名字/路径错误会静默成功）"
  tail -15 "$log" | sed 's/^/  /'
  exit 1
fi

# ── 第二层判据：独立工具逐像素比对 ──
fail=0
pct_of() { node "$ROOT/tools/pixel-diff-pair.mjs" "$1" "$2" 2>&1 | grep -oE '[0-9]+\.[0-9]{3}%' | head -1 | tr -d '%'; }
ge() { awk -v a="$1" -v b="$2" 'BEGIN{ exit !(a+0 >= b+0) }'; }

check_pair() {   # $1=基准 $2=对照 $3=说明 $4=下限% $5=视角标签
  local a="$1" b="$2" why="$3" min="$4" tag="$5" pct
  if [ ! -f "$a" ] || [ ! -f "$b" ]; then
    echo "  ✗ $tag 缺图：$(basename "$a") / $(basename "$b")"; fail=1; return
  fi
  node "$ROOT/tools/pixel-diff-pair.mjs" "$a" "$b" | sed 's/^/    /'
  pct="$(pct_of "$a" "$b")"
  if [ -z "$pct" ]; then echo "  ✗ $tag 无法解析变化占比（pixel-diff-pair 输出异常）"; fail=1; return; fi
  if ge "$pct" "$min"; then
    echo "  ✓ $tag $why：变化 $pct% ≥ $min%"
  else
    echo "  ✗ $tag $why：变化 $pct% < $min% —— 判据不成立（对渲染没有实际作用）"; fail=1
  fi
}

echo ""
echo "── 证据① 光照开关（同一场景同一相机，唯一变量=灯）──"
# 必判：**在房间内部**的视角（几何铺满画面 → 灯一关，每个墙面像素都变）。
# 不判：orbit33 外部全景视角（房间只占画面一部分，变化占比会被人为摊薄）—— 仍会打印数值供人工复看。
for v in entrance_safe_eye corridor_main_eye corridor_main_alongX morgue_deep_eye; do
  check_pair "$OUT_DIR/${v}_lightOff_fogDefault.png" "$OUT_DIR/${v}_lightOn_fogDefault.png" \
             "开灯 vs 关灯可分辨" "$LIGHT_MIN_PCT" "$v"
done
for v in entrance_safe_orbit33 corridor_main_orbit33; do
  echo "  · [$v] 参考数值（外部全景，不参与判红）"
  node "$ROOT/tools/pixel-diff-pair.mjs" "$OUT_DIR/${v}_lightOff_fogDefault.png" "$OUT_DIR/${v}_lightOn_fogDefault.png" | sed 's/^/    /'
done

echo ""
echo "── 证据② 雾开关与调浓（默认雾 = 近黑淡雾，8m 起、40m 饱和）──"
# 判据必须落在**长视线**视角上：雾是距离雾，3~4m 的房间里（视线 < 8m）**按设计就没有雾**，
# 拿小房间判"雾没生效"是判据错位（本脚本第一版就差点这么判）。
# 判定视角取外部全景（相机在 30m 外 → 混合 69%）：这是**唯一**能把"关雾/开雾"拉出 8/255 判据的视角；
# 走廊内部视角（17m → 28%）数值更小，作为参考打印，不参与判红（避免用临界值当判据）。
for v in corridor_main_orbit33; do
  check_pair "$OUT_DIR/${v}_lightOn_fogOff.png" "$OUT_DIR/${v}_lightOn_fogDefault.png" \
             "关雾 vs 默认雾可分辨" "$FOG_MIN_PCT" "$v"
  check_pair "$OUT_DIR/${v}_lightOn_fogOff.png" "$OUT_DIR/${v}_lightOn_fogStrong.png" \
             "关雾 vs 调浓可分辨" "$FOG_STRONG_MIN_PCT" "$v"
done
for v in entrance_safe_orbit33 corridor_main_alongX corridor_main_eye entrance_safe_eye morgue_deep_eye; do
  echo "  · [$v] 参考数值（不参与判红）"
  node "$ROOT/tools/pixel-diff-pair.mjs" "$OUT_DIR/${v}_lightOn_fogOff.png" "$OUT_DIR/${v}_lightOn_fogDefault.png" | sed 's/^/    /'
done

echo ""
echo "── 证据③ 手电筒（ForwardAdd）：主光关掉，唯一变量=手电开关 ──"
# 这是"额外像素光能不能照亮画面"的判据：此前着色器只有 ForwardBase → 手电筒对渲染零作用。
for v in entrance_safe_eye corridor_main_eye corridor_main_alongX morgue_deep_eye; do
  check_pair "$OUT_DIR/${v}_lightOff_fogDefault.png" "$OUT_DIR/${v}_lightOff_flashlight.png" \
             "手电开 vs 关可分辨" "$FLASHLIGHT_MIN_PCT" "$v"
done

echo ""
if [ "$fail" != "0" ]; then
  echo "[render-evidence] ✗ 有判据不成立（证据目录：$OUT_DIR）"
  exit 1
fi
echo "[render-evidence] ✓ 两条像素证据全部成立"
echo "  · 证据目录：$OUT_DIR"
echo "  · 指标表：$OUT_DIR/render-index.csv（亮度/标准差/颜色数/洋红占比/相对基准变化）"
echo "  · 人工复看：$OUT_DIR/<房间>_<视角>_<相位>.png"
