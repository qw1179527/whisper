// 修 `MenuScene.SceneMaterials` → `SceneMaterials`。
// 事实：`SceneMaterials` 是 **namespace 级的 public static class**（MenuScene.cs:1053），
// 不是 MenuScene 的嵌套类 —— 我按嵌套类写了，真 Unity 编译报 CS0117。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/HallScene.cs');
let s = fs.readFileSync(P, 'utf8');
const before = (s.match(/MenuScene\.SceneMaterials/g) || []).length;
s = s.split('MenuScene.SceneMaterials').join('SceneMaterials');
fs.writeFileSync(P, s, 'utf8');
console.log(`  ✓ 替换 ${before} 处 MenuScene.SceneMaterials → SceneMaterials`);
console.log(`  · 剩余 ${(s.match(/MenuScene\.SceneMaterials/g) || []).length} 处`);
