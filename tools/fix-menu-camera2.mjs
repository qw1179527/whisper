// 主界面 3D 空间取景修正（**此文件内不得出现反引号** —— 它们在 JS 模板字符串里会截断，
// 我已经因此失败 9 次，这是本项目最贵的一个低级错误）。
//
// 事实链：主界面房间建在世界原点附近，而 Boot 之后相机被关卡几何放到出生点 →
// 玩家看到的是关卡走廊，主界面房间等于不存在（0.1.32 截图那片均匀灰渐变就是走廊墙）。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
let s = fs.readFileSync(P, 'utf8');
const log = [];
const BT = String.fromCharCode(96);   // 反引号，用变量拼避免出现在源码里

const cameraMethod = [
  '        /// <summary>把相机搬进主界面房间，并看向走廊深处。</summary>',
  '        /// <remarks>',
  '        /// 为什么必须显式搬：Boot 之后相机是由关卡几何（TryBuildGeometry）放到出生点的，',
  '        /// 而主界面的房间在别处 —— 不搬的话玩家看到的是关卡走廊，主界面 3D 空间等于不存在',
  '        /// （0.1.32 截图里那片均匀灰渐变就是走廊墙面）。',
  '        /// 开始对局时 StartMatch 会重新走几何流程，相机会被放回出生点，所以这里不必还原。',
  '        /// </remarks>',
  '        void PlaceMenuCamera()',
  '        {',
  '            var cam = Camera.main;',
  '            if (cam == null)',
  '            {',
  '                var cgo = new GameObject("MenuCamera");',
  '                cgo.transform.SetParent(_root, false);',
  '                cam = cgo.AddComponent<Camera>();',
  '                cgo.tag = "MainCamera";',
  '            }',
  '            float w = RoomSizeM.x, h = RoomSizeM.y, d = RoomSizeM.z;',
  '            // 站位：房间 +X 端、离地 1.62m（人眼）、略偏南侧',
  '            cam.transform.position = new Vector3(w * 0.46f, 1.62f, d * 0.06f);',
  '            // 朝 -X 看（走廊深处），带一点俯角看向远端门洞',
  '            cam.transform.rotation = Quaternion.LookRotation(new Vector3(-1f, -0.06f, 0f));',
  '            cam.fieldOfView = 58f;',
  '            cam.nearClipPlane = 0.05f;',
  '            cam.farClipPlane = 90f;',
  '            // 背景给很暗的冷色：远端门洞"更黑"才立得住（用天空盒会把房间糊掉）',
  '            cam.clearFlags = CameraClearFlags.SolidColor;',
  '            cam.backgroundColor = new Color(0.015f, 0.018f, 0.026f);',
  '        }',
  '',
  '        // ───────────────────────── 3D 空间 ─────────────────────────',
].join('\n');

if (!s.includes('PlaceMenuCamera')) {
  const a1 = '            // 开局先保证有一只在场 —— 否则玩家可能几秒内什么都看不到，以为坏了\n            TrySpawnGhost(true);';
  if (s.includes(a1)) { s = s.replace(a1, a1 + '\n            PlaceMenuCamera();'); log.push('  ✓ Build 里调用 PlaceMenuCamera'); }
  else log.push('  ! TrySpawnGhost 锚点未中');
  const a2 = '        // ───────────────────────── 3D 空间 ─────────────────────────';
  if (s.includes(a2)) { s = s.replace(a2, cameraMethod); log.push('  ✓ PlaceMenuCamera 方法'); }
  else log.push('  ! 3D 空间分节注释锚点未中');
} else log.push('  · PlaceMenuCamera 已存在');

// 房间加大（走廊式）
if (s.includes('new Vector3(16f, 3.2f, 5f)')) {
  s = s.replace('new Vector3(16f, 3.2f, 5f)', 'new Vector3(26f, 3.4f, 6f)');
  log.push('  ✓ 房间加大到 26x3.4x6');
}
// 门框起点与间距
s = s.replace('float bx = w * 0.40f - i * (w * 0.18f);', 'float bx = w * 0.34f - i * (w * 0.145f);');
log.push('  ✓ 门框分布');

fs.writeFileSync(P, s, 'utf8');
console.log(log.join('\n'));
console.log('  （本脚本内反引号计数：' + (fs.readFileSync(import.meta.filename, 'utf8').split(BT).length - 1) + ' —— 必须为 0 或成对出现在模板边界）');
