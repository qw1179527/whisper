#!/usr/bin/env bash
# unity-tests.sh — 在**真 Unity 进程**里跑 EditMode + PlayMode，产出结果 XML，失败即非零退出
#
# ## 为什么必须有这个脚本（而不是每次手工敲命令）
# 本工程有两套断言：
#   · `native/csharp-verify`（纯 .NET 跑手）—— 不依赖 Unity，快，但**只能证明内部一致性**；
#   · **Unity Test Framework**（本脚本）—— 真 Unity 里真编译真跑，才是"能不能进包"的判据。
# 历史事故 #18：桩替臆造背书 → 本机全绿、CI 报 `CS0103`。所以"本机桩绿"**不能**替代这一层。
# 本脚本把两条命令沉下来，避免每个小类重造，并让"失败"必然体现为**非零退出码**。
#
# ## 用法
#   bash tools/unity-tests.sh                 # 跑两个平台
#   bash tools/unity-tests.sh EditMode        # 只跑一个
#   bash tools/unity-tests.sh --inject-fail   # 注入一次必失败断言（验证"失败会判红"），跑完自动还原
#
# ## 前置
#   · Unity 编辑器路径（默认 D:\Unity\6000.3.25f1，可用 UNITY_EXE 覆盖）
#   · **纯 ASCII 的工程路径**（Unity 的 Android/测试工具链对非 ASCII 路径有硬限制）。
#     本机用 junction：D:\dsh-whisper-unity → <真源>/unity（见 docs/HANDOFF.md 第四节·五）。
#     可用 UNITY_PROJECT 覆盖。
#
# ## 为什么不需要 -quit
#   `-runTests` 结束时测试框架自己会 `ExitApplication`（日志里可见
#   `Test run completed. Exiting with code 0 (Ok)`）。加 `-quit` 反而可能在测试跑完前退出。
#   **不要加 `-nographics` 之外的图形参数**；EditMode/PlayMode 在 `-nographics` 下都能跑
#   （但 `Camera.Render()` 之类的**离屏渲染**在无图形设备下会崩，那属于取证脚本的范畴）。
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# 【2026-10-05 修】ROOT 可能落在**纯 ASCII junction**（D:\dshr → D:\DSH专用\whisper）上，
# 那种情况下 `$ROOT/../_evidence` 会解析成 `D:\_evidence`（错位），而真实证据目录是
# `D:\DSH专用\_evidence` —— 于是"没有产出结果 XML"**假红**了整整几轮，我一度以为是 Unity 的问题。
# 正解：用 `pwd -P` 解析到物理路径，保证 ROOT 与 OUT_DIR 始终同一棵树。
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
UNITY_EXE="${UNITY_EXE:-D:/Unity/6000.3.25f1/Editor/Unity.exe}"
UNITY_PROJECT="${UNITY_PROJECT:-D:/dsh-whisper-unity}"
OUT_DIR="${OUT_DIR:-$ROOT/../_evidence/unity-tests}"

# 工程锁：同一时刻只允许一个 Unity 消费者。没有它，并发的三线（构建A/B + 我的链条）
# 会互相撞 `another Unity instance is running` → rc=1 假红（2026-10-04 实测发生过两次）。
# shellcheck source=tools/unity-lock.sh
source "$(dirname "${BASH_SOURCE[0]}")/unity-lock.sh"
UNITY_LOCK_PURPOSE="unity-tests($*)"
unity_lock_acquire || exit 3
trap unity_lock_release EXIT

PLATFORMS=()
INJECT=0
for a in "$@"; do
  case "$a" in
    --inject-fail) INJECT=1 ;;
    EditMode|PlayMode) PLATFORMS+=("$a") ;;
    *) echo "未知参数：$a"; exit 2 ;;
  esac
