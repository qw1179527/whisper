#!/usr/bin/env bash
# door-evidence.sh — 门系统的**像素证据**（一条命令复现）
#
# ## 证明什么
#   ① 门扇真的会动：同机位、同场景，唯一变量是门的开合（关 / 半开 45° / 全开）→ 像素必须可分辨
#   ② 一个物理洞口只有一块门扇：旧实现给每个 DSL 条目建板 → 10 对完全同位共面（z-fighting）
#   ③ 门洞真的凿出来了：每扇门的门格数 > 0（为 0 就是"登记不上、关门挡不住"的老毛病）
#
# ## 判据分两层
#   第一层 = 真 Unity 退出码 **且** 日志出现 `DOOR_EVIDENCE OK`（只看退出码不够：executeMethod 名字写错会静默成功）
#   第二层 = 用现成的独立工具 `tools/pixel-diff-pair.mjs` 按 `door-pairs.txt` 逐对比对，
#            阈值由取证脚本写进清单（门槛不写死在本脚本里，避免两边各说一套）
#
# 用法：bash tools/door-evidence.sh    （UNITY_EXE / UNITY_PROJECT / OUT_DIR 可覆盖）
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY_EXE="${UNITY_EXE:-D:/Unity/6000.3.25f1/Editor/Unity.exe}"
UNITY_PROJECT="${UNITY_PROJECT:-D:/dsh-whisper-unity}"
OUT_DIR="${OUT_DIR:-$ROOT/../_evidence/build-a/doors}"
OUT_DIR="$(cd "$(dirname "$OUT_DIR")" 2>/dev/null && pwd)/$(basename "$OUT_DIR")"
METHOD="Whisper.Editor.DoorEvidenceCapture.Run"

echo "[door-evidence] 门系统像素取证"
[ -f "$UNITY_EXE" ] || { echo "  ✗ 找不到 Unity：$UNITY_EXE（可用 UNITY_EXE 覆盖）"; exit 2; }
[ -d "$UNITY_PROJECT" ] || { echo "  ✗ 找不到 ASCII 工程：$UNITY_PROJECT（见 docs/HANDOFF.md 第四节·五）"; exit 2; }
[ -f "$UNITY_PROJECT/Assets/Scripts/Gameplay/Level/LevelGeometry.Doors.cs" ] || { echo "  ✗ junction 指错工程（找不到门几何文件）"; exit 2; }
mkdir -p "$OUT_DIR"
log="$OUT_DIR/door-evidence-unity.log"

# 共享工程锁：与 lead 的链 / unity-tests.sh / kit-bytes-selftest.sh **同一把锁**（tools/unity-lock.sh）。
# 拿不到就明确失败（exit 3 = 环境忙，不是判据不通过）—— 并发启动 Unity 会 fatal abort，那是假红。
if [ -f "$ROOT/tools/unity-lock.sh" ]; then
  # shellcheck disable=SC1091
  source "$ROOT/tools/unity-lock.sh"
  if declare -F unity_lock_acquire >/dev/null 2>&1; then
    unity_lock_acquire "door-evidence" || { echo "  ✗ 拿不到工程锁（别的 Unity 消费者在跑）"; exit 3; }
    trap 'declare -F unity_lock_release >/dev/null 2>&1 && unity_lock_release' EXIT
  fi
fi

# 兜底（锁工具不可用时）：判据 = 无 Unity 进程且无 lockfile；有界等待 + 按日志原因重试。
unity_running() {
  command -v tasklist.exe >/dev/null 2>&1 || return 1
  # 【2026-10-04 修 · 这个 bug 让第 26 步连续假红 40 次 ×20s ≈ 13 分钟】
  # 旧写法：`tasklist /FI "IMAGENAME eq Unity.exe" | grep -qi '^Unity\.exe'`
  #   tasklist 的行首是 **Image Name** 列，而 `Unity Hub.exe` 同样匹配 `^Unity\.exe`
  #   （grep 只锚了前缀）→ **Unity Hub 常驻** 就永远判定"Unity 在跑"
  #   → is_contention() 恒真 → 明明已经跑完也一直重试到 40 次上限。
  # 新写法：① `-NH` 去掉表头 ② 只匹配**精确的 `Unity.exe`**（后接空白/行尾）
  #   ③ 这样 Hub / Licensing / CrashHandler 都不会混进来。
  tasklist.exe -NH -FI "IMAGENAME eq Unity.exe" 2>/dev/null \
    | grep -qiE '^Unity\.exe[[:space:]]'
}
waited=0
while { [ -f "$UNITY_PROJECT/Temp/UnityLockfile" ] || unity_running; } && [ "$waited" -lt "${LOCK_WAIT_S:-60}" ]; do
  [ "$waited" = 0 ] && echo "  · 工程被另一个 Unity 实例占用，等待释放…"
  sleep 10; waited=$((waited + 10))
done
[ "$waited" -gt 0 ] && echo "  · 已等待 ${waited}s"
echo "  · 真 Unity 离屏渲染 → $OUT_DIR"

