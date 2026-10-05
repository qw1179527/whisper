// 校验：KitVisibilityCapture 的取证表 (room, kit) 必须与关卡 DSL 实际引用一致。
// 为什么单独校验：取证拍的是"这个套件在不在渲染"，若表里的 kit 与房间实际用的套件不同，
// 拍出来的图证明的是**另一个套件**——证据无效，而且从图上看不出来（都有几何）。
import fs from 'node:fs';

const SRC = 'D:/DSH专用/whisper/unity/Assets/Editor/KitVisibilityCapture.cs';
const DSL = 'D:/DSH专用/whisper/unity/Assets/Levels/asylum_v1.json';

const src = fs.readFileSync(SRC, 'utf8');
const level = JSON.parse(fs.readFileSync(DSL, 'utf8'));
const byId = Object.fromEntries(level.rooms.map((r) => [r.id, r.kit]));

// 只扫 Targets 数组那一段，避免误匹配文件里其它 ("a", "b") 形态
const seg = src.slice(src.indexOf('static readonly (string room, string kit, string why)[] Targets'));
const end = seg.indexOf('};');
const table = seg.slice(0, end);
const re = /\(\s*"([a-z_0-9]+)"\s*,\s*"([a-z_0-9]+)"/g;

let ok = 0, bad = 0, m;
while ((m = re.exec(table)) !== null) {
  const [, room, kit] = m;
  if (!byId[room]) { console.log(`  ✗ 房间不存在于 DSL：${room}`); bad++; continue; }
  if (byId[room] !== kit) { console.log(`  ✗ ${room}：取证写 kit=${kit}，DSL 实际是 ${byId[room]}`); bad++; }
  else { console.log(`  ✓ ${room.padEnd(16)} kit=${kit}`); ok++; }
}
console.log(`  → 一致 ${ok} · 不一致 ${bad}`);
process.exit(bad ? 1 : 0);
