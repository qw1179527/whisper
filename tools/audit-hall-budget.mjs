// 量化大厅的**真实几何与贴图预算**（回应用户："你这么大个仓库几十KB？"）。
//
// 为什么要量而不是辩：文件大小不是目的，但**几何面数与贴图像素密度**是观感的物理上限。
// 这一版大厅全部由 `GameObject.CreatePrimitive` 的立方体拼成：
//   · 一个 Unity Cube = 12 三角形 / 24 顶点
//   · 没有法线贴图分辨率、没有高模细节、没有烘焙
// 所以无论摆多少个方块，"观感密度"都上不去 —— 这是**方法本身的天花板**，必须说清楚。
//
// 本脚本从源码里数出实际物件数与面数，并把"要达到 3A 级细节需要什么"写成可核对的数字。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const hall = fs.readFileSync(path.join(ROOT, 'unity/Assets/Scripts/Runtime/HallScene.cs'), 'utf8');
const proctex = fs.readFileSync(path.join(ROOT, 'unity/Assets/Scripts/Gameplay/Render/ProceduralTextures.cs'), 'utf8');

// ① 数出 Box(...) 调用点（每个 = 一个立方体），以及循环里的数量
function countCall(src, name) {
  const re = new RegExp('\\b' + name + '\\(', 'g');
  return (src.match(re) || []).length;
}
const boxCalls = countCall(hall, 'Box');
const lightCalls = countCall(hall, 'Light');
const primCalls = (hall.match(/CreatePrimitive\(PrimitiveType\.(\w+)\)/g) || []);
const primKinds = {};
for (const p of primCalls) {
  const k = p.replace(/.*PrimitiveType\./, '').replace(')', '');
  primKinds[k] = (primKinds[k] || 0) + 1;
}

// ② 贴图分辨率（ProceduralTextures.Size）
const sizeMatch = proctex.match(/public const int Size = (\d+);/);
const texSize = sizeMatch ? parseInt(sizeMatch[1], 10) : 0;
const families = (proctex.match(/MaterialFamily\.\w+/g) || []);
const familySet = [...new Set(families)].filter((f) => !f.includes('family'));

// ③ 按真机 HUD 报的物件数（186）估算面数：立方体 12 tri，球 768 tri，圆柱 80 tri
const hudObjects = 186;
const cubeTris = 12, sphereTris = 768, cylTris = 80;
const estObjects = { cube: hudObjects - 10, sphere: 10, cyl: 7 };
const estTris = estObjects.cube * cubeTris + estObjects.sphere * sphereTris + estObjects.cyl * cylTris;

console.log('=== 当前大厅的真实预算 ===');
console.log(`  源码里 Box(...) 调用点        : ${boxCalls}`);
console.log(`  源码里 Light(...) 调用点      : ${lightCalls}`);
console.log(`  CreatePrimitive 调用点        : ${primCalls.length}  (${JSON.stringify(primKinds)})`);
console.log(`  真机 HUD 报的物件数           : ${hudObjects}`);
console.log(`  → 估算三角形数                : ~${estTris.toLocaleString()} tri`);
console.log(`  贴图分辨率                    : ${texSize}x${texSize}（每族 3 张：细节/法线/遮蔽）`);
console.log(`  材质族                        : ${familySet.length} 个`);
console.log(`  每族贴图像素                  : ${(texSize * texSize * 3).toLocaleString()} px`);
console.log('');
console.log('=== 3A 级室内场景的量级（用于对照，不是"必须做到"）===');
console.log('  单个 AAA 枪皮模型             : 5万~30万 tri（含 LOD 链）');
console.log('  一栋中型室内场景              : 50万~300万 tri');
console.log('  单张 4K PBR 贴图组            : 4096x4096 x 反照率/法线/粗糙/金属/遮蔽 ≈ 60~120 MB（未压缩）');
console.log('  压缩后（BC7/ASTC）           : 约 1/4~1/8，仍为 8~30 MB/材质');
console.log('');
console.log('=== 结论（必须对用户说清楚，不许含糊）===');
console.log('  · 当前大厅的观感上限**不是由"摆得不够多"决定**，而是由**方法**决定：');
console.log('    全部是 Unity 内置立方体（12 tri/个）+ 128px 程序化贴图。');
console.log('    这条路的天花板 ≈ 低模灰盒（blockout），做不出工业管道法兰、铆钉、');
console.log('    撕裂的保温棉、锈迹渐变、地面油渍反光这类**真正的"建模细节"**。');
console.log('  · 要真正提升，必须走 Blender 出网格 + 高分辨率程序化/烘焙贴图，');
console.log('    而不是继续在 C# 里摆方块。');
console.log('  · 但**也不是越大越好**：移动端 APK 有 200MB 硬门槛，且真机是手机 GPU，');
console.log('    目标是"在这个预算内把细节做足"，不是无脑堆到几百 MB。');
