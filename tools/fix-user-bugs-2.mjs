// 修用户 2026-10-05 报的问题（第二批，行号插入版）。
//
// 上一版脚本因**替换文本里出现反引号**而语法报错（第 12 次踩这个坑 —— 本文件因此
// 全程不写反引号，并用数组 join 拼代码块）。
//
// 本批做四件事：
//   ① 大厅：立柱避开中间（菜单板前不该有结构柱）—— 已在 fix-user-bugs-1 里做过，这里校验
//   ② 旧 UI 容器：板模式下整体隐藏旧 UI（修"点一次退出后无法再点"）
//   ③ 相机到位后停手（修"抖屏"）
//   ④ 从后台切回前台时重建渲染状态（修"切回前台黑屏"）
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const NL = '\n';
const log = [];

// ════════════════════════════════════════════════════════════════════
// ① 校验大厅立柱已避开中间
// ════════════════════════════════════════════════════════════════════
{
  const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/HallScene.cs');
  const s = fs.readFileSync(P, 'utf8');
  const ok = s.includes('i < 4') && s.includes('0.375f') && !s.includes('float x = -w * 0.5f + i * (w * 0.25f);');
  console.log(ok ? '  ✓ ① 大厅立柱已避开中间' : '  ! ① 立柱仍是旧布局，需重做');
}

// ════════════════════════════════════════════════════════════════════
// ② MenuScene：旧 UI 容器 + 板模式显隐
// ════════════════════════════════════════════════════════════════════
{
  const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
  let s = fs.readFileSync(P, 'utf8');

  // ②a 字段
  if (!s.includes('GameObject _legacyUi;')) {
    s = s.replace('        string _boardUiDiag = "板UI:未建";',
      ['        string _boardUiDiag = "板UI:未建";',
       '        /// <summary>',
       '        /// 旧 UI 的容器（右侧竖排按钮 / 标题 / 副标题 / 左侧面板 / 底部提示）。',
       '        /// </summary>',
       '        /// <remarks>',
       '        /// 【为什么需要 · 用户 2026-10-05 报"点一次退出后就无法再点"】',
       '        /// 旧的 8 个竖排按钮与左侧面板在板模式里**没有被隐藏**，于是退出操作视角后旧按钮又盖在场景上；',
       '        /// 而板模式下的点击先被 BoardInput 消费（点在板上就 return）→ 表现就是"点了没反应 / 无法再点"。',
       '        /// 用户已明确旧 UI 要废弃 → 正确行为是：**板模式 = 唯一 UI**。',
       '        /// 用一个容器统一显隐，比逐个 SetActive 更不容易漏。',
       '        /// </remarks>',
       '        GameObject _legacyUi;'].join(NL));
    log.push('  ✓ ②a 旧 UI 容器字段');
  } else log.push('  · ②a 字段已存在');

  // ②b ShowBoardUi 里加旧 UI 显隐
  const oldShow = [
    '            if (_boardHint != null) _boardHint.gameObject.SetActive(!show);',
    '        }',
  ].join(NL);
  const newShow = [
    '            if (_boardHint != null) _boardHint.gameObject.SetActive(!show);',
    '            // 板模式 = 唯一 UI：旧界面整体隐藏（用户已明确旧 UI 要废弃）',
    '            if (_legacyUi != null) _legacyUi.SetActive(!show);',
    '        }',
  ].join(NL);
  if (s.includes(oldShow) && !s.includes('_legacyUi.SetActive(!show)')) {
    s = s.replace(oldShow, newShow);
    log.push('  ✓ ②b ShowBoardUi 联动旧 UI');
  } else log.push('  · ②b 已联动或锚点未中');

  // ②c BuildOptions 里：把标题/副标题/左侧面板/底部提示挂进容器
  const subAnchor = 'SceneMaterials.Label(_canvas.transform, "MenuSub",';
  if (s.includes(subAnchor) && !s.includes('_legacyUi.transform')) {
    // 用**行级**替换：把 BuildOptions 里所有 _canvas.transform 的 Label 调用改成 _legacyUi.transform
    const lines = s.split(NL);
    let inBuildOptions = false, changed = 0;
    for (let i = 0; i < lines.length; i++) {
      if (lines[i].includes('void BuildOptions()')) inBuildOptions = true;
      else if (inBuildOptions && lines[i].includes('void ') && lines[i].includes(')') && lines[i].includes('{') && !lines[i].includes('BuildOptions')) inBuildOptions = false;
      if (inBuildOptions && lines[i].includes('_canvas.transform, "MenuTitle"')) { lines[i] = lines[i].replace('_canvas.transform', '_legacyUi.transform'); changed++; }
      if (inBuildOptions && lines[i].includes('_canvas.transform, "MenuSub"')) { lines[i] = lines[i].replace('_canvas.transform', '_legacyUi.transform'); changed++; }
      if (inBuildOptions && lines[i].includes('_canvas.transform, "MenuHint"')) { lines[i] = lines[i].replace('_canvas.transform', '_legacyUi.transform'); changed++; }
      if (inBuildOptions && lines[i].includes('_canvas.transform, "LobbyPanel"')) { lines[i] = lines[i].replace('_canvas.transform', '_legacyUi.transform'); changed++; }
      if (inBuildOptions && lines[i].includes('_canvas.transform, "MenuFallbackHint"')) { lines[i] = lines[i].replace('_canvas.transform', '_legacyUi.transform'); changed++; }
    }
    if (changed > 0) { s = lines.join(NL); log.push(`  ✓ ②c ${changed} 个旧 UI 元素挂进容器`); }
    else log.push('  ! ②c 没找到可迁移的旧 UI 元素');
  } else log.push('  · ②c 已完成或锚点未中');

  // ②d 容器必须在 BuildCanvas 之后建 —— 插在 BuildOptions 第一行
  if (!s.includes('_legacyUi = new GameObject')) {
    s = s.replace('        void BuildOptions()\n        {\n            BuildCanvas();',
      ['        void BuildOptions()',
       '        {',
       '            BuildCanvas();',
       '            // 旧 UI 容器：本轮之后旧界面整体废弃；迁移完成前先做到"板模式下不出现"。',
       '            if (_legacyUi == null)',
       '            {',
       '                _legacyUi = new GameObject("LegacyUiRoot");',
       '                _legacyUi.transform.SetParent(_canvas.transform, false);',
       '            }'].join(NL));
    log.push('  ✓ ②d 建容器');
  } else log.push('  · ②d 容器已建');

  fs.writeFileSync(P, s, 'utf8');
}

