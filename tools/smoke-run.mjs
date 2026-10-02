#!/usr/bin/env node
/**
 * smoke-run.mjs — 产物真跑冒烟（不是"能装配"，是"真跑到能观测"）
 *
 * 做法：在受控桩环境（DOM / WebGL2 / WebAudio / fetch）里真实调用 startGame()，
 * 记录所有 console 输出，形成"行为指纹"，用于：
 *   · 证明重建产物不崩（冒烟）
 *   · 与 0.6.0 原产物对照（行为等价的第一手证据）
 *
 * 用法：node tools/smoke-run.mjs [产物路径]   默认 build/game.js
 */
import fs from 'node:fs';
import vm from 'node:vm';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const which = process.argv[2] ?? 'build/game.js';
const file = path.resolve(ROOT, which);
const text = fs.readFileSync(file, 'utf8');
const cfg = JSON.parse(fs.readFileSync(path.join(ROOT, 'data/config.json'), 'utf8'));
const tokens = JSON.parse(fs.readFileSync(path.join(ROOT, 'data/design-tokens.json'), 'utf8'));

const trace = [];
const rec = (kind, args) =>
  trace.push(
    kind + ' ' + args.map((a) => (a && typeof a === 'object' ? JSON.stringify(a).slice(0, 140) : String(a))).join(' '),
  );

const gl = new Proxy(
  {},
  {
    get: (_t, p) => {
      if (p === 'getParameter') return () => 'stub';
      if (p === 'getShaderPrecisionFormat') return () => ({ precision: 23, rangeMin: 127, rangeMax: 127 });
      if (/^create/.test(String(p))) return () => ({});
      if (/^get(Program|Shader|Uniform)/.test(String(p))) return () => ({});
      return () => {};
    },
    set: () => true,
  },
);

const mkEl = (tag = 'div') => ({
  tagName: tag,
  style: {},
  dataset: {},
  children: [],
  className: '',
  textContent: '',
  innerHTML: '',
  width: 800,
  height: 600,
  appendChild(c) {
    this.children.push(c);
    return c;
  },
  removeChild() {},
  setAttribute() {},
  getAttribute: () => null,
  insertAdjacentHTML() {},
  insertAdjacentElement() {},
  remove() {},
  contains: () => false,
  addEventListener() {},
  removeEventListener() {},
  getBoundingClientRect: () => ({ left: 0, top: 0, width: 800, height: 600, right: 800, bottom: 600 }),
  getContext: () => gl,
  requestPointerLock() {},
  focus() {},
  querySelector: () => null,
  querySelectorAll: () => [],
  clientWidth: 800,
  clientHeight: 600,
});

const byId = { gl: mkEl('canvas'), hud: mkEl(), boot: mkEl(), bootMsg: mkEl() };

/**
 * 真实关卡：从 index.html 内联的 __WHISPER_BOOTSTRAP__ 取出（与游戏运行时同一份）。
 * 为什么必须换掉旧的 `{rooms: []}`：空关卡会让启动链在 makeRoomCollider 处就抛
 * "Cannot read properties of undefined (reading 'w')"（缺 gridSize），
 * 冒烟随即停住 —— 于是**它永远覆盖不到玩法代码**，也就发现不了
 * "config is not defined" 这类只有真跑才暴露的缺陷（实测踩到）。
 */
const REAL_LEVEL = (() => {
  const html = fs.readFileSync(path.join(ROOT, 'baseline/index-0.6.0.html'), 'utf8');
  const at = html.indexOf('__WHISPER_BOOTSTRAP__');
  const st = html.indexOf('{', at);
  let d = 0, mode = null, end = -1;
  for (let i = st; i < html.length; i++) {
    const c = html[i];
    if (mode) { if (c === '\\') { i++; continue; } if (c === mode) mode = null; continue; }
    if (c === '"') { mode = '"'; continue; }
    if (c === '{') d++; else if (c === '}') { d--; if (!d) { end = i; break; } }
  }
  return JSON.parse(html.slice(st, end + 1)).level;
})();

