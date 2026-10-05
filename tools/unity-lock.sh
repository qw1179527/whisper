#!/usr/bin/env bash
# unity-lock.sh — **同一时刻只允许一个 Unity 消费者**的跨进程锁（供各脚本 source）
#
# ## 为什么必须有（这不是洁癖，是已经发生过的故障）
# 本机所有 Unity 相关脚本都指向**同一个工程**（`UNITY_PROJECT=D:/dsh-whisper-unity`），
# 而 Unity 对同一工程**同一时刻只允许一个实例**。于是并发跑就会：
#   · 后启动的那个报 `Aborting batchmode due to fatal error:
#     It looks like another Unity instance is running with this project open.` → **rc=1**
#   · 对"测试/取证"脚本而言这就是**假红**：代码没问题，只是撞锁
#   · 更隐蔽的反向风险：在别人刚改完源码、Unity 还没重编译完时跑，
#     可能拿到"半编译状态"的**假绿**
#
# 实证（2026-10-04 构建三线并行期间）：
#   `_evidence/unity-tests/EditMode.log`(17:12:27) 仅 3KB、以 `return code 1` 结束、**无结果 XML**
#   `_evidence/render-v2/run2-console.log`(17:13:30) 明确报 another Unity instance
# → 假红真实发生过两次。加锁后这类失败不再出现。
#
# ## 用法
#   source "$(dirname "$0")/unity-lock.sh"
#   unity_lock_acquire || exit 3        # 拿不到锁就明确失败（exit 3 = 环境忙，不是判据不通过）
#   trap unity_lock_release EXIT
#
# ## 实现说明
#   用**目录**当锁（`mkdir` 在 Windows 与 POSIX 上都是原子的"不存在才创建"），
#   锁里写 PID 与用途便于排查；超过 STALE_SEC 秒的锁视为**陈旧**（上次崩溃没清理）自动接管。
set -uo pipefail

UNITY_LOCK_DIR="${UNITY_LOCK_DIR:-${TMPDIR:-/tmp}/whisper-unity.lock}"
UNITY_LOCK_TIMEOUT_SEC="${UNITY_LOCK_TIMEOUT_SEC:-420}"   # 等锁上限（一次 Unity 跑约 20~90 秒）
UNITY_LOCK_STALE_SEC="${UNITY_LOCK_STALE_SEC:-1800}"      # 超过 30 分钟的锁视为陈旧

# 兼容 Git-Bash：把 /tmp 之类转成 Unix 风格即可，mkdir 两个平台都认
unity_lock_acquire() {
  local waited=0
  while true; do
    if mkdir "$UNITY_LOCK_DIR" 2>/dev/null; then
      printf 'pid=%s\npurpose=%s\nat=%s\n' "$$" "${UNITY_LOCK_PURPOSE:-unknown}" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$UNITY_LOCK_DIR/info" 2>/dev/null || true
      echo "  [unity-lock] 已持有工程锁（${UNITY_LOCK_PURPOSE:-unknown} · pid $$）"
      return 0
    fi

    # 陈旧锁接管有两种情形（都必须处理，否则锁会假性占住）：
    #   ① **孤儿锁**：持有者进程已消失（最常见于被 kill / Ctrl-C —— bash 的 EXIT trap
    #      在 SIGKILL 下不会执行，锁就留在盘上）。判据：PID 不再存活 → 立刻接管，不必等 30 分钟。
    #      这一条是实测补的：我 kill 了一条正在跑锁的链，随后锁目录仍在、而 Unity 进程为 0。
    #   ② **超时锁**：进程还活着但卡住 → 用 STALE_SEC 兜底。
    if [ -f "$UNITY_LOCK_DIR/info" ]; then
      local pid at age now ats
      pid=$(sed -n 's/^pid=//p' "$UNITY_LOCK_DIR/info" 2>/dev/null | head -1)
      if [ -n "$pid" ] && ! kill -0 "$pid" 2>/dev/null; then
        echo "  [unity-lock] 接管孤儿锁（持有者 pid ${pid} 已不存在）"
        rm -rf "$UNITY_LOCK_DIR"
        continue
      fi
      at=$(sed -n 's/^at=//p' "$UNITY_LOCK_DIR/info" 2>/dev/null | head -1)
      if [ -n "$at" ]; then
        now=$(date -u +%s)
        ats=$(date -u -d "$at" +%s 2>/dev/null || echo 0)
        age=$(( now - ats ))
        if [ "$ats" -gt 0 ] && [ "$age" -gt "$UNITY_LOCK_STALE_SEC" ]; then
          echo "  [unity-lock] 接管陈旧锁（已 ${age}s，超 ${UNITY_LOCK_STALE_SEC}s）"
          rm -rf "$UNITY_LOCK_DIR"
          continue
        fi
      fi
    else
      # 锁目录在、但 info 还没写出来（刚创建的瞬间）——给它一个宽限，避免误抢
      sleep 1
    fi

    if [ "$waited" -ge "$UNITY_LOCK_TIMEOUT_SEC" ]; then
      echo "  [unity-lock] ✗ 等锁超时（${UNITY_LOCK_TIMEOUT_SEC}s）—— 别的 Unity 消费者仍在跑"
      [ -f "$UNITY_LOCK_DIR/info" ] && sed 's/^/      /' "$UNITY_LOCK_DIR/info"
      return 1
    fi
    [ "$waited" -eq 0 ] && echo "  [unity-lock] 工程锁被占用，等待中（上限 ${UNITY_LOCK_TIMEOUT_SEC}s）…"
    sleep 5
    waited=$(( waited + 5 ))
  done
}

unity_lock_release() {
  if [ -d "$UNITY_LOCK_DIR" ]; then
    rm -rf "$UNITY_LOCK_DIR"
    echo "  [unity-lock] 已释放工程锁"
  fi
}
