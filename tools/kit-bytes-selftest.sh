#!/usr/bin/env bash
# kit-bytes-selftest.sh — 在**真 Unity** 里自检"套件的产品读取路径"
#
# ## 为什么单独有这一步（R6 只读审计指出的最大剩余风险）
# 产品读套件字节走的是**两条路**，优先级是 Resources 优先：
#   ① `Resources.Load<TextAsset>("Kits/<id>.glb")` —— 磁盘上其实是 `<id>.glb.bytes`
#      （扩展名故意不是 `.glb`：`.glb` 会被 ModelImporter 当模型导入，取不到）
#   ② `StreamingAssets/Kits/<id>.glb` —— 兜底
# 而**所有自动门禁读的都是磁盘字节**（`native/csharp-verify`、`verify-packed-kits.mjs`），
# 结构上够不到 ① 这条**运行时**路径。唯一覆盖它的是 `KitByteAssetsSelfTest`（真 Unity 里跑），
# 审计当时它**既不在链条也不在 CI** —— 于是"产品真的能读到套件"这件事没有自动判据。
# 本脚本把它接进链条第 23 步。
#
# ## 判据
#   Unity 退出码（自检内部：5/5 通过 → `EditorApplication.Exit(0)`；否则 `Exit(1)`）
#   **并且**要求日志里出现 `KIT_BYTES_SELFTEST OK` —— 只看退出码不够：
#   Unity 有"没跑到测试就退出 0"的先例（脚本路径写错、方法名拼错都会静默成功）。
#
# 用法：bash tools/kit-bytes-selftest.sh
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY_EXE="${UNITY_EXE:-D:/Unity/6000.3.25f1/Editor/Unity.exe}"
UNITY_PROJECT="${UNITY_PROJECT:-D:/dsh-whisper-unity}"
OUT_DIR="${OUT_DIR:-$ROOT/../_evidence/unity-tests}"

[ -f "$UNITY_EXE" ] || { echo "✗ 找不到 Unity：$UNITY_EXE（可用 UNITY_EXE 覆盖）"; exit 2; }
[ -d "$UNITY_PROJECT" ] || { echo "✗ 找不到 ASCII 工程：$UNITY_PROJECT（见 docs/HANDOFF.md 第四节·五）"; exit 2; }
mkdir -p "$OUT_DIR"

# 工程锁：与 unity-tests.sh 共用同一把（同一时刻只允许一个 Unity 消费者）。
# 没有它，并发的构建/取证线会互相撞 `another Unity instance is running` → rc=1 假红。
# shellcheck source=tools/unity-lock.sh
source "$(dirname "${BASH_SOURCE[0]}")/unity-lock.sh"
UNITY_LOCK_PURPOSE="kit-bytes-selftest"
unity_lock_acquire || exit 3
trap unity_lock_release EXIT

log="$OUT_DIR/kit-bytes-selftest.log"
rm -f "$log"

"$UNITY_EXE" -quit -batchmode \
    -projectPath "$UNITY_PROJECT" \
    -executeMethod Whisper.Editor.KitByteAssetsSelfTest.Run \
    -logFile "$log"
rc=$?

result=$(grep -c 'KIT_BYTES_SELFTEST OK' "$log" 2>/dev/null || echo 0)
echo "  Unity rc=$rc · 日志：$log"
grep -E '\[KIT_BYTES\]' "$log" 2>/dev/null | sed 's/^/  /' || true

if [ "$rc" != "0" ]; then
  echo "  ✗ 套件产品读取路径自检失败（Unity rc=$rc）"
  grep -E '\[KIT_BYTES\]|error CS|Exception' "$log" 2>/dev/null | tail -10 | sed 's/^/  /' || true
  exit 1
fi
if [ "$result" -lt 1 ]; then
  echo "  ✗ Unity 退出码为 0，但日志里没有 KIT_BYTES_SELFTEST OK ——"
  echo "     很可能是自检根本没跑到（executeMethod 名字/脚本路径错误会静默成功）"
  tail -15 "$log" | sed 's/^/  /'
  exit 1
fi
# 【2026-10-04 修】原来这里**写死**打印 "5/5"，而套件数已随"按房型出套件"增长到 8。
# 症状很隐蔽：自检本身早就改成"从清单读 id"并如实报了 `8/8`，**只有这句摘要还在说 5/5** ——
# 看链条的人会以为自检只覆盖了 5 个（这正是本项目最怕的"汇报与事实不符"）。
# 现在从日志里**抽出真实条数**再打印，摘要永远等于自检的实际结果。
kitcount=$(grep -oE 'KIT_BYTES_SELFTEST OK：[0-9]+/[0-9]+' "$log" 2>/dev/null | tail -1 | grep -oE '[0-9]+/[0-9]+$')
echo "  ✓ 套件产品读取路径自检通过（Resources 文本资产 ${kitcount:-?} 可加载并解析）"
