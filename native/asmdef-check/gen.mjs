#!/usr/bin/env node
/**
 * gen.mjs — 解析 unity 工程的 .asmdef，为每个程序集生成一个 classlib 项目
 *
 * 关键规则（按 Unity 的 ASMDEF 语义实现）：
 *   · 只编译 .asmdef 所在目录及其子目录下的 .cs（Unity 的 asmdef 作用域规则）
 *   · references 声明 -> ProjectReference（模拟 Unity 的程序集引用）
 *   · 排除引用 UnityEngine 的文件（MonoBehaviour/UI/Editor），这些本机编不了
 */
import fs from 'node:fs';
import path from 'node:path';

const [, , root, work] = process.argv;
const scriptsRoot = path.join(root, 'unity/Assets/Scripts');

/** 递归收集 .cs（相对路径） */
function collectCs(dir, out = []) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) { if (!['obj', 'bin'].includes(e.name)) collectCs(p, out); }
    else if (p.endsWith('.cs')) out.push(p);
  }
  return out;
}

/** 找所有 asmdef */
function findAsmdefs(dir, out = []) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) findAsmdefs(p, out);
    else if (p.endsWith('.asmdef')) out.push(p);
  }
  return out;
}

const asmdefs = findAsmdefs(scriptsRoot);
const byDir = new Map();          // asmdef 目录 -> 定义
for (const f of asmdefs) {
  const def = JSON.parse(fs.readFileSync(f, 'utf8'));
  byDir.set(path.dirname(f), { file: f, def, name: def.name });
}

/** 判断某文件是否引用 UnityEngine（这类本机无法编译） */
function usesUnity(file) {
  const t = fs.readFileSync(file, 'utf8');
  return /^\s*using\s+Unity(Engine|Editor)/m.test(t) || /:\s*MonoBehaviour/.test(t);
}

let made = 0;
for (const [dir, info] of byDir) {
  if (info.name.includes('Tests')) continue;                 // 测试程序集本机不编（需要 Unity Test Framework）
  // 该 asmdef 作用域内的 .cs：只取"同目录或更深、且没有被更内层 asmdef 覆盖"的文件
  const nested = [...byDir.keys()].filter((d) => d !== dir && d.startsWith(dir + path.sep));
  const all = collectCs(dir).filter((f) => !nested.some((n) => f.startsWith(n + path.sep)));
  const srcs = all.filter((f) => !usesUnity(f));
  const skipped = all.length - srcs.length;
  if (srcs.length === 0) { console.log(`  · ${info.name}: 全部文件依赖 Unity，跳过（${all.length} 个）`); continue; }

  const projDir = path.join(work, info.name);
  fs.mkdirSync(projDir, { recursive: true });

  // references -> ProjectReference（相对路径）
  const refs = (info.def.references ?? [])
    .map((r) => (typeof r === 'string' ? r : r.name))
    .filter((n) => byDir.size && [...byDir.values()].some((v) => v.name === n))
    .map((n) => `    <ProjectReference Include="${path.relative(projDir, path.join(work, n)).replace(/\\/g, '/')}/${n}.csproj" />`)
    .join('\n');

  const items = srcs.map((f) => `    <Compile Include="${path.relative(projDir, f).replace(/\\/g, '/')}" />`).join('\n');

  fs.writeFileSync(path.join(projDir, `${info.name}.csproj`), `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>${info.name}</AssemblyName>
    <RootNamespace>${info.def.rootNamespace ?? info.name}</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
${items}
  </ItemGroup>
${refs ? `  <ItemGroup>\n${refs}\n  </ItemGroup>` : ''}
</Project>
`, 'utf8');
  made++;
  if (skipped) console.log(`  · ${info.name}: ${srcs.length} 个源文件（跳过 ${skipped} 个依赖 Unity 的）`);
}
console.log(`  已生成 ${made} 个程序集项目`);
