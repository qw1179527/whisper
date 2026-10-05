// 主界面 3D 空间**取景与摆位**修正 —— 这才是"主界面也不对"的根因。
//
// ## 事实链
// · 主界面建房几何在**世界原点**附近（`room` 挂在 `_root` 下，`_root` 挂在组合根下）；
// · 建房后 `Boot` → `TryBuildGeometry` 会把**相机放到关卡出生点**（入口安全区，坐标 x≈2.7 z≈1.2）；
// · 于是相机在走廊里，视线方向由关卡生成器决定 → **主界面的房间在别处，玩家看到的是关卡走廊**。
// · 0.1.32 截图里那片均匀灰渐变 = 关卡走廊的一面墙（不是主界面房间）。
//
// ## 修法（不动关卡代码，只在主界面里做）
// 主界面建好后**把相机搬到主界面房间里、并对准走廊深处**；开始对局时再由
// `StartMatch` → `TryBuildGeometry` 把相机放回出生点（那是既有逻辑，不必改）。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
let s = fs.readFileSync(P, 'utf8');
const log = [];

// ① Build 末尾（BuildRoom/BuildFlashlight/BuildOptions 之后）把相机摆到房间里
if (!s.includes('PlaceMenuCamera')) {
  s = s.replace('            // 开局先保证有一只在场 —— 否则玩家可能几秒内什么都看不到，以为坏了\n            TrySpawnGhost(true);',
`            // 开局先保证有一只在场 —— 否则玩家可能几秒内什么都看不到，以为坏了
            TrySpawnGhost(true);
            PlaceMenuCamera();`);
  s = s.replace('        // ───────────────────────── 3D 空间 ─────────────────────────',
`        /// <summary>把相机搬进主界面房间，并看向走廊深处。</summary>
        /// <remarks>
        /// 为什么必须显式搬：`Boot` 之后相机是由**关卡几何**（`TryBuildGeometry`）放到出生点的，
        /// 而主界面的房间在别处 —— 不搬的话玩家看到的是关卡走廊，主界面 3D 空间等于不存在
        /// （0.1.32 截图里那片均匀灰渐变就是走廊墙面）。
        /// 开始对局时 `StartMatch` 会重新走几何流程，相机会被放回出生点，所以这里不必还原。
        /// </remarks>
        void PlaceMenuCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                // 相机不在场景里（少见）：创建一个最小可用相机，保证"看得见"优先于"讲究"
                var cgo = new GameObject("MenuCamera");
                cgo.transform.SetParent(_root, false);
                cam = cgo.AddComponent<Camera>();
                cgo.tag = "MainCamera";
            }
            float w = RoomSizeM.x, h = RoomSizeM.y, d = RoomSizeM.z;
            // 站位：房间 +X 端、离地 1.6m（人眼）、略偏南侧
            cam.transform.position = new Vector3(w * 0.46f, 1.62f, d * 0.06f);
            // 朝 -X 看（走廊深处），带一点俯角看向远端门洞
            cam.transform.rotation = Quaternion.LookRotation(new Vector3(-1f, -0.06f, 0f));
            cam.fieldOfView = 58f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 80f;
            // 背景**不要**用天空盒/纯色把房间糊掉：给一个很暗的冷色，让远端门洞"更黑"才立得住
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.015f, 0.018f, 0.026f);
        }

        // ───────────────────────── 3D 空间 ─────────────────────────`);
  log.push('  ✓ PlaceMenuCamera');
}

// ② 房间尺寸改成"更像走廊"：长 16 → 26，并让门框分布在相机前方
s = s.replace('        public Vector3 RoomSizeM = new Vector3(16f, 3.2f, 5f);',
              '        public Vector3 RoomSizeM = new Vector3(26f, 3.4f, 6f);   // 走廊式：更长更宽，门框才有纵深');
log.push('  ✓ 房间加大');

// ③ 门框/壁灯的 X 分布跟着加长（原先是按 w*0.18 步进，w 变大后自然拉开，这里只调起点与间距占比）
s = s.replace('                float bx = w * 0.40f - i * (w * 0.18f);',
              '                float bx = w * 0.34f - i * (w * 0.145f);   // 门框沿走廊均匀分布，起点在相机前方');
s = s.replace('                SceneMaterials.Box(room, "MenuDoorFrame", new Vector3(bx, h * 0.5f, d * 0.5f - 0.06f),\n                    new Vector3(0.12f, h * 0.82f, 0.12f), wallMat);',
              '                SceneMaterials.Box(room, "MenuDoorFrame", new Vector3(bx, h * 0.5f, d * 0.5f - 0.10f),\n                    new Vector3(0.14f, h * 0.84f, 0.14f), wallMat);');
log.push('  ✓ 门框分布');

fs.writeFileSync(P, s, 'utf8');
console.log(log.join('\n'));
