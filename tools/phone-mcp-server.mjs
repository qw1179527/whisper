#!/usr/bin/env node
/**
 * phone-mcp-server.mjs — 手机端独立 MCP 服务（stdio · JSON-RPC 2.0）
 *
 * ## 为什么需要它（用户要求：电脑关机也能调用工具）
 * DSH 宿主只在电脑上；`dsh-sh` 依赖 PowerShell（Android 没有）。
 * 所以手机上要"能调用工具"，只能**自带一个服务端**。
 *
 * ## 设计
 * · **零依赖**：只用 Node 内置模块（手机 Termux 装 nodejs 即可跑，不需要 npm install）；
 * · **stdio 传输**：MCP 标准输入输出协议，任何 MCP 客户端都能连；
 * · **能力来自本机真实操作**：读写文件、列目录、跑 shell（Termux 的 sh）、取网页正文 ——
 *   这四件覆盖了本项目 90% 的日常（改代码、跑门禁、抓官方资料）；
 * · **不含 DSH 专有工具**（continuum/memory 等需要宿主状态，手机上没有）——
 *   本服务**不假装**有它们。
 *
 * ## 用法
 * ```bash
 * node phone-mcp-server.mjs            # 以 stdio 方式提供 MCP 服务
 * node phone-mcp-server.mjs --selftest # 自检：列出工具并本地跑一次
 * ```
 *
 * ## 协议要点（MCP / JSON-RPC 2.0）
 * 客户端发 `initialize` → 服务端回能力；随后 `tools/list`、`tools/call`。
 * 每行一个 JSON 对象（NDJSON over stdio），这是 MCP 的 stdio 传输约定。
 */

import { createInterface } from 'node:readline';
import { readFile, writeFile, readdir, stat, mkdir } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { spawn } from 'node:child_process';
import path from 'node:path';

const SERVER_NAME = 'phone-tools';
const SERVER_VERSION = '1.0.0';

// ── 工具定义（name/description/inputSchema）────────────────────────────
const TOOLS = [
  {
    name: 'read_file',
    description: '读取文本文件（UTF-8）。返回带行号的正文，便于精确引用行。',
    inputSchema: {
      type: 'object',
      properties: {
        path: { type: 'string', description: '文件路径（相对或绝对）' },
        offset: { type: 'number', description: '起始行（1-based，可选）' },
        limit: { type: 'number', description: '最多返回行数（可选，默认 2000）' },
      },
      required: ['path'],
    },
  },
  {
    name: 'write_file',
    description: '写入文本文件（UTF-8，覆盖）。父目录不存在会自动创建。返回写入字节数。',
    inputSchema: {
      type: 'object',
      properties: {
        path: { type: 'string', description: '文件路径' },
        content: { type: 'string', description: '完整内容' },
      },
      required: ['path', 'content'],
    },
  },
  {
    name: 'list_dir',
    description: '列出目录内容（名称/类型/大小），按名称排序。',
    inputSchema: {
      type: 'object',
      properties: { path: { type: 'string', description: '目录路径' } },
      required: ['path'],
    },
  },
  {
    name: 'run_shell',
    description: '在手机上执行一条 shell 命令（Termux 的 sh -c）。返回 exit code + stdout + stderr。'
      + '用于跑本项目 Node 门禁，例如 `node tools/gate-model.mjs`。有超时保护（默认 120 秒）。',
    inputSchema: {
      type: 'object',
      properties: {
        command: { type: 'string', description: '要执行的命令' },
        cwd: { type: 'string', description: '工作目录（可选）' },
        timeoutMs: { type: 'number', description: '超时毫秒（可选，默认 120000，上限 600000）' },
      },
      required: ['command'],
    },
  },
  {
    name: 'fetch_url',
    description: '取网页正文（跟随跳转、剥离脚本样式、按 UTF-8 解码），返回纯文本。'
      + '本机实测可读 phasmophobia.su；fandom 系站点会超时。',
    inputSchema: {
      type: 'object',
      properties: {
        url: { type: 'string', description: '完整 URL（http/https）' },
        maxChars: { type: 'number', description: '最多返回字符数（可选，默认 20000）' },
      },
      required: ['url'],
    },
  },
];

// ── 工具实现 ───────────────────────────────────────────────────────────
async function toolReadFile({ path: p, offset = 1, limit = 2000 }) {
  const text = await readFile(p, 'utf8');
  const all = text.split(/\r?\n/);
  const from = Math.max(1, offset);
  const slice = all.slice(from - 1, from - 1 + limit);
  const out = slice.map((l, i) => `${from + i}: ${l}`).join('\n');
  return `# ${p}（共 ${all.length} 行，返回 ${from}..${from + slice.length - 1}）\n${out}`;
}

async function toolWriteFile({ path: p, content }) {
  await mkdir(path.dirname(path.resolve(p)), { recursive: true });
  await writeFile(p, content, 'utf8');
  const n = Buffer.byteLength(content, 'utf8');
  return `已写入 ${p}（${n} 字节）`;
}

