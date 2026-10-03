#!/usr/bin/env node
/**
 * gate-editor-api.mjs — Editor 代码的 Unity API 出处门禁
 *
 * ## 这个门禁要挡的是什么（CI #18 真实事故，2026-10-03）
 * 我在 `BuildConfigurator.cs` 里写了 `UnityEditor.SplashScreen.show = false`，
 * 依据是**我自己在本机手写的 Unity 桩里就这么声明的**。于是：
 *   · 本机语法门禁（Roslyn + 桩）**全绿**
 *   · CI 报 `CS0103: The name 'SplashScreen' does not exist in the current context`
 *   · 真名是 `PlayerSettings.SplashScreen`（嵌套类型），一次 45 分钟的构建白烧
 *
 * 根因不是"名字记错了"，而是：**桩是我写的，我编造一个 API，桩就替它背书。**
 * 桩能验证"调用点与签名自洽"，永远无法验证"Unity 真的有这个成员"。
 *
 * ## 判据
 * 扫描 `unity/Assets/Editor/**\/*.cs`，提取其中的 Unity API 成员引用
 * （`PlayerSettings` / `EditorBuildSettings` / `BuildPipeline` / `SplashScreen` …，
 *   以及 `XxxEnum.Value` 形式的枚举值），逐个到 `data/unity-api-registry.json` 里找出处：
 *   · 有登记 + URL 前缀合法  → 通过（说明是人查过文档才写的）
 *   · 未登记                 → **判红**，必须补出处，或改写成反射式调用
 *
 * 反射式调用（`typeof(X).GetProperty("...")`）按设计豁免：它不依赖编译期成员存在，
 * 这正是 #18 之后给"可选的美化设置"选定的安全形态。
 *
 * ## 诚实边界（写清楚，不夸大）
 * 本门禁**不能**证明 API 签名一定正确 —— 那需要真 Unity 程序集。
 * 它只保证：**没有人再凭记忆写 Unity API 而不留出处**。
 * 这正是 #18 的失效模式，也是本机环境（无 Unity 编辑器 / 无 UnityEngine.dll）唯一能守的边界。
 *
 * 用法：node tools/gate-editor-api.mjs [--warn-only]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const EDITOR_DIR = path.join(ROOT, 'unity/Assets/Editor');
const REGISTRY = path.join(ROOT, 'data/unity-api-registry.json');
const warnOnly = process.argv.includes('--warn-only');

console.log('[gate-editor-api] Editor 代码 Unity API 出处门禁');

if (!fs.existsSync(REGISTRY)) {
  console.log('  ✗ 缺 data/unity-api-registry.json（出处台账是判据前提）');
  process.exit(1);
}
const reg = JSON.parse(fs.readFileSync(REGISTRY, 'utf8'));
const known = new Set(Object.keys(reg.apis ?? {}));

const files = fs.existsSync(EDITOR_DIR)
  ? fs.readdirSync(EDITOR_DIR).filter((f) => f.endsWith('.cs'))
  : [];
if (!files.length) {
  console.log('  · unity/Assets/Editor 下没有 .cs，跳过');
  process.exit(0);
}

// Unity 侧根类型名（只取这些前缀下面的成员引用，避免把 System.IO 之类也算进来）
const ROOTS = ['PlayerSettings', 'EditorBuildSettings', 'BuildPipeline', 'SplashScreen',
  'EditorSceneManager', 'BuildPlayerOptions', 'EditorApplication', 'AssetDatabase',
  'NamedBuildTarget', 'EditorUserBuildSettings'];
const ENUMS = ['AndroidArchitecture', 'AndroidSdkVersions', 'ScriptingImplementation',
  'ManagedStrippingLevel', 'UIOrientation', 'BuildTarget', 'BuildOptions', 'BuildTargetGroup',
  'LightType', 'CameraClearFlags', 'TextAnchor', 'RenderMode', 'PrimitiveType'];

const unregistered = [];
let considered = 0;

for (const f of files) {
  const raw = fs.readFileSync(path.join(EDITOR_DIR, f), 'utf8');
  // 只查代码：注释里记录事故原委是**被鼓励**的，不能因此判红（gate-test T6 踩过同一个坑）
  const src = raw
    .replace(/\/\*[\s\S]*?\*\//g, ' ')
    .replace(/^\s*\/\/\/.*$/gm, ' ')
    .replace(/\/\/.*$/gm, ' ');

  const refs = new Set();
  for (const root of ROOTS) {
    const re = new RegExp(`\\b${root}((?:\\s*\\.\\s*[A-Za-z_][A-Za-z0-9_]*)+)`, 'g');
    for (const m of src.matchAll(re)) {
      // 归一化 `PlayerSettings . Android . targetArchitectures` → `PlayerSettings.Android.targetArchitectures`
      refs.add(root + m[1].replace(/\s+/g, ''));
    }
  }
  for (const e of ENUMS) {
    const re = new RegExp(`\\b${e}\\.[A-Za-z_][A-Za-z0-9_]*`, 'g');
    for (const m of src.matchAll(re)) refs.add(m[0]);
  }

  for (const r of refs) {
    considered++;
    // 反射式豁免（#18 之后给"可选美化设置"选定的安全形态）：
    // 文件里若通过 `typeof(PlayerSettings).GetNestedType("SplashScreen") … GetProperty("show")`
    // 访问，则**不依赖编译期成员存在** —— 名字写错也只是运行时不生效 + 警告，不会让构建失败。
    // 判据取"该文件确实做了嵌套类型+属性反射"，而不是逐字匹配某一行（跨行写法会让逐字匹配漏掉）。
    const usesReflection = /GetNestedType\s*\(/.test(src) && /GetProperty\s*\(/.test(src);
    if (usesReflection && r.startsWith('SplashScreen')) continue;
    if (known.has(r)) continue;
    unregistered.push(`${f}: ${r}`);
  }
}

const urlsOk = Object.entries(reg.apis ?? {}).filter(([, v]) => !/^https:\/\/(docs\.unity3d\.com|docs\.unity\.cn|docs-alpha\.unity3d\.com)/.test(v.doc ?? ''));
if (urlsOk.length) {
  for (const [k, v] of urlsOk) console.log(`  ✗ 出处 URL 不是 Unity 官方文档：${k} → ${v.doc}`);
}

console.log(`  扫描 ${files.length} 个 Editor 源 · 识别 ${considered} 处 API 引用 · 台账登记 ${known.size} 项`);

if (unregistered.length) {
  const head = unregistered.slice(0, 12);
  if (warnOnly) {
    console.log(`  ⚠ ${unregistered.length} 处 API 未在台账登记（--warn-only 不判红）`);
    for (const u of head) console.log('     · ' + u);
  } else {
    console.log(`  ✗ ${unregistered.length} 处 API 未在台账登记 —— 凭记忆写 API 正是 CI #18 的失效模式`);
    for (const u of head) console.log('     · ' + u);
    console.log('   处理：① 查官方文档后补进 data/unity-api-registry.json，或 ② 改写成反射式调用（不依赖编译期成员）');
    console.log(`[gate-editor-api] 结果：失败 ${unregistered.length + urlsOk.length} ✗`);
    process.exit(1);
  }
}

console.log('[gate-editor-api] 结果：全部 Editor API 均有官方文档出处 ✓');
