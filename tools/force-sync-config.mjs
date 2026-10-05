// 核验并强制同步 config 镜像（data-mirror 需要 --sync 才复制，dry-run 只报告）。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const SRC = path.join(ROOT, 'data/config.json');
const DESTS = [
  path.join(ROOT, 'unity/Assets/Data/config.json'),
  path.join(ROOT, 'unity/Assets/Resources/Data/config.json'),
];

const src = fs.readFileSync(SRC, 'utf8');
const need = ['"shop"', '"progression"', '"objectives"', '"power"', '"interaction"', '"tasks"'];
console.log(`源 ${SRC}`);
console.log(`  ${src.length} 字节`);
for (const k of need) {
  const n = (src.match(new RegExp(k.replace(/"/g, '"'), 'g')) || []).length;
  console.log(`  含 ${k}: ${n > 0 ? '✓ ' + n + ' 次' : '✗ 缺失'}`);
}

let bad = 0;
for (const d of DESTS) {
  const before = fs.existsSync(d) ? fs.readFileSync(d, 'utf8') : null;
  const same = before === src;
  console.log(`\n镜像 ${d.replace(ROOT, '.')}`);
  console.log(`  同步前: ${before === null ? '不存在' : before.length + ' 字节'} · ${same ? '已一致' : '不一致'}`);
  if (!same) {
    fs.mkdirSync(path.dirname(d), { recursive: true });
    fs.writeFileSync(d, src, 'utf8');
    const after = fs.readFileSync(d, 'utf8');
    console.log(`  同步后: ${after.length} 字节 · ${after === src ? '✓ 一致' : '✗ 仍不一致'}`);
    if (after !== src) bad++;
  }
}
console.log(bad ? '\n✗ 有镜像未同步成功' : '\n✓ 全部镜像与真源一致');
process.exit(bad ? 1 : 0);
