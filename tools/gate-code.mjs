#!/usr/bin/env node
/**
 * gate-code.mjs — 代码质量门禁（用户要求的第三类门禁）
 *
 * 覆盖：
 *   C1 无害的空白/空实现：`catch { }` 静默吞异常（本项目要求"失败必须可见"）
 *   C2 禁用 API：GameObject.Find / SendMessage / 每帧 new WaitForSeconds 等 Unity 反模式
 *   C3 悬挂标记：TODO / FIXME / XXX / HACK（要么做完，要么登记成缺口，不留悬空注释）
 *   C4 作用域卫生：模块级使用**本模块拿不到**的标识符（我连续踩三次的坑：
 *      config / cfg 用错作用域 → 装上启动即崩，而语法检查与构建门禁全过）
 *   C5 规模纪律：单函数过长（>120 行）与单文件过长（>600 行）——提示拆分，避免不可评审
 *   C6 接口纪律：Gameplay/UI/Core 不得 using 第三方 SDK 命名空间（V9 §13.1 禁令的补充面）
 *   C7 注释纪律：新增/修改的类必须有 <summary>（本项目的注释写"为什么"，不写"是什么"）
 *
 * ## 明确不覆盖
 *   · 编辑器/真机行为（需要 Unity）
 *   · 性能（需要运行时采样，属 §13.8 门禁，本机无法判定）
 *
 * 用法：node tools/gate-code.mjs [--inject-catch|--inject-banned|--inject-todo|--inject-scope|--inject-sdk|--inject-summary]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const SCRIPTS = path.join(ROOT, 'unity/Assets/Scripts');
const args = process.argv.slice(2);
const inject = (n) => args.includes(`--inject-${n}`);

const fails = [], oks = [];
const ok = (m) => { oks.push(m); console.log('  ✓ ' + m); };
const bad = (m) => { fails.push(m); console.log('  ✗ ' + m); };

function collect(dir, out = []) {
  if (!fs.existsSync(dir)) return out;
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) { if (!['obj', 'bin'].includes(e.name)) collect(p, out); }
    else if (p.endsWith('.cs')) out.push(p);
  }
  return out;
}
const files = collect(SCRIPTS).map((f) => ({ f, rel: path.relative(ROOT, f), t: fs.readFileSync(f, 'utf8') }));
let injected = null;
const inj = (name, text) => { if (inject(name)) { files.push({ f: '(inject)', rel: `(注入-${name}).cs`, t: text }); injected = name; } };
inj('catch', 'class X { void M() { try { A(); } catch { } } }');
inj('banned', 'class X { void M() { var o = GameObject.Find("a"); } }');
inj('todo', 'class X { // TODO: 以后再说\n void M() {} }');
inj('scope', 'class X { void M() { return config.network.tickRate; } }');
inj('sdk', 'using Photon.Pun;\nclass X { }');
inj('summary', 'public sealed class NoSummary { public int A; }');
if (injected) console.log(`[gate-code] 注入模式：${injected}（预期判红）`);

console.log('[gate-code] 代码质量门禁');

// ── C1 静默吞异常 ──
{
  const bads = [];
  for (const { rel, t } of files) {
    const lines = t.split('\n');
    lines.forEach((l, i) => {
      if (/catch\s*(\([^)]*\))?\s*\{\s*\}/.test(l)) bads.push(`${rel}:${i + 1} 空 catch（异常被静默吞掉）`);
    });
  }
  bads.length === 0 ? ok(`C1 无静默吞异常（${files.length} 个文件）`) : bads.slice(0, 4).forEach(bad);
}

// ── C2 禁用 API ──
{
  const banned = [
    { re: /\bGameObject\.Find\b/, why: '运行时查找，性能与可维护性都差（应用注入或引用）' },
    { re: /\bFindObjectOfType\b(?!\s*<)/, why: '同上' },
    { re: /\bSendMessage\s*\(/, why: '反射调用，编译期不可查' },
    { re: /\bResources\.Load\b(?!<)/, why: '无类型参数形式（应 Resources.Load<T>）' },
    { re: /\bApplication\.Quit\s*\(/, why: '移动端不允许自行退出' },
  ];
  const bads = [];
  for (const { rel, t } of files) {
    if (rel.includes('/Tests/')) continue;
    for (const b of banned) if (b.re.test(t)) bads.push(`${rel} 使用 ${b.re.source} —— ${b.why}`);
  }
  bads.length === 0 ? ok('C2 禁用 API：无 GameObject.Find / SendMessage / 无类型 Resources.Load / Application.Quit') : bads.slice(0, 4).forEach(bad);
}

// ── C3 悬挂标记 ──
{
  const bads = [];
  for (const { rel, t } of files) {
    const lines = t.split('\n');
    lines.forEach((l, i) => {
      const m = l.match(/\b(TODO|FIXME|XXX|HACK)\b/);
      if (m) bads.push(`${rel}:${i + 1} 悬挂标记 ${m[1]}`);
    });
  }
  bads.length === 0 ? ok('C3 无悬挂 TODO/FIXME/XXX/HACK（未完成的事应登记进 docs/mechanism-gaps.md）') : bads.slice(0, 5).forEach(bad);
}

// ── C4 作用域卫生（模块级使用本模块拿不到的标识符）──
{
  const ALIASES = ['config', 'cfg'];
  const bads = [];
  for (const { rel, t } of files) {
    if (rel.includes('/Tests/')) continue;
    const lines = t.split('\n');
    for (const name of ALIASES) {
      const useRe = new RegExp(`(?<![\\w$.-])${name}(?!\\s*:)(?![\\w$-])`);
      const declRe = new RegExp(`^\\s{0,8}(?:let|const|var|function)\\s+${name}\\b|^\\s{0,8}[\\w$.]+\\s*=\\s*${name}\\b`);
      // C# 里没有模块级作用域概念，这里检的是"文件里是否根本没有任何声明却使用了该名字"
      // 排除三类合法出现：注释、形参/实参（`(... cfg ...)`）、字段或声明行（`_cfg`、`cfg =`）。
      // 我第一版没排除形参，于是 `public Hearing(GameConfigReader cfg) => _cfg = cfg;` 被误报。
      // ⚠ 先去掉字符串字面量与注释：复核 F5 指出 GameBootstrap.cs 唯一的 config 匹配是
      // 第 36 行的字符串 `"Data/config"` —— 那是路径文本，不是标识符引用（Roslyn 语义检查
      // 也确认该文件没有 CS0103，说明根本没有未声明标识符）。
      const codeLines = lines.map((l) => l
        .replace(/\/\/[^\n]*/g, ' ')
        .replace(/"(?:[^"\\]|\\.)*"/g, '""')
        .replace(/'(?:[^'\\]|\\.)*'/g, "''"));
      const used = codeLines.some((l) => {
        if (/^\s*(\/\/|\*|\/\*)/.test(l)) return false;
        if (!useRe.test(l)) return false;
        if (/\(\s*(?:[\w<>,\[\]\?\.]+\s+)?cfg\s*[,)]/.test(l)) return false;   // 形参
        if (/(?:this\.)?cfg\s*=/.test(l)) return false;                              // 赋值/声明
        return true;
      });
      if (!used) continue;
      // 声明来源（C# 语境）：字段/局部声明、形参（含构造函数形参 `Ctor(GameConfigReader cfg, ...)`）、
      // reader 字段（`readonly GameConfigReader _cfg;` + `_cfg = cfg;`）。
      // 我前两版分别漏了构造函数形参与 `Type cfg` 形式，误报了 SanitySystem 等 4 个文件。
      const declared = lines.some((l) => declRe.test(l))
        || new RegExp(`\\(\\s*${name}\\s*[,)]`).test(t)
        || new RegExp(`(?:[A-Z][\\w<>,\\[\\]\\.]*)\\s+${name}\\b`).test(t)
        || new RegExp(`[\\w$]+\\s+${name}\\s*[,;)]`).test(t);
      if (!declared) bads.push(`${rel} 使用了 ${name} 但文件内无任何声明/形参（编译期也不会报错时才危险）`);
    }
  }
  bads.length === 0 ? ok('C4 作用域卫生：无"使用了本文件未声明标识符"的情况') : bads.slice(0, 4).forEach(bad);
}

