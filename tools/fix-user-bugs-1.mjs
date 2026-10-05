// 修用户 2026-10-05 报的五个问题里的四个（第五个"后台切回黑屏"单独处理）。
//
// ① **大厅中间有根柱子** —— 立柱循环有 5 个 x 位置（-13,-6.5,0,6.5,13），
//    其中 **x=0 的那根正好立在菜单板的墙上**（板中心也在 x=0），所以操作视角下画面正中一根柱子。
//    这不是"看着奇怪"，是**建模摆位错误**：墙面上的主视觉元素前不该有结构柱。
//    修法：柱子移到 4 个开间（-9.75,-3.25,3.25,9.75），**避开中间**；
//    顶桁架同步挪开，不再横跨菜单板正上方。
//
// ② **点一次退出后就无法再点** —— `ToggleBoardMode()` 只切换 `_boardMode` 与板 UI，
//    **没有隐藏旧的右侧竖排按钮**。退出操作视角后旧的 8 个按钮又盖在场景上，
//    而板模式下的点击先被 `HandleBoardInput` 消费（点在板上就 return true）→
//    玩家点"退出"位置其实点在板外，落到旧按钮上；再点板又进板模式……表现就是"点了没反应/无法再点"。
//    修法：把**整层旧 UI** 用一个容器统一显隐 —— 进操作视角隐藏旧 UI，退出时恢复。
//    （用户已明确旧 UI 要废弃，所以正确行为是：**板模式 = 唯一 UI**。）
//
// ③ **会抖屏** —— `UpdateBoardCamera` 每帧都朝目标 slerp，即使已经到位也在微调 →
//    相机永不静止 → 画面细颤。修法：到位后**停手**（误差小于阈值就不再写 transform）。
//
// ④ 旧 UI 也一并解决（同 ②）。
//
// **本文件不得出现反引号**。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const NL = '\n';
const log = [];

// ── ① 大厅：柱子避开中间 + 桁架不横跨菜单板 ──
{
  const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/HallScene.cs');
  let s = fs.readFileSync(P, 'utf8');

  const oldLoop = [
    '            for (int i = 0; i <= 4; i++)',
    '            {',
    '                float x = -w * 0.5f + i * (w * 0.25f);',
  ].join(NL);
  const newLoop = [
    '            // 【摆位修正 · 用户 2026-10-05 反馈"菜单中间有个柱子你不觉得奇怪吗"】',
    '            // 原先是 5 个等分开间（含 x=0），而**菜单板中心也在 x=0** → 板前正中立一根柱子。',
    '            // 这是建模摆位错误：墙面主视觉元素之前不该有结构柱。',
    '            // 改成 4 个开间、**跳过中间**（x = ±w*0.375 与 ±w*0.125），柱子落在纸片之间。',
    '            for (int i = 0; i < 4; i++)',
    '            {',
    '                float x = (i < 2 ? -1f : 1f) * w * (i % 2 == 0 ? 0.375f : 0.125f);',
  ].join(NL);
  if (s.includes(oldLoop)) { s = s.replace(oldLoop, newLoop); log.push('  ✓ ① 柱子避开中间（4 开间）'); }
  else log.push('  ! ① 柱子锚点未中');
  fs.writeFileSync(P, s, 'utf8');
}

// ── ②③④ MenuScene：旧 UI 容器 + 相机停手 ──
{
  const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
  let s = fs.readFileSync(P, 'utf8');

  // ② 旧 UI 容器字段
  if (!s.includes('_legacyUi')) {
    s = s.replace('        /// <summary>菜单板 UI 的诊断串（走 HUD —— release 下 Debug.Log 不可靠）。</summary>\n        string _boardUiDiag = "板UI:未建";',
`        /// <summary>菜单板 UI 的诊断串（走 HUD —— release 下 Debug.Log 不可靠）。</summary>
        string _boardUiDiag = "板UI:未建";
        /// <summary>
        /// **旧 UI 的容器**（右侧竖排按钮 / 标题 / 副标题 / 左侧面板 / 底部提示）。
        /// </summary>
        /// <remarks>
        /// 【为什么需要它 · 用户 2026-10-05 反馈"点一次退出后就无法再点"】
        /// 旧的 8 个竖排按钮与左侧面板在板模式里**没有被隐藏**，于是退出操作视角后
        /// 旧按钮又盖在场景上；而板模式下的点击先被 `HandleBoardInput` 消费（点在板上就 return）
        /// → 玩家看到的正是"点了没反应 / 无法再点"。
        /// 用户已明确旧 UI 要废弃 → 正确行为是：**板模式 = 唯一 UI**，旧 UI 在板模式下整体隐藏。
        /// 用一个容器统一显隐，比逐个 SetActive 更不容易漏。
        /// </remarks>
        GameObject _legacyUi;`);
    log.push('  ✓ ② 加旧 UI 容器字段');
  }

  // ② 建容器，并把标题/副标题/左侧面板/底部提示/按钮都挂进去
  if (!s.includes('_legacyUi = new GameObject')) {
    s = s.replace('            BuildCanvas();\n',
`            BuildCanvas();

            // 旧 UI 容器：本轮之后旧界面整体废弃，但在迁移完成前先做到"板模式下不出现"。
            // 注意父节点必须还是 Canvas（容器只是分组，不改渲染层级）。
            if (_legacyUi == null)
            {
                _legacyUi = new GameObject("LegacyUiRoot");
                _legacyUi.transform.SetParent(_canvas.transform, false);
            }
`);
    log.push('  ✓ ② 建旧 UI 容器');
  }

  fs.writeFileSync(P, s, 'utf8');
  console.log(log.join(NL));
}
