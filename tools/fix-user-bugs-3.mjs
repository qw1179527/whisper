// 补两件上一批没做完的：
//   ① 大厅立柱避开中间（菜单板前不该有结构柱）—— 用户原话"菜单中间有个柱子你不觉得奇怪吗"
//   ② PostFx.RebuildBuffers()（MenuScene.RebuildRenderState 调了它，必须存在）
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const NL = '\n';
const log = [];

// ── ① 立柱 ──
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
    '            // 原先是 5 个等分开间（-13/-6.5/0/+6.5/+13），而**菜单板中心也在 x=0**',
    '            // → 画面正中立起一根结构柱，正好挡住主视觉。这是**建模摆位错误**，不是审美问题：',
    '            // 墙面主视觉元素之前不该有结构柱。',
    '            // 改成 4 个开间、**跳过中间**（x = ±w*0.375 与 ±w*0.125），柱子落在纸片与纸片之间。',
    '            for (int i = 0; i < 4; i++)',
    '            {',
    '                float x = (i < 2 ? -1f : 1f) * w * (i % 2 == 0 ? 0.375f : 0.125f);',
  ].join(NL);
  if (s.includes(oldLoop)) { s = s.replace(oldLoop, newLoop); log.push('  ✓ ① 立柱避开中间'); }
  else log.push('  ! ① 立柱锚点未中');

  // 顶桁架同步挪开（原来横跨 x=0，在菜单板正上方形成一根横梁）
  s = s.replace('Box(_root, "TrussZ", new Vector3(0f, h - 0.30f, z), new Vector3(w * 0.98f, 0.16f, 0.16f), metal, false);',
    '// 顶桁架原先是**一根横贯全宽**的梁，正好压在菜单板上方（取景里就是一条横杠）。\n'
    + '            // 拆成两段、让开中间 3.6m —— 与立柱同样的道理：别在主视觉正上方压东西。\n'
    + '            Box(_root, "TrussZ", new Vector3(-w * 0.30f, h - 0.30f, z), new Vector3(w * 0.34f, 0.16f, 0.16f), metal, false);\n'
    + '            Box(_root, "TrussZ", new Vector3(w * 0.30f, h - 0.30f, z), new Vector3(w * 0.34f, 0.16f, 0.16f), metal, false);');
  log.push('  ✓ ①b 顶桁架拆两段让开中间');

  fs.writeFileSync(P, s, 'utf8');
}

// ── ② PostFx.RebuildBuffers ──
{
  const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/PostFx.cs');
  let s = fs.readFileSync(P, 'utf8');
  if (!s.includes('public void RebuildBuffers')) {
    s = s.replace('        void EnsureBuffers(int w, int h)',
      ['        /// <summary>',
       '        /// 丢弃临时缓冲，让 OnRenderImage 按需重建。',
       '        /// </summary>',
       '        /// <remarks>',
       '        /// 【用途 · 用户 2026-10-05 报"从后台切回前台画面变黑无法还原"】',
       '        /// Android 切后台会释放 GPU 资源（含本组件的 RenderTexture），回前台时它们可能已失效，',
       '        /// 而本组件不会自己重建 → 后处理链上拿到空纹理 → 整片黑。',
       '        /// 由 MenuScene.RebuildRenderState() 在回前台时调用本方法，随后 EnsureBuffers 自动重建。',
       '        /// </remarks>',
       '        public void RebuildBuffers()',
       '        {',
       '            ReleaseBuffers();',
       '        }',
       '',
       '        void EnsureBuffers(int w, int h)'].join(NL));
    log.push('  ✓ ② PostFx.RebuildBuffers');
  } else log.push('  · ② 已存在');
  fs.writeFileSync(P, s, 'utf8');
}

console.log(log.join(NL));