async function toolListDir({ path: p }) {
  const names = await readdir(p);
  const rows = [];
  for (const n of names.sort()) {
    try {
      const s = await stat(path.join(p, n));
      rows.push(`${s.isDirectory() ? '[目录]' : '[文件]'} ${n}  ${s.isDirectory() ? '' : s.size + ' B'}`);
    } catch { rows.push(`[????] ${n}`); }
  }
  return `${p}（${rows.length} 项）\n${rows.join('\n')}`;
}

function toolRunShell({ command, cwd, timeoutMs = 120000 }) {
  const ms = Math.min(Math.max(1000, timeoutMs), 600000);
  return new Promise((resolve) => {
    const child = spawn('sh', ['-c', command], { cwd: cwd || process.cwd() });
    let out = '', err = '';
    const timer = setTimeout(() => child.kill('SIGKILL'), ms);
    child.stdout.on('data', (d) => { out += d.toString(); });
    child.stderr.on('data', (d) => { err += d.toString(); });
    child.on('close', (code) => {
      clearTimeout(timer);
      resolve(`exit=${code}${code !== 0 ? '（非零）' : ''}\n--- stdout ---\n${out.slice(-12000)}`
        + `\n--- stderr ---\n${err.slice(-6000)}`);
    });
    child.on('error', (e) => { clearTimeout(timer); resolve(`启动失败：${e.message}`); });
  });
}

async function toolFetchUrl({ url, maxChars = 20000 }) {
  if (!/^https?:\/\//i.test(url)) throw new Error('只支持 http/https');
  const res = await fetch(url, {
    redirect: 'follow',
    headers: { 'user-agent': 'Mozilla/5.0 (Android) phone-mcp-server/1.0' },
    signal: AbortSignal.timeout(25000),
  });
  const html = await res.text();
  const text = html
    .replace(/<script[\s\S]*?<\/script>/gi, ' ')
    .replace(/<style[\s\S]*?<\/style>/gi, ' ')
    .replace(/<[^>]+>/g, ' ')
    .replace(/&nbsp;/g, ' ').replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"').replace(/&#39;/g, "'")
    .replace(/[ \t]+/g, ' ')
    .replace(/\n{3,}/g, '\n\n')
    .trim();
  return `HTTP ${res.status} · ${url}\n字符数 ${text.length}（截断到 ${maxChars}）\n\n${text.slice(0, maxChars)}`;
}

const IMPL = {
  read_file: toolReadFile,
  write_file: toolWriteFile,
  list_dir: toolListDir,
  run_shell: toolRunShell,
  fetch_url: toolFetchUrl,
};

// ── JSON-RPC over stdio ────────────────────────────────────────────────
function send(obj) { process.stdout.write(JSON.stringify(obj) + '\n'); }

async function handle(msg) {
  const { id, method, params } = msg;
  if (method === 'initialize') {
    return {
      jsonrpc: '2.0', id,
      result: {
        protocolVersion: '2024-11-05',
        capabilities: { tools: {} },
        serverInfo: { name: SERVER_NAME, version: SERVER_VERSION },
      },
    };
  }
  if (method === 'notifications/initialized') return null;   // 通知，不回
  if (method === 'tools/list') return { jsonrpc: '2.0', id, result: { tools: TOOLS } };
  if (method === 'tools/call') {
    const nm = params?.name;
    const fn = IMPL[nm];
    if (!fn) return { jsonrpc: '2.0', id, error: { code: -32601, message: `未知工具：${nm}` } };
    try {
      const text = await fn(params.arguments ?? {});
      return { jsonrpc: '2.0', id, result: { content: [{ type: 'text', text: String(text) }] } };
    } catch (e) {
      return { jsonrpc: '2.0', id, result: { content: [{ type: 'text', text: `错误：${e.message}` }], isError: true } };
    }
  }
  if (id === undefined) return null;
  return { jsonrpc: '2.0', id, error: { code: -32601, message: `未知方法：${method}` } };
}

async function main() {
  if (process.argv.includes('--selftest')) {
    console.log(`[self-test] ${SERVER_NAME} v${SERVER_VERSION} · 工具 ${TOOLS.length} 个`);
    for (const t of TOOLS) console.log(`  · ${t.name} — ${t.description.split('。')[0]}`);
    console.log('[self-test] 本地调用 list_dir / run_shell 验证实现可用：');
    console.log(await toolListDir({ path: process.cwd() }));
    console.log(await toolRunShell({ command: 'echo phone-ok && node -v 2>/dev/null || echo no-node' }));
    process.exit(0);
  }

  const rl = createInterface({ input: process.stdin });
  rl.on('line', async (line) => {
    const s = line.trim();
    if (!s) return;
    let msg;
    try { msg = JSON.parse(s); } catch { return; }        // 非法行忽略（协议容错）
    const reply = await handle(msg);
    if (reply) send(reply);
  });
  process.stderr.write(`[${SERVER_NAME}] ready on stdio · tools=${TOOLS.length}\n`);
}

main();