// ── C5 规模纪律 ──
{
  const bads = [];
  for (const { rel, t } of files) {
    if (rel.includes('/Tests/')) continue;
    const lines = t.split('\n');
    if (lines.length > 600) bads.push(`${rel} 文件 ${lines.length} 行（>600，建议拆分）`);
    // 粗略函数长度：从 `{` 缩进 8 到下一个同缩进 `}` —— 只做提示级
    let fnStart = -1, fnName = '', depth = 0, indent = 0;
    lines.forEach((l, i) => {
      const m = l.match(/^(\s{4,8})(?:public|private|internal|protected|static|async|sealed|override|virtual|\s)*[\w<>,\[\]\?]+\s+(\w+)\s*\([^;]*\)\s*$/);
      if (m && fnStart < 0) { fnStart = i; fnName = m[2]; indent = m[1].length; depth = 0; return; }
      if (fnStart >= 0) {
        depth += (l.match(/\{/g) ?? []).length - (l.match(/\}/g) ?? []).length;
        const ind = l.match(/^\s*/)[0].length;
        if (depth <= 0 && /^\s*\}/.test(l) && ind === indent) {
          const len = i - fnStart;
          if (len > 120) bads.push(`${rel}:${fnStart + 1} 函数 ${fnName} 约 ${len} 行（>120）`);
          fnStart = -1;
        }
      }
    });
  }
  bads.length === 0 ? ok('C5 规模纪律：无超长函数（>120 行）或超长文件（>600 行）') : bads.slice(0, 4).forEach(bad);
}

