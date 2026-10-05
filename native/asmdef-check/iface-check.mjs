#!/usr/bin/env node
/**
 * iface-check.mjs — 接口实现一致性静态检查（补"本机不编译 Tests/"的盲区）
 *
 * 为什么需要（真实事故）：
 *   CI 构建报 CS0535「FakeNet 未实现 INetService.Phase / Snapshot / OnPhaseChanged…」——
 *   我在给 INetService 加"四类同步对象"时，忘了同步更新测试桩 FakeNet。
 *   而本机断言跑手只编译 Core/Gameplay/Net/Audio/Backend，**不编译 Tests/**，
 *   于是这个缺陷在本机永远测不出来（Unity 侧才炸）。
 *
 * 做法：扫描所有 `class X : ...INetService/IVoiceService/IBackendService` 的实现类，
 *   逐个比对接口声明的成员是否齐全（属性 / 方法 / 事件，按名字匹配）。
 *   不做完整语义分析，只保证"成员名字一个不少"——这正好覆盖 CS0535 那类缺陷。
 */
import fs from 'node:fs';
import path from 'node:path';

const ROOT = process.argv[2];
const SCRIPTS = path.join(ROOT, 'unity/Assets/Scripts');

function collect(dir, out = []) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) { if (!['obj', 'bin'].includes(e.name)) collect(p, out); }
    else if (p.endsWith('.cs')) out.push(p);
  }
  return out;
}

const files = collect(SCRIPTS).map((f) => ({ f, rel: path.relative(ROOT, f), t: fs.readFileSync(f, 'utf8') }));

/** 从接口文件里取成员名 */
function ifaceMembers(shortName) {
  const src = files.find((x) => new RegExp(`interface\\s+${shortName}\\b`).test(x.t));
  if (!src) return null;
  // 取接口体
  const body = src.t.slice(src.t.search(new RegExp(`interface\\s+${shortName}\\b`)));
  const members = new Set();
  // 属性/事件：以分号结束的声明
  for (const m of body.matchAll(/(?:event\s+)?[A-Za-z_][\w<>,\[\]\.\? ]*\s+(\w+)\s*\{\s*get/g)) members.add(m[1]);
  for (const m of body.matchAll(/event\s+[A-Za-z_][\w<>,\[\]\.\? ]*\s+(\w+)\s*;/g)) members.add(m[1]);
  // 方法
  for (const m of body.matchAll(/\)\s*;\s*$/gm)) {
    const decl = body.slice(0, m.index + 1).split(/[;\n]/).pop() ?? '';
    const name = decl.match(/(\w+)\s*\(/);
    if (name) members.add(name[1]);
  }
  return members;
}

const IFACES = ['INetService', 'IVoiceService', 'IBackendService'];
let problems = 0;

for (const iface of IFACES) {
  const members = ifaceMembers(iface);
  if (!members || members.size === 0) { console.log(`  ⚠ ${iface}: 未解析到成员，跳过`); continue; }
  // 找实现类：`class X : ... IFace`
  for (const { rel, t } of files) {
    for (const m of t.matchAll(/class\s+(\w+)[^{]*?:\s*([^{]+)\{/g)) {
      const impl = m[2];
      if (!new RegExp(`\\b${iface}\\b`).test(impl)) continue;
      const body = t.slice(m.index);
      const missing = [...members].filter((name) => !new RegExp(`\\b${name}\\b`).test(body));
      if (missing.length) {
        problems++;
        console.log(`  ✗ ${rel}: ${m[1]} 实现 ${iface} 但缺少成员：${missing.join(', ')}`);
      } else {
        console.log(`  ✓ ${rel}: ${m[1]} 完整实现 ${iface}（${members.size} 个成员）`);
      }
    }
  }
}

if (problems) { console.log(`  ✗ ${problems} 处接口实现不完整（Unity 侧会报 CS0535）`); process.exit(1); }
console.log('  ✓ 接口实现一致性检查通过');
