// 变异验证：`unity-syntax-check` 的 CS0103 新增判据（"名字在项目里声明为类型但本文件没 using"）
//
// 动机：`TemperatureSystem.cs` 用了 `MiniJson`（声明在 `Gameplay/Level/MiniJson.cs`）却没写 using，
// 旧判据归类为"Unity 缺失（允许）"→ 本机绿灯、真 Unity 才炸 CS0103。
//
// 做法：临时往一个**引用 UnityEngine 的既有文件**里插一行引用项目类型（不带 using），
// 跑预检，断言它必须判红；然后还原，断言恢复绿。全程不改动其它文件。
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';

const VICTIM = 'unity/Assets/Scripts/Runtime/PlayerController.cs';
const MARK = '/* VARIANT-CS0103 */';

const original = fs.readFileSync(VICTIM, 'utf8');
if (original.includes(MARK)) { console.error('残留变异标记，先清理'); process.exit(2); }

// 在文件里插一个方法体，引用一个**项目类型**但不给 using（PlayerController 的 using 里没有 Whisper.Gameplay.Level）
const probe = `
        ${MARK}
        void __VariantProbe()
        {
            // MiniJson 声明在 Whisper.Gameplay.Level 命名空间；本文件没有它的 using
            var x = MiniJson.Parse("{}");
            UnityEngine.Debug.Log(x);
        }
`;
// 插到最后一个 '}' 之前（类体内）
const idx = original.lastIndexOf('}');
const mutated = original.slice(0, idx) + probe + original.slice(idx);
fs.writeFileSync(VICTIM, mutated, 'utf8');

function run() {
  try {
    const out = execFileSync('bash', ['tools/unity-syntax-check.sh'], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
    return { rc: 0, out };
  } catch (e) {
    return { rc: e.status === undefined ? 1 : e.status, out: (e.stdout || '') + (e.stderr || '') };
  }
}

let result;
try {
  const r = run();
  // 判据看**退出码 + 「真实错误」分类**（错误行本身不逐字打印缺失的名字，所以不苛求点名）。
  // rc !== 0 是「有牙」的硬信号：旧判据下这类错误会被归入 [允许]，rc 为 0。
  const caught = r.rc !== 0 && /真实错误/.test(r.out);
  console.log('  [变异] 插入「项目类型缺 using」后：rc=' + r.rc + ' · 判红=' + caught);
  console.log('  [变异] 关键输出行：');
  for (const line of r.out.split('\n')) if (/MiniJson|真实错误/.test(line)) console.log('      ' + line.trim().slice(0, 150));
  result = { caught, rc: r.rc };
} finally {
  fs.writeFileSync(VICTIM, original, 'utf8');
}

// 还原后必须回绿
const r2 = run();
const restored = r2.rc === 0 && /无与 Unity 缺失无关的真实错误/.test(r2.out);
console.log('  [还原] rc=' + r2.rc + ' · 恢复绿=' + restored);

const src2 = fs.readFileSync(VICTIM, 'utf8');
console.log('  [残留] 变异标记仍在=' + src2.includes(MARK));

if (!result.caught) { console.error('  ✗ 判据没有牙：这类错误仍会被放行'); process.exit(1); }
if (!restored || src2.includes(MARK)) { console.error('  ✗ 还原不干净'); process.exit(1); }
console.log('  ✓ 判据有牙且可自愈');
