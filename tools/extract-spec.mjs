#!/usr/bin/env node
/**
 * extract-spec.mjs — 从方案 PDF 抽取纯文本（V9 为终稿基线，V5~V8 归档备查）
 *
 * 为什么需要：项目总目标是"完成方案里的所有要求"，而"所有要求"必须从 PDF 逐条取出，
 * 不能靠记忆或转述。本机没有 PDF 库，但有 **Ghostscript**（imagemagick 扩展自带），
 * 它的 txtwrite 设备能抽出可读文本（实测中文正常，表格会折行）。
 *
 * 用法：node tools/extract-spec.mjs            # 抽取全部（含 V5~V8 归档）
 *       node tools/extract-spec.mjs --v9-only  # 只抽 V9（终稿）
 */
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const SRC_DIR = '/storage/emulated/0/DSH专用';
const OUT_DIR = path.join(ROOT, 'docs/spec');
const GS = '/data/user/0/app.dsh.mobile/files/engine/extensions/imagemagick/bin/gs';
const GS_LIB = '/data/user/0/app.dsh.mobile/files/engine/extensions/imagemagick/share/ghostscript/Resource/Init';

const SPECS = [
  { id: 'V9', file: '恐怖整合.pdf', out: 'V9-fulltext.txt', role: '终稿冻结基线（所有要求的唯一出处）' },
  { id: 'V8', file: '恐怖多人联机游戏开发方案-V8.0-三版综合评审与混元推荐.pdf', out: 'V8-fulltext.txt', role: '归档' },
  { id: 'V7', file: '恐怖多人联机游戏开发方案-V7.0-深度分析与后端设计全量完善版.pdf', out: 'V7-fulltext.txt', role: '归档' },
  { id: 'V6', file: '6abf455d7140e84f5fdd87f5_恐怖多人联机游戏开发方案-V6.0-深度评审与全量完善版.pdf', out: 'V6-fulltext.txt', role: '归档' },
  { id: 'V5', file: '恐怖多人联机游戏开发方案-Android版-V5.pdf', out: 'V5-fulltext.txt', role: '归档' },
];

if (!fs.existsSync(GS)) { console.error(`✗ 缺 Ghostscript：${GS}`); process.exit(1); }
fs.mkdirSync(OUT_DIR, { recursive: true });

const onlyV9 = process.argv.includes('--v9-only');
const list = onlyV9 ? SPECS.filter((s) => s.id === 'V9') : SPECS;
const index = [];

for (const s of list) {
  const src = path.join(SRC_DIR, s.file);
  const dst = path.join(OUT_DIR, s.out);
  if (!fs.existsSync(src)) { console.error(`✗ 缺 PDF：${src}`); process.exit(1); }
  // 已存在且比 PDF 新则跳过（幂等）
  if (fs.existsSync(dst) && fs.statSync(dst).mtimeMs > fs.statSync(src).mtimeMs) {
    console.log(`  · ${s.id} 已是最新，跳过`);
  } else {
    execFileSync(GS, ['-q', '-dNOPAUSE', '-dBATCH', '-sDEVICE=txtwrite', `-sOutputFile=${dst}`, src],
      { env: { ...process.env, GS_LIB }, stdio: 'pipe', timeout: 600000 });
  }
  const txt = fs.readFileSync(dst, 'utf8');
  const lines = txt.split('\n').length;
  const cjk = (txt.match(/[\u4e00-\u9fff]/g) ?? []).length;
  console.log(`  ✓ ${s.id.padEnd(3)} ${s.out.padEnd(20)} ${String(lines).padStart(5)} 行 · ${String(cjk).padStart(7)} 汉字 · ${s.role}`);
  index.push({ id: s.id, source: s.file, extracted: s.out, lines, cjkChars: cjk, role: s.role });
}

fs.writeFileSync(path.join(OUT_DIR, 'INDEX.json'),
  JSON.stringify({ note: '方案 PDF 抽取台账。V9 为终稿基线，其余归档备查；抽取工具 tools/extract-spec.mjs', specs: index }, null, 2) + '\n', 'utf8');
console.log(`[spec] 台账已写入 docs/spec/INDEX.json（${index.length} 份）`);