// ════════════════════════════════════════════════════════════════════
// ③ 相机到位后停手（修抖屏）+ ④ 前后台切换重建（修切回黑屏）
// ════════════════════════════════════════════════════════════════════
{
  const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
  let s = fs.readFileSync(P, 'utf8');

  const oldCam = [
    '            _cam.transform.position = Vector3.Lerp(posFree, posBoard, k);',
    '            var dir = (Vector3.Lerp(lookFree, lookBoard, k) - _cam.transform.position).normalized;',
    '            if (dir.sqrMagnitude > 0.0001f)',
    '                _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation,',
    '                    Quaternion.LookRotation(dir), Mathf.Clamp01(dt * 8f));',
  ].join(NL);
  const newCam = [
    '            // 【修"抖屏" · 用户 2026-10-05 反馈】',
    '            // 原先**每帧**都朝目标 slerp，即使已经到位也在微调 → 相机永不静止 → 画面细颤。',
    '            // 到位后必须**停手**：只在还没到位时写 transform，到位后一个字节都不动。',
    '            bool settled = Mathf.Abs(_boardBlend - target) < 0.0005f;',
    '            if (settled && _boardSettled) return;',
    '            if (settled) _boardSettled = true; else _boardSettled = false;',
    '',
    '            var wantPos = Vector3.Lerp(posFree, posBoard, k);',
    '            var wantLook = Vector3.Lerp(lookFree, lookBoard, k);',
    '            _cam.transform.position = wantPos;',
    '            var dir = (wantLook - wantPos).normalized;',
    '            if (dir.sqrMagnitude > 0.0001f)',
    '            {',
    '                // 到位的那一帧直接**精确对准**（slerp 永远差一点点，这就是抖的来源之一）',
    '                _cam.transform.rotation = settled ? Quaternion.LookRotation(dir)',
    '                                                  : Quaternion.Slerp(_cam.transform.rotation,',
    '                                                        Quaternion.LookRotation(dir), Mathf.Clamp01(dt * 8f));',
    '            }',
  ].join(NL);
  if (s.includes(oldCam)) { s = s.replace(oldCam, newCam); log.push('  ✓ ③ 相机到位后停手（修抖屏）'); }
  else log.push('  ! ③ 相机锚点未中');

  if (!s.includes('bool _boardSettled')) {
    s = s.replace('        bool _boardMode;',
      ['        bool _boardMode;',
       '        /// <summary>相机是否已在目标位（到位后不再每帧写 transform —— 否则会抖屏）。</summary>',
       '        bool _boardSettled;'].join(NL));
    log.push('  ✓ ③b _boardSettled 字段');
  }

  // ④ 前后台切换：重建渲染状态
  if (!s.includes('OnApplicationFocus')) {
    s = s.replace('        void Update()\n        {',
      ['        /// <summary>',
       '        /// 前后台切换时重建渲染状态。',
       '        /// </summary>',
       '        /// <remarks>',
       '        /// 【用户 2026-10-05 报"从后台重新切回前台时画面变黑无法还原"】',
       '        /// Android 在应用切到后台时会**释放**相机的 RenderTarget 与临时缓冲，',
       '        /// 而本工程的相机与 UI 全部是代码建的（场景只有 Boot.unity），没有任何组件会在回前台时重建它们。',
       '        /// 结果：回前台后相机没有可用的渲染目标 → 整片黑且**不会自己恢复**。',
       '        /// 正解：在 OnApplicationFocus(true) / OnApplicationPause(false) 时',
       '        /// **重新施加一次相机与画质设置**（幂等），并把后处理的临时缓冲释放掉让它们按需重建。',
       '        /// </remarks>',
       '        void OnApplicationFocus(bool hasFocus)',
       '        {',
       '            if (!hasFocus) return;',
       '            RebuildRenderState();',
       '        }',
       '',
       '        void OnApplicationPause(bool paused)',
       '        {',
       '            if (paused) return;',
       '            RebuildRenderState();',
       '        }',
       '',
       '        /// <summary>回前台后重建渲染状态（幂等，可重复调用）。</summary>',
       '        void RebuildRenderState()',
       '        {',
       '            if (_isRebuilding) return;',
       '            _isRebuilding = true;',
       '            try',
       '            {',
       '                if (_cam != null)',
       '                {',
       '                    _cam.enabled = true;',
       '                    _cam.clearFlags = CameraClearFlags.SolidColor;',
       '                    _cam.backgroundColor = new Color(0.012f, 0.014f, 0.020f);',
       '                    _cam.targetTexture = null;   // 关键：清掉可能已被释放的 RT 绑定',
       '                }',
       '                // 画质档重新施加（targetFrameRate/vSync/阴影/MSAA 在后台会被系统改掉）',
       '                if (_boot != null) _boot.ApplyRenderQuality();',
       '                // 后处理：把临时缓冲丢掉，OnRenderImage 会按需重建',
       '                if (_boot != null && _boot.PostFx != null) _boot.PostFx.RebuildBuffers();',
       '                _lastTouch = "已从后台恢复";',
       '            }',
       '            catch (System.Exception e) { Debug.LogWarning("[Whisper] 回前台重建失败：" + e.Message); }',
       '            finally { _isRebuilding = false; }',
       '        }',
       '',
       '        bool _isRebuilding;',
       '',
       '        void Update()',
       '        {'].join(NL));
    log.push('  ✓ ④ 前后台切换重建渲染状态');
  } else log.push('  · ④ 已存在');

  fs.writeFileSync(P, s, 'utf8');
}

console.log(log.join(NL));
