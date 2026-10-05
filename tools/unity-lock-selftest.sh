#!/usr/bin/env bash
# 验证 unity-lock.sh：两个进程争锁时，后到者应"等待并最终拿到"，而不是让 Unity 报 another instance
set -uo pipefail
cd "$(dirname "$0")/.."

echo "=== 场景 1：前一个进程持锁 8 秒，后一个应等待后拿到 ==="
UNITY_LOCK_PURPOSE=holder bash -c 'source tools/unity-lock.sh; unity_lock_acquire; sleep 8; unity_lock_release' &
HOLDER=$!
sleep 1
UNITY_LOCK_PURPOSE=waiter bash -c 'source tools/unity-lock.sh; if unity_lock_acquire; then echo "  ✓ 后到者拿到锁（等待后成功）"; else echo "  ✗ 后到者失败"; exit 1; fi'
WAIT_RC=$?
wait $HOLDER
echo "  后到者退出码 = $WAIT_RC"

echo
echo "=== 场景 2：陈旧锁应被接管（伪造 40 分钟前的锁） ==="
LOCK="${TMPDIR:-/tmp}/whisper-unity.lock"
mkdir -p "$LOCK"
printf 'pid=999999\npurpose=simulated-crash\nat=%s\n' "$(date -u -d '40 minutes ago' +%Y-%m-%dT%H:%M:%SZ)" > "$LOCK/info"
UNITY_LOCK_STALE_SEC=1800 bash -c 'source tools/unity-lock.sh; if unity_lock_acquire; then echo "  ✓ 陈旧锁被接管"; else echo "  ✗ 未接管"; exit 1; fi'

echo
echo "=== 场景 3：超时应明确失败（exit 非 0），不能静默继续 ==="
# ⚠️ 这里必须用**真实存活**的 pid。用假 pid（如 999999）会被场景 4 的"孤儿锁"判据
#    立刻接管，于是根本走不到超时路径 —— 我自己踩过一次，实测修正。
mkdir -p "$LOCK"
printf 'pid=%s\npurpose=fresh-alive\nat=%s\n' "$$" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$LOCK/info"
UNITY_LOCK_TIMEOUT_SEC=3 bash -c 'source tools/unity-lock.sh; unity_lock_acquire' && { echo "  ✗ 超时却成功了"; rm -rf "$LOCK"; exit 1; } || echo "  ✓ 超时明确失败（不会被误当成判据不通过）"

echo
echo "=== 场景 4：孤儿锁（持有者进程已消失）应**立刻**接管，不等 30 分钟 ==="
mkdir -p "$LOCK"
printf 'pid=999999\npurpose=orphan\nat=%s\n' "$(date -u -d '5 minutes ago' +%Y-%m-%dT%H:%M:%SZ)" > "$LOCK/info"
OUT=$(UNITY_LOCK_STALE_SEC=1800 bash -c 'source tools/unity-lock.sh; unity_lock_acquire' 2>&1)
echo "$OUT" | sed 's/^/  /'
case "$OUT" in
  *孤儿锁*) echo "  ✓ 按 PID 判据立刻接管（未等 1800s 阈值）" ;;
  *) echo "  ✗ 未按 PID 接管"; rm -rf "$LOCK"; exit 1 ;;
esac
rm -rf "$LOCK"
echo
echo "全部场景结束"