// ── C6 接口纪律：Gameplay/UI/Core 不得 using 第三方 SDK ──
{
  const sdkRe = /^\s*using\s+(Photon|Firebase|Unity\.Services|Vivox|Google|Unity\.Netcode|System\.Net\.Sockets)\b/m;
  const bads = [];
  for (const { rel, t } of files) {
    // 【跨平台坑，实测】path.relative 在 Windows 上给的是反斜杠，而下面两条判断都按正斜杠写：
    //   rel.includes('/Tests/') 与 /\/(Net|Audio|Backend)\// 都会**静默失效** ——
    //   前者让测试文件被误扫，后者让 Net/Audio/Backend 的 §13.2 槽位豁免失效
    //   （表现为：把合法的 UDP 实现 UdpV6NetService.cs 判成"引用第三方 SDK"→ 链在第 3 步断掉）。
    //   归一化成正斜杠后再判断，两个平台行为一致。
    const relPosix = rel.replace(/\\/g, '/');
    if (relPosix.includes('/Tests/')) continue;
    const isImplSlot = /\/(Net|Audio|Backend)\//.test(relPosix);   // §13.2：这三个是 SDK 的唯一槽位
    if (isImplSlot) continue;
    if (sdkRe.test(t)) bads.push(`${rel} 引用了第三方 SDK 命名空间（只允许在 Net/Audio/Backend 槽位内）`);
  }
  bads.length === 0 ? ok('C6 接口纪律：Gameplay/UI/Core 无第三方 SDK using（唯一槽位=Net/Audio/Backend）') : bads.slice(0, 4).forEach(bad);
}

// ── C7 注释纪律：每个 public 类型必须有文档注释 ──
{
  // 判据：该类型声明**上方最近的一段 `///` 注释**里，出现 <summary> 或 auto-generated 即算有文档。
  // 我前三版分别踩了：固定回看 6 行（长文档漏判）、遇空行即停（被文件头注释挡住）、
  // 遇非注释即停（被上一段字段注释挡住）—— 都属于"把简单判据复杂化"。
  // 现在只做一件事：跳过紧邻的特性行与 `///` 行，向上收集，遇到真正的代码行就停。
  const bads = [];
  for (const { rel, t } of files) {
    if (rel.includes('/Tests/')) continue;
    const lines = t.split('\n');
    lines.forEach((l, i) => {
      if (!/^\s*(public|internal)\s+(sealed\s+|static\s+|abstract\s+|readonly\s+)?(class|struct|enum|interface)\s+\w+/.test(l)) return;
      let j = i - 1, doc = false, scanned = 0;
      while (j >= 0 && scanned < 80) {   // 生成文件的文档头在文件最开始，40 步不够（实测漏判 DesignTokens）
        const l2 = lines[j];
        if (/^\s*\[[^\]]+\]\s*$/.test(l2)) { j--; scanned++; continue; }              // 特性
        // 跳过命名空间/类的开括号行：生成文件的 <auto-generated> 标记在文件头，
        // 与类型声明之间隔着 namespace 的 `{`，遇到它就停会看不到标记（复核 F5 指出的误报）
        // 跳过空行 / namespace 行 / using 行：
        // 生成文件（DesignTokens.cs）的文档头在**文件最开头**，与类型声明之间隔着
        // `namespace X {` 与空行；遇到这些就停会看不到 <auto-generated>（复核 F5 的误报）。
        if (/^\s*$/.test(l2)
            || /^\s*(namespace\s+[\w.]+\s*\{?|\{)\s*$/.test(l2)
            || /^\s*using\s/.test(l2)) { j--; scanned++; continue; }
        if (/^\s*\/\//.test(l2)) {                                                      // 文档行
          if (/<summary>|auto-generated/.test(l2)) { doc = true; break; }
          j--; scanned++; continue;
        }
        break;                                                                           // 真正的代码行
      }
      if (!doc) bads.push(`${rel}:${i + 1} 类型缺文档注释（<summary> 或 auto-generated）`);
    });
  }
  bads.length === 0 ? ok('C7 注释纪律：所有 public 类型都有文档注释') : bads.slice(0, 5).forEach(bad);
}

console.log(`\n[gate-code] 结果：通过 ${oks.length} · 失败 ${fails.length}${fails.length ? ' ✗' : ' ✓'}`);
if (injected && fails.length === 0) {
  console.log(`[gate-code] ✗ 注入 ${injected} 后仍未判红 —— 该门禁不可信`);
  process.exit(1);
}
process.exit(fails.length ? 1 : 0);
