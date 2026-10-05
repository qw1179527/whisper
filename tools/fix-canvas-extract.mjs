// 修抽 BuildCanvas 后的两处连带编译错（真 Unity 报的）：
//   ① `canvasGo` 随画布段落被搬进 BuildCanvas，BuildOptions 里剩下的引用全部失效 → CS0103
//   ② `PlaceAtScreen` 声明成 static，却要读实例字段 `_canvas.scaleFactor` → CS0120
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
let s = fs.readFileSync(P, 'utf8');
const log = [];

// ① canvasGo.transform → _canvas.transform（只改 BuildOptions 之后的部分；
//    BuildCanvas 内部那三行用 canvasGo 是**正确的**，不能动）
const marker = 'void BuildOptions()';
const at = s.indexOf(marker);
if (at < 0) { console.error('  ✗ 找不到 BuildOptions'); process.exit(1); }
const head = s.slice(0, at);
let tail = s.slice(at);
const n1 = (tail.match(/canvasGo\.transform/g) || []).length;
tail = tail.split('canvasGo.transform').join('_canvas.transform');
s = head + tail;
log.push(`  ✓ BuildOptions 起 canvasGo.transform → _canvas.transform（${n1} 处）`);

// ② PlaceAtScreen 去掉 static
const before = s;
s = s.replace('        static void PlaceAtScreen(Text t, Vector3 screenPoint, float w, float h)',
              '        void PlaceAtScreen(Text t, Vector3 screenPoint, float w, float h)');
log.push(s !== before ? '  ✓ PlaceAtScreen 去掉 static' : '  ! PlaceAtScreen 签名未中');

// ③ `_canvas` 字段确认是实例字段（非 static）——若也是 static 则不能读 scaleFactor，需要改
if (/static\s+Canvas\s+_canvas/.test(s)) {
  s = s.replace(/static\s+Canvas\s+_canvas/, 'Canvas _canvas');
  log.push('  ✓ _canvas 由 static 改为实例字段');
} else {
  log.push('  · _canvas 已是实例字段');
}

fs.writeFileSync(P, s, 'utf8');
console.log(log.join('\n'));
