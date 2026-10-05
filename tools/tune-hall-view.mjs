// 大厅取景与菜单板亮度调优（0.1.60 真机截图：相机偏低、菜单板不够亮）。
// 依据：官方大厅「**主菜单板是视觉焦点/最亮点**」，而 0.1.60 截图里板子偏暗、且被下沿切掉。
// **本文件不得出现反引号**。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/HallScene.cs');
let s = fs.readFileSync(P, 'utf8');
const log = [];

// ① 菜单板加大加亮 + 双灯
s = s.replace('            float by = 2.35f;\n            float bw = 6.4f, bh = 3.0f;',
              '            float by = 2.55f;\n            float bw = 7.2f, bh = 3.4f;');
s = s.replace('var boardMat = Mat(new Color(0.42f, 0.36f, 0.28f), 0.75f, MaterialFamily.Wood);',
              'var boardMat = Mat(new Color(0.52f, 0.44f, 0.34f), 0.78f, MaterialFamily.Wood);');
s = s.replace('var frameMat = Mat(new Color(0.24f, 0.24f, 0.26f), 0.55f, MaterialFamily.Metal);',
              'var frameMat = Mat(new Color(0.26f, 0.26f, 0.29f), 0.55f, MaterialFamily.Metal);');
s = s.replace(`            Light(_root, "MenuSpot", new Vector3(0f, h - 0.9f, z + 1.6f), LightType.Spot,
                  new Color(1.0f, 0.96f, 0.88f), 3.4f, 9f, true);`,
`            // 官方明确"**主菜单板是最亮点**"。第一版单盏 3.4 强度在真机截图里仍偏暗
            // （仓库整体很暗 + 点光衰减快）→ 改**双灯**：射灯打板面 + 近距点光补亮纸片。
            // 为什么不干脆整体提亮：那会毁掉仓库的暗调（用户要"画面暗调"）。
            Light(_root, "MenuSpot", new Vector3(0f, h - 1.0f, z + 2.2f), LightType.Spot,
                  new Color(1.0f, 0.96f, 0.88f), 6.5f, 11f, true);
            Light(_root, "MenuFill", new Vector3(0f, by + 1.1f, z + 1.1f), LightType.Point,
                  new Color(0.95f, 0.95f, 0.92f), 2.4f, 5.5f, false);`);
log.push('  ✓ 菜单板加大加亮（双灯）');

// ② 相机抬高并上仰，让菜单板落在画面中心（0.1.60 截图里板子下沿被切）
s = s.replace('            ViewPos = new Vector3(0f, 1.68f, LengthM * 0.22f);\n            ViewLookAt = new Vector3(0f, 2.15f, -LengthM * 0.5f);',
`            // 【取景依据】0.1.60 真机截图：相机在 1.68m 平视 → 菜单板（中心 2.55m）落在画面上沿之外，
            // 玩家看不到"主菜单板"这个焦点。所以站位后移 + 微仰视，把板子放到画面中心偏上。
            ViewPos = new Vector3(0f, 1.72f, LengthM * 0.30f);
            ViewLookAt = new Vector3(0f, 2.55f, -LengthM * 0.5f);`);
log.push('  ✓ 相机取景（后移 + 仰视看菜单板）');

// ③ 大厅整体亮度微调：吊灯抬一点，让"能看清结构"而不是"全黑"
s = s.replace('new Color(1.0f, 0.90f, 0.76f), 0.85f, 8.5f, true);',
              'new Color(1.0f, 0.90f, 0.76f), 1.35f, 9.5f, true);');
s = s.replace('key.intensity = 0.22f;', 'key.intensity = 0.30f;');
log.push('  ✓ 吊灯/主光微提（结构可辨）');

fs.writeFileSync(P, s, 'utf8');
console.log(log.join('\n'));