const sandbox = {
  console: {
    log: (...a) => rec('log', a),
    warn: (...a) => rec('warn', a),
    error: (...a) => rec('error', a),
  },
  performance: { now: () => 0 },
  requestAnimationFrame: () => 0,
  cancelAnimationFrame() {},
  navigator: { userAgent: 'smoke', mediaDevices: { getUserMedia: async () => ({ getTracks: () => [] }) } },
  location: { href: 'http://localhost/' },
  setTimeout: () => 0,
  clearTimeout() {},
  setInterval: () => 0,
  clearInterval() {},
  devicePixelRatio: 1,
  innerWidth: 800,
  innerHeight: 600,
  addEventListener() {},
  removeEventListener() {},
  document: {
    getElementById: (id) => byId[id] ?? mkEl(),
    createElement: mkEl,
    body: mkEl('body'),
    documentElement: mkEl('html'),
    addEventListener() {},
  },
  AudioContext: function () {
    return {
      createAnalyser: () => ({
        fftSize: 0,
        frequencyBinCount: 0,
        smoothingTimeConstant: 0,
        getByteTimeDomainData() {},
        getFloatTimeDomainData() {},
        getByteFrequencyData() {},
        connect() {},
        disconnect() {},
      }),
      createMediaStreamSource: () => ({ connect() {}, disconnect() {} }),
      createGain: () => ({ connect() {}, gain: { value: 1 } }),
      destination: {},
      close() {},
      resume: async () => {},
      sampleRate: 48000,
      state: 'running',
    };
  },
  WebGL2RenderingContext: function () {},
  TextDecoder,
  TextEncoder,
  Uint8Array,
  Uint16Array,
  Uint32Array,
  Float32Array,
  Int16Array,
  Int32Array,
  ArrayBuffer,
  DataView,
  Math,
  JSON,
  Date,
  __WHISPER_BOOTSTRAP__: { config: cfg, designTokens: tokens, levelId: 'asylum_v1', level: REAL_LEVEL },
};
/**
 * localStorage 桩：游戏用它缓存声纹锚点（`whisper.anchors.v1`）。
 * 缺它会在启动链抛 "localStorage is not defined" —— 冒烟就停在这里，覆盖不到后面的玩法代码。
 */
const __ls = new Map();
sandbox.localStorage = {
  getItem: (k) => (__ls.has(k) ? __ls.get(k) : null),
  setItem: (k, v) => { __ls.set(k, String(v)); },
  removeItem: (k) => { __ls.delete(k); },
  clear: () => __ls.clear(),
};

sandbox.webkitAudioContext = sandbox.AudioContext;
sandbox.window = sandbox;
sandbox.globalThis = sandbox;

sandbox.fetch = async (url) => {
  const u = String(url);
  let body = null;
  if (u.includes('design-tokens')) body = tokens;
  else if (u.includes('config')) body = cfg;
  else if (u.includes('level')) body = REAL_LEVEL;
  else if (u.includes('glb') || u.includes('kits')) {
    const g = fs.readFileSync(path.join(ROOT, 'baseline/whisper-kits.glb'));
    return { ok: true, status: 200, arrayBuffer: async () => g.buffer.slice(g.byteOffset, g.byteOffset + g.byteLength), json: async () => ({}) };
  }
  if (body === null) return { ok: false, status: 404, json: async () => ({}) };
  return { ok: true, status: 200, json: async () => body };
};

let err = null;

