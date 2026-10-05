// 变异验证：`unity-syntax-check` 的 CS0103「下划线开头 → 真错误」判据。
//
// 动机（2026-10-04 真实事故）：`GameBootstrap.Temperature.cs` 用了未声明的 `_ghostRoom`，
// 而该文件不含 `using UnityEngine`、不是"目标文件" → 旧判据直接放行 →
// **本机绿灯、真 Unity 报 CS0103**，白烧一次出包 + 一次 Unity 测试回归（EditMode/PlayMode 双双 rc=1）。
//
// 做法：把一个 **partial 文件里已有的字段声明**临时删掉（该文件是非目标文件），
// 断言预检必须判红；然后还原，断言恢复绿。只动那一个文件、只动那一行。
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';

const VICTIM = 'unity/Assets/Scripts/Runtime/GameBootstrap.Temperature.cs';
const NEEDLE = 'Whisper.Gameplay.Monsters.GhostRoom _ghostRoom;';

const original = fs.readFileSync(VICTIM, 'utf8');
if (!original.includes(NEEDLE)) {
  console.error('  ✗ 靶子不存在（字段没声明？先确认文件现状）');
  process.exit(2);
}

function run() {
  try {
    const out = execFileSync('bash', ['tools/unity-syntax-check.sh'], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
    return { rc: 0, out };
  } catch (e) {
    return { rc: e.status === undefined ? 1 : e.status, out: (e.stdout || '') + (e.stderr || '') };
  }
}

let caught = false;
try {
  // 删掉字段声明（模拟"忘了声明"）
  fs.writeFileSync(VICTIM, original.replace(NEEDLE, ''), 'utf8');
  const r = run();
  caught = r.rc !== 0 && /_ghostRoom/.test(r.out);
  console.log('  [变异] 删掉字段声明后：rc=' + r.rc + ' · 判红并点名 _ghostRoom=' + caught);
  for (const line of r.out.split('\n')) if (/_ghostRoom|真实错误/.test(line)) console.log('      ' + line.trim().slice(0, 160));
} finally {
  fs.writeFileSync(VICTIM, original, 'utf8');
}

const r2 = run();
const restored = r2.rc === 0 && /无与 Unity 缺失无关的真实错误/.test(r2.out);
console.log('  [还原] rc=' + r2.rc + ' · 恢复绿=' + restored);
console.log('  [残留] 文件与原文一致=' + (fs.readFileSync(VICTIM, 'utf8') === original));

if (!caught) { console.error('  ✗ 判据没有牙：非目标文件里的 _字段 仍会被放行'); process.exit(1); }
if (!restored) { console.error('  ✗ 还原后没回绿'); process.exit(1); }
console.log('  ✓ 判据有牙且可自愈');
