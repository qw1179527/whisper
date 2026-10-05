#!/usr/bin/env node
/**
 * gen-kit-resources.mjs — 把清单里的套件 GLB **确定性复制**到 `unity/Assets/Resources/Kits/`
 *
 * ## 为什么必须复制（本项目的头号失效模式）
 * `Assets/ThirdParty/CC0/**` 下**没有任何场景/预制体/代码引用**这些 GLB，Unity 打包时
 * 会直接把"没有被引用的资产"排除在包外 —— 于是出现"套件已生成、已过门禁、就是不在 APK 里"。
 * 历史上同类失效模式已经栽过四次（几何层 / 内容管线 / 怪物实例化 / 理智系统）。
 *
 * `Assets/Resources/**` 里的资产**无条件进包**（这正是 2026-10-03 修真机黑屏时用的同一机制：
 * 内置着色器被剥离 → 把着色器作为资产放进 Resources）。所以套件也走这条路。
 *
 * ## 确定性（可复核，而不是"我拷过了"）
 *   · 源 = 清单里 `kits[].file`（相对 `sourceRoot`），目标 = `Assets/Resources/Kits/<id>.glb`
 *   · 复制后**重新算 sha256**，必须与清单记录一致；不一致 → 报错退出（不静默覆盖）
 *   · 每个 kit 还把 `resPath` 写回清单（供 validate-assets / gate 校验"引用与产物一致"）
 *   · `--check` 只校验不写（CI 用）；默认写
 *
 * 用法：
 *   node tools/gen-kit-resources.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const CHECK = process.argv.includes('--check');
// 真源与镜像的口径与 tools/data-mirror.mjs 一致：asset-manifest 的真源是 Assets/Data/，
// 镜像到 Resources/Data/（运行时经 Resources.Load 读）与 native/micprobe/res/raw/。
// 注意：`data/` 下**没有** asset-manifest（只有 config/design-tokens 走那条镜像）——别想当然对齐。
const MANIFEST = path.join(ROOT, 'unity/Assets/Data/asset-manifest.json');
const MIRRORS = [
  path.join(ROOT, 'unity/Assets/Resources/Data/asset-manifest.json'),
  path.join(ROOT, 'native/micprobe/res/raw/asset_manifest.json'),
  // 【2026-10-04 补】`native/csharp-verify/asset-manifest.json` 此前是**手工副本**，
  // 全仓没有任何工具同步它，而 `native/csharp-verify/Program.cs` 拿它当真源比对
  // sha256 / 字节数 / footprint。后果：**只要重新生成任何一个套件（sha 必变），
  // 步骤 21 就会拿旧副本比对 → 必红**，等于"改了资产就过不了门禁"的假红
  // （构建智能体 B 踩到并报上来）。加到镜像列表后由生成器每次主动同步。
  path.join(ROOT, 'native/csharp-verify/asset-manifest.json'),
];
// 两个落点，各有各的必要性：
//
//   ① StreamingAssets/Kits/<id>.glb（原始字节、不经 Import 器）
//      —— 构建产物取证用（`tools/verify-packed-kits.mjs` 直接读得到）。
//
//   ② Resources/Kits/<id>.glb.bytes（**扩展名故意不是 .glb**）
//      —— **Android 必须走这条**。官方手册明说：StreamingAssets 在 Android 上位于 APK 内部，
//         **不能用 File API 直接读**，要用 UnityWebRequest 走 `jar:file://…!/assets/…`；
//         而本项目把 `UnityEngine.Networking` 列为**禁入的第三方命名空间**（门禁 C6），
//         那条路在本工程纪律下走不通。
//         `.bytes` 不是模型扩展名 → Unity 当**二进制文本资产**导入 → `Resources.Load<TextAsset>()`
//         读取**平台无关**（桌面/Android/iOS 一致）。
//
// （历史坑：`.glb` 直接放 Resources 会被 **ModelImporter** 当模型导入 —— Unity 原生不支持 glTF，
//   于是既没网格也不是文本资产，`Resources.Load<TextAsset>()` 恒返回 null；而且构建产物里
//   搜得到 `Kits/` 与套件名（那是清单 JSON 里的路径字符串），差点被当成"已进包"的假绿。）
const RES_DIR = path.join(ROOT, 'unity/Assets/StreamingAssets/Kits');
const RES_ALT_DIR = path.join(ROOT, 'unity/Assets/Resources/Kits');

const sha256 = (buf) => crypto.createHash('sha256').update(buf).digest('hex');

/// 写一份副本并校验（存在且 sha 一致则不动；不一致/缺失则按 --check 报错或写入）
function writeOne(dst, buf, sha, label, problems) {
  if (fs.existsSync(dst)) {
    const cur = sha256(fs.readFileSync(dst));
    if (cur !== sha) {
      if (CHECK) problems.push(`${label} 与源不一致（副本 ${cur.slice(0, 12)} vs 源 ${sha.slice(0, 12)}）`);
      else { fs.writeFileSync(dst, buf); console.log(`  更新 ${label}（副本曾与源不一致）`); }
    }
  } else if (CHECK) {
    problems.push(`${label} 缺失（跑 node tools/gen-kit-resources.mjs）`);
  } else {
    fs.mkdirSync(path.dirname(dst), { recursive: true });
    fs.writeFileSync(dst, buf);
    console.log(`  复制 → ${label}（${buf.length} B · sha ${sha.slice(0, 12)}）`);
  }
}

function main() {
  if (!fs.existsSync(MANIFEST)) { console.error(`✗ 清单不存在：${MANIFEST}`); process.exit(1); }
  const manifest = JSON.parse(fs.readFileSync(MANIFEST, 'utf8'));
  // sourceRoot 形如 "Assets/ThirdParty/CC0"（相对 unity 工程），与 gen-kits/validate-assets 同口径
  const srcRoot = path.join(ROOT, 'unity', manifest.sourceRoot ?? 'Assets/ThirdParty/CC0');

  const problems = [];
  const planned = [];

  for (const kit of manifest.kits) {
    const src = path.join(srcRoot, kit.file);
    if (!fs.existsSync(src)) { problems.push(`套件 ${kit.id} 源文件不存在：${kit.file}`); continue; }
    const buf = fs.readFileSync(src);
    const actual = sha256(buf);

    // 清单记录必须与源一致（否则说明源被改过而清单没回写 → 先跑 gen-kits.mjs）
    if (!kit.sha256) problems.push(`套件 ${kit.id} 清单缺 sha256`);
    else if (kit.sha256 !== actual) problems.push(`套件 ${kit.id} 源 sha256 与清单不一致（清单 ${kit.sha256.slice(0, 12)} vs 实际 ${actual.slice(0, 12)}）`);

    const resRel = `Kits/${kit.id}.glb`;
    const dst = path.join(RES_DIR, `${kit.id}.glb`);
    const altRel = `Kits/${kit.id}.glb.bytes`;
    const altDst = path.join(RES_ALT_DIR, `${kit.id}.glb.bytes`);
    planned.push({ id: kit.id, resRel, altRel, buf, sha: actual });

    // ① StreamingAssets（产物取证用）
    writeOne(dst, buf, actual, `StreamingAssets/${resRel}`, problems);
    // ② Resources/<id>.glb.bytes（Android 运行时读取用；扩展名确保被当二进制文本资产）
    writeOne(altDst, buf, actual, `Resources/${altRel}`, problems);

    if (kit.resPath !== resRel) {
      if (CHECK) problems.push(`套件 ${kit.id} 清单的 resPath 未回写（现 ${JSON.stringify(kit.resPath)}，应 "${resRel}"）`);
      else { kit.resPath = resRel; console.log(`  回写清单 resPath = ${resRel}`); }
    }
  }

  if (problems.length) {
    console.error('\n✗ gen-kit-resources 校验失败：');
    for (const p of problems) console.error('  - ' + p);
    process.exit(1);
  }

  if (!CHECK) {
    // 清单是"唯一入口"：真源 + 镜像必须同步（与 data-mirror.mjs 同一口径）
    const text = JSON.stringify(manifest, null, 2) + '\n';
    for (const target of [MANIFEST, ...MIRRORS]) {
      if (!fs.existsSync(path.dirname(target))) continue;
      const before = fs.existsSync(target) ? fs.readFileSync(target, 'utf8') : null;
      if (before !== text) { fs.writeFileSync(target, text); console.log(`  写回 ${path.relative(ROOT, target)}`); }
    }
    console.log(`\n✓ ${planned.length} 个套件已落进 Assets/StreamingAssets/Kits（原始字节进包，不经 Import 器）`);
  } else {
    console.log(`\n✓ ${planned.length} 个套件的 StreamingAssets 副本与清单一致（--check 通过）`);
  }
}

main();