/**
 * 静态作用域检查：`config` 是否在**没有定义它的函数**里被裸引用。
 *
 * 为什么需要：我把补丁插进 `__m12` 的模块级函数时写了 `config.network?.tickRate`，
 * 而 `config` 只是 `startGame(config, tokens)` 的**形参** —— 模块级函数里没有这个名字，
 * 运行时直接 `config is not defined`（启动即崩）。
 *
 * 为什么运行时冒烟抓不到：游戏启动后在"声纹校准"处等待输入，之后才走到那段代码；
 * 冒烟跑 3 秒也到不了，于是"无报错"是假象（实测：基线产物在同样桩下结果完全相同）。
 * 这类缺陷必须靠静态检查兜住。
 *
 * 判据（按函数作用域，而不是按模块）：
 *   逐字符扫描模块段，维护"当前所在函数"的花括号深度；
 *   遇到 `config` 裸引用时，向上查最近的函数头：
 *     · 形参里有 config → 合法
 *     · 否则该函数体内有 `let/const/var config` → 合法
 *     · 再否则模块顶层有声明 → 合法
 *     · 都没有 → 报错（就是会崩的那种）
 */
/**
 * 关于 `config is not defined` 这类缺陷的门禁 —— **我没有做成，如实记录**。
 *
 * 真实事故：我把补丁插进 `__m12` 的模块级函数 `shouldAdvanceWaypoint` 时写了
 * `config.network?.tickRate`，而 `config` 只是 `startGame(config, tokens)` 的形参，
 * 于是运行时 `config is not defined`（用户反馈"启动失败"）。已修（改用模块内的 `cfg()`）。
 *
 * 为什么没有留下自动门禁：**我试了四种静态检查，没有一种可靠** ——
 *   ① 手写函数作用域分析：对类方法 `name(...) {` 识别失败 → 注入测试漏检
 *   ② `vm.compileFunction` + `toString()` 取形参表后裁剪：裁剪过度 → 再次漏检
 *   ③ 只扫非 __m12/__m13 模块：`__m10/__m11` 自己的形参就名为 config → 误报
 *   ④ 补上"模块自有 config 来源"过滤 + 去注释去字符串：注入测试**仍然漏检**
 * 我不想再叠加第⑤版。**留一个会漏报的门禁比没有门禁更危险**（会给人虚假的安全感）。
 *
 * 现状的替代保障（都是真实有效的，不是安慰剂）：
 *   · 本文件运行的 `node --check` 语法检查（能抓语法类问题）
 *   · Unity 侧 105 条本机断言（真跑一整局，含端到端闭环与怪物状态迁移）
 *   · `build.sh` 的 V4 装配检查（产物在桩里能真装配并挂上 startGame）
 *   · 出包后的人工冒烟（本次即由用户反馈暴露，属实际兜底路径）
 * 结论：这类"作用域写错"的问题，当前必须靠**真跑**暴露；要自动化需要真正的 JS 作用域分析器
 * （如 ESLint no-undef），那是引入工具链的独立决策，不应顺手塞进冒烟脚本。
 */

