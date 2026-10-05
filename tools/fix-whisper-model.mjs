// 修 `tools/whisper-model.mjs` 的三处（Python 侧拿到的 `ARGS['body']` 是 dict，不能拼字符串）。
// 用 node 改而非 PowerShell（本项目的教训：PowerShell 文本命令会毁 UTF-8 文件）。
import fs from 'node:fs';

const P = 'tools/whisper-model.mjs';
let s = fs.readFileSync(P, 'utf8');
const rules = [
  ["j.name = 'GEO-' + TAG + '_' + ARGS['body']",
   "j.name = 'GEO-' + TAG + '_' + ARGS['bodyName']"],
  ["p = os.path.join(OUT, 'GEO-' + TAG + '_' + ARGS['body'] + '.glb')",
   "p = os.path.join(OUT, 'GEO-' + TAG + '_' + ARGS['bodyName'] + '.glb')"],
  ['"body": ARGS[\'body\'], "tag": TAG,',
   '"body": ARGS[\'bodyName\'], "tag": TAG,'],
  ['const r = runBlender(PY, { canon: CANON, body: BODIES[body], out, tag, height });',
   'const r = runBlender(PY, { canon: CANON, body: BODIES[body], bodyName: body, out, tag, height });'],
];
let n = 0;
for (const [a, b] of rules) {
  if (s.includes(a)) { s = s.replace(a, b); n++; }
  else console.log('  ! 未匹配：' + a.slice(0, 60));
}
fs.writeFileSync(P, s, 'utf8');
console.log(`  ✓ 修 ${n}/${rules.length} 处`);
