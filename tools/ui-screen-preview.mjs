// ui-screen-preview.mjs —— 把 `TruckScreens.DrawMap` 的绘制逻辑**照搬**到 node 并输出 PNG。
//
// ══════════════════════════════════════════════════════════════════════════════
// 为什么需要它（UI 的**本机验证通道**）
// ══════════════════════════════════════════════════════════════════════════════
// 卡车内四屏是**程序化贴图**（`TruckScreens` 运行时画 Texture2D）。
// 这类 UI 的正确性判据是"**看上去对不对**"（布局铺满没有、分翼颜色能不能分辨），
// 而它在 CI 里要跑一整轮（约 11 分钟）才出一张图。
// ⇒ 把同一套绘制逻辑照搬到 node，**本机秒级出图**，看一眼就知道布局对不对。
// 本轮靠它当场查出两个真缺陷：地图只占画布下半部、按全关卡算范围导致右侧留白。
//
// ⚠ 口径必须与 C# **完全一致**（同一套 TexSize / FillK / 配色 / 逐层范围），
//   否则"看的是另一张图"，那这个通道就变成了假证据。
//   本文件的常量与 `unity/Assets/Scripts/Runtime/TruckScreens.cs` 一一对应。
//
// 用法：node tools/ui-screen-preview.mjs   → 输出到 tmp/screens/
import fs from 'node:fs';
import zlib from 'node:zlib';

const TexSize = 128;
const level = JSON.parse(fs.readFileSync('unity/Assets/Levels/asylum_v1.json', 'utf8'));
const rooms = level.rooms;

// ── 与 C# 同口径的绘制原语 ──
const px = new Uint8Array(TexSize * TexSize * 3);
const idx = (x, y) => ((TexSize - 1 - y) * TexSize + x) * 3;   // Unity 纹理原点在左下
function clear(r, g, b) { for (let i = 0; i < px.length; i += 3) { px[i] = r; px[i + 1] = g; px[i + 2] = b; } }
function rect(x, y, w, h, r, g, b) {
  for (let yy = y; yy < y + h; yy++) { if (yy < 0 || yy >= TexSize) continue;
    for (let xx = x; xx < x + w; xx++) { if (xx < 0 || xx >= TexSize) continue;
      const i = idx(xx, yy); px[i] = r; px[i + 1] = g; px[i + 2] = b; } }
}
const lerp = (a, b, t) => a + (b - a) * t;

// ── 照搬 DrawMap ──
function drawMap(floor, playerWing, sealedWing) {
  clear(8, 15, 13);
  let minX = 1e9, minZ = 1e9, maxX = -1e9, maxZ = -1e9;
  for (const r of rooms) {
    if (r.floor !== floor) continue;                 // 与 C# 同口径：只按当前楼层算
    minX = Math.min(minX, r.pos[0]); minZ = Math.min(minZ, r.pos[1]);
    maxX = Math.max(maxX, r.pos[0] + r.size[0]); maxZ = Math.max(maxZ, r.pos[1] + r.size[2]);
  }
  if (!(minX <= maxX)) { minX = minZ = 0; maxX = maxZ = 1; }
  const spanX = Math.max(maxX - minX, 1), spanZ = Math.max(maxZ - minZ, 1);
  const scaleX = 0.96 / spanX, scaleZ = 0.96 / spanZ;   // 各轴独立拉满（与 C# 同口径）
  const ox = (1 - spanX * scaleX) * 0.5, oz = (1 - spanZ * scaleZ) * 0.5;
  for (const r of rooms) {
    const X = Math.round((ox + (r.pos[0] - minX) * scaleX) * TexSize);
    const Y = Math.round((oz + (r.pos[1] - minZ) * scaleZ) * TexSize);
    const W = Math.max(1, Math.round(r.size[0] * scaleX * TexSize));
    const H = Math.max(1, Math.round(r.size[2] * scaleZ * TexSize));
    let c = r.lightZone === 'safe' ? [89, 191, 140]
          : r.lightZone === 'high-risk' ? [191, 115, 77] : [102, 158, 217];
    const here = r.floor === floor;
    if (!here) c = c.map(v => v * 0.35);
    if (r.evidencePoint && here) c = c.map((v, i) => lerp(v, [255, 242, 153][i], 0.45));
    if (here && playerWing && r.wing === playerWing) c = c.map((v, i) => lerp(v, 255, 0.35));
    if (here && sealedWing && r.wing === sealedWing) c = c.map((v, i) => lerp(v, [217, 38, 31][i], 0.55));
    rect(X, Y, W, H, c[0] | 0, c[1] | 0, c[2] | 0);
  }
  // 出入口绿色横线
  if (level.extraction) {
    for (const r of rooms) {
      if (r.id !== level.extraction.standard) continue;
      const X = Math.round((ox + (r.pos[0] - minX) * scaleX) * TexSize);
      const W = Math.max(2, Math.round(r.size[0] * scaleX * TexSize));
      const Y = Math.round((oz + (r.pos[1] - minZ) * scaleZ) * TexSize);
      rect(X, Y - 1, W, 2, 51, 255, 89);
    }
  }
}

function writePNG(path, w, h) {
  const raw = Buffer.alloc((w * 3 + 1) * h);
  for (let y = 0; y < h; y++) {
    raw[y * (w * 3 + 1)] = 0;
    for (let x = 0; x < w; x++) {
      const src = ((h - 1 - y) * w + x) * 3;          // 翻回 PNG 的上到下
      const dst = y * (w * 3 + 1) + 1 + x * 3;
      raw[dst] = px[src]; raw[dst + 1] = px[src + 1]; raw[dst + 2] = px[src + 2];
    }
  }
  const chunk = (type, data) => {
    const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
    const td = Buffer.concat([Buffer.from(type), data]);
    const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(td) >>> 0);
    return Buffer.concat([len, td, crc]);
  };
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4);
  ihdr[8] = 8; ihdr[9] = 2; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
  const png = Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw)), chunk('IEND', Buffer.alloc(0)),
  ]);
  fs.writeFileSync(path, png);
}
let T = null;
function crc32(buf) {
  if (!T) { T = []; for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; T[n] = c >>> 0; } }
  let c = 0xffffffff;
  for (const b of buf) c = T[(c ^ b) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

fs.mkdirSync('tmp/screens', { recursive: true });
for (const [tag, floor, pw, sw] of [['f0_ward', 0, 'ward', null], ['f0_sealed', 0, 'ward', 'ward'], ['f1_chapel', 1, 'chapel', null], ['f2', 2, 'reception', null]]) {
  drawMap(floor, pw, sw);
  writePNG(`tmp/screens/map_${tag}.png`, TexSize, TexSize);
  console.log('写出 map_' + tag + '.png');
}