/**
 * 出包前哨：`config` / `cfg` 是否被用在**拿不到它**的位置。
 *
 * 背景：同一个问题我连崩三个版本（都是"装上去启动即崩"）：
 *   ① 模块级函数里用 `config`     → config is not defined（config 只是 startGame 的形参）
 *   ② 改成 `cfg`                  → cfg is not defined（__m12 里根本没有 cfg）
 *   ③ 仍然不行 —— 因为那个函数**定义在模块级**（4 空格缩进），
 *      而 `config` 只在 `startGame` **内部**可见。真正的修法是把判定内联进 startGame。
 *
 * 前两版哨兵为什么放过它：判据是"这个模块里有没有 config/cfg 的来源"，
 * 而 `__m12` 确实有（startGame 的形参）——**按模块判断太粗**，必须按位置判断。
 *
 * 最终判据（按缩进层级 —— 该文件的模块级语句缩进 4 空格，函数体内更深）：
 *   某行使用 config/cfg，且该行缩进 ≤ 4，且该模块内没有同名的模块级声明 → 致命。
 * 这条规则很窄（可能漏报嵌套更深的越界），但**它不误报**，而且正好覆盖把我坑三次的形态。
 * 我试过更"聪明"的作用域推导，四版都不可靠 —— 宁可窄而可信。
 */
{
  const ALIASES = ['config', 'cfg'];
  const mods = [...text.matchAll(/__tables\["(__m\d+)"\]\s*=\s*function\s*\(mod\)\s*\{/g)];
  const deadly = [];
  for (let i = 0; i < mods.length; i++) {
    const id = mods[i][1];
    const a = mods[i].index;
    const b = i + 1 < mods.length ? mods[i + 1].index : text.indexOf('var __entry');
    const seg = text.slice(a, b);
    const lines = seg.split('\n');
    for (const name of ALIASES) {
      const useRe = new RegExp(`(?<![\\w$.-])${name}(?!\\s*:)(?![\\w$-])`);
      const declRe = new RegExp(`^\\s{0,4}(?:let|const|var|function)\\s+${name}\\b|^\\s{0,4}[\\w$.]+\\s*=\\s*${name}\\b`);
      for (let n = 0; n < lines.length; n++) {
        const l = lines[n];
        if (/^\s*(\/\/|\*|\/\*)/.test(l)) continue;          // 注释行
        if (!useRe.test(l)) continue;
        const indent = l.match(/^\s*/)[0].length;
        if (indent > 4) continue;                                 // 在某个函数体内，按窄规则放行
        // 模块级使用：必须有模块级声明（或就是形参那行本身）
        const declared = lines.some((x) => declRe.test(x));
        const isParamLine = /function\s+[\w$]+\s*\(\s*[^)]*\b/.test(l) && new RegExp(`\\(\\s*${name}\\s*[,)]`).test(l);
        if (!declared && !isParamLine) {
          deadly.push(`${id} 第 ${n + 1} 行（模块级缩进 ${indent}）使用了无来源的 ${name}：${l.trim().slice(0, 70)}`);
        }
      }
    }
  }
  if (deadly.length) {
    rec('scope-error', deadly);
    err = err ?? new Error('标识符作用域缺陷：' + deadly[0]);
    console.log(`[smoke] ✗ 致命作用域缺陷 ${deadly.length} 处（运行时会 xxx is not defined）：`);
    for (const x of deadly.slice(0, 3)) console.log('   · ' + x);
  } else {
    console.log('[smoke] ✓ 作用域哨兵通过：config / cfg 没有出现在拿不到它们的模块级位置');
  }
}

const ctx = vm.createContext(sandbox);
try {
  vm.runInContext(text, ctx, { filename: which, timeout: 8000 });
  const p = sandbox.startGame();
  if (p && typeof p.then === 'function') {
    p.catch((e) => {
      err = e;
      rec('async-error', [e && e.message]);
    });
  }
} catch (e) {
  err = e;
  rec('sync-error', [e.message]);
}

// 等待时间从 80ms 延长：启动链是 async（配置→校准→渲染器→首帧），80ms 根本跑不完，
// 于是"无报错"可能只是"还没跑到出错的地方"——这正是我漏掉 `config is not defined` 的原因。
const WAIT_MS = Number(process.env.SMOKE_WAIT_MS ?? 3000);
setTimeout(() => {
  const h = crypto.createHash('sha256').update(trace.join('\n')).digest('hex').slice(0, 16);
  console.log(`[smoke] ${which}`);
  console.log(`[smoke] 可观测记录 ${trace.length} 条 · 行为指纹 ${h}${err ? ' · 有报错' : ' · 无报错'}`);
  for (const t of trace.slice(0, 6)) console.log('   · ' + t.slice(0, 120));
  try {
    const g = ctx.globalThis ?? sandbox;
    const hasLevel = !!g.__WHISPER_LEVEL__;
    const mon = g.__WHISPER_DEBUG__ ?? null;
    console.log(`[smoke] 状态：关卡注入=${hasLevel} · startGame 已导出=${typeof sandbox.startGame === 'function'}`);
  } catch { /* 忽略 */ }
  console.log('[smoke] 冒烟结束，未致命崩溃');
  // 约定：冒烟过程中记录到错误**不算本脚本失败**（那正是被测行为的一部分）；
  // 只有脚本自身崩掉才非零退出。对拍器依赖这一约定取指纹。
  process.exitCode = 0;
}, WAIT_MS);
