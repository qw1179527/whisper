#!/data/user/0/app.dsh.mobile/files/engine/bin/bash
# unity-syntax-check.sh — 对**引用 UnityEngine 的源文件**做语法 + 语义检查
#
# 为什么需要：本机没有 Unity，这些文件无法真正编译；但"不能编译"不等于"不能检查"。
# 做法：用 native/unity-syntax（Roslyn）编译「目标文件 + 全部非 Unity 源 + 最小 Unity 桩」，
# 任何与 Unity 缺失无关的错误都必须失败。
#
# 这道门禁已被两次真实错误证明有效：
#   ① LevelBuilder 引用不存在的 DesignTokens.ColorConcrete（类型名笔误）
#   ② LevelBuilder 漏了 using Whisper.Core（DesignTokens 解析不到，几何配色会全部落到兜底色）
# 反例也踩过：早期版本只给 System.Private.CoreLib 一个引用、且没给 Unity 桩，
# 于是满屏"类型缺失"把笔误淹没，检查器变成假绿——现在允许错误数必须为 0。
set -uo pipefail
cd "$(dirname "$0")/.."

STUBS="native/unity-stubs/UnityStubs.cs"
# 扫描范围：Assets/Scripts（业务）**+ Assets/Editor（构建入口）**。
# 为什么必须含 Editor：此前只扫 Scripts，`unity/Assets/Editor/` 成了本机盲区——
# BuildScript / EditorSceneBootstrap / BuildConfigurator 里的编译错误只能等 CI
# 构建（~47 分钟）才暴露。真机首包事故后补上这一半。
# **不含测试**：测试依赖 NUnit，另有 Unity Test Framework 覆盖。
SCAN_DIRS="unity/Assets/Scripts unity/Assets/Editor"
# 目标 = 生产代码中引用 UnityEngine / UnityEditor 的文件
TARGETS=$(rg -l "using Unity(Engine|Editor)" $SCAN_DIRS --glob '*.cs' | rg -v "/Tests/" | tr '\n' ' ')
if [ -z "$TARGETS" ]; then echo "[unity-syntax] 未找到引用 UnityEngine 的生产源文件（异常）"; exit 1; fi
# 上下文 = 全部非 Unity 源（含 Gameplay/Level 等），但排除目标自身避免重复定义
REFS=""
for f in $(find $SCAN_DIRS -name '*.cs' -not -path '*/Tests/*' | sort); do
  case " $TARGETS " in *" $f "*) continue;; esac
  REFS="$REFS $f"
done

echo "[unity-syntax] 目标 $(echo $TARGETS | wc -w) 个文件 · 上下文 $(echo $REFS | wc -w) 个源 + Unity 桩"
exec "$(dirname "$0")/../native/dotnet.sh" run --project native/unity-syntax --nologo -c Release -- \
  $TARGETS "$STUBS" --refs $REFS
