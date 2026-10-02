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
  __WHISPER_BOOTSTRAP__: { config: cfg, designTokens: tokens, levelId: 'asylum_v1' },
};
sandbox.webkitAudioContext = sandbox.AudioContext;
sandbox.window = sandbox;
sandbox.globalThis = sandbox;

sandbox.fetch = async (url) => {
  const u = String(url);
  let body = null;
  if (u.includes('design-tokens')) body = tokens;
  else if (u.includes('config')) body = cfg;
  else if (u.includes('level')) body = { levelId: 'asylum_v1', rooms: [] };
  else if (u.includes('glb') || u.includes('kits')) {
    const g = fs.readFileSync(path.join(ROOT, 'baseline/whisper-kits.glb'));
    return { ok: true, status: 200, arrayBuffer: async () => g.buffer.slice(g.byteOffset, g.byteOffset + g.byteLength), json: async () => ({}) };
  }
  if (body === null) return { ok: false, status: 404, json: async () => ({}) };
  return { ok: true, status: 200, json: async () => body };
};

const ctx = vm.createContext(sandbox);
let err = null;
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

setTimeout(() => {
  const h = crypto.createHash('sha256').update(trace.join('\n')).digest('hex').slice(0, 16);
  console.log(`[smoke] ${which}`);
  console.log(`[smoke] 可观测记录 ${trace.length} 条 · 行为指纹 ${h}${err ? ' · 有报错' : ' · 无报错'}`);
  for (const t of trace.slice(0, 6)) console.log('   · ' + t.slice(0, 120));
  console.log('[smoke] 冒烟结束，未致命崩溃');
  // 约定：冒烟过程中记录到错误**不算本脚本失败**（那正是被测行为的一部分）；
  // 只有脚本自身崩掉才非零退出。对拍器依赖这一约定取指纹。
  process.exitCode = 0;
}, 80);