done
[ ${#PLATFORMS[@]} -eq 0 ] && PLATFORMS=(EditMode PlayMode)

[ -x "$UNITY_EXE" ] || [ -f "$UNITY_EXE" ] || { echo "✗ 找不到 Unity：$UNITY_EXE（可用 UNITY_EXE 覆盖）"; exit 2; }
[ -d "$UNITY_PROJECT" ] || { echo "✗ 找不到工程（ASCII）路径：$UNITY_PROJECT"; echo "  提示：Unity 工具链不接受非 ASCII 工程路径，请建 junction，见 docs/HANDOFF.md"; exit 2; }
mkdir -p "$OUT_DIR"

# ── 注入失败：临时把一个 EditMode 用例的断言改反，跑完还原（验证"失败会判红"） ──
INJECT_FILE="$ROOT/unity/Assets/Scripts/Tests/EditMode/MiniJsonTests.cs"
INJECT_BAK=""
if [ "$INJECT" = "1" ]; then
  [ -f "$INJECT_FILE" ] || { echo "✗ 注入目标不存在：$INJECT_FILE"; exit 2; }
  INJECT_BAK="$(mktemp)"
  cp "$INJECT_FILE" "$INJECT_BAK"
  # 在第一个 [Test] 方法体开头插入一条必失败断言（不依赖具体用例内容）
  python - "$INJECT_FILE" <<'PY'
import io, re, sys
p = sys.argv[1]
s = io.open(p, encoding='utf-8').read()
marker = 'Assert.Fail("【注入验证】本断言是为验证门禁有牙而临时插入的");'
if marker in s:
    print('已存在注入标记，跳过'); sys.exit(0)
# 找到第一个 [Test] 后紧跟的 '{'
m = re.search(r'\[Test\][^\n]*\n(\s*)(public\s+)?void\s+\w+\s*\([^)]*\)\s*\{', s)
if not m:
    print('找不到可注入的 [Test] 方法'); sys.exit(3)
ins = m.end()
s = s[:ins] + '\n' + m.group(1) + '    ' + marker + '\n' + s[ins:]
io.open(p, 'w', encoding='utf-8').write(s)
print('已注入必失败断言 →', p)
PY
  rc=$?
  if [ $rc -ne 0 ]; then echo "✗ 注入失败（rc=$rc）"; cp "$INJECT_BAK" "$INJECT_FILE"; rm -f "$INJECT_BAK"; exit 2; fi
  echo "（注入模式：预期本轮 EditMode 判红）"
fi

cleanup() {
  if [ -n "$INJECT_BAK" ] && [ -f "$INJECT_BAK" ]; then
    cp "$INJECT_BAK" "$INJECT_FILE"; rm -f "$INJECT_BAK"
    echo "（已还原注入的源文件）"
  fi
}
trap cleanup EXIT

# ── 跑平台 ──
FAILED=0
declare -a SUMMARY
for plat in "${PLATFORMS[@]}"; do
  xml="$OUT_DIR/$plat.xml"; log="$OUT_DIR/$plat.log"
  rm -f "$xml" "$log"
  echo "── $plat：真 Unity 进程（$UNITY_EXE） ──"
  "$UNITY_EXE" -batchmode -nographics \
      -projectPath "$UNITY_PROJECT" \
      -runTests -testPlatform "$plat" \
      -testResults "$xml" -logFile "$log"
  rc=$?

  if [ ! -f "$xml" ]; then
    echo "  ✗ 没有产出结果 XML：$xml（Unity rc=$rc）"
    [ -f "$log" ] && tail -15 "$log"
    FAILED=1; SUMMARY+=("$plat: 无 XML"); continue
  fi

  # 从 XML 读值（不靠日志文本猜）
  line=$(grep -m1 '<test-run ' "$xml")
  total=$(echo "$line" | sed -n 's/.* total="\([0-9]*\)".*/\1/p')
  passed=$(echo "$line" | sed -n 's/.* passed="\([0-9]*\)".*/\1/p')
  failed=$(echo "$line" | sed -n 's/.* failed="\([0-9]*\)".*/\1/p')
  skipped=$(echo "$line" | sed -n 's/.* skipped="\([0-9]*\)".*/\1/p')
  result=$(echo "$line" | sed -n 's/.* result="\([^"]*\)".*/\1/p')
  echo "  XML: $xml"
  echo "  total=$total passed=$passed failed=$failed skipped=$skipped result=$result (unity rc=$rc)"
  SUMMARY+=("$plat: total=$total passed=$passed failed=$failed result=$result")

  if [ "$failed" != "0" ] || [ "$result" != "Passed" ]; then FAILED=1; fi
  if [ "$rc" != "0" ]; then FAILED=1; fi
  if [ "${total:-0}" = "0" ]; then echo "  ✗ 一个用例都没跑（total=0）—— 视为失败"; FAILED=1; fi
done

echo
echo "── 汇总 ──"
for s in "${SUMMARY[@]}"; do echo "  $s"; done
if [ "$FAILED" = "0" ]; then echo "✓ Unity 测试回归通过"; exit 0; fi
echo "✗ Unity 测试回归失败"; exit 1