attempt=0
max_attempts="${LOCK_MAX_ATTEMPTS:-40}"
# "并发占用"的三种症状（都在实测里出现过）：① 明确报另一实例占用；
# ② 许可客户端被别的实例抢走（`LicensingClient has failed validation` / `Failed to handshake to channel`）；
# ③ 日志还没写出结论行、而工程锁/Unity 进程仍在（我自己这条被挤掉）。
#
# 【2026-10-04 修 · 两个 bug 叠加，让这一步连续假红 40 次 ×20s ≈ 13 分钟，且**掩盖了真原因**】
#  bug A：判据 ② 只看"日志里有没有许可告警"，没看**是否已经拿到结论**。
#    Unity **每次启动都会打**这两条告警（`LicensingClient has failed validation` /
#    `Failed to handshake to channel`），随后自己重启许可客户端并**正常跑完** —— 实测日志末尾
#    是干净的 "Shut down"，取证图 100+ 张全部生成。但只要告警在，② 就恒真 → 一直重试。
#  bug B：注释里写着"判据一律排除已经拿到结论的情况"，但**只有判据 ③ 做了这个排除**，
#    ①② 没有 → 注释与实现不一致（这正是"注释替实现背书"的老毛病）。
#
# 修法：把"是否已经拿到结论"提到**函数最前面**当成短路条件 —— 结论行一旦出现，
#   立刻 return 1（不是并发），让外层按真实 rc / 结论行判红或判绿。
#   另外日志是 **UTF-8**，grep 必须显式 `-a`（否则二进制判定会跳过含中文的行，
#   连 `DOOR_EVIDENCE FAIL` 都可能看不到 —— 实测就是这个现象）。
has_conclusion() {
  grep -aqE 'DOOR_EVIDENCE (OK|FAIL)' "$log" 2>/dev/null
}
is_contention() {
  # 已经拿到结论 → 绝不是并发（无论日志里有多少许可告警）
  has_conclusion && return 1
  grep -aq 'another Unity instance is running' "$log" 2>/dev/null && return 0
  grep -aqE 'LicensingClient has failed validation|Failed to handshake to channel' "$log" 2>/dev/null && return 0
  if [ -f "$UNITY_PROJECT/Temp/UnityLockfile" ] || unity_running; then return 0; fi
  return 1
}
while :; do
  attempt=$((attempt + 1))
  rm -f "$log"
  "$UNITY_EXE" -quit -batchmode \
      -projectPath "$UNITY_PROJECT" \
      -executeMethod "$METHOD" \
      -captureDir "$OUT_DIR" \
      -logFile "$log"
  rc=$?
  if is_contention; then
    if [ "$attempt" -lt "$max_attempts" ]; then
      echo "  · 第 $attempt 次：判定为**并发占用**（不是代码问题），${LOCK_RETRY_S:-20}s 后重试…"
      sleep "${LOCK_RETRY_S:-20}"; continue
    fi
    echo "  ✗ 重试 $max_attempts 次后工程/许可仍被占用 —— 请与另一个 Unity 任务错开"; exit 3
  fi
  break
done

echo "  Unity rc=$rc · 日志：$log"
# 日志是 **UTF-8**，grep 必须 `-a`：否则含中文的行会被当二进制跳过 ——
# 实测正是这个原因让 `[DOOR] ✗ …` 与 `DOOR_EVIDENCE FAIL` 都看不见，只看到"占用"。
grep -aE '\[DOOR\]' "$log" 2>/dev/null | sed 's/^/  /' || true
[ "$rc" != "0" ] && { echo "  ✗ 门取证判红（Unity rc=$rc）—— 逐条理由见上方 [DOOR] ✗ 行"; exit 1; }
if [ "$(grep -ac 'DOOR_EVIDENCE OK' "$log" 2>/dev/null || echo 0)" -lt 1 ]; then
  echo "  ✗ Unity 退出码为 0，但日志里没有 DOOR_EVIDENCE OK —— 很可能取证脚本根本没跑到"
  tail -15 "$log" | sed 's/^/  /'; exit 1
fi

# ── 第二层：独立工具逐像素比对（清单由取证脚本产出）──
pairs="$OUT_DIR/door-pairs.txt"
[ -f "$pairs" ] || { echo "  ✗ 缺比对清单 $pairs"; exit 1; }
fail=0; n=0
while IFS='|' read -r base other minpct label region; do
  [ -z "${base:-}" ] && continue
  n=$((n + 1))
  a="$OUT_DIR/$base"; b="$OUT_DIR/$other"
  if [ ! -f "$a" ] || [ ! -f "$b" ]; then echo "  ✗ $label：缺图（$base / $other）"; fail=1; continue; fi
  if [ -n "${region:-}" ]; then
    out=$(node "$ROOT/tools/pixel-diff-pair.mjs" "$a" "$b" --region "$region" 2>&1)
  else
    out=$(node "$ROOT/tools/pixel-diff-pair.mjs" "$a" "$b" 2>&1)
  fi
  pct=$(echo "$out" | grep -oE '[0-9]+\.[0-9]{3}%' | head -1 | tr -d '%')
  echo "$out" | tail -2 | sed 's/^/    /'
  if [ -z "$pct" ]; then echo "  ✗ $label 无法解析变化占比"; fail=1; continue; fi
  if awk -v a="$pct" -v b="$minpct" 'BEGIN{ exit !(a+0 >= b+0) }'; then
    echo "  ✓ $label：变化 $pct% ≥ $minpct%"
  else
    echo "  ✗ $label：变化 $pct% < $minpct%"; fail=1
  fi
done < "$pairs"

echo ""
if [ "$fail" != "0" ]; then echo "[door-evidence] ✗ 有判据不成立（$OUT_DIR）"; exit 1; fi
echo "[door-evidence] ✓ 门系统像素证据全部成立（比对 $n 对）"
echo "  · 证据目录：$OUT_DIR"
echo "  · 指标表：$OUT_DIR/door-index.csv · 人工复看：$OUT_DIR/<门型>_<房间_门>_<相位>.png"
