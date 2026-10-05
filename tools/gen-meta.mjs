#!/usr/bin/env node
/**
 * gen-meta.mjs — 为 Unity 工程生成缺失的 .meta 文件
 *
 * 为什么必须生成（真实事故）：
 *   Unity 的每个资产（.cs / .asmdef / 文件夹 / .json）都需要配套的 `.meta` 文件，
 *   里面存着**稳定的 GUID**。本工程此前 0 个 .meta，导致 CI 构建时：
 *     · 只有 Analytics / UI 两个 asmdef 被识别，且提示 "no assembly associated"
 *     · Core / Gameplay / Net / Audio / Backend / Runtime 的 .cs 没被正确归入程序集
 *     · 结果 181 条 CS0234「命名空间 Whisper.Core 不存在」——所有程序集互相看不见
 *   （本机断言跑手测不出这个：它把源码当普通 C# 编译，不经过 Unity 的资产数据库。）
 *
 * 做法：递归遍历 unity/Assets 下所有资产与文件夹，缺 .meta 就补一个。
 *   · .cs / .json / .asmdef 等用 TextScriptImporter/MonoImporter 等对应类型
 *   · 文件夹用 DefaultImporter
 *   · GUID 用内容的 sha1 前 32 位（确定性：同样内容生成同样 GUID，避免每次重建都变）
 *
 * 用法：node tools/gen-meta.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const ASSETS = path.join(ROOT, 'unity/Assets');
const checkOnly = process.argv.includes('--check');

/** 按扩展名选 importer 类型 */
function importerFor(p, isDir) {
  if (isDir) return 'DefaultImporter';
  const lower = p.toLowerCase();
  // 【实测踩坑】`<名字>.glb.bytes` 的 path.extname 是 `.bytes`，会落到 default: DefaultImporter ——
  // 而 DefaultImporter **不是 TextAsset**，`Resources.Load<TextAsset>()` 恒返回 null。
  // 套件的 `.glb.bytes` 落点正是靠"被当二进制文本资产"才能在 Android 上读到（StreamingAssets
  // 在 APK 内不能用 File API 读），所以这里必须先识别 `.glb.bytes` 这个复合扩展名。
  if (lower.endsWith('.glb.bytes') || lower.endsWith('.gltf.bytes')) return 'TextScriptImporter';
  const e = path.extname(p).toLowerCase();
  switch (e) {
    case '.cs': return 'MonoImporter';
    case '.asmdef': return 'AssemblyDefinitionImporter';
    case '.json': case '.txt': case '.md': case '.bytes': return 'TextScriptImporter';
    case '.shader': return 'ShaderImporter';
    case '.glb': case '.gltf': return 'ModelImporter';
    case '.png': case '.jpg': case '.jpeg': return 'TextureImporter';
    case '.mat': return 'NativeFormatImporter';
    default: return 'DefaultImporter';
  }
}

/** 确定性 GUID：基于相对路径（稳定，不随内容编辑而变） */
function guidFor(rel) {
  return crypto.createHash('sha1').update('dsh-whisper:' + rel).digest('hex').slice(0, 32);
}

function metaBody(rel, isDir) {
  const guid = guidFor(rel);
  const importer = importerFor(rel, isDir);
  if (isDir) {
    return `fileFormatVersion: 2
guid: ${guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
`;
  }
  return `fileFormatVersion: 2
guid: ${guid}
${importer}:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
`;
}

let made = 0, missing = [];
function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (e.name === '.DS_Store') continue;
    const p = path.join(dir, e.name);
    const rel = path.relative(ROOT, p);
    // 文件夹本身也要 meta（Assets 顶层除外，Unity 对 Assets 自身不生成）
    if (e.isDirectory()) {
      const mp = p + '.meta';
      if (!fs.existsSync(mp)) {
        missing.push(rel);
        if (!checkOnly) { fs.writeFileSync(mp, metaBody(rel, true), 'utf8'); made++; }
      }
      walk(p);
    } else if (!e.name.endsWith('.meta')) {
      const mp = p + '.meta';
      if (!fs.existsSync(mp)) {
        missing.push(rel);
        if (!checkOnly) { fs.writeFileSync(mp, metaBody(rel, false), 'utf8'); made++; }
      }
    }
  }
}
walk(ASSETS);

if (checkOnly) {
  if (missing.length) { console.log(`  ✗ 缺 ${missing.length} 个 .meta（例：${missing.slice(0,3).join(', ')}）`); process.exit(1); }
  console.log('  ✓ 所有资产都有 .meta');
} else {
  console.log(`  ✓ 生成 ${made} 个 .meta 文件`);
  if (missing.length > made) console.log(`    （原本缺失 ${missing.length} 个）`);
}
