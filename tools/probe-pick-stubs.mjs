// 探测 3D 拾取所需的桩成员。用**实际调用写法**做探针（子串探测会给出假结论：
// 例如 `GetComponent<Collider>` 在桩里可能写作 `GetComponent<T>()` 泛型形式）。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'native/unity-stubs/UnityStubs.cs');
const s = fs.readFileSync(P, 'utf8');

// 我打算在代码里写什么，就探什么
const NEED = [
  ['ScreenPointToRay', 'Camera.ScreenPointToRay(Vector3)'],
  ['public static float Clamp01', 'Mathf.Clamp01'],
  ['public static float SmoothStep', 'Mathf.SmoothStep'],
  ['public static Vector3 Lerp', 'Vector3.Lerp'],
  ['public static Quaternion Slerp', 'Quaternion.Slerp'],
  ['public T GetComponent<T>', 'Component.GetComponent<T>() 泛型'],
  ['public T AddComponent<T>', 'GameObject.AddComponent<T>() 泛型'],
];
let bad = 0;
for (const [sig, desc] of NEED) {
  const ok = s.includes(sig);
  if (!ok) bad++;
  console.log(`  ${ok ? '✓' : '✗'} ${desc}   [探针 ${sig}]`);
}
console.log(bad ? `\n  → 需补 ${bad} 项` : '\n  ✓ 全部齐全');
process.exit(0);   // 不靠退出码传递结论（bash 管道会吃掉它），结论看输出
