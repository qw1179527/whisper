#!/usr/bin/env node
/**
 * wire-free-look-camera.mjs — 把大厅自由视角接进 MenuScene（P0）
 *
 * ## 改什么
 * `MenuScene.UpdateBoardCamera(dt)` 原先**每帧直接写相机位姿**，在"自由位"与"看板位"之间插值 →
 * 玩家不能走、不能转头（用户：「我要的自由视角呢，恐鬼症官方是这么设定的？」）。
 *
 * 改为：
 * 1. 建一个 `LobbyCamera`（挂在本组件所在 GameObject 上，用它驱动 `_cam`）；
 * 2. `Configure(...)` 用 `HallScene.WidthM/LengthM` 算出**墙内可行走矩形**；
 * 3. **自由态**：`UpdateBoardCamera` 直接 `return`，相机完全交给 `LobbyCamera`（可自由行走+环视）；
 * 4. **操作视角**：不再把相机平移过去，而是**只让朝向对准菜单板**（官方：按空格/点击进入"操作界面"，
 *    玩家仍站在原处）；退出时恢复自由。
 *
 * ## 为什么保留 `UpdateBoardCamera` 这个方法名
 * 它被 `Update()` 与其他处调用；重写其内部实现比改多处调用点风险小。
 *
 * ## 官方依据
 * 用户《补充说明》第 5 行：「大厅的3D模型包含完整的碰撞体（Collider），**玩家可以在其中自由行走**」；
 * 「主菜单板…按 空格键 或鼠标左键点击即可进入操作界面，再按一次 空格键 或 Esc 键则可退出」——
 * **行走与"进操作界面"是两件事**。
 *
 * 用法：node tools/wire-free-look-camera.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
const checkOnly = process.argv.includes('--check');
const log = [];
const fail = (m) => { console.error('[free-look] ✗ ' + m); process.exit(1); };

const raw = fs.readFileSync(FILE, 'utf8');
if (raw.includes('_lobbyCam')) { console.log('[free-look] 已接线，跳过'); process.exit(0); }
let s = raw;
const sub = (from, to, what) => {
  const n = s.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  s = s.replace(from, to);
  log.push('  ✓ ' + what);
};

// ── ① 字段 ────────────────────────────────────────────────────────────
sub('        HallScene _hall;',
  [
    '        HallScene _hall;',
    '',
    '        /// <summary>大厅第一人称自由行走 + 自由环视（用户要求；官方原文见 LobbyCamera 类注释）。</summary>',
    '        LobbyCamera _lobbyCam;',
  ].join('\n'),
  '① 新增 _lobbyCam 字段');

// ── ② 建相机控制器（大厅建好之后）────────────────────────────────────
sub('            _hall = new HallScene(_root, _cam);',
  [
    '            _hall = new HallScene(_root, _cam);',
    '',
    '            // ── 大厅自由视角（P0）────────────────────────────────────────────',
    '            // 官方：玩家可在带完整碰撞体的大厅里**自由行走**；"进操作界面"是按键/点击触发的 UI 状态。',
    '            // 这里给 LobbyCamera 一个**墙内收 0.4m 的可行走矩形**（大厅是规则矩形仓库；真实道具碰撞',
    '            // 留给后续"道具物理"那一轮）。起点放在大厅靠前、面向菜单板（玩家一进来就面对虚拟焦点）。',
    '            {',
    '                float m = 0.4f;',
    '                float hx = _hall.WidthM * 0.5f - m;',
    '                float hz = _hall.LengthM * 0.5f - m;',
    '                _lobbyCam = gameObject.AddComponent<LobbyCamera>();',
    '                // 起点：靠前墙（-Z）一侧、略偏左，朝向 +Z（面向大厅纵深与菜单板）',
    '                _lobbyCam.Configure(-hx, hx, -hz, hz,',
    '                    new Vector3(0f, LobbyCamera.EyeHeightM, -hz + 1.2f), 0f);',
    '                _lobbyCam.Apply();',
    '            }',
  ].join('\n'),
  '② 建 LobbyCamera 并配置可行走矩形');

// ── ③ 重写 UpdateBoardCamera：自由态 return，操作视角只转向 ──────────
{
  const startMark = '        void UpdateBoardCamera(float dt)';
  const i = s.indexOf(startMark);
  if (i < 0) fail('未找到 UpdateBoardCamera');
  const brace = s.indexOf('{', i);
  if (brace < 0) fail('未找到 UpdateBoardCamera 左括号');
  let depth = 0, end = -1;
  for (let j = brace; j < s.length; j++) {
    if (s[j] === '{') depth++;
    else if (s[j] === '}') { depth--; if (depth === 0) { end = j; break; } }
  }
  if (end < 0) fail('未找到 UpdateBoardCamera 结束');
  const body = [
    '        void UpdateBoardCamera(float dt)',
    '        {',
    '            if (_cam == null || _hall == null || _lobbyCam == null) return;',
    '',
    '            // 【P0 自由视角 · 用户 2026-10-05】',
    '            // 官方大厅形态（用户《补充说明》第 5 行原文）：玩家可以**在带完整碰撞体的大厅里自由行走**；',
    '            // 「按 空格键 或鼠标左键点击即可进入操作界面，再按一次 空格键 或 Esc 键则可退出」——',
    '            // 即**行走与"进操作界面"是两件事**。',
    '            //',
    '            // 旧实现在这里**每帧直接写相机位姿**，在"自由位/看板位"之间插值 → 玩家不能走、不能转头',
    '            // （用户："我要的自由视角呢"）。现在：',
    '            //   · 自由态：本方法**直接 return**，相机完全交给 LobbyCamera（可走 + 可环视）；',
    '            //   · 操作视角：不把相机平移过去，只**原地转向对准菜单板**（玩家仍站在原地）。',
    '            if (!_boardMode)',
    '            {',
    '                _lobbyCam.Active = true;   // 自由行走 + 环视',
    '                return;                    // ← 绝不写相机 transform',
    '            }',
    '',
    '            // 操作视角：停住脚步，只转向菜单板（平滑收敛，避免抖动）',
    '            _lobbyCam.Active = false;',
    '            var b = _hall.MenuBoardPos;',
    '            var eye = _lobbyCam.Position;',
    '            var to = new Vector3(b.x - eye.x, 0f, b.z - eye.z);',
    '            if (to.sqrMagnitude < 0.0001f) return;',
    '            float wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;',
    '            float curYaw = _lobbyCam.YawDeg;',
    '            float diff = Mathf.DeltaAngle(curYaw, wantYaw);',
    '            if (Mathf.Abs(diff) < 0.05f)',
    '            {',
    '                // 到位后**一个字节都不动**（此前抖屏的教训：永不静止 = 细颤）',
    '                return;',
    '            }',
    '            float step = Mathf.Clamp(diff, -220f * Mathf.Max(dt, 0.0001f), 220f * Mathf.Max(dt, 0.0001f));',
    '            _lobbyCam.Teleport(eye, curYaw + step, 0f);',
    '            _lobbyCam.Apply();',
    '        }',
  ].join('\n');
  s = s.slice(0, i) + body + s.slice(end + 1);
  log.push('  ✓ ③ 重写 UpdateBoardCamera（自由态不写相机 / 操作视角只转向）');
}

if (!checkOnly) {
  const bak = FILE + '.bak-freelook';
  if (!fs.existsSync(bak)) fs.writeFileSync(bak, raw, 'utf8');
  fs.writeFileSync(FILE, s, 'utf8');
}
console.log('[free-look] 大厅自由视角接线' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
