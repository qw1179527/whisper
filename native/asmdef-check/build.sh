#!/data/user/0/app.dsh.mobile/files/engine/bin/bash
# asmdef-check/build.sh — 按 Unity 的 ASMDEF 粒度逐程序集编译检查
#
# 为什么需要它（真实事故）：
#   本机断言跑手（native/csharp-verify）把 Core/Gameplay/Net/Audio/Backend **编进同一个程序集**，
#   而 Unity 是**每个 asmdef 独立编译**、只允许 through asmdef 声明的引用。
#   结果：CI 构建在 `Whisper.Net` 上报 CS0246（找不到 PropState），
#         而本机跑手 0 错误 —— 这类"跨程序集不可见"的缺陷从来测不到。
#
# 做法：为每个 asmdef 生成一个 classlib 项目，按 asmdef 的 references 建 ProjectReference，
#       排除引用 UnityEngine 的文件（MonoBehaviour / UI / Editor），逐个 dotnet build。
#       任一程序集编译失败即非零退出。
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WORK="$ROOT/native/asmdef-check/work"
DOTNET="$ROOT/native/dotnet.sh"
rm -rf "$WORK"; mkdir -p "$WORK"

# 1) 生成 Directory.Build.props（统一目标框架，避免每个项目重复）
cat > "$WORK/Directory.Build.props" <<'PROPS'
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>disable</Nullable>
    <LangVersion>latest</LangVersion>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <NoWarn>CS1591;CS0169;CS0649;CS0414</NoWarn>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
  </PropertyGroup>
</Project>
PROPS

# 2) 解析每个 asmdef → 生成 csproj
node "$ROOT/native/asmdef-check/gen.mjs" "$ROOT" "$WORK"

# 3) 逐个编译
FAIL=0
for proj in "$WORK"/*/*.csproj; do
  name="$(basename "$(dirname "$proj")")"
  out=$(cd "$(dirname "$proj")" && "$DOTNET" build --nologo -v q 2>&1) || true
  errs=$(echo "$out" | grep -c "error CS" || true)
  if [ "$errs" -gt 0 ]; then
    FAIL=$((FAIL+1))
    echo "  ✗ $name：$errs 个编译错误"
    echo "$out" | grep "error CS" | head -5 | sed 's/^/      /'
  else
    echo "  ✓ $name"
  fi
done
echo
if [ "$FAIL" -gt 0 ]; then echo "  ✗ 有 $FAIL 个程序集编译失败（Unity 侧同样会失败）"; exit 1; fi
echo "  ✓ 全部程序集按 ASMDEF 粒度编译通过"
